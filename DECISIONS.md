# DECISIONS — BeanStock

> Version de travail. Limite imposée : 2 pages. Le détail vit dans `documentation/`.
> Les points marqués **[À TRANCHER]** sont encore ouverts.

## 1. Contexte et périmètre

BeanStock est la **marque** du service ; le catalogue est celui de DummyJSON, synchronisé intégralement comme l'exige l'énoncé (aucun filtrage « café »). Le service expose un catalogue paginé et un panier personnel, et doit rester cohérent avec **plusieurs instances** de l'API et du worker en parallèle.

## 2. Architecture

**Clean Architecture**, sept projets (`src/`, préfixe `BeanStock.`) :

| Projet | Rôle | Dépend de |
|---|---|---|
| `Domain` | Agrégat `Cart` / `CartLine`, `Product`, règles métier, erreurs métier | — |
| `Application.Abstractions` | Ports implémentés par l'infrastructure (`IReadRepository<T>`, `ICatalogProvider`…) | Domain |
| `Application` | Commandes / requêtes, handlers, specifications, validators, behaviors | Application.Abstractions |
| `Infrastructure.Persistence` | EF Core / SQL Server, migrations, repositories, verrou `sp_getapplock` | Application.Abstractions |
| `Infrastructure.DummyJson` | Client HTTP du fournisseur (pagination, résilience) | Application.Abstractions |
| `Api` | Minimal APIs, identification utilisateur, ProblemDetails | Application, Persistence (composition) |
| `Worker` | Déclencheurs de synchronisation | Application, Persistence, DummyJson (composition) |

*Pourquoi :* les règles métier sont testables sans base ni HTTP, et l'infrastructure (base, fournisseur, ordonnanceur) est remplaçable. Séparer les ports des cas d'usage permet à l'infrastructure de ne dépendre que des contrats ; découper l'infrastructure par technologie garde chaque projet petit et limite les dépendances de chaque hôte (l'API n'embarque pas le client DummyJSON). *Coût :* plus de projets et d'indirections qu'une API monolithique ; accepté car le produit est destiné à évoluer.

## 3. CQRS léger avec Mediator

- **Pourquoi CQRS :** les lectures (catalogue paginé, consultation du panier) et les écritures (concurrence, versions, transactions) ont des besoins très différents. Les séparer garde les handlers d'écriture focalisés sur les invariants. *Léger* : une seule base, un seul modèle EF — pas de base de lecture séparée (l'énoncé impose une base unique, et rien ne le justifie à ce stade).
- **Pourquoi un médiateur :** découplage endpoint ↔ cas d'usage et **pipeline behaviors** transverses (validation, logging, transaction) écrits une seule fois.
- **Pourquoi `Mediator` (martinothamar) et pas MediatR :** MediatR est passé en double licence RPL-1.5 / commerciale à partir de la v13 (juillet 2025). `Mediator` est sous **MIT**, offre une API quasi identique (migration triviale dans un sens comme dans l'autre), et **génère le code à la compilation** : un handler manquant est une erreur de build, pas une exception en production.
- **Pourquoi pas Wolverine :** MIT et capable de messaging (RabbitMQ…), mais plus lourd à apprendre et son middleware transactionnel masquerait notre contrôle explicite des transactions, central dans ce sujet. Un besoin de messaging futur se placera derrière une abstraction d'infrastructure.

## 4. Autres choix techniques

- **EF Core** : mapping, migrations, `ExecuteUpdateAsync` pour les mises à jour atomiques conditionnelles.
- **Ardalis.Specification** (MIT) : requêtes de **lecture** réutilisables et testables. Les écritures concurrentes n'utilisent volontairement **pas** de specification (SQL conditionnel dédié dans le repository).
- **ErrorOr** (MIT) : les erreurs métier attendues sont des valeurs retournées, pas des exceptions. Chaque erreur a un code stable et des métadonnées, traduites en ProblemDetails (RFC 9457).
- **FluentValidation** : validation des entrées dans un behavior.
- **Minimal APIs**, groupées par module.
- **Tests :** xUnit, Testcontainers SQL Server (un vrai moteur est indispensable pour tester verrous et index uniques), Shouldly/AwesomeAssertions (FluentAssertions est devenu commercial).

## 5. Concurrence et cohérence

**Création de panier idempotente :** index unique sur `Carts.UserId`. INSERT ; en cas de violation d'unicité, relecture et retour du panier existant.

**Écritures sur un panier :** chaque commande s'exécute dans une transaction qui commence par `UPDATE Carts SET Version = Version + 1 WHERE Id = @id`. Ce verrou de ligne sérialise les écritures **d'un même panier**, sur toutes les instances, sans composant supplémentaire. Si la demande est rejetée → rollback, la version ne change pas (règle 7).

**Versions de ligne (verrouillage optimiste) :** `UPDATE CartLines … WHERE Id = @id AND Version = @expected`. Zéro ligne affectée → conflit. Deux modifications sur des lignes différentes réussissent toutes deux (règle 9) ; deux modifications de la même ligne depuis la même version → une seule réussit.

**Suppression :** physique ; une mise à jour n'insère jamais une ligne absente → pas de résurrection (règle 10).

**Ajout d'un produit déjà présent :** index unique `(CartId, ProductId)`, incrément de quantité contrôlé (≤ 999) dans la transaction.

*Compromis :* les écritures d'un même panier sont sérialisées. Acceptable : un panier appartient à un seul utilisateur, la contention est faible.

**[À TRANCHER]** Transport de la version de ligne : corps de requête + `409 Conflict`, ou en-tête `If-Match` + `412 Precondition Failed`.

## 6. Synchronisation du catalogue

- Logique dans `Application` (`SynchronizeCatalogCommand`) ; le `Worker` ne contient que des **déclencheurs** (V1 : `BackgroundService` + `PeriodicTimer`). Déclenchement manuel : `POST /admin/catalog/sync`, même commande.
- **Exclusivité multi-instance :** `sp_getapplock` (verrou distribué natif SQL Server).
- **Tout ou rien :** récupération complète en mémoire, puis `MERGE` sur `ExternalId` (unique) en une transaction. Un échec de récupération n'écrit rien.
- **Produits disparus :** désactivés (`IsActive = false`), jamais supprimés ; ils restent visibles dans les paniers mais ne peuvent plus être ajoutés. Garde-fou contre une réponse vide ou anormalement courte.
- **Évolution :** passer à Quartz, Hangfire ou Azure Functions = écrire un autre déclencheur qui envoie la même commande.

## 7. Identification de l'utilisateur

Handler d'authentification simple lisant l'en-tête `X-User-Id` et produisant un `ClaimsPrincipal`. Routes en `/me/cart` : aucun identifiant de panier exposé. Remplaçable par JWT sans toucher aux cas d'usage.

## 8. Limites et évolutions

- Pas d'authentification réelle (hors périmètre).
- Synchronisation en mémoire : adaptée à la taille du catalogue DummyJSON ; un catalogue volumineux demanderait une table de staging et un traitement par lots.
- **[À TRANCHER]** Moteur CI (fichiers YAML livrés dans `pipelines/`), Dockerfiles de livraison.
