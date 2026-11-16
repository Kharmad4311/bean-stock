# BeanStock

API e-commerce de démonstration : un **catalogue de produits** synchronisé depuis un fournisseur externe et un **panier personnel** par utilisateur, conçue pour fonctionner de façon cohérente avec **plusieurs instances** en parallèle.

> Projet réalisé dans le cadre d'un test technique. Les contributions externes ne sont pas acceptées.

BeanStock est le nom de marque du service. Le catalogue provient de [DummyJSON](https://dummyjson.com/docs/products) et est synchronisé intégralement, sans filtrage.

## Statut

🚧 **En cours de développement.** La solution démarre en local : base SQL Server, API (santé, catalogue paginé, synchronisation manuelle) et Worker (synchronisation automatique). Le panier arrive dans la prochaine itération. Les sections marquées *(à venir)* seront complétées au fur et à mesure.

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
documentation/   Documentation détaillée (synchronisation, verrou distribué, tests manuels…)
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

| Projet | Contenu | Dépendance externe |
|---|---|---|
| `BeanStock.Domain.UnitTests` | Règles du panier et du produit | — |
| `BeanStock.Application.UnitTests` | Cas d'usage (synchronisation…), validations | — |
| `BeanStock.Infrastructure.DummyJson.UnitTests` | Client DummyJSON, avec un faux serveur HTTP | — |
| `BeanStock.Worker.UnitTests` | Planificateur de synchronisation, en temps simulé | — |
| `BeanStock.IntegrationTests` | API + SQL Server réel : concurrence, rollback, verrou distribué | **Docker** (Testcontainers) |

Aucun test n'appelle le vrai DummyJSON. Pour lancer uniquement les tests sans Docker : `dotnet test --filter "FullyQualifiedName!~IntegrationTests"`.

### Couverture de code

```powershell
dotnet tool restore
Remove-Item TestResults/coverage -Recurse -ErrorAction Ignore   # bash : rm -rf TestResults/coverage
dotnet test --coverage --coverage-output-format cobertura --coverage-settings coverage.config --results-directory TestResults/coverage
dotnet reportgenerator -reports:"TestResults/coverage/*.cobertura.xml" -targetdir:coverage-report -reporttypes:"Html;TextSummary" -filefilters:"-*.g.cs"
```

Le rapport HTML est dans `coverage-report/index.html` et le résumé dans `coverage-report/Summary.txt`. Chaque projet de test produit son propre fichier (nom unique) dans `TestResults/coverage/`, et ReportGenerator les fusionne. On vide le dossier avant chaque mesure pour ne pas mélanger les exécutions. Les assemblies de test, les migrations et le code généré sont exclus de la mesure (`coverage.config`).

### Moteur de tests

Les projets xUnit v3 tournent sur **Microsoft Testing Platform** : `global.json` y bascule `dotnet test` (obligatoire avec le SDK .NET 10). Conséquences :

- un projet de test est un exécutable : `dotnet run --project tests/BeanStock.IntegrationTests` lance ses tests, sans passer par `dotnet test` ;
- les options sont celles de la plateforme (`--coverage`, `--report-trx`…) et non plus celles de VSTest (`--collect`, `--logger`) ;
- Visual Studio (Explorateur de tests) fonctionne sans changement.

## Synchronisation du catalogue

Le catalogue DummyJSON est copié dans la base locale par **un seul cas d'usage**, déclenché de deux façons. Le fonctionnement complet (verrou multi-instance, lots, scénarios d'échec) est décrit dans [`documentation/synchronisation.md`](documentation/synchronisation.md), le verrou distribué dans [`documentation/verrou-distribue.md`](documentation/verrou-distribue.md), et un mode opératoire de test manuel dans [`documentation/tests-manuels-synchronisation.md`](documentation/tests-manuels-synchronisation.md).

### Déclenchement manuel

```bash
curl -X POST http://localhost:5080/admin/catalog/sync
```

L'appel attend la fin de la synchronisation, quelques secondes, puis renvoie le rapport :

