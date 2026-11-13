#!/usr/bin/env bash
# Live check of the news module: the real gateway, the real WordPress theme and the real Telegram chat.
# It asks for the gateway URL and API key (hidden input) and never prints or stores them outside a private temp file.
#
#   bash scripts/check-news.sh              publish the fixture under today + 1 year, then show the duplicate report
#   bash scripts/check-news.sh --cleanup    remove that fixture again (gateway rows + item posts; see INSTALL.md for the day post)
set -euo pipefail
cd "$(dirname "$0")/.."
umask 077
fixture=examples/news-sample-issue.json
export PYTHONIOENCODING=utf-8  # Ukrainian titles in the tables, also on a Windows console.

usage() { printf 'Usage: bash scripts/check-news.sh [--cleanup [yyyy-MM-dd]]\n' >&2; }
mode=check
cleanup_date=""
case "${1:-}" in
  "") ;;
  --cleanup) mode=cleanup; cleanup_date="${2:-}" ;;
  *) usage; exit 2 ;;
esac

python=""
for candidate in python3 python; do
  if command -v "$candidate" >/dev/null 2>&1 && "$candidate" -c 'import json, sys; sys.exit(0 if sys.version_info >= (3, 8) else 1)' 2>/dev/null; then
    python=$candidate; break
  fi
done
if [[ -z "$python" ]]; then echo 'Python 3.8+ is required (python3 or python).' >&2; exit 1; fi
command -v curl >/dev/null || { echo 'curl is required.' >&2; exit 1; }
[[ -f "$fixture" ]] || { echo "Missing $fixture; run the script from the repository." >&2; exit 1; }

