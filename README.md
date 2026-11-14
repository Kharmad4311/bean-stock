# BeanStock

API e-commerce de démonstration : un **catalogue de produits** synchronisé depuis un fournisseur externe et un **panier personnel** par utilisateur, conçue pour fonctionner de façon cohérente avec **plusieurs instances** en parallèle.

> Projet réalisé dans le cadre d'un test technique. Les contributions externes ne sont pas acceptées.

BeanStock est le nom de marque du service. Le catalogue provient de [DummyJSON](https://dummyjson.com/docs/products) et est synchronisé intégralement, sans filtrage.

## Statut

🚧 **En cours de développement.** La solution démarre en local : base SQL Server, API (santé, catalogue paginé) et Worker. Le panier et la synchronisation du catalogue arrivent dans les prochaines itérations. Les sections marquées *(à venir)* seront complétées au fur et à mesure.

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
api/             Collection Bruno pour appeler l'API
documentation/   Documentation détaillée (à venir)
pipelines/       Pipelines CI en YAML (à venir)
compose.yaml     Dépendances locales (SQL Server)
DECISIONS.md     Décisions d'architecture et compromis
AGENTS.md        Instructions fournies aux agents IA utilisés sur le projet
```

## Prérequis

- [SDK .NET 10](https://dotnet.microsoft.com/download/dotnet/10.0) (version fixée par `global.json`)
- **SQL Server** : au choix, le conteneur fourni (Docker) ou une instance installée localement (Developer, Express…)
- [Docker](https://www.docker.com/) pour les tests d'intégration (Testcontainers) et, si on le souhaite, pour SQL Server en local
- Windows 11 : **Smart App Control désactivé** (Sécurité Windows → Contrôle des applications et du navigateur). Il bloque les assemblies non signées produites par chaque build, y compris lorsqu'elles sont chargées par `dotnet ef` ; aucune exclusion par dossier n'existe.
- [Bruno](https://www.usebruno.com/) (facultatif) pour appeler l'API avec la collection fournie

## Démarrage local

### 1. Démarrer SQL Server

**Option A — conteneur Docker fourni**

```bash
cp .env.example .env        # puis renseigner MSSQL_SA_PASSWORD dans .env
docker compose up -d        # démarre le conteneur beanstock-sqlserver
docker compose ps           # attendre l'état "healthy"
```

Si un SQL Server local occupe déjà le port 1433, changer `MSSQL_HOST_PORT` dans `.env` (par exemple `14333`) et l'utiliser dans la chaîne de connexion ci-dessous.

**Option B — instance SQL Server installée localement**

Aucune étape préalable : il suffit que le compte utilisé ait le droit de créer une base, les migrations créant `BeanStock` au premier lancement.

### 2. Configurer la chaîne de connexion

La chaîne de connexion n'est jamais versionnée. L'API et le Worker partagent les mêmes *user secrets* (`UserSecretsId` commun) : une seule commande suffit.

```bash
dotnet user-secrets set "ConnectionStrings:BeanStock" \
  "Server=localhost,1433;Database=BeanStock;User Id=sa;Password=<MSSQL_SA_PASSWORD>;TrustServerCertificate=True" \
  --project src/BeanStock.Api
```

Avec une instance locale et l'authentification Windows, la chaîne devient par exemple `Server=localhost;Database=BeanStock;Trusted_Connection=True;TrustServerCertificate=True` (ou `Server=.\SQLEXPRESS;…` pour une instance nommée).

Sous Visual Studio : clic droit sur `BeanStock.Api` → *Gérer les secrets utilisateur*. En dehors du poste de développement, utiliser la variable d'environnement `ConnectionStrings__BeanStock`.

### 3. Lancer l'API et le Worker

- **Visual Studio** : *Configurer les projets de démarrage* → *Plusieurs projets de démarrage* → `BeanStock.Api` et `BeanStock.Worker` sur *Démarrer*.
- **CLI** : `dotnet run --project src/BeanStock.Api` et, dans un autre terminal, `dotnet run --project src/BeanStock.Worker`.

En environnement `Development`, l'API applique les migrations EF Core au démarrage (`Database:ApplyMigrationsOnStartup`). La documentation interactive s'ouvre sur http://localhost:5080/scalar.

| URL | Rôle |
|---|---|
| `GET /health/live` | Le processus répond (aucune dépendance vérifiée) |
| `GET /health/ready` | L'instance peut servir des requêtes (base joignable) |
| `GET /openapi/v1.json` | Contrat OpenAPI (Development) |

### Migrations

Les outils sont déclarés dans `.config/dotnet-tools.json` :

```bash
dotnet tool restore
dotnet ef migrations add <Nom> --project src/BeanStock.Infrastructure.Persistence --startup-project src/BeanStock.Api --output-dir Migrations
```

## Lancer les tests

```bash
dotnet test
```

- `BeanStock.Domain.UnitTests` et `BeanStock.Application.UnitTests` : sans dépendance externe.
- `BeanStock.IntegrationTests` : démarrent un SQL Server jetable via Testcontainers ; **Docker doit être lancé**.

## Synchronisation manuelle du catalogue *(à venir)*

## Utilisation de l'API

La collection [Bruno](https://www.usebruno.com/) du dossier `api/` contient les requêtes principales. Ouvrir le dossier dans Bruno (*Open Collection*) puis sélectionner l'environnement **Local** (`BaseUrl` = `http://localhost:5080`). D'autres environnements (recette, production…) pourront y être ajoutés sous `api/environments/`.

### Catalogue

```bash
curl "http://localhost:5080/products?page=1&pageSize=20"
```

Retourne les produits **actifs** du catalogue local, triés par identifiant fournisseur : `items`, `page`, `pageSize`, `totalCount`, `totalPages`. `pageSize` est limité à 100 ; une valeur invalide renvoie un `400` au format ProblemDetails.

### Panier *(à venir)*

## Usage de l'IA

Ce projet est développé avec l'assistance d'outils d'IA. Les instructions qui encadrent les agents sont versionnées dans [`AGENTS.md`](AGENTS.md) ; le bilan d'utilisation (outils, vérifications, exemples de propositions retenues ou rejetées) sera documenté dans `documentation/`.
