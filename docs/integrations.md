# Client integrations

AgentGateway has two kinds of callers. AI agents, applications, scripts and monitoring systems
submit notifications for delivery to one configured private Telegram chat. A Claude cloud routine
submits the daily news issue, which the gateway publishes to the WordPress site. Callers prepare
the content and decide when to submit it. Telegram and WordPress credentials remain in the
gateway; callers receive the gateway URL and either the API key or OAuth client credentials.

## HTTP API

Use HTTPS and send exactly one `X-Api-Key` header. Submit notifications to `POST /api/messages`
and news issues to `POST /api/news/issues`. `POST /api/digests` (dated Telegram digests) still
works but is deprecated. See [README.md](../README.md#http-api) for every route.

Submission returns `202 Accepted` with a `Location` for status polling. For Telegram, only
`Delivered` confirms recorded message IDs. An uncertain result with `requiresReview: true`
requires an operator to inspect the destination chat before attempting another submission.

Use a stable idempotency key for the same logical notification. When retrying, retain both the
key and the original payload. A different payload under the same key is rejected.

## News workflow for AI callers

The issue format is the shared contract "news issue v1"
([docs/prompts/news-module.md](prompts/news-module.md#shared-contract-news-issue-v1)): date
(Europe/Kyiv day), title, language, intro, 5–40 items by default (number, category, isTop, importance,
title, plain-text summary and whyItMatters, publishedOn, eventOn, releaseStatus, version,
eventKey, source, extraSources, image, tags), releases, securityAlerts and research stats.

0. **Leads from the feeds (optional).** `get_news_feed` (or `GET /api/news/feed`) lists entries the
   gateway collected from the publishers' own RSS/Atom feeds in the last 1–168 hours, newest first,
   without the ones the registry already knows. Narrow it with `category` and `query`
   (`Proxmox|ZFS|WireGuard`: any term), follow `nextCursor`, and read one entry in full with
   `get_news_feed_entry`. The publication date is the publisher's own; facts beyond the entry text
   need the source page or another source. See README, "News feeds".
1. **Read the registry first.** `get_news_history(days=30)` (or `GET /api/news/history?days=30`)
   lists every item already published: date, number, category, title, eventKey, canonical URL,
   plus releases and alerts. Skip events that are already there before researching them in depth.
2. **Check candidates.** `check_news_candidates(urls, eventKeys)` (or `POST /api/news/check`,
   up to 200 of each) returns the ones already known, with the issue date, item number and title.
   URLs are compared in canonical form, so tracking parameters, `www.`, http/https, default ports,
   fragments and trailing slashes do not hide a duplicate.
3. **Submit once.** `submit_news_issue(issue)` (or `POST /api/news/issues`). The result is:
   - `Accepted` (202): queued for publication.
   - `Existing` (200): this exact issue was already accepted; nothing changes.
   - `Invalid` (400): fix the structural `errors` (paths such as `items[3].summary`).
   - `Rejected` (422): fix exactly the listed `violations`: `min_items` / `max_items` (5–40 by
     default), `category_min` (only when the owner set `News:MinPerCategory`; off by default),
     `ai_share` (at most 30% `ai`), `top_count` (exactly 5 TOP), `top_numbering` (TOP are 1..5),
     `numbering` (1..N without gaps).
   - `Duplicate` (409): replace exactly the items in `duplicates`. Each entry names the path in your
     issue and the earlier issue date, item number and title it repeats (by canonical URL, by
     `eventKey`, inside the issue, or an `extraSources` URL that is an earlier item's primary URL).
   - `Conflict` (409): an issue for that date exists with other content; use `replace_news_issue`.
4. **Poll the status.** `get_news_issue_status(date)` (or `GET /api/news/issues/{date}`) every
   30–60 seconds until `Published`. `PartiallyPublished` and `Failed` name the cause per item; the
   owner fixes it and calls `POST /api/news/issues/{date}/republish`. An image the site could not
   download is shown as `image: failed` and does not block publication.
5. **Fixes after the fact.** `replace_news_issue(date, issue)` (or `PUT /api/news/issues/{date}`)
   runs the same checks (its own items do not count as duplicates), bumps the revision,
   republishes every item and removes posts of dropped items. Each revision sends one Telegram line.

The news texts are never sent to Telegram; only one line with the day page link is.

## MCP tools

The optional Streamable HTTP endpoint uses the official
[MCP C# SDK](https://github.com/modelcontextprotocol/csharp-sdk).
Set `ENABLE_MCP=true` in Compose and recreate the gateway container. Connect a client to
`https://gateway.example.invalid/mcp`, replacing the hostname with the configured gateway.
Every request requires `X-Api-Key` or, when OAuth is enabled, a valid bearer token.
Host and Origin checks remain enabled.

| Tool | Scope | Purpose | Inputs |
|---|---|---|---|
| `send_telegram_message` | `telegram:send` | Queue a notification | `text`, `idempotencyKey`; optional `format`, `disableNotification` |
| `get_delivery_status` | `telegram:send` | Delivery metadata for any notification or digest | `id` (the UUID returned as `Delivery.Id`) |
| `send_technology_digest` | `telegram:send` | **Deprecated.** Queue a prepared Telegram digest | `title`, `date`, `content`, `idempotencyKey`; optional `language`, `format`, `disableNotification` |
| `get_digest_delivery_status` | `telegram:send` | Digest delivery metadata | `date` (`yyyy-MM-dd`) |
| `get_news_history` | `news:publish` | Registry of the last days | optional `days` (1–180, default 30) |
| `check_news_candidates` | `news:publish` | Which candidates are already known | `urls`, `eventKeys` |
| `submit_news_issue` | `news:publish` | Submit the whole issue | `issue` (contract v1) |
| `replace_news_issue` | `news:publish` | Replace an accepted issue | `date`, `issue` |
| `get_news_issue_status` | `news:publish` | Publication state per item | `date` |
| `get_news_feed` | `news:publish` | Entries from the publishers' feeds, newest first, under 25 KB per page | optional `sinceHours` (1–168, default 168), `category`, `query` (≤ 20 terms separated by `\|`), `excludePublished` (default true), `limit` (1–50, default 30), `cursor` |
| `get_news_feed_entry` | `news:publish` | One feed entry with its whole summary | `id` |

A token only sees and can call the tools of its scopes; a call outside them returns a tool error
naming the missing scope. The API key grants both scopes. When `News:Enabled` is false the news
tools are hidden and refused.

After `send_telegram_message`, retain `Delivery.Id` and poll `get_delivery_status`. `Pending` and
`Sending` do not confirm delivery; `Delivered` confirms recorded Telegram message IDs.
`requiresReview: true` needs chat inspection before another submission. Status queries do not
send or retry anything. An unknown UUID produces an empty MCP result; a malformed UUID a tool error.

## OAuth for MCP

The optional OAuth server uses OpenIddict with authorization codes, PKCE S256, rotating refresh
tokens and explicit owner consent. It supports several confidential clients, each with its own
secret, exact callbacks and scopes.

| Variable | Value |
|---|---|
| `ENABLE_MCP` | `true` |
| `ENABLE_MCP_OAUTH` | `true` |
| `MCP_PUBLIC_URL` | Canonical public HTTPS origin |
| `MCP_OAUTH_CLIENT_ID` | Client 0 ID; default `telegram-gateway` (ChatGPT) |
| `OAUTH_CLIENT_SECRET` | Client 0 secret, 32–256 characters |
| `MCP_OAUTH_REDIRECT_URI` | Client 0 exact HTTPS callback |
| `MCP_OAUTH_CLIENT_1_ID` | Optional client 1 ID, e.g. `claude-news-routine`; blank = no client 1 |
| `OAUTH_CLIENT_1_SECRET` | Client 1 secret (`openssl rand -hex 32`) |
| `MCP_OAUTH_CLIENT_1_REDIRECT_URI` / `_2` | Client 1 callbacks; for claude.ai see below |
| `MCP_OAUTH_CLIENT_1_SCOPE` | Client 1 scope; default `news:publish` |
| `TRUSTED_PROXY_IP` | Actual reverse-proxy IP seen by the gateway |

In configuration terms these are `McpOAuth:Clients:{n}:ClientId`, `…:ClientSecret`,
`…:RedirectUris:{i}` and `…:Scopes:{i}`. The original single-client keys (`McpOAuth:ClientId`,
`OAUTH_CLIENT_SECRET`, `McpOAuth:RedirectUris`) remain client 0 with scope `telegram:send`, so an
existing ChatGPT registration, its tokens and the issuer need no change. More clients can be
added in Compose as `AGENTGATEWAY_McpOAuth__Clients__2__…`. A client removed from configuration
is deleted at the next start, together with its tokens.

The helper for client 0 backs up `.env`, preserves other settings and generates a secret when absent:

```bash
bash scripts/configure-mcp-oauth.sh https://gateway.example.invalid \
  https://client.example.invalid/oauth/callback 192.0.2.10
```

Protected-resource discovery is at `/.well-known/oauth-protected-resource` and advertises
`telegram:send` and `news:publish`. Authorization-server discovery is at
`/.well-known/openid-configuration`. A client may request any advertised scope; the gateway grants
only those configured for that client (so ChatGPT keeps exactly `telegram:send` even if it asks
for both), and the consent page lists what the client will be able to do. Access tokens last
15 minutes; refresh tokens 30 days; request `offline_access` for refresh tokens.

Validation checks signature, issuer, audience, lifetime, scope, token status and gateway-owner
identity. Rotating API_KEY invalidates existing owner tokens. OAuth records and private keys
persist in the data volume; include `oauth.db` and `.secrets` in private backups.
See [ADR 0002](adr/0002-single-owner-mcp-oauth.md). Dynamic registration, CIMD and anonymous
access are not implemented.

If authorization stops before the consent form, compare the actual request's `redirect_uri`
with the registered callback. If consent succeeds but the browser remains on the form, check the
browser console for a `form-action` violation.

### claude.ai custom connector (the news routine)

The Claude cloud routine reaches the gateway as a claude.ai custom connector with OAuth client 1.

1. Generate a secret: `openssl rand -hex 32`. In `.env` set `MCP_OAUTH_CLIENT_1_ID` (for example
   `claude-news-routine`), `OAUTH_CLIENT_1_SECRET`, `MCP_OAUTH_CLIENT_1_REDIRECT_URI=https://claude.ai/api/mcp/auth_callback`
   and `MCP_OAUTH_CLIENT_1_REDIRECT_URI_2=https://claude.com/api/mcp/auth_callback`. Anthropic
   documents the first as Claude's OAuth callback and says it may move to the second, so register
   both ([Building custom connectors via remote MCP servers](https://support.claude.com/en/articles/11503834)).
   Keep `MCP_OAUTH_CLIENT_1_SCOPE=news:publish` so the routine cannot send Telegram messages.
2. Recreate the container (`docker compose up -d`, or redeploy the Portainer stack).
3. In claude.ai: Settings → Connectors → Add custom connector. URL `https://<gateway host>/mcp`;
   under Advanced settings enter the client ID and secret from step 1.
4. Connect: the browser opens the gateway's consent page, which says the client will be able to
   submit news issues and read the registry. Enter the gateway API key there (only there) and allow.
5. Enable the connector for the routine and give it the workflow above. Check that the routine
   sees the seven news tools and no Telegram tools.

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

For news, run `bash scripts/check-news.sh` (INSTALL.md §8): it publishes the fixture to the real
site, shows the per-item result and the duplicate report, and `--cleanup` removes it again.
