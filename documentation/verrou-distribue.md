# Le verrou distribué `sp_getapplock`

Fiche de référence sur le mécanisme qui garantit qu'**une seule synchronisation du catalogue s'exécute à la fois**, toutes instances confondues. Son usage dans le flux de synchronisation est décrit dans [`synchronisation.md`](synchronisation.md) §4 ; le code se trouve dans `src/BeanStock.Infrastructure.Persistence/Concurrency/SqlServerDistributedLock.cs`.

## 1. Pourquoi ce choix

**Le besoin.** L'API et le Worker peuvent tourner en plusieurs exemplaires, et chacun peut déclencher une synchronisation. Un `lock` C# ou un `SemaphoreSlim` ne protège que le processus où il s'exécute. Il faut un arbitre que **toutes** les instances partagent.

**Toutes les instances partagent déjà SQL Server**, et SQL Server dispose d'un gestionnaire de verrous. `sp_getapplock` permet d'y poser un verrou sur un **nom choisi par l'application** (ici `BeanStock.CatalogSynchronization`), au lieu d'une ligne ou d'une table.

| Atout | Détail |
|---|---|
| Rien à ajouter à l'infrastructure | Pas de Redis, de ZooKeeper ni de stockage Azure : la base suffit |
| Libération automatique en cas de crash | Un verrou de session est libéré quand la session se termine. Un processus qui plante ne laisse pas de verrou bloqué, contrairement à une ligne « verrou » dans une table, qui demande une date d'expiration |
| Fonctionnalité native et documentée | Disponible sur SQL Server, Azure SQL Database et Azure SQL Managed Instance |
| S'articule avec les transactions | Le verrou peut appartenir à une transaction, ou à la session qui porte la transaction |

## 2. Comment ça marche

### L'appel

```sql
DECLARE @result int;
EXEC @result = sp_getapplock
    @Resource    = 'BeanStock.CatalogSynchronization', -- nom libre (255 caractères max)
    @LockMode    = 'Exclusive',  -- Shared, Update, IntentShared, IntentExclusive, Exclusive
    @LockOwner   = 'Session',    -- ou 'Transaction' (valeur par défaut)
    @LockTimeout = 0;            -- 0 = ne pas attendre ; -1 = attendre indéfiniment ; sinon en ms
SELECT @result;
```

| Code retour | Signification |
|---|---|
| `0` | Verrou accordé immédiatement |
| `1` | Verrou accordé après attente |
| `-1` | Délai dépassé : le verrou est détenu ailleurs (notre cas « synchronisation déjà en cours ») |
| `-2` | Demande annulée |
| `-3` | Choisi comme victime d'un interblocage |
| `-999` | Erreur de paramètre ou d'appel |

Libération : `EXEC sp_releaseapplock @Resource = '…', @LockOwner = 'Session';`. Un même verrou demandé plusieurs fois doit être libéré autant de fois.

### Propriétaire : `Transaction` ou `Session`

- **`Transaction`** (par défaut) : l'appel doit être fait dans une transaction, et le verrou est libéré au commit ou au rollback.
- **`Session`** : le verrou appartient à la connexion. Il est libéré par `sp_releaseapplock`, ou quand la session se ferme (déconnexion, `KILL`, arrêt du serveur).

**Notre choix : `Session`.** Le verrou doit couvrir **aussi la récupération HTTP** du catalogue, qui dure plusieurs secondes. Avec `Transaction`, il faudrait garder une transaction ouverte pendant un appel réseau, ce qui retient des verrous et le journal pour rien.

### Portée

La ressource est qualifiée par l'**identifiant de la base courante** : le même nom dans deux bases différentes désigne deux verrous distincts. Le nom est haché en interne ; c'est à l'application de garantir son unicité.

### Dans BeanStock

1. `TryAcquireAsync` ouvre la connexion du `BeanStockDbContext` de la requête et la **garde ouverte**.
2. `sp_getapplock` en mode `Session`, `Exclusive`, délai `0`. Code `-1` : on renvoie `null`, et le cas d'usage répond `CatalogSync.AlreadyRunning` (`409`).
3. Récupération du catalogue, puis écriture dans une transaction ouverte **sur la même connexion**.
4. Juste avant le commit : vérification que le verrou est toujours détenu (§5).
5. Libération par `sp_releaseapplock` (pas par la simple fermeture de la connexion, voir §3), puis fermeture.

Le verrou est pris **avant** l'appel au fournisseur pour éviter ce scénario : l'instance A récupère le catalogue à 3h00, l'instance B le récupère à 3h01 et l'écrit, puis A écrit **par-dessus** des données plus anciennes.

## 3. Limites

