# Changelog

## Development — 2026-11-15

- **The editorial gate accepts 5–40 news items and no longer requires every category.** Defaults
  changed from `News:MinItems` 35, `News:MaxItems` 80, `News:MinPerCategory` 1 to 5, 40 and 0;
  `News:MaxAiShare` stays 0.30. On 2026-11-15 the routine had verified fewer than 35 stories and the
  all-or-nothing gate left the day without an issue. The contract still allows 1..80 items, so an
  owner can raise the gate again through the options.
- **Feed catalog: 668 publisher feeds, without the noise.** The first live polls showed broad
  outlets burying the stack's news: in one 48-hour homelab page 50 of 95 entries were Arch Linux
  package updates and 19 were dev.to posts. Removed the Arch package feed, the 14 dev.to tag feeds,
  Reddit (always 429), the failing hnrss.org search feeds, four raw CVE databases and outlets that
  are consumer, EV-only or blocked (Telepolis, Chip.pl, Spider's Web, IT Home, ITC.ua, Neowin,
  Windows Central, CleanTechnica, electrive). heise, Golem, WIRED, TechRadar, PC Gamer, Tom's Guide,
  Electrek, ITmedia, Le Monde Informatique, AIN, Antyweb and Numerama now come in through their topic
  feeds. A second pass ranked every feed by the share of its last 7 days that touches the stack and
  dropped general and consumer outlets with almost none (Gigazine, Xataka, Genbeta, Engadget, CNET,
  phone sites, gaming, general Ukrainian news), the per-build llama.cpp release feed, the SQLite
  change log, Planet Debian, the Ars Technica index (its topic feeds stay) and the all-CVE cvefeed.io
  stream (its high-severity feed stays), the Fedora package-announce list, How-To Geek, MakeUseOf
  and Mezha. The Home Assistant community comes in through "Share your Projects" (integrations,
  blueprints, devices) instead of every new forum thread.
- **Added the news feed collector (ADR 0004).** The catalog
  `src/AgentGateway.Api/NewsFeeds/news-feeds.json` ships with the build and the image and is synced
  into the new `NewsFeeds` table at startup (added, updated, removed feeds disabled with their
  entries kept in the database but left out of `get_news_feed` at once). `NewsFeedWorker` polls the RSS 2.0, RSS 1.0/RDF and Atom feeds shortly after start
  and every `News:FeedPollMinutes` (60) as a plain feed reader: own User-Agent
  (`News:FeedUserAgent`), conditional GET, 20 s timeout, 16 MiB cap, `News:FeedParallelism` (4) hosts
  at a time but one request per host, nothing after a host's 429 until the next run, no retry of any
  refusal, XML without DTDs (undeclared common prefixes such as `media:`, a blank line before the XML
  declaration and stray control characters are tolerated). Entries keep the feed's title, link,
  date, plain-text summary (≤ 4000 characters) and an https picture named by the feed; they are unique by canonical URL and deleted
  14 days after first seen. One summary log line per run. Additive migration `AddNewsFeeds`
  (`NewsFeeds`, `NewsFeedEntries`, indexed by publication-or-first-seen time).
- Added MCP tools `get_news_feed` (`sinceHours`, `category`, `query` with up to 20 `|` alternatives,
  `excludePublished`, `limit`, `cursor`; pages under 25 KB with a feed health block) and
  `get_news_feed_entry` (`id`, the whole summary), both under scope `news:publish`, and their HTTP
  counterparts `GET /api/news/feed` and `GET /api/news/feed/entries/{id}`.
- **Added the news watchdog.** Daily at `News:WatchdogTime` (06:00) in `News:WatchdogTimeZone` (UTC)
  it checks that an issue exists for that day's issue date (Europe/Kyiv); if not, it queues one
  Telegram alert through the message outbox (idempotency key `news-watchdog-{date}`), naming the
  date, the check time and the newest issue. A start after the check time catches up; a restart or
  repeat never alerts twice.
- New options `News:FeedsEnabled`, `News:FeedCatalogPath`, `News:FeedPollMinutes`,
  `News:FeedUserAgent`, `News:FeedParallelism`, `News:WatchdogEnabled`, `News:WatchdogTime`,
  `News:WatchdogTimeZone`, validated at startup (a browser User-Agent, an unknown zone or a time
  that is not HH:mm stop the start with a sentence naming the setting).
- The Docker smoke checks that the catalog is inside the image and switches the watchdog off so its
  own Telegram assertions stay deterministic.

## Development — 2026-11-13

- Fixed the OAuth data-protection key ring being unreadable after every restart. Keys were
  encrypted with `.secrets/oauth.pfx`, but the framework's decryptor looked the certificate up in
  the OS certificate store, so each start logged "Unable to retrieve the decryption key", wrote a
  new key and invalidated consent forms open during a restart. OAuth tokens were not affected.
  A gateway-specific encryptor/decryptor pair now resolves the certificate from the data volume.
  Keys written by older versions stay unreadable; move them out of
  `.secrets/data-protection` (the gateway writes a fresh key on the next start).
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
