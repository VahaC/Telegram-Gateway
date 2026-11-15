# AgentGateway

A single-user .NET 10 gateway between AI agents and two outputs:

- **Telegram**: agents, applications, scripts and monitoring systems send notifications to one
  configured private Telegram chat through an HTTP API or MCP tools.
- **WordPress news**: a Claude cloud routine submits one finished daily tech-news issue; the
  gateway keeps every accepted item as the duplicate registry and publishes the issue to the
  vahac.com theme (one private post per item, with the source's picture, plus a day page), then
  sends one Telegram line with the day page link. To feed that routine, the gateway reads the
  publishers' own RSS/Atom feeds like a personal feed reader, and a watchdog tells the owner on
  Telegram when no issue arrived.

Content generation belongs to the caller. AgentGateway was called TelegramGateway; existing
installations keep working unchanged (see [Renaming](INSTALL.md#9-renaming-to-agentgateway)).

**Required before production:** your own bot token, private chat ID, strong API key, and an
HTTPS reverse proxy. The news module additionally needs a WordPress Application Password.

## Architecture

```text
AI agent / application / script / monitoring system
  -> HTTPS + X-Api-Key, or MCP (API key or OAuth bearer)
  -> Minimal HTTP API / optional MCP tools
  -> DeliveryService -> SQLite outbox (WAL) -> delivery worker -> Telegram Bot API -> private chat

Claude cloud routine (daily news)
  -> POST /api/news/issues or MCP submit_news_issue (whole issue, one call)
  -> NewsService: structure (400) -> editorial gate (422) -> duplicate registry (409)
  -> SQLite: issue, items, sources, releases, alerts = the registry and the outbox
  -> news worker, one call at a time -> WordPress /wp-json/vahac/v1/news (day, items, verify)
  -> one Telegram line through DeliveryService: "📰 <title> — 38 новин: <day page>"

Feed collector (homelab, hourly)                Watchdog (daily, 06:00 UTC)
  catalog NewsFeeds/news-feeds.json               no issue for today's Kyiv date?
  -> RSS/Atom GET per feed (one per host)         -> one Telegram alert per date
  -> SQLite NewsFeedEntries (14 days)                through DeliveryService
  -> MCP get_news_feed / get_news_feed_entry -> Claude routine
```

Adapters isolate Telegram HTTP, WordPress HTTP and formatting; Queue/Worker uses SQLite as the
durable queue for both outputs. The API and MCP reuse the same application services. Core has no
package references. Dependencies are EF Core SQLite, Markdig, FluentValidation, ASP.NET Core
OpenAPI, OpenIddict and the official MCP C# SDK. No SPA, Redis, or broker is required.

One process per local data volume is supported; an exclusive file lock prevents a second worker
from reconciling live attempts. Avoid NFS.

## Telegram delivery behavior

- Submission returns **202 Accepted**. Poll its `Location`; only `Delivered` confirms recorded
  Telegram message IDs. Statuses: Pending, Sending, PartiallyDelivered, Delivered, Failed.
- A key binds to a SHA-256 hash of the original payload. Changed payload or a different key
  for an existing digest date returns 409. Completed submissions are not sent again.
- Repeat a known failed/partial submission with the identical key and payload to retry only
  parts without recorded Telegram IDs. Attempts and IDs persist across restarts.
- Telegram 429 honors `retry_after`; an excessive delay stops instead of retrying early.
  Its durable `retryNotBeforeUtc` blocks subsequent sends to the configured chat across restart.
  5xx and known pre-send connection failures use bounded exponential backoff. Permanent 4xx
  errors are not retried automatically.
- A timeout, dropped response, invalid success response, or interrupted in-flight attempt sets
  `requiresReview: true`. Automatic resubmission returns 409; inspect the private chat first.

**Exactly-once delivery is not promised.** Telegram `sendMessage` has no idempotency key.
See [ADR 0001](docs/adr/0001-stop-after-uncertain-telegram-writes.md).

HTML accepts well-formed `b`, `strong`, `i`, `em`, `u`, `ins`, `s`, `strike`, `del`, `code`,
`pre`, `blockquote`, `tg-spoiler`, and `a href` with absolute HTTP(S) URLs. Unknown tags,
attributes, unsafe URLs and external entities are rejected. Use literal newlines rather than
browser `<br>` tags. Markdown headings become bold, lists retain labels, source links retain
destinations, and raw HTML is escaped. Tables/images/extensions are outside the text interface.
The splitter conservatively counts decoded UTF-16 units against 4096, preserves graphemes and
entities, and closes/reopens tags. It prefers newlines when a chunk is mostly full.

The dated digest interface (`/api/digests`, MCP `send_technology_digest`) still works but is
**deprecated**: the daily news now goes to the website through the news module.

## News module

The issue format is the shared contract "news issue v1" in
[docs/prompts/news-module.md](docs/prompts/news-module.md#shared-contract-news-issue-v1); the
WordPress theme implements the other side. The workflow for AI callers is in
[docs/integrations.md](docs/integrations.md#news-workflow-for-ai-callers).

- **Validation**: structure (400 with `errors`), then the editorial gate (422 with `violations`,
  each starting with a code: `min_items`, `max_items`, `category_min`, `ai_share`, `top_count`,
  `top_numbering`, `numbering`), then duplicates (409 with `duplicates`). By default an issue has
  5–40 items, no category is mandatory (`category_min` only applies when
  `News:MinPerCategory` > 0) and at most 30% are `ai`.
- **Registry**: every accepted item stays stored. A source URL is compared in canonical form
  (lower-case host, no `www.`, no default port, no fragment, no `utm_*`/`fbclid`/`gclid`/`mc_cid`/
  `mc_eid`/`ref`/`source` parameters, sorted query, no trailing slash, http = https). An item is
  refused when its canonical URL or `eventKey` exists in any other issue, repeats inside the
  issue, or when one of its `extraSources` is another item's primary URL. Releases and security
  alerts are compared only with earlier releases and alerts.
- **Publication** ([ADR 0003](docs/adr/0003-idempotent-wordpress-publication.md)): the worker
  sends the day, every unfinished item (in number order), removals of items a replacement dropped,
  then reads the day back to verify. Retries are automatic because every call is an upsert by
  key and revision. States: Pending → Publishing → Published, PartiallyPublished or Failed.
  An image WordPress cannot fetch is recorded (`image: failed`) but does not fail anything.
- **Notice**: one Telegram line per revision with the day URL WordPress returned, e.g.
  `📰 Техдайджест 14.11.2026 — 38 новин: https://vahac.com/news/2026-11-14/`
  (or `37 з 38 новин` when partially published). The news texts never go to Telegram.

## News feeds

Several key publishers block the cloud agent's requests, so the gateway, running at home, reads
their own RSS/Atom feeds and hands the entries to the routine ([ADR 0004](docs/adr/0004-the-gateway-collects-rss-instead-of-the-agent-fetching-sites.md)).
It is on whenever the news module is on; `News:FeedsEnabled=false` switches it off.

- **Catalog**: `src/AgentGateway.Api/NewsFeeds/news-feeds.json` ships with the build and the image
  (`/app/NewsFeeds/`); `News:FeedCatalogPath` points elsewhere. Each feed: `id` (unique kebab-case,
  ≤ 60), `name`, `url` (https only), `categories` (1–3 of the six), `kind` (`vendor`, `release`,
  `news`, `blog`, `advisory`, `community`, `aggregator`), `language` (ISO 639-1). At startup the
  catalog is synced into the database: new feeds are added, changed ones updated, removed ones
  disabled (their entries stay). A broken feed is skipped with a warning naming the field; an
  unreadable file leaves the table as it was.
- **Polling**: shortly after start, then every `News:FeedPollMinutes` (60). Up to
  `News:FeedParallelism` (4) hosts at a time, one request per host, with the gateway's own
  User-Agent `AgentGateway-NewsFeed/1.0 (personal feed reader)` and a conditional GET (ETag /
  Last-Modified), 20 s timeout and a 16 MiB cap. 402/403/429 and every other failure are recorded
  on the feed and never retried or worked around; after a 429 the rest of that host's feeds wait
  for the next run. One log line per run: feeds ok/failed (with the failing ids), new entries.
- **Entries**: title, link, the feed's own date (null when it has none), plain-text summary of at
  most 4000 characters and an https picture named by the feed itself; article pages are never
  fetched. Unique by canonical URL; deleted 14 days after the gateway first saw them.
- **Reading them**: MCP `get_news_feed` / `GET /api/news/feed` with `sinceHours` (1–168, default
  168), `category`, `query` (up to 20 alternatives separated by `|`, case-insensitive for Latin
  letters, in title or summary), `excludePublished` (default true: drops entries the news registry
  already knows), `limit` (1–100, default 50) and `cursor` (the `nextCursor` of the previous page).
  Newest first; each page stays under 60 KB and carries `total`, `nextCursor` and `health`
  (enabled feeds, failing feeds, last successful poll). `get_news_feed_entry` /
  `GET /api/news/feed/entries/{id}` returns one entry with its whole summary.

## News watchdog

Every day at `News:WatchdogTime` (`06:00`) in `News:WatchdogTimeZone` (`UTC`, because the
routine's cron runs in UTC) the gateway checks whether an issue exists, in any status, for that
day's issue date (Europe/Kyiv). If not, it queues one Telegram message through the ordinary
outbox, for example:

```text
⚠️ Техдайджест за 16.11.2026 не надійшов: о 06:00 UTC випуску в шлюзі немає. Останній випуск: 15.11.2026. Перевір запуск routine «Daily tech news digest» у claude.ai → Code → Routines. Докладніше: README → News watchdog.
```

"Останній випуск" is the newest earlier issue date in the registry (`ще не було` when there is
none). The delivery's idempotency key is `news-watchdog-{date}`, so a repeat or a restart never
sends a second alert; a start after the check time catches up on the latest check. The time is a
wall-clock time in an IANA zone and follows DST.

When the alert arrives: open claude.ai → Code → Routines → *Daily tech news digest*, read the last
run (a failure report names the cause), fix it or run it again. A late issue is accepted as usual.
Switch the watchdog off with `News:WatchdogEnabled=false`.

## Docker quick start

```bash
cp .env.example .env
chmod 600 .env
# Fill bot token, positive private chat ID, API key and public hostname.
docker compose -f docker-compose.yml -f docker-compose.build.yml up --build -d
docker compose exec -T gateway curl -fsS http://localhost:8080/api/ready
```

The image is `agent-gateway:local`. Compose publishes **no host port**; attach your reverse
proxy/tunnel to the `telegram-gateway` network (name kept from the original install). The runtime is
non-root, with a read-only root filesystem, temporary `/tmp`, and persistent `/app/Data`.
[INSTALL.md](INSTALL.md) covers bot setup, WordPress, Portainer, Nginx Proxy Manager, Cloudflare
Tunnel, backup and the rename. For loopback-only testing add `-f docker-compose.local.yml`.

## Configuration

Settings are read from environment variables with the prefix `AGENTGATEWAY_`; the old prefix
`TELEGRAMGATEWAY_` keeps working, and the new one wins when both name the same setting. At
startup the gateway logs (names only) which old-prefix variables are still in use. Required
aliases win over their prefixed options. Secrets belong in environment variables or development
user-secrets, never committed appsettings.

| Variable | Purpose / default |
|---|---|
| `TELEGRAM_BOT_TOKEN` | Required; alias for `AGENTGATEWAY_Gateway__BotToken` |
| `TELEGRAM_CHAT_ID` | Required positive private chat ID; alias for `AGENTGATEWAY_Gateway__ChatId` |
| `API_KEY` | Required 32–256 characters; alias for `AGENTGATEWAY_Gateway__ApiKey` |
| `ASPNETCORE_URLS` | Image: `http://+:8080` |
| `AGENTGATEWAY_Gateway__DataPath` | Local: `Data`; image: `/app/Data` |
| `AGENTGATEWAY_Gateway__AllowedHosts__0` | Explicit hostname; default `localhost` |
| `AGENTGATEWAY_Gateway__AllowedHosts__1` | Optional public hostname, without scheme/port |
| `AGENTGATEWAY_Gateway__KnownProxies__0` | Optional exact proxy IP; blank disables forwarding |
| `AGENTGATEWAY_Gateway__EnableMcp` | `false`; enables `/mcp` |
| `AGENTGATEWAY_Gateway__MessageLength` | `4096`, allowed 16–4096 |
| `AGENTGATEWAY_Gateway__MaxParts` | `64`, allowed 1–64 |
| `AGENTGATEWAY_Gateway__MaxAttempts` | `4`, allowed 1–6 per part and processing run |
| `AGENTGATEWAY_Gateway__RetryBaseMilliseconds` | `1000`; default backoff 1s, 2s, 4s |
| `AGENTGATEWAY_Gateway__MaxRetryAfterSeconds` | `300`; larger delays stop |
| `AGENTGATEWAY_Gateway__MessageIntervalMilliseconds` | `1100`; chat pacing |
| `AGENTGATEWAY_Gateway__RequestLimitPerMinute` | `30` per connection IP; no queue |
| `AGENTGATEWAY_News__Enabled` | `false`; enables the news routes, tools and worker |
| `AGENTGATEWAY_News__WordPressBaseUrl` | `https://vahac.com` (https origin) |
| `AGENTGATEWAY_News__WordPressUser` | WordPress user with role `vahac_news_publisher` |
| `WORDPRESS_APP_PASSWORD` | Its Application Password; alias for `AGENTGATEWAY_News__WordPressAppPassword`; env only, never logged |
| `AGENTGATEWAY_News__MinItems` / `MaxItems` | `5` / `40` news items per issue (allowed 1–80) |
| `AGENTGATEWAY_News__MinPerCategory` | `0`: no category is mandatory; `1` requires each of the 6 |
| `AGENTGATEWAY_News__MaxAiShare` | `0.30` share of `ai` items |
| `AGENTGATEWAY_News__CallIntervalMilliseconds` | `500` between WordPress calls |
| `AGENTGATEWAY_News__MaxAttempts` | `6` per WordPress call |
| `AGENTGATEWAY_News__RetryBaseMilliseconds` | `2000`; backoff 2s, 4s, 8s … |
| `AGENTGATEWAY_News__ItemTimeoutSeconds` | `60` for item calls (WordPress downloads the image) |
| `AGENTGATEWAY_News__NotifyTelegramOnPublish` | `true`; one Telegram line per published revision |
| `AGENTGATEWAY_News__FeedsEnabled` | `true`; the feed collector and its tools (with `News:Enabled`) |
| `AGENTGATEWAY_News__FeedCatalogPath` | `NewsFeeds/news-feeds.json`, relative to the application folder |
| `AGENTGATEWAY_News__FeedPollMinutes` | `60`, allowed 5–1440 |
| `AGENTGATEWAY_News__FeedUserAgent` | `AgentGateway-NewsFeed/1.0 (personal feed reader)`; a browser token is refused |
| `AGENTGATEWAY_News__FeedParallelism` | `4` hosts at a time (one request per host), allowed 1–16 |
| `AGENTGATEWAY_News__WatchdogEnabled` | `true`; the missing-issue alert (with `News:Enabled`) |
| `AGENTGATEWAY_News__WatchdogTime` | `06:00`, 24-hour `HH:mm` |
| `AGENTGATEWAY_News__WatchdogTimeZone` | `UTC`; any IANA zone, e.g. `Europe/Kyiv` |
| `AGENTGATEWAY_McpOAuth__*` | OAuth; see [OAuth for MCP](docs/integrations.md#oauth-for-mcp) |

Compose maps `GATEWAY_HOST`, `TRUSTED_PROXY_IP`, `GATEWAY_NETWORK`, `GATEWAY_VOLUME_NAME`,
`ENABLE_MCP`, the OAuth client variables, `REQUEST_LIMIT_PER_MINUTE` and the `NEWS_*` variables
(see `.env.example`). Add advanced options explicitly to its `environment:`. `.env` is
interpolation, not automatic container environment passthrough. Startup validates required
configuration. Generate an API key with `openssl rand -hex 32`.

## HTTP API

Use `X-Api-Key` on HTTP API routes except `/api/health` and `/api/ready`. Unknown JSON fields
(including destination `chatId`) are rejected. Status routes return metadata only.

| Method | Route | Behavior |
|---|---|---|
| POST | `/api/messages` | Queue plain/HTML/Markdown notification; optional idempotency key |
| POST | `/api/digests` | **Deprecated.** Queue one Telegram digest per date |
| GET | `/api/digests/{date}` | Metadata for `yyyy-MM-dd` |
| GET | `/api/deliveries/{id}` | Metadata, IDs, attempts and safe errors |
| POST | `/api/news/issues` | Submit an issue: 202 + `Location`; identical repeat 200; 400/422/409 as above; other content for an existing date 409 (use PUT) |
| PUT | `/api/news/issues/{date}` | Replace an issue; same checks, its own items do not clash; bumps `revision` and republishes |
| GET | `/api/news/issues/{date}` | Publication state, day URL, counts per category, per item state/URL/image result/error, removals, Telegram notice |
| POST | `/api/news/issues/{date}/republish` | Re-queue the unfinished calls of a Failed/PartiallyPublished issue; no-op otherwise |
| DELETE | `/api/news/issues/{date}` | Remove the **test fixture only** (`scripts/check-news.sh --cleanup`); any real issue gets 409 |
| GET | `/api/news/history?days=30` | Registry of the last 1–180 days, newest first: items and releases/alerts with canonical URLs |
| POST | `/api/news/check` | `{ "urls": [...], "eventKeys": [...] }` (≤ 200 each) → `{ "known": [...] }` |
| GET | `/api/news/feed` | Feed entries, newest first: `sinceHours`, `category`, `query`, `excludePublished`, `limit`, `cursor` (see News feeds); 404 with an error while `News:FeedsEnabled` is false |
| GET | `/api/news/feed/entries/{id}` | One feed entry with its whole summary; bare 404 for an unknown id |
| GET | `/api/health` | Anonymous liveness |
| GET | `/api/ready` | Anonymous local table/storage readiness; no Telegram call |
| GET | `/openapi/v1.json` | Authenticated OpenAPI document with API-key security scheme |
| POST | `/mcp` | Optional secured SDK Streamable HTTP endpoint |

The news routes answer 404 while `News:Enabled` is false.

```bash
export GATEWAY_URL=https://gateway.example.invalid
# Set API_KEY privately; do not paste its literal value into shell history.
curl --fail-with-body "$GATEWAY_URL/api/messages" \
  -H "X-Api-Key: $API_KEY" -H 'Content-Type: application/json' \
  --data '{"text":"Test notification","format":"html","disableNotification":false,"idempotencyKey":"setup-test"}'

curl --fail-with-body "$GATEWAY_URL/api/news/history?days=30" -H "X-Api-Key: $API_KEY"
curl --fail-with-body "$GATEWAY_URL/openapi/v1.json" -H "X-Api-Key: $API_KEY"
```

Failures use `{ "error": "Readable sentence." }` (news rejections add `errors`, `violations` or
`duplicates`); missing rows have a bare 404. Malformed input → 400; wrong/missing key → identical
401; uncertain/conflicting delivery or duplicate news → 409; editorial rule → 422; rate limit → 429.
Bodies are limited to 128 KiB, except `/api/news/issues*` and `/mcp`, which accept up to 1 MiB
(a 45-item Ukrainian issue is about 150–250 KiB). Ukrainian text reaches byte limits sooner than
character limits.

## Client integrations

MCP clients use `send_telegram_message`, `get_delivery_status`, `send_technology_digest`
(deprecated) and `get_digest_delivery_status` for Telegram, and `get_news_history`,
`check_news_candidates`, `submit_news_issue`, `replace_news_issue`, `get_news_issue_status`,
`get_news_feed` and `get_news_feed_entry` for news. Telegram tools need scope `telegram:send`, news tools `news:publish`; the API key
grants both. OAuth supports several confidential clients (ChatGPT stays client 0; a claude.ai
connector can be client 1). See [docs/integrations.md](docs/integrations.md).

## Security and operations

- Terminate HTTPS at the proxy; restrict direct access to its network.
- Key comparison hashes both credentials to a fixed size and uses constant-time equality.
- Telegram and WordPress HttpClient loggers are disabled (the bot token is in the URL, the
  Application Password in a header). Logs/status contain stable error codes and IDs; vendor
  descriptions and message/news content are not logged.
- Forwarded headers require exact trusted IPs. Host allowlisting and MCP Origin checks are
  enabled. Failed auth counts toward rate limits; health probes are excluded.
- Protect .env and Docker/Portainer administration. Docker administrators can read container
  environment values. API keys are single-user credentials, not per-user authorization.
- The private data volume retains message HTML and news issues **without content encryption**.
  Restrict host access, encrypt the disk/backups as needed, and never share DB files. OAuth
  records and private key files are also sensitive backup data.
- Optional MCP OAuth uses owner consent, exact HTTPS callbacks, PKCE S256 and per-client scopes.
  See [ADR 0002](docs/adr/0002-single-owner-mcp-oauth.md).
- Cloudflare Access or equivalent network controls are recommended as an additional layer.
- Back up the stopped volume or use SQLite's online backup, rather than copying only a live
  DB file while ignoring its WAL. Deleting ledger rows also removes idempotency protection, and
  deleting news rows removes them from the duplicate registry.
- Ambiguous Telegram outcomes require operator review; there is no unsafe HTTP reset endpoint.

## Local development and validation

```bash
dotnet user-secrets set 'Gateway:BotToken' 'YOUR_BOT_TOKEN' --project src/AgentGateway.Api
dotnet user-secrets set 'Gateway:ChatId' 'YOUR_PRIVATE_CHAT_ID' --project src/AgentGateway.Api
dotnet user-secrets set 'Gateway:ApiKey' 'YOUR_RANDOM_32_PLUS_CHARACTER_KEY' --project src/AgentGateway.Api
dotnet run --project src/AgentGateway.Api --urls http://127.0.0.1:5080
dotnet restore
dotnet build -c Release --no-restore
bash run-tests.sh
python3 -m unittest discover -s scripts -p 'test_*.py'
docker build -t agent-gateway:local .
python3 scripts/docker-smoke.py
```

The user-secrets ID is unchanged by the rename, so existing development secrets still apply.
Use Git Bash on Windows, or the equivalent `dotnet test -c Release --no-build --no-restore`.
HTTP tests use WebApplicationFactory, real SQLite/migrations/auth, and recording fake Telegram,
WordPress and feed HTTP handlers (the WordPress fake follows the contract's upsert/revision rules;
the feed tests use their own small catalogs, one test checks the real catalog).
Background work is driven deterministically. Docker smoke uses fake credentials and an internal
network that blocks outbound Telegram and WordPress, then cleans up only its unique stack.
`scripts/check-news.sh` is the live check against the real site (it asks for the URL and key).

## Structure and migrations

`src/AgentGateway.Core` contains pure contracts/entities/options and the canonical URL rule;
`Infrastructure` contains SQLite, Telegram, WordPress and formatting; `Api` contains endpoints,
validation, services, MCP and migrations. There is one test project per source layer and shared
`TestInfrastructure`. `scripts`, `examples`, and `docs` hold operations and integration material;
`examples/news-sample-issue.json` is the fixture shared with the WordPress theme;
`src/AgentGateway.Api/NewsFeeds/news-feeds.json` is the feed catalog.

Migrations live in the API project and apply on startup; the news tables come from the additive
`AddNewsRegistry` migration, the feed tables from the additive `AddNewsFeeds` migration. The SQLite files keep their names (`gateway.db`, `oauth.db`).

```bash
dotnet ef migrations add Name --project src/AgentGateway.Api \
  --startup-project src/AgentGateway.Api --context ApplicationDbContext --configuration Release
```

## Troubleshooting

| Symptom | Action |
|---|---|
| Immediate `process_interrupted` with an older image | Rebuild with the Telegram URL fix; inspect existing uncertain deliveries before any new submission |
| Options startup failure | Fill required env vars, positive chat ID, allowed hostnames; with `NEWS_ENABLED=true` also the WordPress user and Application Password |
| Empty data after a redeploy | The project/volume name changed; set `GATEWAY_VOLUME_NAME` to the old volume (INSTALL.md §9) |
| Startup warning about `TELEGRAMGATEWAY_` | Harmless; rename those variables to `AGENTGATEWAY_` when convenient |
| File lock / DB locked | Stop duplicate instance; use a local volume with correct permissions |
| Host not allowed | Set GATEWAY_HOST; retain localhost for probes |
| Gateway 401 | Exactly one X-Api-Key header; check Access policy separately |
| Invalid HTML | Escape ampersands/use allowed well-formed tags, or send Markdown/plain |
| telegram_400 / 401 / 403 | Formatting or chat ID / BotFather token / start or unblock the bot |
| telegram_rate_limited | Reduce frequency and wait for flood control |
| Outcome unknown / interrupted | Inspect chat before replay; see ADR 0001 |
| News 413 behind the proxy | Raise the proxy body limit for `/api/news/` and `/mcp` to 1 MiB (INSTALL.md §4) |
| `wp_401` / `wp_403` | Wrong user/Application Password, or the user lacks role `vahac_news_publisher`; or Cloudflare challenges `/wp-json/vahac/` |
| `wp_404` | The theme's news routes are missing (theme not updated) |
| `wp_400` | The theme refused a body; compare the theme and gateway contract versions |
| `wp_5xx` / `wp_timeout` / `wp_network` | WordPress or the network was down for all retries; fix it, then republish |
| `verify_mismatch` / `wp_missing` | WordPress reported different counts or missing posts; check the theme, then republish |
| `image: failed` on an item | WordPress could not download the source picture; the post is published without it |
| Feed `LastError` `http_403` / `http_402` / `http_429` | The publisher refuses feed readers or asked for less; nothing is retried. Remove or replace the feed in the catalog if it stays failing |
| Feed `LastError` `skipped_host_429` | Another feed on the same host answered 429 in this run; it is polled again next run |
| Feed `LastError` `not_a_feed (root <html>)` / `xml_invalid` / `too_large` / `timeout` / `network (…)` | The URL returns a web page, broken XML, more than 16 MiB, nothing within 20 s, or cannot be reached; fix the URL in the catalog |
| Startup warning "News feed catalog entry skipped" | That feed breaks a catalog rule; the message names the index, id and field |
| Watchdog alert in Telegram | No issue for the date at the check time; see News watchdog |
| Gateway 429 | Wait; configure the exact trusted proxy if distinct client IPs are needed |
| Client cannot connect | Verify HTTPS, interface and authentication; OAuth requires the configured client ID/secret and exact callback |
| OAuth tool missing from a client | Its token lacks the scope (`telegram:send` / `news:publish`); check `McpOAuth:Clients:{n}:Scopes` |

Test results and installation verification steps are in [docs/validation.md](docs/validation.md).

## License

See [LICENSE](LICENSE).
