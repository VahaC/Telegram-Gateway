# Roadmap

The HTTP API, optional MCP tools and durable Telegram delivery are implemented. Further
integration work includes:

- Verify additional OAuth clients against the implemented discovery, consent and token flow.
- Verify additional AI-agent and application clients against the HTTP and MCP interfaces.
- Expand deployment examples for reverse proxies running on separate hosts.

OAuth browser callback redirects have regression coverage; each deployed client still needs
its own installation verification.
MCP delivery verification by UUID is implemented for both notifications and digests, alongside
the existing digest-date lookup. HTTP tests cover pending, delivered, failed and uncertain outcomes.

No Redis/broker, multi-user UI, arbitrary chat destinations or ambiguous-send override is planned.
