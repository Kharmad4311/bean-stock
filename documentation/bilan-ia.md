# Bilan de l'usage de l'IA

Livrable 6 de l'énoncé. Ce document décrit l'outil utilisé, la façon de travailler, les vérifications faites et trois exemples de propositions de l'IA : une validée, une adaptée, une rejetée.

## Outil

**Claude** (Anthropic), utilisé dans un *Projet* claude.ai et depuis l'application Claude Desktop. Aucun autre assistant n'a servi (ni Copilot ni ChatGPT).

- **Le Projet claude.ai** regroupe l'énoncé, des instructions permanentes (voir l'[annexe](#annexe--instructions-du-projet-claude)) et une copie des documents de référence (`AGENTS.md`, `DECISIONS.md`, fiches de `documentation/`). Chaque conversation repart de ce contexte.
- **Claude Desktop, avec le dossier local du dépôt connecté.** Claude lit le code et y écrit directement. Il ne compile pas, ne pousse rien et n'a pas accès à GitHub.
- **Aucun serveur MCP, skill ni hook spécifique au projet.** Le seul fichier d'instructions versionné est [`AGENTS.md`](../AGENTS.md) (stack, règles d'architecture, invariants métier, sécurité du dépôt, règles de documentation). Il est relu par l'agent à chaque session et mis à jour quand une règle change.

## Façon de travailler

1. **Une session par sujet** : squelette, synchronisation, panier, livraison.
2. **Le plan avant le code.** Chaque session commence par une passe de conception. Claude propose les options, avec leurs coûts. Je tranche, et les décisions vont dans `DECISIONS.md`.
3. **Claude écrit, je vérifie.** Je compile sous Visual Studio, je lance les tests, je relis le diff et je commite moi-même, par branche et par pull request.
4. **Les erreurs remontent par fichier.** Je redirige la sortie de `dotnet test` vers `TestResults/*.log`, et Claude lit le journal dans le dossier connecté pour corriger.
5. **La documentation dans le même changement que le code.** C'est une règle d'`AGENTS.md`, que je vérifie à la relecture.

## Vérifications

| Vérification | Ce qu'elle garantit |
|---|---|
| Build avec `TreatWarningsAsErrors`, analyseurs .NET (`latest-recommended`), style imposé (`.editorconfig`) | Pas de code approximatif accepté « parce qu'il compile » |
| Générateur de sources Mediator | Un handler manquant ou mal câblé est une erreur de compilation |
| Tests unitaires (domaine, cas d'usage, client DummyJSON, planificateur en temps simulé) | Règles métier et cas limites |
| Tests d'intégration sur un vrai SQL Server (Testcontainers), avec deux instances de l'API | Concurrence (créations parallèles, ajouts simultanés, scénario d'Alice, conflits de version), rollback, verrou distribué |
| Couverture mesurée (`coverage.config`, ReportGenerator) | Zones non testées repérées ; environ 97 % des lignes à la fin de la synchronisation |
| Tests manuels outillés : collection Bruno, scripts PowerShell, requêtes SQL | Comportement observé sur l'application lancée, deux instances comprises ([panier](tests-manuels-panier.md), [synchronisation](tests-manuels-synchronisation.md)) |
| CI GitHub Actions | Build, tests et couverture rejoués sur une machine neutre à chaque pull request ; images Docker en plus sur `main` |
| Relecture des sources citées par l'IA | Les affirmations techniques (`sp_getapplock`, RCSI, licences MediatR et FluentAssertions) sont vérifiées dans la documentation officielle ([fiche verrou](verrou-distribue.md), section *Sources*) |

## Trois exemples

### Validé : le verrou distribué de la synchronisation

Pour empêcher deux synchronisations simultanées sur plusieurs instances, Claude a proposé `sp_getapplock` sans attente, porté par la session de la transaction. Il a ajouté un contrôle avant le commit : on vérifie que le verrou est toujours détenu (*fencing*). La proposition a été retenue telle quelle, pour deux raisons : elle n'ajoute aucune infrastructure (pas de Redis), et elle s'appuie sur la base imposée par l'énoncé. Je ne connaissais pas cette procédure. Avant de valider, j'ai demandé une [fiche](verrou-distribue.md) avec ses sources et un script SSMS pour expérimenter. Un test d'intégration et un scénario de test manuel couvrent le comportement.

### Adapté : l'écriture concurrente du panier

La première version de Claude reposait sur un service `CartWriter`. Il assemblait les étapes (transaction, verrou `UPDLOCK`, chargement, enregistrement) et était injecté dans chaque handler. Les tests passaient, mais je l'ai refusé à la relecture. Le service devait être public à cause du générateur Mediator, et rien n'empêchait un futur handler d'oublier une étape. Sur ma remarque, la conception a été revue. Le port n'expose plus qu'une opération atomique, `ICartRepository.ChangeAsync(userId, modification)`, et la transaction et le verrou vivent dans l'implémentation EF. Une règle d'`AGENTS.md` interdit de réintroduire les étapes séparées.

### Rejeté : l'architecture initiale

La première proposition d'architecture comptait quatre projets en couches (`Api`, `Application`, `Domain`, `Infrastructure`), sans CQRS ni médiateur, au nom de la simplicité. Je l'ai rejetée. Pour un produit destiné à évoluer, je voulais des responsabilités plus nettes : des ports séparés des cas d'usage (`Application.Abstractions`), une infrastructure découpée par dépendance externe, et des pipelines transverses (validation) écrits une seule fois. Claude a alors justifié chaque brique de la solution retenue, de `Mediator` (plutôt que MediatR, devenu commercial) à Ardalis.Specification limité aux lectures, dans [`DECISIONS.md`](../DECISIONS.md).

## Limites constatées

- **L'IA ne voit pas l'exécution.** Elle ne compile pas et ne lance pas les tests dans ce mode de travail. Chaque proposition est donc vérifiée sur mon poste avant d'être commitée.
- **Le contexte se perd entre les sessions.** Les décisions doivent être écrites (`DECISIONS.md`, `AGENTS.md`, documents du Projet) pour être respectées ensuite.
- **Les propositions vont spontanément au plus simple.** Les exigences de structure et de qualité (découpage en projets, documentation XML complète, tests de concurrence sur un vrai moteur) doivent être formulées explicitement.

## Annexe : instructions du Projet Claude

Ces instructions sont configurées dans le Projet claude.ai et s'appliquent à chaque conversation. Elles sont reproduites ici telles qu'utilisées.

> **Description.** BeanStock est une application de démo dans un environnement e-commerce. Elle sert à montrer mes capacités de développement. Il s'agit d'une API codée en C# sous le framework .Net 10 avec une base SQLServer. Aucun élément risqué ne doit figurer sur le repository, qui est hébergé sur github. Nous devons créer un environnement complet, structuré, et documenté selon les exigences spécifiées dans le document, et en respectant au maximum les bonnes pratiques de développement usuelles.
>
> **Instructions.** Le développement se fait non pas sur Rider comme l'indique le profil, mais sur Visual Studio Community. Il n'est pas déconseillé de multiplier les projets pour mieux encadrer les responsabilités. Plutôt que d'avoir un projet "Application" global, "fourre-tout", on préférera le découper en plusieurs projets plus léger et plus faciles à maintenir (exemple : "Application.Abstractions", "Application.Plugins", etc)
