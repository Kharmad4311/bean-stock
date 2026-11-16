# Panier personnel

Ce document décrit le contrat HTTP du panier, le protocole qui garantit sa cohérence quand plusieurs requêtes ou instances agissent en même temps, et les tests qui le démontrent. Les choix et compromis sont résumés dans [`DECISIONS.md`](../DECISIONS.md) (section 4) ; le mode opératoire de test manuel est dans [`tests-manuels-panier.md`](tests-manuels-panier.md).

## Identification de l'utilisateur

L'authentification est hors périmètre. L'utilisateur courant est déclaré dans l'en-tête **`X-User-Id`** (1 à 64 caractères parmi lettres, chiffres, `.`, `_`, `-`, `@`). Un handler d'authentification ASP.NET Core le transforme en `ClaimsPrincipal` ; les endpoints et les cas d'usage ne voient que ce principal. Passer à un vrai schéma (JWT…) ne change que l'enregistrement de l'authentification.

En-tête absent ou invalide → `401 Unauthorized` (ProblemDetails expliquant le format attendu).

## Contrat HTTP

Toutes les routes sont sous `/me/cart` : elles ne portent **jamais** d'identifiant de panier. Un utilisateur ne peut donc atteindre que son propre panier ; une ligne d'un autre panier est simplement introuvable (`404`).

| Opération | Requête | Succès |
|---|---|---|
| Créer / récupérer | `POST /me/cart` | `201` si l'appel a créé le panier, `200` s'il existait |
| Consulter | `GET /me/cart` | `200` |
| Ajouter un article | `POST /me/cart/lines` `{ "productId": "…", "quantity": 2 }` | `200` |
| Modifier une quantité | `PUT /me/cart/lines/{lineId}` `{ "quantity": 5, "version": 0 }` | `200` |
| Supprimer une ligne | `DELETE /me/cart/lines/{lineId}?version=1` | `200` |

Chaque succès renvoie **le panier complet** avec ses nouvelles versions, pour que le client n'ait pas à le relire :

```json
{
  "version": 3,
  "total": 33.00,
  "lines": [
    { "lineId": "…", "productId": "…", "productName": "Arabica", "unitPrice": 12.50,
      "quantity": 2, "subtotal": 25.00, "version": 1 }
  ],
  "createdAt": "…",
  "updatedAt": "…"
}
```

- `productId` est l'identifiant **local** du produit (champ `id` de `GET /products`).
- `productName` et `unitPrice` sont figés au premier ajout ; `total` est calculé à partir de ces valeurs (règle 6).
- La suppression renvoie le panier plutôt qu'un `204` : le client récupère la nouvelle version globale.
- Le `DELETE` porte la version en paramètre de requête, un corps de requête sur `DELETE` étant mal supporté par les clients et proxys.

### Erreurs

Toutes les erreurs sont des ProblemDetails (RFC 9457). Les erreurs métier portent un code stable dans l'extension `code`, et leurs détails en extensions.

