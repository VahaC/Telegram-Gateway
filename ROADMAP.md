# Roadmap

The HTTP API, optional MCP tools and durable Telegram delivery are implemented. The news module
(registry, editorial gate, WordPress publication, MCP tools, `news:publish` scope and several OAuth
clients) is implemented and tested against a fake of the theme. Further integration work includes:

- Run `scripts/check-news.sh` against the real vahac.com theme and the real Telegram chat, then
  connect the Claude cloud routine through the claude.ai connector (OAuth client 1).
- Rename the GitHub repository, local folder and Portainer stack to AgentGateway (INSTALL.md §9).
- Remove the deprecated `/api/digests` and `send_technology_digest` once nothing uses them.

- Verify additional OAuth clients against the implemented discovery, consent and token flow.
- Verify additional AI-agent and application clients against the HTTP and MCP interfaces.
- Expand deployment examples for reverse proxies running on separate hosts.

OAuth browser callback redirects have regression coverage; each deployed client still needs
its own installation verification.
MCP delivery verification by UUID is implemented for both notifications and digests, alongside
the existing digest-date lookup. HTTP tests cover pending, delivered, failed and uncertain outcomes.

No Redis/broker, multi-user UI, arbitrary chat destinations or ambiguous-send override is planned.
