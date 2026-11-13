#!/usr/bin/env bash
# Turns the news module on for a running gateway stack (INSTALL.md §8, step 3).
# Run it on the Docker host, as the user that owns the stack folder:
#
#   bash scripts/enable-news.sh [stack folder] [compose project]
#
# It asks for the WordPress user and its Application Password (hidden input), checks them against the
# theme's news route before changing anything, backs up .env, writes the NEWS_* settings and recreates
# the gateway container. The password is never printed.
set -euo pipefail
umask 077
stack=${1:-/opt/telegram-gateway}
project=${2:-telegram-gateway}
site=${NEWS_WORDPRESS_BASE_URL:-https://vahac.com}
cd "$stack"
[[ -f .env ]] || { echo "No .env in $stack." >&2; exit 1; }

read -rp "WordPress user with the role 'News publisher' [news-gateway]: " wp_user
wp_user=${wp_user:-news-gateway}
read -rsp 'Its Application Password (input hidden): ' wp_password; echo
wp_password=${wp_password// /}  # WordPress shows it in groups of four; the spaces are optional.
if [[ ${#wp_password} -lt 16 ]]; then echo 'That is not an Application Password (expected 24 characters).' >&2; exit 2; fi

# A known date with no issue: 404 means the user and password are accepted, 401/403 means they are not.
work=$(mktemp -d); trap 'rm -rf -- "$work"' EXIT
printf 'user = "%s:%s"\n' "$wp_user" "$wp_password" > "$work/curlrc"
status=$(curl -sS -o "$work/body" -w '%{http_code}' --max-time 30 -K "$work/curlrc" "$site/wp-json/vahac/v1/news/days/2000-01-01" || echo 000)
case "$status" in
  404) echo "WordPress accepted $wp_user." ;;
  401|403)
    code=$(sed -n 's/.*"code":"\([a-z_]*\)".*/\1/p' "$work/body")
    case "$code" in
      incorrect_password) hint="the password is not an Application Password of $wp_user (create one in that user's profile, not your own)" ;;
      invalid_username) hint="there is no user $wp_user" ;;
      application_passwords_disabled) hint="Application Passwords are switched off (Wordfence → All Options)" ;;
      rest_forbidden) hint="the user lacks the role 'News publisher'" ;;
      *) hint="see the code" ;;
    esac
    echo "WordPress refused $wp_user ($status, ${code:-no code}): $hint. Nothing was changed." >&2; exit 1 ;;
  *) echo "Unexpected answer $status from $site; nothing was changed." >&2; head -c 300 "$work/body" >&2; echo >&2; exit 1 ;;
esac

cp -p .env ".env.before-news-$(date +%Y%m%d-%H%M%S)"
set_env() {  # set_env NAME VALUE: replace the line or append it, without echoing the value
  local name=$1 value=$2 tmp
  tmp=$(mktemp "$stack/.env.XXXXXX")
  grep -v "^${name}=" .env > "$tmp" || true
  printf '%s=%s\n' "$name" "$value" >> "$tmp"
  chmod 600 "$tmp"; mv "$tmp" .env
}
set_env NEWS_ENABLED true
set_env NEWS_WORDPRESS_BASE_URL "$site"
set_env NEWS_WORDPRESS_USER "$wp_user"
set_env WORDPRESS_APP_PASSWORD "$wp_password"
set_env NEWS_NOTIFY_TELEGRAM true
unset wp_password

files=(-f docker-compose.yml)
[[ -f docker-compose.build.yml ]] && files+=(-f docker-compose.build.yml)
[[ -f docker-compose.npm.yml ]] && files+=(-f docker-compose.npm.yml)
docker compose -p "$project" "${files[@]}" up -d --no-build --pull never --wait --wait-timeout 120
echo 'News module enabled. Undo: restore the newest .env.before-news-* and run the same docker compose up.'
