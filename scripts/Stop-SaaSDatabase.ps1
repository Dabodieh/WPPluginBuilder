#Requires -Version 7.0
<#
.SYNOPSIS
  Stops the local SaaS PostgreSQL database without deleting it.

.DESCRIPTION
  Runs `docker compose down` (no -v) against docker/docker-compose.saas.yml.
  The named volume (wpaiplugin_saas_db) is preserved, so registered
  accounts are still there next time you run scripts/Start-SaaSDatabase.ps1.

  To completely delete all local SaaS account data instead, run:
    docker compose -f docker/docker-compose.saas.yml down -v
  WARNING: that command permanently deletes all local SaaS account data.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File scripts/Stop-SaaSDatabase.ps1
#>

$ErrorActionPreference = "Stop"

$RepoRoot = Split-Path -Parent $PSScriptRoot
$ComposeFile = Join-Path $RepoRoot "docker/docker-compose.saas.yml"
$ComposeProjectName = "wpaiplugin-saas"

Write-Host "Stopping SaaS PostgreSQL database (docker compose down)"
& docker compose -p $ComposeProjectName -f $ComposeFile down
if ($LASTEXITCODE -ne 0) {
    throw "docker compose down failed with exit code $LASTEXITCODE"
}

Write-Host ""
Write-Host "Stopped. Persistent volume (wpaiplugin_saas_db) was preserved."
Write-Host "Run scripts/Start-SaaSDatabase.ps1 to bring the same database back up."
