#Requires -Version 7.0
<#
.SYNOPSIS
  Starts the local PostgreSQL database used by the SaaS accounts layer.

.DESCRIPTION
  Starts docker/docker-compose.saas.yml. The ASP.NET application is NOT
  containerised - run it normally with `dotnet run --project src/WPAIPlugin.Api`.

  This is separate from, and does not touch, docker-compose.validate.yml or
  docker-compose.dev.yml.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File scripts/Start-SaaSDatabase.ps1
#>

$ErrorActionPreference = "Stop"

$RepoRoot = Split-Path -Parent $PSScriptRoot
$ComposeFile = Join-Path $RepoRoot "docker/docker-compose.saas.yml"
$ComposeProjectName = "wpaiplugin-saas"

Write-Host "Checking Docker is available"
& docker version --format "{{.Server.Version}}" | Out-Null
if ($LASTEXITCODE -ne 0) {
    throw "Docker daemon is not reachable. Start Docker Desktop and try again."
}

Write-Host "Starting SaaS PostgreSQL database (docker compose up -d)"
& docker compose -p $ComposeProjectName -f $ComposeFile up -d
if ($LASTEXITCODE -ne 0) {
    throw "docker compose up failed with exit code $LASTEXITCODE"
}

Write-Host ""
Write-Host "PostgreSQL is available at 127.0.0.1:15432 (database: wpaiplugin, user: wpaiplugin)."
Write-Host "Apply migrations with:"
Write-Host "  dotnet ef database update --project src/WPAIPlugin.Api --startup-project src/WPAIPlugin.Api"
