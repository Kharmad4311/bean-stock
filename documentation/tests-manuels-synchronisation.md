# Tests manuels de la synchronisation

Mode opératoire pour vérifier à la main les garanties de la synchronisation du catalogue. Chaque scénario dure quelques minutes ; ils sont pensés pour être suivis dans l'ordre. Le fonctionnement est décrit dans [`synchronisation.md`](synchronisation.md), le verrou dans [`verrou-distribue.md`](verrou-distribue.md).

**Outils** : Bruno (dossier `Admin`), Scalar (http://localhost:5080/scalar) ou PowerShell pour les requêtes ; SSMS (base `BeanStock`) pour le SQL.

## 0. Préparation

1. SQL Server démarré (`docker compose up -d`) et chaîne de connexion dans les *user secrets* (voir README).
2. Lancer **uniquement l'API**, avec le profil normal. En Development, elle applique les migrations au démarrage.
3. Vérifier que tout est prêt :
   - `GET http://localhost:5080/health/ready` doit répondre `200` ;
   - `SELECT is_read_committed_snapshot_on FROM sys.databases WHERE name = 'BeanStock';` doit renvoyer `1`.

## 1. Synchronisation manuelle et idempotence

| Étape | Action | Attendu |
|---|---|---|
| 1.1 | `POST /admin/catalog/sync` (Bruno : *Admin / Synchronize Catalog*) | `200`, `trigger: "Manual"`, `added` ≈ 194 |
| 1.2 | `GET /products?pageSize=100` | `totalCount` ≈ 194 |
| 1.3 | Relancer `POST /admin/catalog/sync` | `added: 0`, `unchanged` ≈ 194 : aucun doublon, rien de réécrit |
| 1.4 | `SELECT TOP 5 * FROM CatalogSyncRuns ORDER BY StartedAt DESC;` | Deux runs `Succeeded`, avec leurs compteurs |

En PowerShell : `Invoke-RestMethod -Method Post http://localhost:5080/admin/catalog/sync`

## 2. Mise à jour, désactivation, réactivation

On fausse la base pour simuler des changements chez le fournisseur, puis on relance la synchronisation.

```sql
-- Un prix modifié, un produit désactivé, un produit « disparu » chez le fournisseur
UPDATE Products SET Price = 0.01 WHERE ExternalId = 2;
UPDATE Products SET IsActive = 0 WHERE ExternalId = 1;
INSERT INTO Products (Id, ExternalId, Name, Category, Price, Stock, AvailabilityStatus, ProviderUpdatedAt, LastUpdatedAt, IsActive)
VALUES (NEWID(), 99999, N'Produit fantôme', N'test', 1, 1, N'In Stock', NULL, SYSDATETIMEOFFSET(), 1);
```

| Étape | Attendu |
|---|---|
| Avant la synchronisation, `GET /products` | Le produit 1 n'apparaît plus, le 99999 apparaît |
| `POST /admin/catalog/sync` | `updated: 1` (prix rétabli), `reactivated: 1` (produit 1), `deactivated: 1` (99999) |
| SQL sur 99999 | Toujours présent, avec `IsActive = 0` : jamais supprimé |
| `LastUpdatedAt` | A changé pour les produits 1, 2 et 99999 seulement |

## 3. Garde-fou contre un catalogue suspect

On gonfle le catalogue local pour que la réponse de DummyJSON paraisse anormalement courte : moins de 50 % des produits actifs.

```sql
INSERT INTO Products (Id, ExternalId, Name, Category, Price, Stock, AvailabilityStatus, ProviderUpdatedAt, LastUpdatedAt, IsActive)
SELECT NEWID(), 100000 + value, CONCAT(N'Faux ', value), N'test', 1, 1, N'In Stock', NULL, SYSDATETIMEOFFSET(), 1
FROM GENERATE_SERIES(1, 300);
```

- `POST /admin/catalog/sync` doit répondre `502` `CatalogSync.SuspiciousCatalog`, avec les compteurs dans la réponse ;
- aucun des 300 produits ne doit avoir été désactivé, et le run doit être enregistré en `Failed` ;
- **Nettoyage** : `DELETE FROM Products WHERE ExternalId > 99998;`, puis relancer une synchronisation.

## 4. Synchronisation déjà en cours (`409`)

DummyJSON répond trop vite pour lancer deux synchronisations simultanées à la main. On prend donc le verrou dans SSMS, comme le ferait une autre instance :

```sql
EXEC sp_getapplock @Resource = 'BeanStock.CatalogSynchronization', @LockMode = 'Exclusive', @LockOwner = 'Session';
SELECT request_session_id, request_mode FROM sys.dm_tran_locks WHERE resource_type = 'APPLICATION';
```

- `POST /admin/catalog/sync` doit répondre `409` `CatalogSync.AlreadyRunning`, sans créer de run ;
- `EXEC sp_releaseapplock @Resource = 'BeanStock.CatalogSynchronization', @LockOwner = 'Session';` ; fermer la fenêtre SSMS marche aussi et simule un crash ;
- relancer la synchronisation : `200`.

## 5. Fournisseur en panne (`502`)

1. Arrêter l'API et ajouter dans `src/BeanStock.Api/appsettings.Development.json` :
   ```json
   "DummyJson": { "BaseAddress": "https://dummyjson.invalid/" }
   ```
2. Relancer l'API, puis `POST /admin/catalog/sync`. Attendu, après quelques secondes de nouvelles tentatives automatiques : `502` `CatalogSync.ProviderUnavailable`.
3. `GET /products` doit toujours renvoyer le catalogue complet, et le run être en `Failed` avec la cause dans `FailureReason`.
4. Retirer la ligne, puis relancer l'API.

## 6. Lectures non bloquées pendant une écriture

Dans SSMS, sans valider :

```sql
BEGIN TRAN;
UPDATE Products SET Name = N'Nom non validé' WHERE ExternalId = 3;
```

- `GET /products` doit répondre **immédiatement**, avec l'ancien nom du produit 3 (`READ_COMMITTED_SNAPSHOT`) ;
- `ROLLBACK;`

## 7. Worker : planning partagé

1. Démarrer le Worker avec le profil **`BeanStock.Worker (synchro rapide)`** : intervalle de 2 min, vérification toutes les 15 s.
2. Dans la console du Worker :
   - au démarrage : `schedule started (enabled: True, interval: 00:02:00…)` ;
   - si la dernière synchronisation réussie date de moins de 2 min : des messages *not due* toutes les 15 s ;
   - dès que les 2 min sont écoulées : `Catalog synchronization … started (Scheduled)`, puis `completed` avec les compteurs.
3. SQL : un run avec `Trigger = Scheduled`.
4. Lancer `POST /admin/catalog/sync` : la synchronisation automatique suivante est repoussée de 2 min.

## 8. Plusieurs Workers en même temps

Dans deux terminaux PowerShell, à la racine du dépôt :

```powershell
$env:CatalogSync__Schedule__Interval = "00:01:00"
$env:CatalogSync__Schedule__CheckInterval = "00:00:10"
dotnet run --project src/BeanStock.Worker
```

Attendu : **une seule synchronisation par minute**, toutes instances confondues. Les deux consoles affichent des messages *not due* ; quand l'échéance tombe, une console synchronise et l'autre affiche *skipped: another one is running* ou *not due*. Côté SQL, les runs `Scheduled` sont espacés d'environ une minute, sans doublon.

## 9. Configuration modifiée sans redémarrage

1. Démarrer le Worker avec le profil **normal** : le profil rapide fixe le délai par variable d'environnement, qui l'emporte sur le fichier.
2. Pendant qu'il tourne, mettre `"Interval": "00:01:00"` dans `src/BeanStock.Worker/appsettings.Development.json` et enregistrer. La nouvelle valeur s'applique à la vérification suivante, sans redémarrage.
3. Mettre ensuite `"Interval": "00:00:01"` (sous le minimum de 10 s). Attendu : un log d'erreur *Invalid catalog synchronization schedule ignored*, et le Worker continue avec le réglage précédent.
4. Remettre `1.00:00:00`.

## 10. Redémarrage et rattrapage

- Arrêter puis relancer le Worker juste après une synchronisation : il ne doit pas resynchroniser (*not due*).
- Vider l'historique (`DELETE FROM CatalogSyncRuns;`) puis relancer le Worker : il synchronise **immédiatement**, puisqu'aucune réussite n'est enregistrée.
