# Development notes

## AgentGateway and the news module

TelegramGateway is now AgentGateway. Telegram works exactly as before, through the same
hostname, `/mcp`, OAuth client and API key; ChatGPT needs no change. New: a Claude routine can
submit the daily tech-news issue once; the gateway checks it, refuses items that were already
published on an earlier day (and says which), publishes every news item as its own private post
with the source's picture on vahac.com, and sends one Telegram line with the day page link.

### Turning it on

The news module is off by default.

1. Read INSTALL.md §9 before redeploying: set `GATEWAY_VOLUME_NAME` (and, for plain Compose,
   `COMPOSE_PROJECT_NAME`) to the existing names, back up the volume, build `agent-gateway:local`.
2. Install the theme version with the news routes, create the `vahac_news_publisher` user and its
   Application Password (INSTALL.md §8).
3. Set `NEWS_ENABLED=true`, `NEWS_WORDPRESS_USER`, `WORDPRESS_APP_PASSWORD`; recreate the container.
4. Run `bash scripts/check-news.sh`, look at the day page and the Telegram line, then
   `bash scripts/check-news.sh --cleanup`.
5. For the Claude routine: set the `MCP_OAUTH_CLIENT_1_*` variables and add the claude.ai custom
   connector (docs/integrations.md, "claude.ai custom connector").

AI agents, applications and scripts can submit notifications and structured digests through
an authenticated HTTP API or optional MCP tools. Delivery metadata includes Telegram message
IDs; retries of confirmed partial failures skip parts with recorded IDs.
Uncertain external writes stop for chat review.

MCP clients can now check any notification or digest using `get_delivery_status` with the
returned `Delivery.Id`. This read-only query returns the persisted status, Telegram message
IDs, attempts and review flag without resending. Rebuild the image and refresh the client's
tool discovery to use it; existing authentication and stored deliveries remain valid.

OAuth consent now permits the validated client callback in the form's Content Security Policy.
Rebuild the image to apply this browser redirect correction.

Telegram sends now resolve to the configured HTTPS host correctly. Earlier builds could stop
before contacting Telegram because the bot token was parsed as a URI scheme. Rebuild the image
using INSTALL.md; existing `requiresReview` deliveries remain blocked rather than replayed.

## Turning it on

Configure bot token, private chat ID, API key and HTTPS, then follow INSTALL.md. MCP is off by
default; enable ENABLE_MCP for MCP clients. Optional OAuth uses OpenIddict, owner consent and
PKCE S256; configure ENABLE_MCP_OAUTH, public origin, callback and client secret to enable it.
The calling system prepares content and controls when notifications are submitted.
See docs/integrations.md for client setup and delivery verification.
