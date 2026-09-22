#Requires -Version 7.0
<#
.SYNOPSIS
  Starts the persistent local WordPress development site for manually
  testing WPAIPlugin-generated plugin ZIPs through the real wp-admin UI.

.DESCRIPTION
  Starts docker/docker-compose.dev.yml and waits until WordPress responds
  on http://127.0.0.1:18437. Does not run the WordPress setup wizard and
  does not open a browser - browse to the URL yourself and complete the
  normal WordPress installer by hand.

  This is separate from, and does not touch, the disposable automated
  validation environment (docker/docker-compose.validate.yml,
  DockerPluginValidator, scripts/Validate-GeneratedPlugin.ps1).

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File scripts/Start-WordPressDev.ps1
#>

$ErrorActionPreference = "Stop"

$RepoRoot = Split-Path -Parent $PSScriptRoot
$ComposeFile = Join-Path $RepoRoot "docker/docker-compose.dev.yml"
$ComposeProjectName = "wpaiplugin-dev"
$DevUrl = "http://127.0.0.1:18437"

Write-Host "Checking Docker is available"
& docker version --format "{{.Server.Version}}" | Out-Null
if ($LASTEXITCODE -ne 0) {
    throw "Docker daemon is not reachable. Start Docker Desktop and try again."
}

Write-Host "Starting WordPress development site (docker compose up -d)"
& docker compose -p $ComposeProjectName -f $ComposeFile up -d
if ($LASTEXITCODE -ne 0) {
    throw "docker compose up failed with exit code $LASTEXITCODE"
}

Write-Host "Waiting for WordPress to respond on $DevUrl ..."
$ready = $false
for ($attempt = 1; $attempt -le 60; $attempt++) {
    try {
        $response = Invoke-WebRequest -Uri $DevUrl -TimeoutSec 2 -SkipHttpErrorCheck
        if ($response.StatusCode -gt 0) { $ready = $true; break }
    } catch {
        # Not ready yet; keep polling.
    }
    Write-Host "Waiting... attempt $attempt/60"
    Start-Sleep -Seconds 1
}

if (-not $ready) {
    throw "WordPress did not respond on $DevUrl within timeout."
}

Write-Host ""
Write-Host "WordPress development site is ready:"
Write-Host $DevUrl
