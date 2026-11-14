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
- Les écritures concurrentes passent par des méthodes de repository dédiées (`ExecuteUpdateAsync` conditionnel), jamais par une specification.
- Toute commande d'écriture sur un panier commence par l'incrément de `Carts.Version` dans sa transaction (verrou de ligne). Ne pas contourner ce mécanisme.
- Les erreurs métier attendues sont retournées via `ErrorOr`, pas levées en exception. Les exceptions sont réservées à l'inattendu.
- Toute réponse d'erreur HTTP est un ProblemDetails.

## Invariants à ne jamais casser

1. Un utilisateur = au plus un panier, même sous appels parallèles.
2. Une demande rejetée ne modifie rien (ni ligne, ni version).
3. Chaque modification acceptée incrémente la version du panier d'exactement 1.
4. Modifications concurrentes de lignes différentes → toutes deux réussissent.
5. Ligne modifiée depuis la version fournie → conflit explicite, jamais d'écrasement.
6. Une ligne supprimée ne réapparaît jamais.
7. La synchronisation est exclusive entre instances et tout-ou-rien.

Tout changement touchant ces points doit être accompagné d'un test d'intégration sur SQL Server réel.

## Sécurité du dépôt (public, GitHub)

- **Aucun secret** : pas de mot de passe, chaîne de connexion réelle, jeton ou URL de feed privé. Utiliser `.env` (ignoré) et `.env.example`.
- Ne jamais ajouter de source NuGet privée dans `NuGet.config`.

## Documentation

- Tenir la documentation à jour **dans le même changement** que le code : `README.md`, `DECISIONS.md` (2 pages max), `documentation/`.
- Documenter ce qui aide à comprendre ou à exploiter — pas de paraphrase du code.

## Langue

Code, noms et commentaires techniques en anglais ; documentation en français.
