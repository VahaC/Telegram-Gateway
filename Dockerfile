# syntax=docker/dockerfile:1.7
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props global.json ./
COPY src/AgentGateway.Core/AgentGateway.Core.csproj src/AgentGateway.Core/
COPY src/AgentGateway.Infrastructure/AgentGateway.Infrastructure.csproj src/AgentGateway.Infrastructure/
COPY src/AgentGateway.Api/AgentGateway.Api.csproj src/AgentGateway.Api/
RUN --mount=type=cache,target=/root/.nuget/packages,sharing=locked dotnet restore src/AgentGateway.Api/AgentGateway.Api.csproj
COPY src/ src/
RUN --mount=type=cache,target=/root/.nuget/packages,sharing=locked dotnet publish src/AgentGateway.Api -c Release --no-restore -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
# Curl is only for the container health probe; no SDK or build tools enter the runtime.
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build --chown=app:app /app/publish ./
RUN mkdir -p /app/Data && chown app:app /app/Data && chmod 700 /app/Data
USER app
# Unprefixed on purpose: a TELEGRAMGATEWAY_ or AGENTGATEWAY_ override from an existing stack still wins over this default.
ENV ASPNETCORE_URLS=http://+:8080 ASPNETCORE_ENVIRONMENT=Production Gateway__DataPath=/app/Data
EXPOSE 8080
HEALTHCHECK --interval=30s --timeout=5s --start-period=15s --retries=3 CMD curl -fsS http://localhost:8080/api/ready || exit 1
ENTRYPOINT ["dotnet", "AgentGateway.Api.dll"]
