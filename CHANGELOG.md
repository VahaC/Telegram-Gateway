# Changelog

## Development — 2026-10-08

- Fixed Telegram URL resolution: the bot token's colon now remains in the HTTPS path instead
  of becoming an unsupported URI scheme. Added outbound URL regression coverage and safe
  exception-type diagnostics for unexpected worker failures.
- Added .NET 10 Minimal API, authenticated message/digest submission and metadata endpoints.
- Added SQLite migrations, durable idempotency, partial recovery and explicit uncertain outcomes.
- Added Telegram HTTP adapter, safe formatting/splitting, bounded retries and rate limiting.
- Added optional official SDK MCP tools and a separately configured external research workflow.
- Added non-root Docker/Compose, isolated container smoke checks, CI and deployment documentation.
- No release version is assigned; real Telegram/AI/Portainer installation checks remain external.