```json
{
  "runId": "019a8c5e-…",
  "trigger": "Manual",
  "startedAt": "2026-11-14T14:02:11.513+00:00",
  "completedAt": "2026-11-14T14:02:13.870+00:00",
  "providerProducts": 194,
  "added": 194,
  "updated": 0,
  "reactivated": 0,
  "deactivated": 0,
  "unchanged": 0,
  "skipped": 0
}
```

| Statut | Signification | Catalogue |
|---|---|---|
| `200` | Synchronisation terminée | À jour |
| `409` `CatalogSync.AlreadyRunning` | Une synchronisation est déjà en cours, sur n'importe quelle instance | Inchangé |
| `409` `CatalogSync.LockLost` | Le verrou exclusif a été perdu en cours de route (coupure de connexion) : rien n'est validé | Inchangé |
| `502` `CatalogSync.ProviderUnavailable` | DummyJSON injoignable ou réponse incohérente | Inchangé |
| `502` `CatalogSync.SuspiciousCatalog` | Catalogue reçu vide ou anormalement court (garde-fou) | Inchangé |

Également disponible dans Bruno (`Admin / Synchronize Catalog`) et Scalar.

### Synchronisation automatique (Worker)

Le Worker synchronise **une fois par jour**. Le planning est commun à toutes les instances : la synchronisation a lieu dès que la dernière réussite date de plus de `Interval`. Un Worker qui démarre sur une base vide synchronise donc immédiatement, et plusieurs Workers ne synchronisent pas plusieurs fois.

Section `CatalogSync:Schedule` (format `TimeSpan` : `"1.00:00:00"` = 1 jour, `"00:02:00"` = 2 minutes) :

| Clé | Défaut | Rôle |
|---|---|---|
| `Enabled` | `true` | Active la synchronisation automatique |
| `Interval` | `1.00:00:00` | Délai minimal entre deux synchronisations réussies |
| `CheckInterval` | `00:01:00` | Fréquence des vérifications de chaque Worker |
| `RetryDelay` | `00:15:00` | Attente avant une nouvelle tentative après un échec |

**Raccourcir le délai pour un test manuel :**

- choisir le profil de lancement **`BeanStock.Worker (synchro rapide)`** dans Visual Studio (`Interval` = 2 min, vérification toutes les 15 s) ;
- ou modifier `src/BeanStock.Worker/appsettings.Development.json` : la nouvelle valeur s'applique **sans redémarrage**, à la vérification suivante ;
- ou définir la variable d'environnement `CatalogSync__Schedule__Interval=00:05:00`.

Une variable d'environnement, y compris celles du profil « synchro rapide », l'emporte sur le fichier JSON.

### Historique

Chaque exécution est tracée dans la table `CatalogSyncRuns` (déclencheur, statut, compteurs, cause d'échec) :

```sql
SELECT TOP (10) StartedAt, [Trigger], Status, AddedCount, UpdatedCount, DeactivatedCount, FailureReason
FROM CatalogSyncRuns ORDER BY StartedAt DESC;
```

## Utilisation de l'API

La collection [Bruno](https://www.usebruno.com/) du dossier `api/` contient les requêtes principales. Ouvrir le dossier dans Bruno (*Open Collection*) puis sélectionner l'environnement **Local** (`BaseUrl` = `http://localhost:5080`). D'autres environnements (recette, production…) pourront y être ajoutés sous `api/environments/`.

### Catalogue

```bash
curl "http://localhost:5080/products?page=1&pageSize=20"
```

Retourne les produits **actifs** du catalogue local, triés par identifiant fournisseur : `items`, `page`, `pageSize`, `totalCount`, `totalPages`. Chaque produit indique `lastUpdatedAt`, la date de sa dernière modification locale (création, changement de valeur, réactivation). Les produits retirés par le fournisseur n'apparaissent plus, mais restent lisibles dans les paniers. `pageSize` est limité à 100 ; une valeur invalide renvoie un `400` au format ProblemDetails.

### Panier *(à venir)*

## Usage de l'IA

Ce projet est développé avec l'assistance d'outils d'IA. Les instructions qui encadrent les agents sont versionnées dans [`AGENTS.md`](AGENTS.md) ; le bilan d'utilisation (outils, vérifications, exemples de propositions retenues ou rejetées) sera documenté dans `documentation/`.
