# Business requirements

The gateway provides authenticated Telegram notification delivery for external callers.

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

Runtime credentials and client configuration are installation prerequisites. Exactly-once
Telegram semantics are outside the provider API contract (ADR 0001).
