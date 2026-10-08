#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
if [[ ! -f .env ]]; then echo 'Create .env from .env.example before deployment.' >&2; exit 1; fi
docker compose -f docker-compose.yml -f docker-compose.build.yml up --build -d
for ((attempt=0; attempt<30; attempt++)); do
  if docker compose exec -T gateway curl -fsS http://localhost:8080/api/ready >/dev/null; then
    echo 'Gateway is ready.'; exit 0
  fi
  sleep 2
done
echo 'Gateway did not become ready. Check docker compose logs gateway locally; redact before sharing.' >&2
exit 1