| Limite | Conséquence | Réponse dans BeanStock |
|---|---|---|
| **Verrou coopératif** | Ne protège que contre le code qui demande le même nom. Un `UPDATE Products` lancé ailleurs passe | Seule la synchronisation écrit dans `Products` |
| **Lié à SQL Server** | Changer de base impose de changer de mécanisme | Caché derrière le port `IDistributedLock` : le cas d'usage n'en dépend pas |
| **Lié à une connexion** | Si la connexion tombe, le verrou est libéré, et une autre instance peut démarrer. EF Core peut rouvrir la connexion sur une nouvelle session **sans** verrou | Vérification avant commit (§5) |
| **Pool de connexions** | Une connexion « fermée » retourne dans le pool sans fermer la session : on ne peut pas compter sur la fermeture pour libérer | `sp_releaseapplock` explicite avant la fermeture |
| **Portée d'une seule base** | Le même nom dans une autre base est un autre verrou | Toutes les instances utilisent la même base |
| **Pas de file d'attente** (délai `0`) | Le second demandeur n'attend pas | Voulu : `409` côté API, « passe son tour » côté Worker |

## 4. Est-ce que ça verrouille la base ?

**Non.** `sp_getapplock` ne verrouille **aucune donnée** : il réserve un **nom** dans le gestionnaire de verrous. Seul un autre appelant qui demande le même nom, dans la même base, attend ou échoue. Les paniers, les lectures du catalogue et toutes les autres écritures continuent normalement.

Ce qui verrouille des données, c'est **la transaction de synchronisation elle-même** : ses `INSERT` et `UPDATE` posent des verrous de ligne classiques, qui peuvent être regroupés en un verrou sur toute la table au-delà d'environ 5 000 verrous (escalade de verrous). Ce sont deux mécanismes distincts, à ne pas confondre.

Conséquences de ces verrous de données :

- **Lectures du catalogue** : non bloquées, grâce à `READ_COMMITTED_SNAPSHOT` activé sur la base. Les lecteurs voient la dernière version validée.
- **Ajout au panier** : peut attendre. Insérer une ligne de panier vérifie la clé étrangère vers `Products`, et **même sous RCSI, SQL Server fait cette vérification avec des verrous classiques** pour ne pas valider une référence sur la base d'une version périmée. Ajouter un produit que la synchronisation est en train de modifier attend donc son commit. L'impact est négligeable avec DummyJSON (environ 200 produits, quelques secondes) ; les pistes pour un gros catalogue sont dans `synchronisation.md` §12.

## 5. Perte du verrou et contrôle avant commit (*fencing*)

**Le problème général.** Un détenteur peut perdre son verrou **sans le savoir** : coupure réseau, basculement de la base, pause du processus. Martin Kleppmann l'a formalisé dans *How to do distributed locking* : un verrou seul ne suffit pas, il faut que **la ressource protégée** refuse les écritures d'un détenteur périmé. Sa solution est le *fencing token*, un numéro croissant présenté à chaque écriture et vérifié par le stockage. Il distingue aussi les verrous utilisés **pour l'efficacité** (éviter un travail en double) de ceux utilisés **pour la correction** (éviter une corruption) : seuls les seconds exigent ce niveau de garantie.

**Notre cas particulier.** Ici, le verrou et les données vivent **dans le même système**, SQL Server, et sur la **même session**. Le handler peut donc vérifier, dans la transaction et juste avant le commit, que sa session détient toujours le verrou :

```sql
SELECT APPLOCK_MODE('public', 'BeanStock.CatalogSynchronization', 'Session'); -- 'Exclusive' ou 'NoLock'
```

- Réponse `Exclusive` : on valide. Si la session meurt entre la vérification et le commit, le commit échoue avec elle : il n'existe aucune fenêtre où l'on écrirait sans verrou.
- Réponse `NoLock`, ou connexion inutilisable : rollback, erreur `CatalogSync.LockLost` (`409`), run `Failed`.

C'est l'équivalent, adapté à notre architecture, du *fencing token* : c'est **l'écriture** qui vérifie le droit d'écrire, et non une supposition du code appelant.

Testé par `Lost_lock_is_detected_when_its_session_is_killed` : on prend le verrou, une autre session fait `KILL` sur celle qui le détient, la perte est détectée, et le verrou redevient disponible pour une autre instance.

## 6. Observer et expérimenter

```sql
-- Verrous applicatifs actifs
SELECT request_session_id, resource_description, request_mode, request_status
FROM sys.dm_tran_locks WHERE resource_type = 'APPLICATION';
```

Expérience dans deux fenêtres SSMS sur la base `BeanStock` :

```sql
-- Fenêtre 1 : prendre le verrou et le garder
EXEC sp_getapplock @Resource = 'demo', @LockMode = 'Exclusive', @LockOwner = 'Session';

-- Fenêtre 2 : tenter de le prendre sans attendre → -1
DECLARE @r int;
EXEC @r = sp_getapplock @Resource = 'demo', @LockMode = 'Exclusive', @LockOwner = 'Session', @LockTimeout = 0;
SELECT @r;

-- Fenêtre 1 : libérer (ou fermer la fenêtre pour simuler un crash) → la fenêtre 2 peut l'obtenir
EXEC sp_releaseapplock @Resource = 'demo', @LockOwner = 'Session';
```

