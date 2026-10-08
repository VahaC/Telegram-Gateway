# Roadmap

The HTTP API, optional MCP tools and durable Telegram delivery are implemented. Further
integration work includes:

- Add compatible OAuth authorization for clients that cannot supply X-Api-Key, including
  discovery metadata, token validation and access restrictions.
- Verify additional AI-agent and application clients against the HTTP and MCP interfaces.
- Expand deployment examples for reverse proxies running on separate hosts.

No Redis/broker, multi-user UI, arbitrary chat destinations or ambiguous-send override is planned.
