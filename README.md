# TelegramGateway

A single-user .NET 10 gateway that lets AI agents, applications, scripts and monitoring systems
send notifications to one configured private Telegram chat through an HTTP API or MCP tools.
It accepts prepared text and structured digests; content generation belongs to the caller.

**Required before production:** your own bot token, private chat ID, strong API key, and an
HTTPS reverse proxy. Verify message delivery when configuring an installation.

## Architecture

```text
AI agent / application / script / monitoring system
  -> HTTPS + X-Api-Key
  -> Minimal HTTP API / optional MCP tools
  -> DeliveryService -> SQLite durable outbox (WAL)
  -> single delivery worker -> Telegram Bot API -> configured private chat
```

Adapter isolates Telegram HTTP and formatting; Queue/Worker uses SQLite as the durable queue.
The API and MCP reuse the same application services. Core has no package references.
Dependencies are EF Core SQLite, Markdig, FluentValidation, ASP.NET Core OpenAPI, and the
official MCP C# SDK. No SPA, Redis, or broker is required.

The digest interface allows one digest per calendar date, enforced by a unique date index.
Use `/api/messages` for other notifications. One process per local data volume is supported;
an exclusive file lock prevents a second worker from reconciling live attempts. Avoid NFS.

## Delivery behavior

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
A send can succeed before its acknowledgement is lost or before the local ledger is committed.
Even a retry after an upstream 5xx can involve upstream side effects. See
[ADR 0001](docs/adr/0001-stop-after-uncertain-telegram-writes.md).

HTML accepts well-formed `b`, `strong`, `i`, `em`, `u`, `ins`, `s`, `strike`, `del`, `code`,
`pre`, `blockquote`, `tg-spoiler`, and `a href` with absolute HTTP(S) URLs. Unknown tags,
attributes, unsafe URLs and external entities are rejected. Use literal newlines rather than
browser `<br>` tags. Markdown headings become bold, lists retain labels, source links retain
destinations, and raw HTML is escaped. Tables/images/extensions are outside the text interface.
The splitter conservatively counts decoded UTF-16 units against 4096, preserves graphemes and
entities, and closes/reopens tags. It prefers newlines when a chunk is mostly full.

## Docker quick start

```bash
cp .env.example .env
chmod 600 .env
# Fill bot token, positive private chat ID, API key and public hostname.
docker compose -f docker-compose.yml -f docker-compose.build.yml up --build -d
docker compose exec -T gateway curl -fsS http://localhost:8080/api/ready
```

Compose publishes **no host port**. Attach your reverse proxy/tunnel to `telegram-gateway`.
The runtime is non-root, with a read-only root filesystem, temporary `/tmp`, and persistent
`/app/Data`. [INSTALL.md](INSTALL.md) covers bot setup, Portainer, Nginx Proxy Manager, Cloudflare
Tunnel and backup. For loopback-only testing add `-f docker-compose.local.yml` (127.0.0.1:5080).

## Configuration

Required aliases win over their equivalent prefixed options. Secrets belong in environment
variables or development user-secrets, never committed appsettings.

| Variable | Purpose / default |
|---|---|
| `TELEGRAM_BOT_TOKEN` | Required; alias for `TELEGRAMGATEWAY_Gateway__BotToken` |
| `TELEGRAM_CHAT_ID` | Required positive private chat ID; alias for `TELEGRAMGATEWAY_Gateway__ChatId` |
| `API_KEY` | Required 32–256 characters; alias for `TELEGRAMGATEWAY_Gateway__ApiKey` |
| `ASPNETCORE_URLS` | Image: `http://+:8080` |
| `TELEGRAMGATEWAY_Gateway__DataPath` | Local: `Data`; image: `/app/Data` |
| `TELEGRAMGATEWAY_Gateway__AllowedHosts__0` | Explicit hostname; default `localhost` |
| `TELEGRAMGATEWAY_Gateway__AllowedHosts__1` | Optional public hostname, without scheme/port |
| `TELEGRAMGATEWAY_Gateway__KnownProxies__0` | Optional exact proxy IP; blank disables forwarding |
| `TELEGRAMGATEWAY_Gateway__EnableMcp` | `false`; enables `/mcp` |
| `TELEGRAMGATEWAY_Gateway__MessageLength` | `4096`, allowed 16–4096 |
| `TELEGRAMGATEWAY_Gateway__MaxParts` | `64`, allowed 1–64 |
| `TELEGRAMGATEWAY_Gateway__MaxAttempts` | `4`, allowed 1–6 per part and processing run |
| `TELEGRAMGATEWAY_Gateway__RetryBaseMilliseconds` | `1000`; default backoff 1s, 2s, 4s |
| `TELEGRAMGATEWAY_Gateway__MaxRetryAfterSeconds` | `300`; larger delays stop |
| `TELEGRAMGATEWAY_Gateway__MessageIntervalMilliseconds` | `1100`; chat pacing |
| `TELEGRAMGATEWAY_Gateway__RequestLimitPerMinute` | `30` per connection IP; no queue |

