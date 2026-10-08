# Changelog

## Development — 2026-10-08

- Added read-only MCP `get_delivery_status` for any notification or digest UUID, with shared
  delivery metadata and submission-tool guidance to verify delivery after acceptance.
- Fixed browser OAuth completion: the consent form's CSP permits the validated callback,
  allowing its POST response to redirect to the client without relaxing script restrictions.
- Added optional single-owner MCP OAuth using OpenIddict: HTTPS consent, PKCE S256, discovery,
  exact callbacks, scoped bearer tokens, rotating refresh tokens and persistent keys/records.
- Added end-to-end OAuth/MCP tests and isolated Docker authorization checks.
- Added optional private client-file configuration for JSON submissions, keeping API keys out
  of command arguments.
- Generalized documentation around Telegram notifications from AI agents and other systems,
  including HTTP/MCP integration, authentication and delivery verification.
- Fixed Telegram URL resolution: the bot token's colon now remains in the HTTPS path instead
  of becoming an unsupported URI scheme. Added outbound URL regression coverage and safe
  exception-type diagnostics for unexpected worker failures.
- Added .NET 10 Minimal API, authenticated message/digest submission and metadata endpoints.
- Added SQLite migrations, durable idempotency, partial recovery and explicit uncertain outcomes.
- Added Telegram HTTP adapter, safe formatting/splitting, bounded retries and rate limiting.
- Added optional official SDK MCP tools and a separately configured external research workflow.
- Added non-root Docker/Compose, isolated container smoke checks, CI and deployment documentation.
- No release version is assigned; real Telegram/AI/Portainer installation checks remain external.
