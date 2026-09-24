#!/usr/bin/env bash
# Deploy/update ModuleMint on this Plesk host.
#
# Deliberately boring: no rollback automation, no destructive commands run
# automatically. Read docs/DEPLOYMENT.md "Plesk + Docker Deployment" before
# running this for the first time.
#
# Usage (from the repository root on the server):
#   ./scripts/Deploy-Production.sh
#
# Requires: .env.production already created (copy .env.production.example
# and fill in real values - never commit it).

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$REPO_ROOT"

COMPOSE_FILE="docker/docker-compose.production.yml"
ENV_FILE=".env.production"
PROJECT_NAME="modulemint"

if [ ! -f "$ENV_FILE" ]; then
  echo "ERROR: $ENV_FILE not found. Copy .env.production.example to $ENV_FILE and fill in real values first." >&2
  exit 1
fi

# Load .env.production into this script's own shell environment too (not
# just docker compose's), so the migration step below can build the same
# connection string without duplicating values.
set -a
# shellcheck disable=SC1090
source "$ENV_FILE"
set +a

compose() {
  docker compose --env-file "$ENV_FILE" -f "$COMPOSE_FILE" -p "$PROJECT_NAME" "$@"
}

echo "==> Take a backup first if you haven't already (see docs/DEPLOYMENT.md 'Backups')."
read -r -p "Have you taken a backup of the database and artifacts? [y/N] " confirm
if [[ ! "$confirm" =~ ^[Yy]$ ]]; then
  echo "Aborted. Take a backup, then re-run this script."
  exit 1
fi

echo "==> Pulling latest code (if this is a git checkout on a tracked branch)"
git pull --ff-only

echo "==> Building the production image"
compose build

echo "==> Applying database migrations"
echo "    (build-stage container on the compose network, see docs/DEPLOYMENT.md"
echo "    'Database migrations' for the exact command and what it does)"
docker build --target build -t modulemint-api:migrate .
docker run --rm --network "${PROJECT_NAME}_default" \
  --env-file "$ENV_FILE" \
  -e "ConnectionStrings__DefaultConnection=Host=postgres;Port=5432;Database=${POSTGRES_DB:-modulemint};Username=${POSTGRES_USER:-modulemint};Password=${POSTGRES_PASSWORD:?set in .env.production}" \
  modulemint-api:migrate \
  bash -c "dotnet tool install --global dotnet-ef --version 8.* >/dev/null && export PATH=\"\$PATH:/root/.dotnet/tools\" && dotnet ef database update --project src/WPAIPlugin.Api --startup-project src/WPAIPlugin.Api"

echo "==> Starting/updating containers"
compose up -d

echo "==> Waiting for health check"
for i in $(seq 1 30); do
  if curl -fsS "http://127.0.0.1:18473/health/ready" >/dev/null 2>&1; then
    echo "Healthy."
    break
  fi
  if [ "$i" -eq 30 ]; then
    echo "WARNING: /health/ready did not become healthy after 30 attempts. Check: docker compose --env-file $ENV_FILE -f $COMPOSE_FILE -p $PROJECT_NAME logs modulemint" >&2
  fi
  sleep 2
done

echo "==> Done. Now manually verify (see docs/DEPLOYMENT.md 'Production Update'):"
echo "    - https://modulemint.co.uk loads"
echo "    - login works"
echo "    - OpenAI planning works"
echo "    - a standard plugin build/download works"
