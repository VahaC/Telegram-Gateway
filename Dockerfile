# syntax=docker/dockerfile:1.7
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props global.json ./
COPY src/TelegramGateway.Core/TelegramGateway.Core.csproj src/TelegramGateway.Core/
COPY src/TelegramGateway.Infrastructure/TelegramGateway.Infrastructure.csproj src/TelegramGateway.Infrastructure/
COPY src/TelegramGateway.Api/TelegramGateway.Api.csproj src/TelegramGateway.Api/
RUN --mount=type=cache,target=/root/.nuget/packages,sharing=locked dotnet restore src/TelegramGateway.Api/TelegramGateway.Api.csproj
COPY src/ src/
RUN --mount=type=cache,target=/root/.nuget/packages,sharing=locked dotnet publish src/TelegramGateway.Api -c Release --no-restore -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
# Curl is only for the container health probe; no SDK or build tools enter the runtime.
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build --chown=app:app /app/publish ./
RUN mkdir -p /app/Data && chown app:app /app/Data && chmod 700 /app/Data
USER app
ENV ASPNETCORE_URLS=http://+:8080 ASPNETCORE_ENVIRONMENT=Production TELEGRAMGATEWAY_Gateway__DataPath=/app/Data
EXPOSE 8080
HEALTHCHECK --interval=30s --timeout=5s --start-period=15s --retries=3 CMD curl -fsS http://localhost:8080/api/ready || exit 1
ENTRYPOINT ["dotnet", "TelegramGateway.Api.dll"]
