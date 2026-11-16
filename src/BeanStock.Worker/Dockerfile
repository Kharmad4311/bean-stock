# syntax=docker/dockerfile:1
# Image du Worker BeanStock (synchronisation planifiée du catalogue).
# Contexte de build : la racine du dépôt.
#   docker build -f src/BeanStock.Worker/Dockerfile .

ARG DOTNET_VERSION=10.0

# ---- build : restore (mis en cache tant que les .csproj ne changent pas), puis publish
FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_VERSION} AS build
WORKDIR /source

COPY global.json nuget.config Directory.Build.props Directory.Packages.props ./
COPY src/BeanStock.Domain/BeanStock.Domain.csproj src/BeanStock.Domain/
COPY src/BeanStock.Application.Abstractions/BeanStock.Application.Abstractions.csproj src/BeanStock.Application.Abstractions/
COPY src/BeanStock.Application/BeanStock.Application.csproj src/BeanStock.Application/
COPY src/BeanStock.Infrastructure.Persistence/BeanStock.Infrastructure.Persistence.csproj src/BeanStock.Infrastructure.Persistence/
COPY src/BeanStock.Infrastructure.DummyJson/BeanStock.Infrastructure.DummyJson.csproj src/BeanStock.Infrastructure.DummyJson/
COPY src/BeanStock.Worker/BeanStock.Worker.csproj src/BeanStock.Worker/
RUN dotnet restore src/BeanStock.Worker/BeanStock.Worker.csproj

# .editorconfig pilote les analyseurs (ex. migrations marquées generated_code) : sans lui,
# le build Release diffère de celui du poste et de la CI. Copié après le restore, qu'il n'influence pas.
COPY .editorconfig ./
COPY src/ src/
RUN dotnet publish src/BeanStock.Worker/BeanStock.Worker.csproj --configuration Release --no-restore --output /app/publish

# ---- final : runtime .NET seul (le Worker n'expose pas d'HTTP)
FROM mcr.microsoft.com/dotnet/runtime:${DOTNET_VERSION} AS final
WORKDIR /app
COPY --from=build /app/publish .
USER $APP_UID
ENTRYPOINT ["dotnet", "BeanStock.Worker.dll"]