read -rsp 'Gateway URL, e.g. https://gateway.example.com (input hidden): ' gateway_url; echo
read -rsp 'Gateway API key (input hidden): ' api_key; echo
gateway_url=${gateway_url%/}
# Plain http is accepted only for a gateway on this machine (docker-compose.local.yml).
if [[ ! $gateway_url =~ ^https://[A-Za-z0-9.-]+(:[0-9]+)?$ && ! $gateway_url =~ ^http://localhost(:[0-9]+)?$ ]]; then
  echo 'The gateway URL must be an https origin without a path, e.g. https://gateway.example.com' >&2; exit 2
fi
if [[ ${#api_key} -lt 32 ]]; then echo 'The API key has fewer than 32 characters; check that you pasted API_KEY from .env.' >&2; exit 2; fi

work=$(mktemp -d)
trap 'rm -rf -- "$work"' EXIT
# The key goes into a private header file, so it never appears in the process list or shell history.
printf 'X-Api-Key: %s\n' "$api_key" > "$work/headers"
unset api_key

# api METHOD PATH [BODY_FILE] -> prints the HTTP status; the body is in $work/response.
api() {
  local arguments=(-sS -o "$work/response" -w '%{http_code}' -X "$1" -H @"$work/headers" --max-time 120)
  if [[ $# -ge 3 ]]; then arguments+=(-H 'Content-Type: application/json' --data-binary @"$3"); fi
  curl "${arguments[@]}" "$gateway_url$2" || echo 000
}

# Dates far in the future never collide with a real issue.
read -r first_date second_date < <("$python" -c '
import datetime
today = datetime.date.today()
try: first = today.replace(year=today.year + 1)
except ValueError: first = today.replace(year=today.year + 1, day=28)
print(first.isoformat(), (first + datetime.timedelta(days=1)).isoformat())')
second_date=${second_date%$'\r'}  # Python on Windows ends the line with CRLF.

report() {
  "$python" - "$work/response" <<'PYEOF'
import json, sys
status = json.load(open(sys.argv[1], encoding="utf-8"))
print(f"Status: {status['status']} (revision {status['revision']}), {status['publishedItemCount']} of {status['itemCount']} items on the site, "
      f"{status['failedItemCount']} failed{', error ' + status['errorCode'] if status.get('errorCode') else ''}")
print(f"Day page: {status.get('dayUrl') or '(not created yet)'}")
print(f"{'#':>3}  {'state':<10} {'image':<10} {'eventKey':<34} url / error")
for item in status["items"]:
    detail = item.get("url") or ""
    if item.get("errorCode"): detail = f"error {item['errorCode']} after {item['attempts']} attempt(s)"
    image = item.get("image") or "-"
    if item.get("imageError"): image += f" ({item['imageError']})"
    print(f"{item['number']:>3}  {item['status']:<10} {image:<10} {item['eventKey']:<34} {detail}")
for removal in status.get("removals", []):
    print(f"  removal {removal['eventKey']}: {removal['status']} {removal.get('errorCode') or ''}")
notice = status.get("notice")
print("Telegram notice: " + (f"{notice['status']}{' (requires review)' if notice['requiresReview'] else ''}" if notice else "not queued"))
PYEOF
}

if [[ $mode == cleanup ]]; then
  date=${cleanup_date:-$first_date}
  code=$(api DELETE "/api/news/issues/$date")
  case $code in
    202) echo "Deleting fixture $date: the gateway trashes its item posts now." ;;
    404) echo "No issue for $date in the gateway; nothing to delete there." ; exit 0 ;;
    *) echo "Gateway answered $code:"; cat "$work/response"; echo; exit 1 ;;
  esac
  for _ in $(seq 1 90); do
    sleep 10
    code=$(api GET "/api/news/issues/$date")
    if [[ $code == 404 ]]; then
      echo "Fixture $date is gone from the gateway registry, and its item posts are in the WordPress trash."
      echo "Trash the day post 'FIXTURE — not real news' in WordPress yourself (Posts list, search FIXTURE); see INSTALL.md."
      exit 0
    fi
    if [[ $code == 200 ]] && "$python" -c 'import json,sys; sys.exit(0 if json.load(open(sys.argv[1], encoding="utf-8"))["status"] == "Failed" else 1)' "$work/response"; then
      report; echo 'Deletion stopped; fix the error above and run --cleanup again.'; exit 1
    fi
  done
  echo 'Deletion did not finish within 15 minutes; run --cleanup again later.' >&2; exit 1
fi

"$python" - "$fixture" "$work/first.json" "$first_date" "$work/second.json" "$second_date" <<'PYEOF'
import json, sys
issue = json.load(open(sys.argv[1], encoding="utf-8"))
for target, date in ((sys.argv[2], sys.argv[3]), (sys.argv[4], sys.argv[5])):
    issue["date"] = date
    with open(target, "w", encoding="utf-8") as file:
        json.dump(issue, file, ensure_ascii=False)
PYEOF

echo "1) Submitting the fixture (36 fake items) as the issue for $first_date ..."
code=$(api POST /api/news/issues "$work/first.json")
case $code in
  202) echo '   Accepted (202): publication is queued.' ;;
  200) echo '   Already accepted earlier (200): showing its current state.' ;;
  *) echo "   Gateway answered $code:"; cat "$work/response"; echo
     echo '   A 409 here usually means an earlier run was not cleaned up: run bash scripts/check-news.sh --cleanup'; exit 1 ;;
esac

echo "2) Waiting for WordPress (one call at a time; about a minute) ..."
final=""
for _ in $(seq 1 120); do
  code=$(api GET "/api/news/issues/$first_date")
  if [[ $code == 200 ]]; then
    final=$("$python" -c 'import json,sys; print(json.load(open(sys.argv[1], encoding="utf-8"))["status"])' "$work/response")
    case $final in Published|PartiallyPublished|Failed) break ;; esac
  else
    echo "   Status request answered $code."
  fi
  sleep 10
done
report
case $final in
  Published|PartiallyPublished) ;;
  Failed) echo 'Publication failed; the error code above names the cause (INSTALL.md, news troubleshooting).' ;;
  *) echo 'Publication did not finish within 20 minutes.' >&2 ;;
esac

echo
echo "3) Submitting the same fixture under $second_date: the gateway must refuse it as duplicates ..."
code=$(api POST /api/news/issues "$work/second.json")
if [[ $code == 409 ]]; then
  "$python" - "$work/response" <<'PYEOF'
import json, sys
body = json.load(open(sys.argv[1], encoding="utf-8"))
duplicates = body.get("duplicates", [])
print(f"   409 as expected: {len(duplicates)} clashes. {body['error']}")
for entry in duplicates[:8]:
    print(f"   {entry['path']}: {entry.get('canonicalUrl') or entry.get('eventKey')} -> issue {entry['issueDate']} item {entry.get('itemNumber', '-')} ({entry['title']})")
if len(duplicates) > 8: print(f"   ... and {len(duplicates) - 8} more")
PYEOF
else
  echo "   Unexpected answer $code:"; cat "$work/response"; echo
fi

echo
echo "Check Telegram: exactly one line '📰 FIXTURE — not real news — ...: <day page>' should have arrived."
echo "Open the day page while logged in to WordPress, then clean up with: bash scripts/check-news.sh --cleanup"
