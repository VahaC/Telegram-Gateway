# Business requirements

AgentGateway (formerly TelegramGateway) provides authenticated Telegram notification delivery for
external callers and publishes a daily tech-news issue to the owner's WordPress site.

- Receive prepared notifications and structured digests from AI agents, applications, scripts
  and monitoring systems, and deliver them to one configured private chat.
- Separate delivery from content preparation, and distinguish accepted jobs from delivery.
- Preserve source URLs, Unicode, useful formatting and safe Telegram message boundaries.
- Persist daily idempotency, attempts, successful IDs and partial/failed state across restart.
- Protect write/content-related metadata with API-key auth, request limits and HTTPS at the edge.
- Use lightweight local storage, non-root Docker and a persistent volume without public ports.
- Expose shared delivery services through HTTP and optional official-SDK MCP tools.
- Document client authentication, submission, status polling and MCP tool discovery.
- Allow MCP callers to verify any submitted notification or digest by its delivery UUID,
  with no resend or retry side effects from status queries.
- Support optional owner-authorized OAuth for MCP clients without custom API-key headers.
- Prove behaviors using real HTTP/SQLite with mocked Telegram and isolated container lifecycle.
- Verify the resolved Telegram HTTPS destination and the real container HTTP handler's failure
  path; readiness and a mocked response alone do not establish a valid send destination.


## News module

- Accept one finished daily issue (35–80 news items, releases, security alerts) from a Claude
  cloud routine in one call, over HTTP or MCP, in the shared format "news issue v1".
- Refuse issues that break the editorial rules (item count, every category present, at most 30%
  AI, exactly five TOP items numbered 1..5, contiguous numbering) with the exact violations.
- Keep every accepted item as the duplicate registry. Refuse an item whose source (in canonical
  form, ignoring tracking parameters) or event key appeared in any earlier issue, and name the
  earlier issue and item, so the routine can replace exactly those items.
- Let the routine read the registry (history, candidate check) before researching.
- Publish every item as its own private post with the source's picture, plus a day page, one call
  at a time, resuming after failures and restarts; an image that cannot be fetched never blocks
  publication.
- Send only one Telegram line per published issue with the link to the day page; never the texts.
- Keep ChatGPT's Telegram access (hostname, `/mcp`, OAuth client, API key) unchanged; give the news
  routine its own OAuth client limited to the news tools.
- Rename the product without losing data or reconfiguring the running installation.

Runtime credentials and client configuration are installation prerequisites. Exactly-once
Telegram semantics are outside the provider API contract (ADR 0001).
