# Tests manuels du panier

Mode opératoire pour vérifier à la main les règles du panier et ses garanties de concurrence, y compris entre **deux instances** de l'API. Les scénarios sont pensés pour être suivis dans l'ordre ; chacun dure quelques minutes. Le contrat et le fonctionnement sont décrits dans [`panier.md`](panier.md).

**Outils**
- **PowerShell 7** (`pwsh`) et le script [`scripts/panier.ps1`](scripts/panier.ps1), qui fournit des fonctions d'appel et affiche chaque réponse de façon lisible (statut, lignes et versions, ou code d'erreur). Windows PowerShell 5.1 ne suffit pas : les scénarios parallèles utilisent `ForEach-Object -Parallel`.
- **SSMS** (base `BeanStock`) pour le SQL, et surtout pour **tenir le verrou d'un panier** à la place d'une autre requête : c'est ce qui rend les scénarios de concurrence reproductibles à la main.
- Bruno ou Postman (dossier `Cart`) conviennent pour les scénarios séquentiels 1 à 8 ; l'utilisateur est la variable `UserId`.

## 0. Préparation

1. SQL Server démarré et chaîne de connexion dans les *user secrets* (voir README).
2. Lancer l'API avec le profil **Start API** (http://localhost:5080). En Development, elle applique les migrations au démarrage.
3. Remplir le catalogue si ce n'est pas déjà fait : `Invoke-RestMethod -Method Post http://localhost:5080/admin/catalog/sync`.
4. Dans un terminal PowerShell 7, à la racine du dépôt :
   ```powershell
   . ./documentation/scripts/panier.ps1
   $products = Get-Products -Count 3
   $products   # les identifiants locaux (id) servent à ajouter au panier
   ```
5. Partir de paniers vides pour les utilisateurs de test (SSMS) :
   ```sql
   DELETE FROM Carts WHERE UserId IN (N'alice', N'bob', N'carol', N'dave');   -- les lignes suivent (cascade)
   ```

Chaque fonction renvoie un objet `{ Status, Body }` : on l'affecte à une variable (`$c = Get-Cart`) pour réutiliser identifiants et versions, par exemple `$c.Body.lines[0].lineId`.

**Seconde instance** (scénarios 9 à 13). Dans un autre terminal, à la racine du dépôt :

```powershell
$env:ASPNETCORE_ENVIRONMENT = 'Development'
dotnet run --project src/BeanStock.Api --no-launch-profile --urls http://localhost:5081
```

Elle partage la base, les *user secrets* et la configuration de la première. Les fonctions du script acceptent `-Instance 2` pour viser http://localhost:5081.

## 1. Identification de l'utilisateur

| Étape | Action | Attendu |
|---|---|---|
| 1.1 | `Get-Cart -User ''` (aucun en-tête) | `401`, avec un message rappelant le format de `X-User-Id` |
| 1.2 | `Get-Cart -User 'alice smith'` | `401` : l'espace n'est pas autorisé |
| 1.3 | `Get-Cart` (alice) | `404 Cart.NotFound` : l'utilisateur est reconnu, mais n'a pas encore de panier |

## 2. Création idempotente

| Étape | Action | Attendu |
|---|---|---|
| 2.1 | `New-Cart` | `201`, panier `v0`, aucune ligne |
| 2.2 | `New-Cart` | `200`, le **même** panier (même `createdAt`) |
| 2.3 | `SELECT COUNT(*) FROM Carts WHERE UserId = N'alice';` | `1` |

La version parallèle de ce scénario est le §12.

## 3. Ajout, cumul et limites

```powershell
$p1 = $products[0].id; $p2 = $products[1].id
```

| Étape | Action | Attendu |
|---|---|---|
| 3.1 | `Add-CartLine $p1 -Quantity 2` | `200`, `v1`, une ligne en version 0, sous-total = 2 × prix |
| 3.2 | `Add-CartLine $p1 -Quantity 3` | `200`, `v2`, **toujours une seule ligne**, quantité 5, ligne en version 1 |
| 3.3 | `Add-CartLine $p2` | `200`, `v3`, deux lignes ; le total est la somme des sous-totaux |
| 3.4 | `Add-CartLine $p1 -Quantity 995` | `400 Cart.QuantityLimitExceeded` (`currentQuantity = 5`, `requestedQuantity = 995`) |
| 3.5 | `Add-CartLine $p1 -Quantity 0` puis `-Quantity 1000` | `400 Cart.QuantityOutOfRange` |
| 3.6 | `Add-CartLine ([guid]::NewGuid())` | `400 Cart.ProductNotFound` |
| 3.7 | `Get-Cart` | Toujours `v3`, quantités inchangées : **une demande rejetée ne modifie rien**, version comprise |
| 3.8 | `Add-CartLine $p1 -User bob` | `404 Cart.NotFound` : bob n'a pas créé de panier, rien n'est créé implicitement |

