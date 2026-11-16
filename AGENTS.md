# AGENTS.md — Instructions pour les agents IA travaillant sur BeanStock

## Projet

API e-commerce de démonstration (test technique) : catalogue synchronisé depuis DummyJSON + panier personnel, **cohérent en multi-instance**. L'énoncé de référence fait foi ; en cas de doute sur une règle métier, s'y reporter plutôt que supposer.

## Stack

- C# / **.NET 10**, **SQL Server**, **EF Core**
- Clean Architecture, 7 projets : `Domain` → `Application.Abstractions` (ports) → `Application` (cas d'usage) ; `Infrastructure.Persistence` et `Infrastructure.DummyJson` ne dépendent que des ports ; points d'entrée `Api` (Minimal APIs) et `Worker`
- `Mediator.SourceGenerator` uniquement dans les hôtes (`Api`, `Worker`) ; les handlers appelés par un hôte sont `public`
- `Mediator` (martinothamar, source-generated) — **ne pas introduire MediatR**
- `Ardalis.Specification` pour les **lectures uniquement**
- `ErrorOr` pour les erreurs métier ; `FluentValidation` pour les entrées
- Tests : xUnit, Testcontainers (SQL Server), Shouldly/AwesomeAssertions — **ne pas introduire FluentAssertions**
- Versions NuGet centralisées dans `Directory.Packages.props` ; source unique nuget.org via `NuGet.config`

## Règles d'architecture

- `Domain` ne référence aucun package d'infrastructure (pas d'EF, pas d'ASP.NET).
- Les règles métier du panier (quantité 1–999, unicité produit, prix/nom figés au premier ajout, versions) vivent dans l'agrégat `Cart`.
- Les écritures passent par des méthodes de repository dédiées, jamais par une specification (réservées aux lectures).
- Toute modification du contenu d'un panier passe par `ICartRepository.ChangeAsync` : l'implémentation ouvre la transaction, verrouille la ligne du panier (`UPDLOCK`), le charge, applique la modification et n'enregistre que si elle est acceptée. Le port n'expose volontairement pas ces étapes séparément : ne pas réintroduire de `GetForUpdate`/`Save` ni de service applicatif qui les assemble. Les jetons de concurrence (`Version`) ne sont qu'un filet de sécurité. Voir `documentation/panier.md`.
- L'utilisateur courant vient du `ClaimsPrincipal` (en-tête `X-User-Id`) ; aucune route n'expose d'identifiant de panier.
- Les erreurs métier attendues sont retournées via `ErrorOr`, pas levées en exception. Les exceptions sont réservées à l'inattendu.
- Toute réponse d'erreur HTTP est un ProblemDetails.
- Les hôtes (`Api`, `Worker`) ne contiennent que des déclencheurs : ils envoient des commandes/requêtes via `ISender`, jamais d'appel direct à un repository ou au fournisseur. La synchronisation est **un seul** cas d'usage (`SynchronizeCatalogCommand`), partagé par le Worker et `POST /admin/catalog/sync`.
- Les délais et seuils sont des options validées au démarrage (`ValidateOnStart`), jamais des constantes en dur dans un hôte. Le temps passe par `TimeProvider` (testable avec `FakeTimeProvider`).
- Les tests n'appellent jamais DummyJSON : fournisseur simulé (`FakeCatalogProvider`, `HttpMessageHandler` de test).

## Invariants à ne jamais casser

1. Un utilisateur = au plus un panier, même sous appels parallèles.
2. Une demande rejetée ne modifie rien (ni ligne, ni version).
3. Chaque modification acceptée incrémente la version du panier d'exactement 1.
4. Modifications concurrentes de lignes différentes → toutes deux réussissent.
5. Ligne modifiée depuis la version fournie → conflit explicite, jamais d'écrasement.
6. Une ligne supprimée ne réapparaît jamais.
7. La synchronisation est exclusive entre instances et tout-ou-rien.
8. Une synchronisation planifiée ne s'exécute qu'une fois par intervalle, toutes instances confondues (échéance vérifiée sous le verrou distribué).
9. Un produit n'est jamais supprimé : disparu chez le fournisseur → désactivé.

Tout changement touchant ces points doit être accompagné d'un test d'intégration sur SQL Server réel.

## Sécurité du dépôt (public, GitHub)

- **Aucun secret** : pas de mot de passe, chaîne de connexion réelle, jeton ou URL de feed privé. Utiliser `.env` (ignoré) et `.env.example`.
- Ne jamais ajouter de source NuGet privée dans `NuGet.config`.

## Documentation

- Tenir la documentation à jour **dans le même changement** que le code : `README.md`, `DECISIONS.md` (2 pages max), `documentation/`.
- Documenter ce qui aide à comprendre ou à exploiter — pas de paraphrase du code.
- Tout endpoint ajouté ou modifié est reflété dans la collection Bruno (`api/`, format YAML OpenCollection), dans le même changement. La collection Bruno est la source : `api/BeanStock-postman.json` en est un export (Bruno → *Share* → *Export* → Postman), à régénérer quand les requêtes changent, en gardant les valeurs par défaut non sensibles de ses variables.
- Tout changement de comportement observable est reflété dans le mode opératoire de test manuel concerné (`documentation/tests-manuels-*.md`).
- Les environnements Bruno (`api/environments/`) ne contiennent que des valeurs non sensibles (URL locales…) ; jamais de jeton ni de mot de passe.
- Le démarrage local doit rester possible avec le conteneur SQL Server fourni **ou** une instance SQL Server installée ; Docker n'est obligatoire que pour les tests d'intégration (Testcontainers).
- Les fichiers de configuration et de documentation à la racine sont référencés dans le dossier de solution `/items` du `.slnx` (sous-dossiers `docker`, `documentation`).

## Langue

Code, noms et commentaires techniques en anglais ; documentation en français.
