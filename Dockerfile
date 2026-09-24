# Multi-stage production image for WPAIPlugin.Api.
#
# Does NOT bundle PostgreSQL. Does NOT bundle Docker-in-Docker: Build &
# Validate calls the host's `docker` CLI (see docker/docker-compose.validate.yml
# and DockerPluginValidator), so a container running this image needs the
# host Docker socket mounted and the `docker` CLI available on PATH to offer
# that feature - see README "Production Deployment" for the tradeoffs.
# Standard builds and everything else work without it.

# ---- build stage ----
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Restore first, on just the project files, so dependency layers cache
# across source-only changes.
COPY WPAIPlugin.sln .
COPY src/WPAIPlugin.Api/WPAIPlugin.Api.csproj src/WPAIPlugin.Api/
COPY src/WPAIPlugin.Generator/WPAIPlugin.Generator.csproj src/WPAIPlugin.Generator/
COPY src/WPAIPlugin.Planning/WPAIPlugin.Planning.csproj src/WPAIPlugin.Planning/
COPY src/WPAIPlugin.Templates/WPAIPlugin.Templates.csproj src/WPAIPlugin.Templates/
COPY tests/WPAIPlugin.Generator.Tests/WPAIPlugin.Generator.Tests.csproj tests/WPAIPlugin.Generator.Tests/
RUN dotnet restore src/WPAIPlugin.Api/WPAIPlugin.Api.csproj

COPY src/ src/
RUN dotnet publish src/WPAIPlugin.Api/WPAIPlugin.Api.csproj -c Release -o /app --no-restore

# ---- runtime stage ----
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

# Run as a non-root user. The image already ships a non-root "app" user
# (UID/GID 64198) since the .NET 8 Debian-based aspnet image.
RUN mkdir -p /app/App_Data/artifacts /app/App_Data/dataprotection-keys \
    && chown -R app:app /app
USER app

COPY --from=build --chown=app:app /app .

ENV ASPNETCORE_ENVIRONMENT=Production
ENV ASPNETCORE_URLS=http://+:8080
# Persist these two paths with a volume/bind mount in production - see
# README "Production Deployment" and docker-compose.prod.example.yml.
ENV Artifacts__RootPath=/app/App_Data/artifacts
ENV DataProtection__KeyRingPath=/app/App_Data/dataprotection-keys

EXPOSE 8080

ENTRYPOINT ["dotnet", "WPAIPlugin.Api.dll"]