## 4. Nom et prix figés au premier ajout

Simuler un changement chez le fournisseur (SSMS, en remplaçant l'identifiant par celui de `$p1`) :

```sql
UPDATE Products SET Name = N'Nom modifié', Price = 999.99 WHERE Id = '<p1>';
```

| Étape | Action | Attendu |
|---|---|---|
| 4.1 | `Get-Products -Count 3` | Le produit affiche le nouveau nom et le nouveau prix |
| 4.2 | `Get-Cart` | La ligne garde l'**ancien** nom et l'**ancien** prix ; le total ne bouge pas |
| 4.3 | `Add-CartLine $p1` | Quantité 6, toujours à l'ancien prix : le réajout ne rafraîchit pas la ligne |

**Nettoyage** : relancer une synchronisation, qui rétablit les valeurs du fournisseur.

## 5. Produit retiré du catalogue

```sql
UPDATE Products SET IsActive = 0 WHERE Id = '<p2>';
```

| Étape | Action | Attendu |
|---|---|---|
| 5.1 | `Get-Products -Count 3` | Le produit n'est plus listé |
| 5.2 | `Get-Cart` | La ligne est toujours là, consultable, avec son nom et son prix |
| 5.3 | `Add-CartLine $p2` | `400 Cart.ProductUnavailable`, panier inchangé |
| 5.4 | `$l = (Get-Cart).Body.lines \| Where-Object productId -eq $p2` puis `Set-CartLineQuantity $l.lineId 3 -Version $l.version` | `200` : la ligne reste modifiable (et supprimable), seul l'ajout est interdit |

**Nettoyage** : relancer une synchronisation, qui réactive le produit.

## 6. Modification et conflit de version

On simule deux onglets qui ont lu le panier au même moment :

```powershell
$c = Get-Cart
$line = $c.Body.lines[0]
```

| Étape | Action | Attendu |
|---|---|---|
| 6.1 | `Set-CartLineQuantity $line.lineId 7 -Version $line.version` (onglet 1) | `200`, quantité **remplacée** par 7, version de ligne +1, version du panier +1 |
| 6.2 | `Set-CartLineQuantity $line.lineId 9 -Version $line.version` (onglet 2, même lecture) | `409 Cart.LineVersionConflict`, `expectedVersion` = ancienne version, `currentVersion` = nouvelle |
| 6.3 | `Get-Cart` | Quantité 7 : la modification de l'onglet 1 n'a pas été écrasée, aucune version n'a bougé en 6.2 |
| 6.4 | `Set-CartLineQuantity $line.lineId 7 -Version ($line.version + 1)` | `200`, mais **aucune version ne bouge** : remettre la même quantité n'est pas une modification effective |
| 6.5 | `Set-CartLineQuantity $line.lineId 3` (sans version) | `400`, erreur sur `ExpectedLineVersion` : la version est obligatoire |

Variante : modifier la version de la ligne directement en base (`UPDATE CartLines SET Version = Version + 1 WHERE Id = '<lineId>';`), comme le ferait une autre requête, puis renvoyer une modification avec l'ancienne version → `409`.

## 7. Suppression sans résurrection

```powershell
$c = Get-Cart
$line = $c.Body.lines[0]
```

| Étape | Action | Attendu |
|---|---|---|
| 7.1 | `Remove-CartLine $line.lineId -Version ($line.version + 1)` | `409` : on ne supprime pas une ligne modifiée depuis sa lecture |
| 7.2 | `Remove-CartLine $line.lineId -Version $line.version` | `200`, la ligne a disparu, version du panier +1 |
| 7.3 | `Set-CartLineQuantity $line.lineId 5 -Version $line.version` | `404 Cart.LineNotFound` : la modification obsolète ne recrée pas la ligne |
| 7.4 | `SELECT * FROM CartLines WHERE Id = '<lineId>';` | Aucune ligne : la suppression est physique |

## 8. Isolation entre utilisateurs

```powershell
$aliceLine = (Get-Cart).Body.lines[0]
New-Cart -User bob
```

| Étape | Action | Attendu |
|---|---|---|
| 8.1 | `Get-Cart -User bob` | Le panier de bob, vide : rien de celui d'alice |
| 8.2 | `Set-CartLineQuantity $aliceLine.lineId 50 -Version $aliceLine.version -User bob` | `404 Cart.LineNotFound` : la ligne d'alice n'existe pas dans le panier de bob |
| 8.3 | `Remove-CartLine $aliceLine.lineId -Version $aliceLine.version -User bob` | `404 Cart.LineNotFound` |
| 8.4 | `Get-Cart` (alice) | Inchangé |

Aucune route ne porte d'identifiant de panier : il n'existe aucun moyen de désigner le panier d'un autre.

## 9. Observer le verrou : écritures en file, lectures libres

Ce scénario montre le mécanisme utilisé par tous les suivants. Dans SSMS, prendre le verrou du panier d'alice **avec la même requête que l'application**, sans valider :

```sql
BEGIN TRAN;
SELECT [Id] FROM [Carts] WITH (UPDLOCK, ROWLOCK) WHERE [UserId] = N'alice';
```

| Étape | Action | Attendu |
|---|---|---|
| 9.1 | `Get-Cart` | Réponse **immédiate** : les lectures ne prennent pas de verrou (instantané RCSI) |
| 9.2 | `Add-CartLine $p1` | **Bloqué** : la requête attend le verrou |
| 9.3 | Dans une autre fenêtre SSMS, la requête de surveillance ci-dessous | La session de SSMS détient un verrou `U` (`GRANT`), celle de l'API en attend un (`WAIT`) |
| 9.4 | `Get-Cart -User bob` (autre terminal) et `Add-CartLine $p1 -User bob` | Immédiat : le verrou ne concerne que le panier d'alice |
| 9.5 | Dans la première fenêtre SSMS : `ROLLBACK;` | La requête 9.2 se termine aussitôt, `200` |

```sql
SELECT request_session_id, resource_type, resource_description, request_mode, request_status
FROM sys.dm_tran_locks
WHERE resource_database_id = DB_ID('BeanStock') AND request_mode IN ('U', 'X');
```

C'est exactement ce que vit une requête qui arrive pendant qu'une autre modifie le même panier, sur n'importe quelle instance : elle attend, puis relit le panier **déjà modifié** avant d'appliquer sa propre demande.

## 10. L'exemple de l'énoncé, sur deux instances

Alice a un panier en version 4 avec deux articles A et B, ouvert dans deux onglets ; chaque onglet est servi par une instance différente. La seconde instance doit tourner (§0) et le script doit être chargé dans **deux terminaux**.

1. Préparer le panier en partant de zéro : `DELETE FROM Carts WHERE UserId = N'alice';` dans SSMS, puis dans le terminal 1 (le produit `$p2` doit être actif, voir le nettoyage du §5) :
   ```powershell
   New-Cart
   Add-CartLine $p1; Add-CartLine $p2
   $c = Get-Cart
   Set-CartLineQuantity $c.Body.lines[0].lineId 2 -Version 0
   Set-CartLineQuantity $c.Body.lines[1].lineId 2 -Version 0
   ```
   Le panier est en `v4`, avec A et B en quantité 2.
2. **Même lecture dans les deux onglets** : `$c = Get-Cart` dans le terminal 1 **et** dans le terminal 2. Les deux affichent `v4`.
3. SSMS : prendre le verrou du panier d'alice (requête du §9), pour que les deux demandes arrivent pendant qu'il est occupé.
4. Terminal 1 (instance 1) : `Set-CartLineQuantity $c.Body.lines[0].lineId 5 -Version $c.Body.lines[0].version` → bloqué.
5. Terminal 2 (instance 2) : `Set-CartLineQuantity $c.Body.lines[1].lineId 7 -Version $c.Body.lines[1].version -Instance 2` → bloqué.
6. SSMS : `ROLLBACK;`

**Attendu** : les deux demandes répondent `200`. La seconde servie affiche `v6`, avec A à 5 et B à 7. Aucun conflit, alors que la version globale a changé entre la lecture et la seconde écriture : seule la version **de la ligne** concernée compte.

## 11. Même ligne, même version, deux instances

Même préparation qu'au §10, étapes 2 et 3 (lecture dans les deux terminaux, verrou dans SSMS), puis :

1. Terminal 1 : `Set-CartLineQuantity $c.Body.lines[0].lineId 10 -Version $c.Body.lines[0].version` → bloqué.
2. Terminal 2 : `Set-CartLineQuantity $c.Body.lines[0].lineId 20 -Version $c.Body.lines[0].version -Instance 2` → bloqué.
3. SSMS : `ROLLBACK;`

**Attendu** : la première demande arrivée répond `200` (quantité 10) ; l'autre répond `409 Cart.LineVersionConflict`, avec `currentVersion` = version + 1. La version du panier n'a augmenté que de 1. Au plus une modification est acceptée, l'autre n'est jamais écrasée en silence.

**Variante suppression / modification** : en terminal 1 `Remove-CartLine` avec la version lue, en terminal 2 `Set-CartLineQuantity` sur la même ligne avec la même version, puis `ROLLBACK;`. La suppression, arrivée en premier, répond `200` ; la modification répond `404 Cart.LineNotFound` et la ligne ne réapparaît pas.

## 12. Requêtes réellement simultanées

Sans verrou manuel, avec de vraies requêtes parallèles réparties sur les deux instances (ajouter `-SingleInstance` si la seconde n'est pas lancée).

**Création** (carol n'a pas de panier) :

```powershell
Invoke-BeanStockParallel POST '/me/cart' -User carol -Count 20
```

Attendu : `1 × 201 Created` et `19 × 200 OK` ; en SQL, `SELECT COUNT(*) FROM Carts WHERE UserId = N'carol';` renvoie `1`.

**Ajouts du même produit** (dave) :

```powershell
New-Cart -User dave
Invoke-BeanStockParallel POST '/me/cart/lines' -Body @{ productId = $p1; quantity = 2 } -User dave -Count 10
Get-Cart -User dave
```

Attendu : `10 × 200 OK`, **une seule ligne** de quantité 20, panier en `v10` : aucune quantité acceptée n'est perdue, chaque ajout a augmenté la version d'exactement 1.

**Plafond sous concurrence** (dave, quantité portée à 990 au préalable) :

```powershell
$line = (Get-Cart -User dave).Body.lines[0]
Set-CartLineQuantity $line.lineId 990 -Version $line.version -User dave
Invoke-BeanStockParallel POST '/me/cart/lines' -Body @{ productId = $p1; quantity = 5 } -User dave -Count 6
Get-Cart -User dave
```

Attendu : `1 × 200 OK` et `5 × 400 BadRequest` ; quantité 995 : le plafond de 999 est vérifié sur la vraie quantité, pas sur une lecture périmée.

## 13. Requête interrompue : panier cohérent

1. SSMS : prendre le verrou du panier d'alice (§9).
2. `$before = Get-Cart`, puis `Set-CartLineQuantity $before.Body.lines[0].lineId 42 -Version $before.Body.lines[0].version` → bloqué.
3. Interrompre la requête avec **Ctrl+C** : le client se déconnecte, l'API annule la requête SQL en attente et sa transaction.
4. Requête de surveillance du §9 : plus aucune session de l'API en `WAIT`.
5. SSMS : `ROLLBACK;`, puis `Get-Cart`.

**Attendu** : panier identique à `$before`, versions comprises. L'échec d'une écriture *pendant* l'enregistrement est couvert par le test d'intégration `Failure_while_saving_a_change_leaves_lines_and_versions_unchanged`, qui injecte une panne de base.

## 14. Contrôle et nettoyage

État complet des paniers de test, à comparer avec ce qu'affichent les réponses :

```sql
SELECT c.UserId, c.Version AS CartVersion, l.ProductName, l.UnitPrice, l.Quantity, l.Version AS LineVersion
FROM Carts AS c
LEFT JOIN CartLines AS l ON l.CartId = c.Id
WHERE c.UserId IN (N'alice', N'bob', N'carol', N'dave')
ORDER BY c.UserId, l.Id;
```

Nettoyage : `DELETE FROM Carts WHERE UserId IN (N'alice', N'bob', N'carol', N'dave');`, puis une synchronisation si les §4 et §5 ont modifié le catalogue.
