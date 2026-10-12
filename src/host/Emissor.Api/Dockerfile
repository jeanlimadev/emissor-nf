# Imagem da API. Build em duas etapas: SDK para compilar, runtime para rodar.
# Contexto do build: a raiz do repositorio (docker build -f src/host/Emissor.Api/Dockerfile .).
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props Directory.Packages.props ./
COPY src/ src/
RUN dotnet publish src/host/Emissor.Api/Emissor.Api.csproj -c Release -o /app/publish --no-self-contained

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
# Fontes para o DANFE (QuestPDF) e certificados raiz atualizados para falar com a SEFAZ.
RUN apt-get update \
    && apt-get install -y --no-install-recommends fontconfig fonts-liberation ca-certificates curl \
    && rm -rf /var/lib/apt/lists/*
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://+:8080 \
    Database__MigrateOnStartup=true
EXPOSE 8080
USER app
ENTRYPOINT ["dotnet", "Emissor.Api.dll"]
