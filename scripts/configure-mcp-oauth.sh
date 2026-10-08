#!/usr/bin/env bash
# Configure OAuth without displaying or evaluating secrets from .env.
set -euo pipefail
umask 077
if [ "$#" -ne 3 ]; then
  printf 'Usage: bash scripts/configure-mcp-oauth.sh HTTPS_ORIGIN EXACT_HTTPS_CALLBACK TRUSTED_PROXY_IP\n' >&2
  exit 2
fi
task_origin=${1%/}
task_callback=$2
task_proxy=$3
if [[ ! $task_origin =~ ^https://[A-Za-z0-9.-]+(:[0-9]+)?$ ]] || [[ ! $task_callback =~ ^https://[A-Za-z0-9./_:%?=\&+-]+$ ]] || [[ ! $task_proxy =~ ^[0-9a-fA-F.:]+$ ]]; then
  printf 'Invalid HTTPS origin, callback or proxy IP.\n' >&2
  exit 2
fi
test -f .env
command -v openssl > /dev/null
task_backup=".env.before-oauth-$(date +%Y%m%d-%H%M%S)-$RANDOM"
cp -p .env "$task_backup"
chmod 600 "$task_backup" .env
task_secret=$(sed -n 's/^OAUTH_CLIENT_SECRET=//p' .env)
if [ -z "$task_secret" ]; then task_secret=$(openssl rand -hex 32); fi
if [[ ! $task_secret =~ ^[A-Za-z0-9_-]+$ ]] || [ "${#task_secret}" -lt 32 ] || [ "${#task_secret}" -gt 256 ]; then
  printf 'Existing OAUTH_CLIENT_SECRET must be an unquoted 32-256 character alphanumeric/base64url value.\n' >&2
  exit 2
fi
task_environment=$(mktemp .env.oauth.XXXXXX)
trap 'rm -f -- "$task_environment"' EXIT
sed '/^\(ENABLE_MCP\|ENABLE_MCP_OAUTH\|MCP_PUBLIC_URL\|MCP_OAUTH_CLIENT_ID\|MCP_OAUTH_REDIRECT_URI\|OAUTH_CLIENT_SECRET\|TRUSTED_PROXY_IP\)=/d' .env > "$task_environment"
printf '\nENABLE_MCP=true\nENABLE_MCP_OAUTH=true\nMCP_PUBLIC_URL=%s\nMCP_OAUTH_CLIENT_ID=telegram-gateway\nMCP_OAUTH_REDIRECT_URI=%s\nOAUTH_CLIENT_SECRET=%s\nTRUSTED_PROXY_IP=%s\n' "$task_origin" "$task_callback" "$task_secret" "$task_proxy" >> "$task_environment"
chmod 600 "$task_environment"
mv -- "$task_environment" .env
printf 'OAuth configured. Client ID: telegram-gateway. Read OAUTH_CLIENT_SECRET privately from .env when registering the client.\n'
printf 'Owner consent uses the gateway API_KEY in the HTTPS authorization form; do not give that key to the OAuth client.\n'
printf 'Configuration backup: %s\n' "$task_backup"
