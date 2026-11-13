#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
if [[ ! -f .env ]]; then echo 'Create .env from .env.example before deployment.' >&2; exit 1; fi
# Guard against a silent new, empty data volume after the folder or project was renamed (INSTALL.md, "Renaming to AgentGateway").
volume=$(docker compose config 2>/dev/null | sed -n '/^volumes:/,$p' | sed -n 's/^ *name: *//p' | head -n 1)
if [[ -n "$volume" ]] && ! docker volume inspect "$volume" >/dev/null 2>&1; then
  existing=$(docker volume ls --format '{{.Name}}' | grep -E '_gateway-data$' || true)
  if [[ -n "$existing" ]]; then
    printf 'Data volume %s does not exist, but these gateway volumes do:\n%s\n' "$volume" "$existing" >&2
    printf 'Set GATEWAY_VOLUME_NAME=<one of them> in .env to keep your data, then run this script again.\n' >&2
    exit 1
  fi
fi
docker compose -f docker-compose.yml -f docker-compose.build.yml up --build -d
for ((attempt=0; attempt<30; attempt++)); do
  if docker compose exec -T gateway curl -fsS http://localhost:8080/api/ready >/dev/null; then
    echo 'Gateway is ready.'; exit 0
  fi
  sleep 2
done
echo 'Gateway did not become ready. Check docker compose logs gateway locally; redact before sharing.' >&2
exit 1
