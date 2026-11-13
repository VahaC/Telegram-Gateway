# Changelog

## Development — 2026-11-13

- Renamed TelegramGateway to AgentGateway: solution, projects, namespaces, assemblies, Docker image
  `agent-gateway:local` and docs. The Compose service, network default `telegram-gateway`, OAuth
  issuer/client ID/scope, `/mcp` and the SQLite files are unchanged.
- Configuration reads `AGENTGATEWAY_` and the old `TELEGRAMGATEWAY_` prefix (the new one wins) and
  logs at startup which old variable names are still in use.
- Compose pins the data volume name (`GATEWAY_VOLUME_NAME`, default `<project>_gateway-data` as
  before); `scripts/deploy.sh` refuses to start on a new empty volume when an old one exists.
- Added the news module: `POST/PUT/GET /api/news/issues`, republish, history and candidate check;
  structure, editorial gate (422) and duplicate registry (409) with canonical URLs; storage of the
  accepted document, items, sources, releases and alerts (additive migration `AddNewsRegistry`).
- Added the WordPress publication worker: one call at a time (day, items, removals, verify),
  per-item state, automatic retries with backoff, safe `wp_*` codes, image results, restart resume
  (ADR 0003), and one Telegram line per published revision with the day page URL.
- Added MCP tools `get_news_history`, `check_news_candidates`, `submit_news_issue`,
  `replace_news_issue`, `get_news_issue_status`; scope `news:publish`; per-tool scope checks.
- Added several OAuth clients (`McpOAuth:Clients:{n}`); the existing keys stay client 0. Grants are
  narrowed to each client's configured scopes.
- Added `examples/news-sample-issue.json` (fixture shared with the theme), `scripts/check-news.sh`
  (live check with `--cleanup`) and a fixture-only `DELETE /api/news/issues/{date}`.
- Marked `/api/digests` and `send_technology_digest` deprecated.
- Raised the request body limit to 1 MiB for `/api/news/issues*` and `/mcp` only.
- Test projects reference the ASP.NET Core shared framework, so they no longer restore vulnerable
  NuGet copies of `System.Security.Cryptography.Xml`/`Pkcs` 10.0.11 (NU1902/NU1903).

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
