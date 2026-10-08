# Business requirements

Development implementation, based on the supplied Telegram News Delivery Gateway brief.

- Receive externally researched Ukrainian digests and deliver to one configured private chat.
- Separate gateway delivery from AI generation, and distinguish accepted jobs from delivery.
- Preserve source URLs, Unicode, useful formatting and safe Telegram message boundaries.
- Persist daily idempotency, attempts, successful IDs and partial/failed state across restart.
- Protect write/content-related metadata with API-key auth, request limits and HTTPS at the edge.
- Use lightweight local storage, non-root Docker and a persistent volume without public ports.
- Expose shared delivery services through HTTP and optional official-SDK MCP tools.
- Supply a supported external scheduled workflow and document separate ChatGPT platform setup.
- Prove behaviors using real HTTP/SQLite with mocked Telegram and isolated container lifecycle.

Runtime credentials and third-party registration are installation prerequisites, not generated
by this change. Exactly-once Telegram semantics are outside the provider API contract (ADR 0001).
