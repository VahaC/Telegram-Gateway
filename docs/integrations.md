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
Every request requires `X-Api-Key` or, when OAuth is enabled, a valid bearer token.
Host and Origin checks remain enabled.

| Tool | Purpose | Inputs |
|---|---|---|
| `send_telegram_message` | Queue a notification | `text`, `idempotencyKey`; optional `format`, `disableNotification` |
| `send_technology_digest` | Queue a prepared digest | `title`, `date` (`yyyy-MM-dd`), `content`, `idempotencyKey`; optional `language`, `format`, `disableNotification` |
| `get_delivery_status` | Read delivery metadata for any notification or digest | `id` (the UUID returned as `Delivery.Id`) |
| `get_digest_delivery_status` | Read digest delivery metadata | `date` (`yyyy-MM-dd`) |

The digest tool retains its existing name but accepts caller-prepared content. It does not
research news. Submission and status tools use the same delivery service and persistent ledger
as the HTTP API. After either submission tool returns, retain `Delivery.Id` and poll
`get_delivery_status` with that UUID. `Pending` and `Sending` do not confirm delivery;
`Delivered` confirms recorded Telegram message IDs. Failed or partially delivered results
include counts, attempts and safe error codes. `requiresReview: true` needs chat inspection
before another submission. Status queries do not send or retry messages. An unknown UUID
produces an empty MCP result; a malformed UUID produces a tool error. The date-based tool
looks up only dated digests, so a missing digest does not establish an ordinary message's status.

## OAuth for MCP

The optional OAuth server uses OpenIddict with authorization codes, PKCE S256, rotating refresh
tokens and explicit owner consent. Configure these Compose variables:

| Variable | Value |
|---|---|
| `ENABLE_MCP` | `true` |
| `ENABLE_MCP_OAUTH` | `true` |
| `MCP_PUBLIC_URL` | Canonical public HTTPS origin |
| `MCP_OAUTH_CLIENT_ID` | Registered client ID; default `telegram-gateway` |
| `OAUTH_CLIENT_SECRET` | Separate secret with 32-256 characters |
| `MCP_OAUTH_REDIRECT_URI` | Exact HTTPS callback from the client registration UI |
| `TRUSTED_PROXY_IP` | Actual reverse-proxy IP seen by the gateway |

The helper backs up `.env`, preserves other settings and generates a client secret when absent:

```bash
bash scripts/configure-mcp-oauth.sh https://gateway.example.invalid \
  https://client.example.invalid/oauth/callback 192.0.2.10
```

Recreate the container after updating its source and configuration. Supply the client ID and
secret privately in the client's OAuth registration UI. The owner enters the gateway API key
only in the gateway's HTTPS consent form; the client receives tokens rather than that key.

Protected-resource discovery is at `/.well-known/oauth-protected-resource`.
Authorization-server discovery is at `/.well-known/openid-configuration`; it advertises
`/oauth/authorize`, `/oauth/token`, S256 and RFC 9207 issuer identification. Tokens target the
canonical `/mcp` resource and `telegram:send` scope. Access tokens last 15 minutes; refresh
tokens last 30 days. Callers may request `offline_access` for refresh tokens.

Validation checks signature, issuer, audience, lifetime, scope, token status and gateway-owner
identity. Rotating API_KEY invalidates existing owner tokens. OAuth records and private keys
persist in the data volume; include `oauth.db` and `.secrets` in private backups.
See [ADR 0002](adr/0002-single-owner-mcp-oauth.md).

There is one preconfigured confidential client. Dynamic registration, CIMD and anonymous
notification access are not implemented. Check that the client accepts manually supplied
OAuth credentials and copy its exact HTTPS callback URI; wildcards are not accepted.

If authorization stops before the consent form, compare the actual request's `redirect_uri`
with the registered callback. If consent succeeds but the browser remains on the form, check
the browser console for a `form-action` violation. Current builds permit the validated callback
in the consent form's Content Security Policy; rebuild older images to apply this correction.

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
