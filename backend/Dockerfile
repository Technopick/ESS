# ESS API: multi-stage build, runs as non-root on Linux.
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY Ess.sln ./
COPY src/ src/
COPY tests/ tests/
RUN dotnet restore src/Ess.Api/Ess.Api.csproj
RUN dotnet publish src/Ess.Api/Ess.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0
# TrueType fonts for PDF reports and exports; wget for the health check.
RUN apt-get update && apt-get install -y --no-install-recommends fonts-dejavu-core wget && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production
EXPOSE 8080
USER app
HEALTHCHECK --interval=30s --timeout=5s CMD wget -qO- http://localhost:8080/health/live || exit 1
ENTRYPOINT ["dotnet", "Ess.Api.dll"]
