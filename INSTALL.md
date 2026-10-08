# Installation

## 1. Telegram bot and private chat

1. Open official [BotFather](https://t.me/BotFather), run `/newbot`, and follow its prompts.
2. Keep the token private; revoke it in BotFather if it becomes exposed.
3. Open the new bot, press Start, and send a private message to it.
4. From a trusted local machine, call `getUpdates` using the token from an environment variable.
   This example prints only private chat IDs, not private message text:

   ```python
   import json, os, urllib.request
   with urllib.request.urlopen("https://api.telegram.org/bot" + os.environ["TELEGRAM_BOT_TOKEN"] + "/getUpdates") as response:
       updates = json.load(response)
   for update in updates.get("result", []):
       chat = update.get("message", {}).get("chat", {})
       if chat.get("type") == "private":
           print(chat["id"])
   ```

   Do not share token-bearing URL tracebacks. If updates are empty, send another private message.
   If an existing webhook consumes updates, use its trusted metadata; do not delete someone
   else's webhook. This service intentionally accepts only positive private chat IDs.

Official references: [bot tutorial](https://core.telegram.org/bots/tutorial),
[getUpdates](https://core.telegram.org/bots/api#getupdates),
[sendMessage](https://core.telegram.org/bots/api#sendmessage).

## 2. Configure and start

Copy `.env.example` to `.env`, protect it with `chmod 600`, fill credentials and real hostname.
Generate API_KEY with `openssl rand -hex 32`. Run the Docker quick start in README.
Avoid sharing `docker compose config`: its interpolated output contains credentials.
The image is local; no registry image has been published.

## 3. Portainer

1. Build `docker build -t telegram-gateway:local .` on the same Linux Docker endpoint managed
   by Portainer. A laptop-local image is not automatically available on the server.
2. Create a stack using `docker-compose.yml` in Portainer's Web editor/upload.
3. Supply required credentials and GATEWAY_HOST in private environment fields.
4. Deploy, inspect health/readiness and named volume, and leave the port unpublished.
5. Attach the proxy/tunnel container to the same network.
6. For updates, rebuild/load the image and recreate the service while retaining its volume.

Choose a registry owner/tag before using a publishing workflow;
[PUBLISHING.md](PUBLISHING.md) covers local packaging.

## 4. Nginx Proxy Manager

Connect NPM to `telegram-gateway`. Create a Proxy Host for GATEWAY_HOST with HTTP upstream
`gateway:8080`; assign a TLS certificate and enable Force SSL. Keep the Host header equal
to the configured hostname. Leave port 8080 unpublished.

Optional advanced configuration, checked against NPM's generated location:

```nginx
client_max_body_size 128k;
proxy_read_timeout 60s;
proxy_buffering off;
```

Do not add a competing `location /` or duplicate existing NPM proxy directives. If enabling
forwarded headers, give NPM a stable exact IP and set TRUSTED_PROXY_IP to it. It must derive
the effective client IP from a trusted edge rather than blindly passing incoming X-Forwarded-For.
When Cloudflare sits ahead of NPM, configure Nginx real-IP handling against verified Cloudflare
ranges first. Do not trust an entire Docker network to work around address changes.

## 5. Cloudflare Tunnel and Access

Replace UUID, credentials path and hostname in [examples/cloudflared.yml](examples/cloudflared.yml).
Run cloudflared on the shared Docker network, forwarding to `http://gateway:8080`. Keep tunnel
credentials outside this repo. A tunnel does not replace API authentication.

Recommend a Cloudflare Access application for this hostname, with a service-token policy for
the workflow. Supply CF-Access-Client-Id and CF-Access-Client-Secret **in addition to** X-Api-Key.
The workflow supports the corresponding `CF_ACCESS_CLIENT_ID` / `CF_ACCESS_CLIENT_SECRET` env
variables. Configure an appropriate browser identity policy separately if needed. Do not
disable authentication to accommodate an incompatible client.

Official [Tunnel](https://developers.cloudflare.com/cloudflare-one/networks/connectors/cloudflare-tunnel/)
and [Access service-token](https://developers.cloudflare.com/cloudflare-one/access-controls/service-credentials/service-tokens/)
documentation.

## 6. Verify delivery and client integration

Submit the README setup message, poll Location until Delivered, and inspect the private chat.
Submit the Ukrainian fixture, check headings/numbering/Unicode/source links, then repeat enough
paragraphs to exercise splitting. Re-submit the identical key/payload and confirm no new chat
messages. Restart the container and confirm IDs/status persist.

Configure the calling agent, application or script using
[docs/integrations.md](docs/integrations.md). Verify that it can supply X-Api-Key, submit
content and read delivery status. For MCP, enable ENABLE_MCP and check tool discovery through
the secured endpoint. Refresh client tool discovery after upgrading. Verify `get_delivery_status`
using the `Delivery.Id` returned by either submission tool; the date lookup only covers digests.
A 202 response or successful submission tool call alone does not establish delivery.
If the caller sends recurring notifications, verify its scheduling separately.

For OAuth-capable MCP clients, configure the optional flow described in
[OAuth integration](docs/integrations.md#oauth-for-mcp). Set the actual trusted proxy IP so
forwarded HTTPS is recognized; do not disable transport-security validation. Register the
exact client callback and provide its client ID/secret privately. Include OAuth records and
`.secrets` when backing up the data volume.

After owner consent, the browser should return to the client. A `form-action` policy violation
means the deployed image lacks the callback CSP correction; rebuild from current source.

## 7. Backup and uncertain outcomes

If a setup test failed with `process_interrupted` on an older build, update the source and
rebuild the image: the Telegram URL now explicitly resolves relative to its HTTPS base address.
The previous code could interpret the token's colon as an unsupported URI scheme before sending.
Keep the data volume and `.env`. Do not clear `requiresReview` or delete the delivery ledger.
Check the chat first; if the test is absent, submit a new setup test with a new key after updating.
New worker logs include only the exception type for unexpected failures, never its message or URL.

Back up the stopped data volume or use SQLite's online backup. Preserve ownership/mode and
include the WAL for a filesystem snapshot. Do not casually delete delivery rows: that removes
their idempotency protection.

For requiresReview, compare attempt positions, recorded IDs and the actual chat. There is no
HTTP override endpoint. If intervention is necessary, stop the worker, back up the volume and
reconcile the ledger against verified Telegram IDs; do not invent IDs or blindly delete rows.