Compose maps `GATEWAY_HOST`, `TRUSTED_PROXY_IP`, `GATEWAY_NETWORK`, `ENABLE_MCP`, and
`REQUEST_LIMIT_PER_MINUTE`. Add advanced options explicitly to its `environment:`.
`.env` is interpolation, not automatic container environment passthrough. Startup validates
required configuration. Generate an API key with `openssl rand -hex 32`.

## HTTP API

Use `X-Api-Key` on every route except `/api/health` and `/api/ready`. Unknown JSON fields
(including destination `chatId`) are rejected. Status routes return metadata only.

| Method | Route | Behavior |
|---|---|---|
| POST | `/api/messages` | Queue plain/HTML/Markdown notification; optional idempotency key |
| POST | `/api/digests` | Queue one digest per date; default key `digest-yyyy-MM-dd` if omitted |
| GET | `/api/digests/{date}` | Metadata for `yyyy-MM-dd` |
| GET | `/api/deliveries/{id}` | Metadata, IDs, attempts and safe errors |
| GET | `/api/health` | Anonymous liveness |
| GET | `/api/ready` | Anonymous local table/storage readiness; no Telegram call |
| GET | `/openapi/v1.json` | Authenticated OpenAPI document with API-key security scheme |
| POST | `/mcp` | Optional secured SDK Streamable HTTP endpoint |

```bash
export GATEWAY_URL=https://gateway.example.invalid
# Set API_KEY privately; do not paste its literal value into shell history.
curl --fail-with-body "$GATEWAY_URL/api/messages" \
  -H "X-Api-Key: $API_KEY" -H 'Content-Type: application/json' \
  --data '{"text":"Test notification","format":"html","disableNotification":false,"idempotencyKey":"setup-test"}'

curl --fail-with-body "$GATEWAY_URL/api/digests" \
  -H "X-Api-Key: $API_KEY" -H 'Content-Type: application/json' \
  --data-binary @examples/sample-ukrainian-digest.json

curl --fail-with-body "$GATEWAY_URL/api/digests/2026-10-08" -H "X-Api-Key: $API_KEY"
curl --fail-with-body "$GATEWAY_URL/openapi/v1.json" -H "X-Api-Key: $API_KEY"
```

The Ukrainian sample is explicitly a formatting fixture, not claimed current news. Poll the
returned Location with the origin prepended, then check all parts/links in Telegram.
Failures use `{ "error": "Readable sentence." }`; missing rows have a bare 404.
Malformed input → 400; wrong/missing key → identical 401; uncertain/conflicting delivery → 409;
body above 128 KiB → 413; rate limit → 429. The 100000-character validation limit does not
override the UTF-8 byte limit: Ukrainian text can reach the byte limit sooner.

## Client integrations

AI agents and other systems can use the authenticated HTTP endpoints directly. MCP clients
can use `send_telegram_message`, `send_technology_digest`, and `get_digest_delivery_status`
after enabling `/mcp` and supplying `X-Api-Key` on every request.

The current authentication mechanism is a static API key. Clients that require OAuth need an
additional compatible authorization implementation; OAuth discovery and token validation are
not implemented. Check the client's transport and authentication requirements before connecting.

The gateway queues and delivers submitted content. A calling system owns content preparation,
event triggers and scheduling. See [docs/integrations.md](docs/integrations.md) for the HTTP
and MCP contracts, optional client helper, and delivery verification.

## Security and operations