| Statut | `code` | Cas |
|---|---|---|
| `400` | `Cart.QuantityOutOfRange` | Quantité hors de 1–999 (extensions `min`, `max`) |
| `400` | `Cart.QuantityLimitExceeded` | L'ajout porterait la ligne au-delà de 999 (`currentQuantity`, `requestedQuantity`) |
| `400` | `Cart.ProductNotFound` | Produit inconnu du catalogue local |
| `400` | `Cart.ProductUnavailable` | Produit retiré du catalogue (désactivé) |
| `400` | *(nom du champ)* | Requête invalide : version absente, identifiant vide |
| `401` | — | En-tête `X-User-Id` absent ou invalide |
| `404` | `Cart.NotFound` | L'utilisateur n'a pas encore de panier (le créer d'abord) |
| `404` | `Cart.LineNotFound` | Ligne absente de **son** panier (supprimée, ou d'un autre utilisateur) |
| `409` | `Cart.LineVersionConflict` | La ligne a changé depuis la version fournie (`expectedVersion`, `currentVersion`) |
| `409` | `Cart.ConcurrentUpdate` | Filet de sécurité, ne devrait pas se produire (voir plus bas) |

Sur un `409 Cart.LineVersionConflict`, le client relit le panier, présente l'état actuel à l'utilisateur et renvoie sa demande avec la nouvelle version s'il la maintient. Le serveur ne fusionne jamais à sa place.

**Pourquoi le corps et `409` plutôt que `If-Match` et `412` :** `If-Match` (requête conditionnelle HTTP) est pensé pour une version par ressource, transmise dans l'en-tête `ETag` de la réponse. Ici, la consultation renvoie toutes les lignes en une fois : leurs versions sont forcément dans le corps, et le client les renvoie au même endroit. `412 Precondition Failed` signale l'échec d'une précondition d'en-tête ; `409 Conflict` signale un conflit avec l'état actuel de la ressource, ce qui correspond au « conflit explicite » demandé.

## Garanties de cohérence

### Création idempotente (règle 1)

Le cas d'usage lit le panier ; s'il n'existe pas, il l'insère. Deux appels parallèles peuvent tous deux ne rien trouver : c'est l'**index unique sur `Carts.UserId`** qui tranche. Une seule insertion réussit ; l'autre reçoit une violation d'unicité (erreurs SQL 2601/2627), relit et renvoie le panier gagnant. Aucun verrou applicatif, aucune course possible : la garantie est portée par la base, donc valable sur toutes les instances.

### Protocole d'écriture (règles 5, 7, 8, 9, 10, 12)

Ajout, modification et suppression passent tous par **une seule opération** du port, `ICartRepository.ChangeAsync(userId, modification)`. Le handler fournit la modification à appliquer à l'agrégat ; l'implémentation (`EfCartRepository`) se charge de tout le reste. Le port n'expose volontairement pas les étapes séparément (charger pour modification, enregistrer, valider) : aucune commande ne peut les assembler dans le mauvais ordre ou oublier la transaction, et l'Application ignore tout du verrou SQL Server.

1. **Transaction**, puis verrou sur la ligne du panier :
   ```sql
   SELECT [Id] FROM [Carts] WITH (UPDLOCK, ROWLOCK) WHERE [UserId] = @userId
   ```
2. **Chargement** du panier et de ses lignes, après l'obtention du verrou.
3. **Application** de la demande à l'agrégat `Cart`, qui vérifie les règles et fait évoluer les versions.
4. Demande **rejetée** → aucune écriture, la transaction est annulée à sa libération : ni ligne ni version ne bougent.
5. Demande **acceptée** → enregistrement puis validation de la transaction, qui libère le verrou.

**Pourquoi ça marche.** Un verrou de mise à jour (`U`) est compatible avec les lectures, mais pas avec un autre verrou `U`. Deux requêtes qui modifient le même panier, sur deux instances différentes, passent donc l'une après l'autre : la seconde attend à l'étape 1 que la première ait validé. Elle charge ensuite le panier **déjà modifié** :

- même ligne, même version lue → la seconde voit la version incrémentée et reçoit un `409` ; au plus une modification est acceptée ;
- lignes différentes → la version de sa ligne n'a pas changé, elle réussit aussi ; la version globale passe de 4 à 6 (exemple de l'énoncé) ;
- ajouts simultanés du même produit → chacun part de la quantité laissée par le précédent, aucune quantité acceptée n'est perdue, et le plafond de 999 est vérifié sur la vraie valeur ;
- suppression puis modification obsolète → la ligne n'existe plus (`404`), et une modification ne crée jamais de ligne : pas de résurrection.

**Subtilité avec `READ_COMMITTED_SNAPSHOT`.** La base lit en instantané (RCSI) : une lecture normale ne pose pas de verrou et voit les données validées **au début de l'instruction**. Le chargement de l'étape 2 est une nouvelle instruction, exécutée après l'obtention du verrou ; l'écrivain précédent a donc déjà validé, et ses changements sont visibles. Le verrou lui-même est une lecture avec indicateur de verrouillage, qui lit toujours la dernière version validée. Les consultations (`GET /me/cart`) ne prennent aucun verrou : elles ne sont jamais bloquées par un écrivain, et voient le panier et ses lignes dans un même instantané.

**Version globale.** Elle est incrémentée par l'agrégat, une fois par demande acceptée. Une modification qui remet la même quantité est acceptée mais n'est pas une modification effective : aucune version ne bouge.

### Filet de sécurité : jetons de concurrence

Les colonnes `Carts.Version` et `CartLines.Version` sont des **jetons de concurrence** EF Core : chaque `UPDATE` ou `DELETE` porte `WHERE Version = @versionLue`. Tant que le protocole est respecté, la condition est toujours vraie. Si un futur code écrivait un panier sans prendre le verrou, l'écriture concurrente serait détectée (`409 Cart.ConcurrentUpdate`) au lieu d'écraser silencieusement l'autre. L'index unique `(CartId, ProductId)` joue le même rôle pour l'unicité d'un produit dans un panier, et la contrainte `CK_CartLines_Quantity` pour la plage 1–999.

### Alternatives écartées

| Option | Pourquoi pas |
|---|---|
| Incrémenter `Carts.Version` en tête de transaction (idée de départ) | Pose bien le verrou, mais fait monter la version pour une demande sans effet et double l'incrément de l'agrégat |
| Tout optimiste (jetons de concurrence seuls) | Deux modifications de lignes **différentes** se disputent la version globale : la seconde échouerait (conflit injustifié, règle 9) ou devrait réessayer en boucle |
| `sp_getapplock` par panier | Fonctionne, mais un verrou nommé est un mécanisme de plus à expliquer ; le verrou de ligne protège exactement la donnée concernée |
| Service applicatif public assemblant « charger pour modification » et « enregistrer » (première version) | Rendu public par la contrainte du générateur Mediator, il restait accessible depuis l'Api ; surtout, rien n'empêchait à la compilation d'appeler les étapes sans transaction ou d'oublier le commit |
| Niveau `SERIALIZABLE` | Verrous de plage plus larges, interblocages fréquents, pour aucun gain ici |
| Verrou distribué externe (Redis…) | Composant supplémentaire, et une garantie qui ne serait plus dans la même transaction que les données |

**Compromis assumé :** les écritures d'un même panier sont sérialisées. Un panier n'appartient qu'à un utilisateur, la contention reste négligeable ; les paniers différents ne se bloquent jamais entre eux.

### Produits retirés du catalogue (règle 11)

Un produit disparu chez le fournisseur est désactivé, jamais supprimé (clé étrangère en `Restrict`). Les lignes existantes restent lisibles avec leurs nom et prix figés ; un nouvel ajout est refusé (`Cart.ProductUnavailable`). Le produit est relu dans la transaction de l'ajout : l'état pris en compte est celui du moment de l'ajout. Une synchronisation qui le désactive juste après n'invalide pas la ligne déjà ajoutée, ce qui est conforme à la règle 11.

## Tests

Les règles sont vérifiées à trois niveaux :

- **Domaine** (`BeanStock.Domain.UnitTests/Carts`) : règles de l'agrégat sans base (quantités, cumul, versions, conflit, absence de résurrection).
- **Application** (`BeanStock.Application.UnitTests/Carts`) : traduction des commandes en modifications de l'agrégat, validations des entrées (version de ligne obligatoire…).
- **Intégration** (`BeanStock.IntegrationTests/Carts`, SQL Server réel via Testcontainers) :
  - `CartTests` : contrat HTTP et règles métier de bout en bout, isolation entre utilisateurs, échec en cours d'enregistrement (panier inchangé, la même demande réessayée passe) ;
  - `CartConcurrencyTests` : requêtes parallèles réparties sur **deux instances de l'application** (deux hôtes distincts partageant la base) : création unique, ajouts parallèles sans perte, plafond de 999 respecté sous concurrence, exemple de l'énoncé (4 → 6), une seule modification acceptée pour une même ligne, suppression et modification simultanées.

| Règle de l'énoncé | Tests d'intégration |
|---|---|
| 1. Pas de panier en double | `Parallel_creations_create_a_single_cart`, `Create_returns_201_…_then_200_…` |
| 2. Panier personnel | `A_user_can_neither_read_nor_change_the_cart_of_another_user` |
| 3. Produit existant et actif | `Adding_an_unknown_product_is_rejected`, `Product_removed_from_the_catalog_…` |
| 4. Quantité 1–999 | `Rejected_addition_leaves_the_cart_unchanged`, `Parallel_additions_beyond_the_limit_…` |
| 5. Un produit = une ligne, cumul | `Adding_a_product_already_in_the_cart_…`, `Parallel_additions_of_the_same_product_…` |
| 6. Nom et prix figés | `Catalog_changes_do_not_alter_the_lines_already_in_the_cart` |
| 7. Version globale +1 | assertions de version dans tous les tests |
| 8. Version de ligne, conflit | `Changing_a_line_from_an_outdated_version_returns_409_…`, `Parallel_changes_of_the_same_line_…` |
| 9. Lignes différentes en parallèle | `Changes_of_two_different_lines_from_the_same_read_both_succeed` |
| 10. Pas de résurrection | `Removed_line_does_not_come_back_…`, `Parallel_removal_and_change_…` |
| 11. Produit retiré consultable | `Product_removed_from_the_catalog_stays_readable_…` |
| 12. Cohérence en cas d'échec | `Failure_while_saving_a_change_leaves_lines_and_versions_unchanged` |