Avec le vrai nom `BeanStock.CatalogSynchronization`, la fenêtre 1 bloque l'application : `POST /admin/catalog/sync` répond `409` tant qu'elle détient le verrou (voir [`tests-manuels-synchronisation.md`](tests-manuels-synchronisation.md) §4).

## 7. Alternatives

| Option | À retenir |
|---|---|
| Ligne « verrou » dans une table, avec date d'expiration | Il faut gérer l'expiration. Trop courte, deux exécutions se chevauchent ; trop longue, un crash bloque tout jusqu'à l'échéance |
| Redis (Redlock) | Un composant de plus. Sûreté contestée par Kleppmann : pas de jeton de *fencing*, dépendance aux horloges |
| Bail sur un blob Azure | Natif dans Azure ; c'est ce qu'utilisent les déclencheurs minuterie d'Azure Functions pour n'exécuter qu'une instance |
| ZooKeeper, etcd | Conçus pour la coordination, avec des jetons croissants. Disproportionnés ici |
| Bibliothèque `DistributedLock.SqlServer` (MIT) | Même mécanisme, mieux outillé (surveillance de la perte de connexion). Le choix le plus défendable si le besoin de verrous se généralise |

## 8. Questions probables en entretien

- **« Que se passe-t-il si le Worker plante pendant la synchronisation ? »** La session SQL se ferme : le verrou est libéré et la transaction annulée. L'échéance reste dépassée, donc une autre instance resynchronise à la vérification suivante.
- **« Deux instances lancent la synchronisation en même temps ? »** Une seule obtient le verrou ; l'autre reçoit `-1`, ce qui donne un `409` côté API, ou un « passe son tour » côté Worker.
- **« Pourquoi pas un verrou de transaction ? »** Il doit couvrir l'appel HTTP au fournisseur, et on ne garde pas une transaction ouverte pendant un appel réseau.
- **« Le verrou bloque-t-il les paniers ? »** Le verrou applicatif, non. La transaction de synchronisation peut faire attendre un ajout au panier sur un produit qu'elle modifie, à cause de la vérification de clé étrangère.
- **« Et si la connexion tombe pendant la synchronisation ? »** Le verrou est perdu, mais rien ne peut être validé sans lui : la vérification `APPLOCK_MODE` avant le commit, sur la session qui porte la transaction, joue le rôle de *fencing*.
- **« Pourquoi pas Redis ? »** Pas de composant supplémentaire, libération automatique liée à la session, et le verrou vit à côté des données qu'il protège, ce qui rend le contrôle avant commit possible.

## Sources

- [sp_getapplock (Transact-SQL)](https://learn.microsoft.com/sql/relational-databases/system-stored-procedures/sp-getapplock-transact-sql) — Microsoft Learn : arguments, codes retour, portée par base de données.
- [sp_releaseapplock (Transact-SQL)](https://learn.microsoft.com/en-us/sql/relational-databases/system-stored-procedures/sp-releaseapplock-transact-sql) — Microsoft Learn : libération, nom haché en interne.
- [sp_getapplock, documentation SQL Server 2005](https://learn.microsoft.com/id-id/previous-versions/sql/sql-server-2005/ms189823(v=sql.90)) — libération à la fin de la transaction ou de la session, appels multiples.
- [APPLOCK_MODE (Transact-SQL)](https://learn.microsoft.com/id-id/previous-versions/sql/sql-server-2005/ms189749(v=sql.90)) — Microsoft Learn : lecture du mode détenu par un propriétaire.
- [An overview of sp_getapplock and sp_releaseapplock](https://www.sqlshack.com/an-overview-of-sp_getapplock-and-sp_releaseapplock-stored-procedures) — SQLShack : exemples avec propriétaire `Session`, libération sur `KILL`.
- [Foreign Keys, Blocking, and Update Conflicts](https://sqlperformance.com/2021/06/sql-performance/foreign-keys-blocking-update-conflicts) — Paul White, SQLPerformance : la vérification de clé étrangère prend des verrous même sous RCSI.
- [SQL Server Foreign Key Locking](https://vladmihalcea.com/sql-server-foreign-key-locking) — Vlad Mihalcea : démonstration du blocage sous RCSI.
- [How to do distributed locking](https://martin.kleppmann.com/2016/02/08/how-to-do-distributed-locking.html) — Martin Kleppmann (2016) : *fencing tokens*, efficacité contre correction, critique de Redlock.
- [Is Redlock safe?](https://www.antirez.com/news/101) — réponse de l'auteur de Redis à Kleppmann.
