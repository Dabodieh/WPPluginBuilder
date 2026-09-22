#Requires -Version 7.0
<#
.SYNOPSIS
  Stops the persistent local WordPress development site without deleting it.

.DESCRIPTION
  Runs `docker compose down` (no -v) against docker/docker-compose.dev.yml.
  The named volumes (wp_dev_db, wp_dev_files) are preserved, so the site,
  its database, and any installed plugins are still there next time you run
  scripts/Start-WordPressDev.ps1.

  To completely delete the site instead, run:
    docker compose -f docker/docker-compose.dev.yml down -v
  WARNING: that command completely deletes the local WordPress site and database.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File scripts/Stop-WordPressDev.ps1
#>

$ErrorActionPreference = "Stop"

$RepoRoot = Split-Path -Parent $PSScriptRoot
$ComposeFile = Join-Path $RepoRoot "docker/docker-compose.dev.yml"
$ComposeProjectName = "wpaiplugin-dev"

Write-Host "Stopping WordPress development site (docker compose down)"
& docker compose -p $ComposeProjectName -f $ComposeFile down
if ($LASTEXITCODE -ne 0) {
    throw "docker compose down failed with exit code $LASTEXITCODE"
}

Write-Host ""
Write-Host "Stopped. Persistent volumes (wp_dev_db, wp_dev_files) were preserved."
Write-Host "Run scripts/Start-WordPressDev.ps1 to bring the same site back up."
