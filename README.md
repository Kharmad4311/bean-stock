# BeanStock

API e-commerce de démonstration : un **catalogue de produits** synchronisé depuis un fournisseur externe et un **panier personnel** par utilisateur, conçue pour fonctionner de façon cohérente avec **plusieurs instances** en parallèle.

> Projet réalisé dans le cadre d'un test technique. Les contributions externes ne sont pas acceptées.

BeanStock est le nom de marque du service. Le catalogue provient de [DummyJSON](https://dummyjson.com/docs/products) et est synchronisé intégralement, sans filtrage.

## Statut

🚧 **En cours d'initialisation.** La structure du dépôt et les conventions sont en place ; le code applicatif arrive dans les prochaines itérations. Les sections marquées *(à venir)* seront complétées au fur et à mesure.

## Fonctionnalités

- **Catalogue** : synchronisation quotidienne automatique et déclenchement manuel, sans doublons, cohérente en cas d'échec et exclusive entre instances. Consultation paginée des produits locaux.
- **Panier** : un panier par utilisateur (création idempotente), ajout, modification et suppression de lignes, avec gestion explicite des modifications concurrentes grâce à une version globale et une version par ligne.

## Stack technique

| Domaine | Choix |
|---|---|
| Langage / runtime | C# · .NET 10 |
| Base de données | SQL Server · Entity Framework Core |
| Architecture | Clean Architecture · CQRS léger |
| Points d'entrée | API HTTP (Minimal APIs) · Worker de synchronisation |
| Bibliothèques clés | Mediator · Ardalis.Specification · ErrorOr · FluentValidation |
| Tests | xUnit · Testcontainers (SQL Server) |

Les raisons de ces choix, leurs compromis et leurs limites sont détaillés dans [`DECISIONS.md`](DECISIONS.md).

## Structure du dépôt

```
src/             Code source (Domain, Application, Infrastructure, Api, Worker)
tests/           Tests unitaires et d'intégration
documentation/   Documentation détaillée (architecture, concurrence, contrat d'erreurs HTTP…)
pipelines/       Pipelines CI en YAML
DECISIONS.md     Décisions d'architecture et compromis
AGENTS.md        Instructions fournies aux agents IA utilisés sur le projet
```

## Prérequis

- [SDK .NET 10](https://dotnet.microsoft.com/download/dotnet/10.0) (version fixée par `global.json`)
- [Docker](https://www.docker.com/) pour SQL Server en local et pour les tests d'intégration

## Démarrage local *(à venir)*

## Lancer les tests *(à venir)*

## Synchronisation manuelle du catalogue *(à venir)*

## Utilisation de l'API *(à venir)*

## Usage de l'IA

Ce projet est développé avec l'assistance d'outils d'IA. Les instructions qui encadrent les agents sont versionnées dans [`AGENTS.md`](AGENTS.md) ; le bilan d'utilisation (outils, vérifications, exemples de propositions retenues ou rejetées) sera documenté dans `documentation/`.
