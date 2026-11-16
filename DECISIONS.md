# DECISIONS — BeanStock

> Limite : 2 pages. Le détail vit dans `documentation/`.

## 1. Contexte

BeanStock est la **marque** du service ; le catalogue est celui de DummyJSON, synchronisé intégralement (aucun filtrage). Le service expose un catalogue paginé et un panier personnel, et reste cohérent avec **plusieurs instances** de l'API et du Worker.

## 2. Architecture

**Clean Architecture**, sept projets (`src/`, préfixe `BeanStock.`) :

| Projet | Rôle | Dépend de |
|---|---|---|
| `Domain` | Agrégat `Cart` / `CartLine`, `Product`, règles et erreurs métier | — |
| `Application.Abstractions` | Ports implémentés par l'infrastructure (`ICartRepository`, `ICatalogProvider`…) | Domain |
| `Application` | Commandes, requêtes, handlers, specifications, validators, behaviors | Application.Abstractions |
| `Infrastructure.Persistence` | EF Core / SQL Server, migrations, repositories, verrous | Application.Abstractions |
| `Infrastructure.DummyJson` | Client HTTP du fournisseur (pagination, résilience) | Application.Abstractions |
| `Api` · `Worker` | Hôtes : Minimal APIs, identification · déclencheurs de synchronisation | Application + infrastructure (composition) |

*Pourquoi :* règles métier testables sans base ni HTTP, infrastructure remplaçable (base, fournisseur, ordonnanceur). Les hôtes ne font que composer et envoyer des commandes. *Coût :* plus de projets et d'indirections qu'une API monolithique, accepté pour un produit destiné à évoluer.

## 3. Choix techniques

- **CQRS léger** : lectures (catalogue, consultation du panier) et écritures (versions, transactions, verrous) ont des besoins différents ; une seule base et un seul modèle EF, l'énoncé imposant une base unique.
- **`Mediator`** (martinothamar, MIT) plutôt que MediatR, passé en double licence RPL / commerciale avec la v13 (juillet 2025). API quasi identique, code **généré à la compilation** : un handler manquant est une erreur de build. *Wolverine écarté* : plus lourd, et son middleware transactionnel masquerait le contrôle explicite des transactions, central ici.
- **EF Core** (mapping, migrations, jetons de concurrence), avec du SQL explicite là où il porte une garantie (verrous).
- **Ardalis.Specification** pour les lectures uniquement ; les écritures passent par des repositories dont les méthodes portent la garantie de concurrence.
- **ErrorOr** : erreurs métier attendues retournées comme valeurs, avec un code stable, traduites en ProblemDetails (RFC 9457). **FluentValidation** dans un behavior.
- **Tests** : xUnit, Testcontainers SQL Server (verrous et index uniques exigent un vrai moteur), Shouldly (FluentAssertions est devenu commercial).

## 4. Panier : concurrence et cohérence

Détail, contrat HTTP et règles ↔ tests : [`documentation/panier.md`](documentation/panier.md).

- **Création idempotente** : index unique sur `Carts.UserId` ; en cas de violation, relecture du panier gagnant (`201` à la création, `200` sinon).
- **Écritures sérialisées par panier** : une seule opération du port, `ICartRepository.ChangeAsync(userId, modification)`. L'implémentation ouvre une transaction, prend `SELECT … WITH (UPDLOCK, ROWLOCK)` sur le panier, le charge **après** le verrou, applique la modification à l'agrégat, puis n'enregistre que si elle est acceptée. Les verrous de mise à jour étant incompatibles entre eux, un second écrivain du même panier attend, quelle que soit l'instance ; les lectures (instantané RCSI) ne sont jamais bloquées. L'Application ne voit pas les étapes : impossible d'oublier la transaction ou le commit.
- **Versions** : la version globale est incrémentée par l'agrégat, une fois par modification effective. La version de ligne envoyée par le client est comparée à celle relue sous verrou → `409 Cart.LineVersionConflict`. Lignes différentes : les deux réussissent (règle 9) ; même ligne, même version : une seule.
- **Filet de sécurité** : versions déclarées jetons de concurrence EF (`WHERE Version = @lue`) ; une écriture qui contournerait le verrou serait détectée, jamais silencieuse.
- **Suppression physique** : une modification obsolète ne trouve plus la ligne (`404`) et ne peut pas la recréer.
- **Version dans le corps + `409`** plutôt que `If-Match` + `412` : la consultation renvoie toutes les lignes, leurs versions sont dans le corps.

*Écartés :* incrément de version en tête de transaction (première idée : la version montait même sans effet) ; tout-optimiste (conflits injustifiés entre lignes différentes) ; un service applicatif assemblant les étapes (première version, public à cause du générateur Mediator et mal utilisable). *Compromis :* les écritures d'un même panier sont sérialisées, acceptable pour un panier personnel.

## 5. Synchronisation du catalogue

Détail : [`documentation/synchronisation.md`](documentation/synchronisation.md) et [`documentation/verrou-distribue.md`](documentation/verrou-distribue.md).

- **Un cas d'usage, deux déclencheurs** : `SynchronizeCatalogCommand`, envoyée par le Worker et par `POST /admin/catalog/sync` (synchrone, renvoie le rapport). Changer d'ordonnanceur = un autre déclencheur pour la même commande.
- **Exclusivité multi-instance** : `sp_getapplock` sans attente, porté par la session de la transaction, sur toute l'exécution. Avant le commit, vérification que la session détient toujours le verrou (*fencing*). Déjà pris → `409`.
- **Planification partagée** : chaque Worker vérifie chaque minute, sous le verrou, si la dernière synchronisation **réussie** (`CatalogSyncRuns`) date de plus d'un jour. Une seule exécution par période, rattrapage automatique, délais modifiables à chaud. *Écarté :* un minuteur par instance.
- **Tout ou rien** : catalogue récupéré entièrement, puis écrit par lots dans **une** transaction ; un échec annule tout. `READ_COMMITTED_SNAPSHOT` : la consultation n'attend jamais. *Écarté :* `MERGE` (moins testable, pièges de concurrence).
- **Données** : produit invalide ignoré et journalisé ; produit disparu désactivé, jamais supprimé (lisible dans les paniers) ; catalogue vide ou inférieur à 50 % des actifs refusé (`502`). Chaque exécution est tracée.

## 6. Identification de l'utilisateur

Handler d'authentification lisant l'en-tête `X-User-Id` et produisant un `ClaimsPrincipal` ; absent ou invalide → `401`. Routes en `/me/cart` : aucun identifiant de panier exposé. Remplaçable par JWT sans toucher aux cas d'usage.

## 7. Limites et évolutions

- Pas d'authentification réelle ; endpoint de synchronisation manuelle non protégé.
- Synchronisation en mémoire et en transaction unique, adaptée jusqu'à quelques centaines de milliers de produits ; au-delà, staging en flux et fusion ensembliste.

## 8. Livraison

- **Images** : un Dockerfile par hôte, non-root. Les migrations passent par un **bundle EF Core** (conteneur `migrator`) exécuté une fois avant l'API et les Workers : aucune instance ne modifie le schéma. `compose.yaml` lance SQL Server seul ou, avec le profil `app`, toute la pile et deux Workers.
- **CI GitHub Actions** (là où vit le dépôt, visible sur chaque PR) : build, tests Testcontainers et couverture sur chaque PR ; images en plus sur `main`. *Écarté :* Azure Pipelines, invisible pour les relecteurs. Ni registre ni déploiement : hors périmètre ; compte `sa` en local.
