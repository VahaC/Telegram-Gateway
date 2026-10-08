# External AI integration

Documentation reviewed on 2026-10-08. Installed account permissions, available models and
client capabilities must still be checked at setup time. No real credentials were used.

## Implemented interfaces

The HTTP API accepts authenticated messages/digests. The official
[MCP C# SDK](https://github.com/modelcontextprotocol/csharp-sdk) hosts optional Streamable HTTP
at `/mcp`. Set ENABLE_MCP=true in Compose and recreate the container. Every request needs
X-Api-Key; Host and Origin are allowlisted. Tools reuse DeliveryService:

- send_telegram_message: text, idempotencyKey, optional format/silent notification.
- send_technology_digest: title, yyyy-MM-dd date, content, idempotencyKey, optional language/format.
- get_digest_delivery_status: date, metadata only.

Tool acceptance is not delivery; check status until Delivered. An official SDK client exercises
discovery and sending against the actual gateway HTTP pipeline in automated tests.

## MCP clients and ChatGPT

A client supporting private HTTP headers can connect to the secured endpoint. For example,
[Codex MCP configuration](https://developers.openai.com/codex/mcp) supports HTTP transport and
environment-sourced headers. Configure your own client; this repository does not modify it:

```toml
[mcp_servers.telegram_gateway]
url = "https://gateway.example.invalid/mcp"
env_http_headers = { "X-Api-Key" = "TELEGRAM_GATEWAY_KEY" }
```

Set TELEGRAM_GATEWAY_KEY privately to the same API_KEY before starting the client. Additional
network protection such as Cloudflare Access requires its service-token headers too. Client
write approvals and account capabilities apply independently of this gateway's API key.

Official OpenAI references:
[custom MCP server registration](https://developers.openai.com/api/docs/guides/custom-mcp-server),
[plugin authentication](https://developers.openai.com/plugins/build/auth), and
[secure MCP tunnel](https://developers.openai.com/api/docs/guides/secure-mcp-tunnels).

The fetched custom-server guide lists OAuth/no-auth registration choices, not arbitrary
X-Api-Key injection. The gateway implements static API-key auth, **not** a ChatGPT OAuth
resource server. A production ChatGPT integration needs separately implemented compatible
authorization, discovery metadata, audience/scope checks, account/workspace approval, and
registration. It may additionally need the documented ChatGPT transport requirements.
Do not choose No authentication for this private write-capable service to bypass those steps.

There is no demonstrated support here for ChatGPT scheduled tasks invoking this custom app.
An interactive registered app and an unattended scheduled task are separate capabilities.
This service does not read ChatGPT chats, import your personalization, or infer account access.

## External scheduled workflow

This is the implemented unattended path independent of ChatGPT task support:

```text
systemd timer -> external researcher (OpenAI Responses API) -> private cached digest
             -> authenticated gateway POST -> delivery status polling -> exit success/failure
```

Alternatively, a different assistant/workflow writes digest JSON and invokes `--input`.
No researcher runs inside the gateway. The Python workflow uses only standard-library modules,
requires Python 3.10+ and Linux tzdata, and rejects remote plaintext HTTP and redirects.

1. Place the repository at `/opt/telegram-gateway`; customize `examples/research-prompt.txt`.
2. Choose an API model with web_search support from current official documentation. No default
   model is invented. Provide a separately billed OpenAI API key.
3. Create a system user `telegram-digest` with no login shell; grant it read access to the code.
4. Create `/etc/telegram-digest.env`, owned by root with mode 0600:

   ```text
   GATEWAY_URL=https://YOUR_GATEWAY_HOST
   API_KEY=YOUR_GATEWAY_KEY
   OPENAI_API_KEY=YOUR_SEPARATE_OPENAI_KEY
   OPENAI_MODEL=YOUR_SUPPORTED_MODEL
   # Optional, both together when using Cloudflare Access:
   CF_ACCESS_CLIENT_ID=YOUR_SERVICE_TOKEN_ID
   CF_ACCESS_CLIENT_SECRET=YOUR_SERVICE_TOKEN_SECRET
   ```

   Remove the two optional lines if Access is not used. Never commit the actual file.
5. Copy the example service/timer into `/etc/systemd/system/`. Review the example time (09:00
   Europe/Kyiv), paths and permissions first. No schedule is installed automatically.
6. Run `systemctl daemon-reload`, then `systemctl start telegram-digest.service` to test.
7. Inspect its exit status and actual Telegram message/links. Only then run
   `systemctl enable --now telegram-digest.timer`.
8. Check the next run with `systemctl list-timers telegram-digest.timer` and verify after it fires.

The default research request uses Responses API with `store: false` and `web_search`, documented
in [OpenAI web search](https://developers.openai.com/api/docs/guides/tools-web-search). It extracts
completed text output, includes source URLs in the prompt, and does not claim the researcher
cannot hallucinate. Review generated content during setup. The cache is written with private
permissions before sending; a competing runner cannot replace the original date's payload.
systemd prevents overlapping runs of the same unit. A manual overlap can still incur additional
AI research cost before the exclusive cache write; the first cached result remains authoritative.

If a gateway submission response is lost, rerun with the same cached JSON. Do not regenerate
different content under an existing key/date. The script never prints raw remote bodies,
credentials or private prompts. Safe error messages indicate configuration/network/delivery
failure. Polling timeout is not proof of delivery failure; inspect persisted status.

Daily files are retained in `/var/lib/telegram-digest` (0700 directory, 0600 files). Back them
up privately and retain today's file for idempotent retries. A failed generation produces no
submission. RequiresReview stops the workflow and needs chat inspection. An existing cache
is reused even if the research prompt/model changes; choose changes for future dates.

Persistent systemd catch-up runs research for the current Kyiv day after downtime. It does not
backfill every missed day. The timer uses wall-clock timezone semantics and no fixed UTC offset.
Do not pick a local time inside DST transitions without reviewing systemd calendar behavior.

## Manual fallback

Use the README curl examples or `--input` with a JSON created by the external assistant.
Do not confuse manual webhook success, SDK mock tests, or API 202 with end-to-end scheduled
ChatGPT delivery. That acceptance check remains installation-specific.
