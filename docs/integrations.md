# Client integrations

The gateway lets AI agents, applications, scripts and monitoring systems submit notifications
for delivery to one configured private Telegram chat. Callers prepare the content and decide
when to submit it. Telegram bot credentials remain in the gateway; callers receive the gateway
URL and API key.

## HTTP API

Use HTTPS and send exactly one `X-Api-Key` header. Submit notifications to `POST /api/messages`
and dated digests to `POST /api/digests`. See [README.md](../README.md#http-api) for request
examples and endpoint details.

Submission returns `202 Accepted` with a delivery identifier and `Location` for status polling.
Read that location until the delivery reaches a terminal state. Only `Delivered` confirms
recorded Telegram message IDs. An uncertain result with `requiresReview: true` requires an
operator to inspect the destination chat before attempting another submission.

Use a stable idempotency key for the same logical notification. When retrying, retain both the
key and the original payload. A different payload under the same key is rejected. Digests are
also unique by date; use the message endpoint for multiple notifications on the same day.

## MCP tools

The optional Streamable HTTP endpoint uses the official
[MCP C# SDK](https://github.com/modelcontextprotocol/csharp-sdk).
Set `ENABLE_MCP=true` in Compose and recreate the gateway container. Connect a client to
`https://gateway.example.invalid/mcp`, replacing the hostname with the configured gateway.
Every request requires `X-Api-Key`; Host and Origin checks remain enabled.

| Tool | Purpose | Inputs |
|---|---|---|
| `send_telegram_message` | Queue a notification | `text`, `idempotencyKey`; optional `format`, `disableNotification` |
| `send_technology_digest` | Queue a prepared digest | `title`, `date` (`yyyy-MM-dd`), `content`, `idempotencyKey`; optional `language`, `format`, `disableNotification` |
| `get_digest_delivery_status` | Read digest delivery metadata | `date` (`yyyy-MM-dd`) |

The digest tool retains its existing name but accepts caller-prepared content. It does not
research news. Submission and status tools use the same delivery service and persistent ledger
as the HTTP API. Clients should poll status after submitting a digest.

The current endpoint uses static API-key authentication. OAuth discovery, OAuth token validation
and user login are not implemented. Verify that the chosen MCP client supports Streamable HTTP
and custom authentication headers. Clients requiring OAuth need a compatible authorization
implementation before they can connect. Do not disable authentication to bypass this requirement.

## Optional Python client

`scripts/deliver_digest.py` can submit prepared digest JSON and poll its delivery status:

```bash
python3 scripts/deliver_digest.py --config /path/to/private/gateway-client.env \
  --input examples/sample-ukrainian-digest.json
```

The client requires Python 3.10+ and timezone data. The sample JSON is a formatting fixture.
The private configuration file uses literal, unquoted entries:

```text
GATEWAY_URL=https://gateway.example.invalid
API_KEY=YOUR_GATEWAY_API_KEY
```

An optional Cloudflare Access layer requires both `CF_ACCESS_CLIENT_ID` and
`CF_ACCESS_CLIENT_SECRET`. These supplement the gateway API key. Do not include the Telegram
bot token. Protect the file with mode 0600 on Linux or a user-restricted ACL on Windows; never
commit it. See [the configuration template](../examples/gateway-client.env.example).

`--config` replaces the client's credential environment variables. The file is parsed as data,
not evaluated as a shell script. The helper rejects remote plaintext HTTP and redirects and
avoids printing credentials or remote response bodies.

## Delivery verification

Verify a notification from the actual calling system, read its final status and inspect the
Telegram chat. Check formatting, links and all parts of a split message. Repeat an identical
submission to verify duplicate prevention, then restart the gateway and confirm status persists.
If the calling system uses event triggers or a schedule, verify those separately: a running
gateway or successful manual submission does not establish that the caller will run again.
