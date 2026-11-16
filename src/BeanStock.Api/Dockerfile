# syntax=docker/dockerfile:1
# Image de l'API BeanStock, et image « migrator » (bundle EF Core qui applique les migrations).
# Contexte de build : la racine du dépôt.
#   docker build -f src/BeanStock.Api/Dockerfile .                    → API
#   docker build -f src/BeanStock.Api/Dockerfile --target migrator .  → migrations

ARG DOTNET_VERSION=10.0

# ---- build : restore (mis en cache tant que les .csproj ne changent pas), puis publish
FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_VERSION} AS build
WORKDIR /source

COPY global.json nuget.config Directory.Build.props Directory.Packages.props ./
COPY .config/dotnet-tools.json .config/
COPY src/BeanStock.Domain/BeanStock.Domain.csproj src/BeanStock.Domain/
COPY src/BeanStock.Application.Abstractions/BeanStock.Application.Abstractions.csproj src/BeanStock.Application.Abstractions/
COPY src/BeanStock.Application/BeanStock.Application.csproj src/BeanStock.Application/
COPY src/BeanStock.Infrastructure.Persistence/BeanStock.Infrastructure.Persistence.csproj src/BeanStock.Infrastructure.Persistence/
COPY src/BeanStock.Infrastructure.DummyJson/BeanStock.Infrastructure.DummyJson.csproj src/BeanStock.Infrastructure.DummyJson/
COPY src/BeanStock.Api/BeanStock.Api.csproj src/BeanStock.Api/
RUN dotnet restore src/BeanStock.Api/BeanStock.Api.csproj

COPY src/ src/
RUN dotnet publish src/BeanStock.Api/BeanStock.Api.csproj --configuration Release --no-restore --output /app/publish

# ---- migrator-build : bundle EF Core (exécutable autonome qui applique les migrations en attente)
FROM build AS migrator-build
RUN dotnet tool restore
RUN dotnet ef migrations bundle \
        --project src/BeanStock.Infrastructure.Persistence \
        --startup-project src/BeanStock.Api \
        --configuration Release \
        --output /app/efbundle

# ---- migrator : exécuté une fois avant l'API et le Worker (voir compose.yaml)
FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_VERSION} AS migrator
WORKDIR /app
COPY --from=migrator-build /app/efbundle .
USER $APP_UID
# La chaîne de connexion est passée en argument : --connection "<chaîne>"
ENTRYPOINT ["./efbundle"]

# ---- final : image de l'API (dernière étape = cible par défaut)
FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_VERSION} AS final
WORKDIR /app
COPY --from=build /app/publish .
# Utilisateur non-root fourni par les images .NET
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "BeanStock.Api.dll"]
