# Telegram News Delivery Gateway

A single-user .NET 10 service that receives an already prepared technology digest and delivers
it to one configured private Telegram chat. The gateway does not research or generate news.

**Required before production:** your own bot token, private chat ID, strong API key, and an
HTTPS reverse proxy. Real AI-to-Telegram delivery requires installation-specific verification.

## Architecture

```text
External assistant / scheduled workflow
  -> HTTPS + X-Api-Key
  -> Minimal HTTP API / optional MCP tools
  -> DeliveryService -> SQLite durable outbox (WAL)
  -> single delivery worker -> Telegram Bot API -> configured private chat
```

Adapter isolates Telegram HTTP and formatting; Queue/Worker uses SQLite as the durable queue.
The API and MCP reuse the same application services. Core has no package references.
Dependencies are EF Core SQLite, Markdig, FluentValidation, ASP.NET Core OpenAPI, and the
official MCP C# SDK. No SPA, Redis, or broker is required. Minimal API follows the explicit
project brief, taking precedence over the house skill's controller default.

Assumption: one technology digest per calendar date, enforced by a unique date index.
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

## AI integration and automatic delivery

See [docs/integrations.md](docs/integrations.md) for exact enablement steps and boundaries.

1. **External scheduled workflow:** implemented in `scripts/deliver_digest.py`. Accepts already
   generated JSON (`--input`) or calls a separate OpenAI Responses API researcher (`--generate`).
   Caches content before submission, uses a stable daily key, and polls to Delivered. A systemd
   service/timer example is included. API billing/key/model are separate from ChatGPT.
2. **MCP client with custom headers:** three tools reuse DeliveryService; enable `/mcp` and
   supply X-Api-Key. Tested with the official SDK client through the actual HTTP pipeline.
3. **Custom ChatGPT app/plugin:** needs separate registration, permitted write tools and
   supported authentication. This endpoint does not implement ChatGPT OAuth 2.1, protected
   resource metadata, token audience validation or mTLS. Do not disable auth to make it connect.
4. **Manual webhook fallback:** the authenticated examples above.

No evidence here proves that ChatGPT scheduled tasks can call this API/app. An HTTP/MCP test
does not establish automatic ChatGPT delivery, and this service cannot read ChatGPT chats.

```bash
python3 scripts/deliver_digest.py --input examples/sample-ukrainian-digest.json
# Privately set OPENAI_API_KEY and an OPENAI_MODEL supporting web_search.
python3 scripts/deliver_digest.py --generate --prompt examples/research-prompt.txt \
  --state-dir /var/lib/telegram-digest
```

The timer's 09:00 Europe/Kyiv is an editable example, not an installed schedule. systemd handles
wall-clock/DST scheduling; pick an hour outside DST transitions. Persistent catch-up runs
produce the current local day's digest, not every missed day. Linux Python 3.10+ and tzdata
are required only for the external workflow.

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
| Workflow fails | Inspect cached payload/status privately; verify API billing/model/tzdata |
| ChatGPT cannot connect | Static API-key auth is not ChatGPT OAuth; see integration guide |

Measured results and remaining installation checks are in [docs/validation.md](docs/validation.md).
No release version, commit, tag, registry push or actual schedule has been created.

## License

See [LICENSE](LICENSE).
