# Development notes

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