- Terminate HTTPS at the proxy; restrict direct access to its network.
- Key comparison hashes both credentials to a fixed size and uses constant-time equality.
- Telegram HttpClient loggers are disabled because the token is in its URL. Logs/status contain
  stable error codes and IDs; vendor descriptions and message content are not logged.
- Forwarded headers require exact trusted IPs. Host allowlisting and MCP Origin checks are
  enabled. Failed auth counts toward rate limits; health probes are excluded.
- Protect .env and Docker/Portainer administration. Docker administrators can read container
  environment values. API keys are single-user credentials, not per-user authorization.
- The private data volume retains message HTML **without content encryption**. Restrict host
  access, encrypt the disk/backups as needed, and never share DB files. Credentials are not in DB.
- Cloudflare Access or equivalent network controls are recommended as an additional layer.
- Back up the stopped volume or use SQLite's online backup, rather than copying only a live
  DB file while ignoring its WAL. Deleting ledger rows also removes idempotency protection.
- Ambiguous outcomes require operator review; there is no unsafe HTTP reset/delete endpoint.

## Local development and validation

```bash
dotnet user-secrets set 'Gateway:BotToken' 'YOUR_BOT_TOKEN' --project src/TelegramGateway.Api
dotnet user-secrets set 'Gateway:ChatId' 'YOUR_PRIVATE_CHAT_ID' --project src/TelegramGateway.Api
dotnet user-secrets set 'Gateway:ApiKey' 'YOUR_RANDOM_32_PLUS_CHARACTER_KEY' --project src/TelegramGateway.Api
dotnet run --project src/TelegramGateway.Api --urls http://127.0.0.1:5080
dotnet restore
dotnet build -c Release --no-restore
bash run-tests.sh
python3 -m unittest discover -s scripts -p 'test_*.py'
docker build -t telegram-gateway:local .
python3 scripts/docker-smoke.py
```

Use Git Bash on Windows, or the equivalent `dotnet test -c Release --no-build --no-restore`.
HTTP tests use WebApplicationFactory, real SQLite/migrations/auth, and a recording fake Telegram
HTTP handler. Background work is driven deterministically. Docker smoke uses fake credentials
and an internal network that blocks outbound Telegram, then cleans up only its unique stack.
CI validates .NET, external workflow mocks, the image and isolated container lifecycle.

## Structure and migrations

`src/TelegramGateway.Core` contains pure contracts/entities/options;
`Infrastructure` contains SQLite/Telegram/formatting; `Api` contains endpoints, validation,
services, MCP and migrations. There is one test project per source layer and shared
`TestInfrastructure`. `scripts`, `examples`, and `docs` hold operations and integration material.

Migrations live in the API project and apply on startup. The design-time factory needs no bot
credentials. Use an EF CLI matching the pinned EF runtime when adding migrations:

```bash
dotnet ef migrations add Name --project src/TelegramGateway.Api \
  --startup-project src/TelegramGateway.Api --configuration Release
```

## Troubleshooting

| Symptom | Action |
|---|---|
| Immediate `process_interrupted` with an older image | Rebuild with the Telegram URL fix; the token must remain in the HTTPS path. Check safe worker failure types in logs; inspect existing uncertain deliveries before any new submission |
| Options startup failure | Fill required env vars, positive chat ID and allowed hostnames |
| File lock / DB locked | Stop duplicate instance; use a local volume with correct permissions |
| Host not allowed | Set GATEWAY_HOST; retain localhost for probes |
| Gateway 401 | Exactly one X-Api-Key header; check Access policy separately |
| Invalid HTML | Escape ampersands/use allowed well-formed tags, or send Markdown/plain |
| telegram_400 | Check formatting/chat ID |
| telegram_401 | Correct/rotate the BotFather token |
| telegram_403 | Start/unblock the bot in the private chat |
| telegram_rate_limited | Reduce frequency and wait for flood control |
| Outcome unknown / interrupted | Inspect chat before replay; see ADR 0001 |
| Gateway 429 | Wait; configure the exact trusted proxy if distinct client IPs are needed |
| Data permission error | Fix volume ownership for image user app; do not run the app as root |
| Client cannot connect | Verify HTTPS, the enabled interface and support for X-Api-Key; OAuth-only clients require additional authorization support |

Test results and installation verification steps are in [docs/validation.md](docs/validation.md).

## License

See [LICENSE](LICENSE).
