# Development notes

You can submit private notifications and structured digests through an authenticated API,
track Telegram message IDs and retry confirmed partial failures without repeating known parts.
Uncertain external writes stop for chat review.

## Turning it on

Configure bot token, private chat ID, API key and HTTPS, then follow INSTALL.md. MCP is off by
default; enable ENABLE_MCP only for a client supporting the required headers. External daily
research needs a separate OpenAI API key/model and reviewed systemd installation. No schedule,
release number, registry publication or production credentials are included.
