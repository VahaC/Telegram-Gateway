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
An existing TelegramGateway installation keeps its `.env` as is; read §9 before redeploying it.
Avoid sharing `docker compose config`: its interpolated output contains credentials.
The image is local; no registry image has been published.

## 3. Portainer

1. Build `docker build -t agent-gateway:local .` on the same Linux Docker endpoint managed
   by Portainer. A laptop-local image is not automatically available on the server.
2. Create a stack using `docker-compose.yml` in Portainer's Web editor/upload.
3. Supply required credentials and GATEWAY_HOST in private environment fields.
4. Deploy, inspect health/readiness and named volume, and leave the port unpublished.
5. Attach the proxy/tunnel container to the same network.
6. For updates, rebuild/load the image and recreate the service while retaining its volume.
   Keep the stack name: Compose names the volume `<stack name>_gateway-data`. If you ever deploy
   under another stack name, set `GATEWAY_VOLUME_NAME` to the existing volume first (§9).

Choose a registry owner/tag before using a publishing workflow;
[PUBLISHING.md](PUBLISHING.md) covers local packaging.

## 4. Nginx Proxy Manager

Connect NPM to `telegram-gateway` (the network keeps its original name). Create a Proxy Host for GATEWAY_HOST with HTTP upstream
`gateway:8080`; assign a TLS certificate and enable Force SSL. Keep the Host header equal
to the configured hostname. Leave port 8080 unpublished.

Optional advanced configuration, checked against NPM's generated location:

```nginx
# A whole news issue (POST/PUT /api/news/issues, or submit_news_issue over /mcp) is 150-250 KiB;
# the gateway itself limits those routes to 1 MiB and every other route to 128 KiB.
client_max_body_size 1m;
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

The gateway calls the WordPress site as a server, not a browser. If vahac.com is behind Cloudflare,
requests to `/wp-json/vahac/` must not be challenged (Bot Fight Mode, Super Bot Fight Mode, a
managed challenge or a WAF rule would answer with an HTML page or 403, recorded as `wp_403` or
`wp_invalid_response`). Add a WAF custom rule *Skip* for `URI Path starts with /wp-json/vahac/`
(optionally also `ip.src eq <gateway's public IP>`), skipping the remaining custom rules, rate
limiting, managed rules and Super Bot Fight Mode. The routes stay protected by the Application
Password.

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

## 8. News module: WordPress Application Password and live check

The theme (vahac-terminal) provides the news routes and the role `vahac_news_publisher`, whose
only real capability is `vahac_publish_news`. Install that theme version first.

1. WordPress admin → Users → Add New: a dedicated user, e.g. `news-gateway`, with a random
   password nobody needs to know, role **vahac_news_publisher** (not Administrator or Editor).
2. Users → that user → Profile → **Application Passwords**: name `AgentGateway`, click
   *Add New Application Password*, copy the shown value (it is shown once; spaces are fine).
   Application Passwords need HTTPS on the site.
3. In the gateway `.env` (or the Portainer stack's environment):

   ```text
   NEWS_ENABLED=true
   NEWS_WORDPRESS_BASE_URL=https://vahac.com
   NEWS_WORDPRESS_USER=news-gateway
   WORDPRESS_APP_PASSWORD=<the Application Password>
   NEWS_NOTIFY_TELEGRAM=true
   ```

4. Check that Cloudflare does not challenge `/wp-json/vahac/` (§5).
5. Recreate the container. Startup fails with a message naming the missing setting if the user or
   password is absent while `NEWS_ENABLED=true`.
6. Run the live check from a machine with Git Bash or Linux, curl and Python 3:

   ```bash
   bash scripts/check-news.sh
   ```

   It asks for the gateway URL and API key (hidden input), submits
   `examples/news-sample-issue.json` (36 clearly fake items titled *FIXTURE — not real news*)
   under today's date + 1 year, polls until `Published`, `PartiallyPublished` or `Failed`, prints
   the day URL and a per-item table (state, image result), then submits the same file under the
   next day and prints the 409 duplicate report. Item 7 has a deliberately broken image: expect
   `failed (http_404)` there and `Published` for the issue. Telegram should show exactly one line
   `📰 FIXTURE — not real news — 36 новин: https://vahac.com/news/<date>/`. Open that page while
   logged in to WordPress.
7. Clean up:

   ```bash
   bash scripts/check-news.sh --cleanup
   ```

   This calls `DELETE /api/news/issues/<date>`, which the gateway accepts **only** for the fixture
   (that exact title and every eventKey starting with `fixture-`; any real issue gets 409). The
   fixture items leave the registry at once and the worker moves their posts to the WordPress
   trash. The contract has no route to delete a day post, so trash the day post
   *FIXTURE — not real news* yourself (WordPress admin → the news posts list → search `FIXTURE`
   → Trash), then empty the trash if you like. If `--cleanup` reports `Failed`, fix the shown
   error and run it again; it retries only the removals that failed.

Publication runs automatically after every accepted issue. If an issue ends `PartiallyPublished`
or `Failed`, fix the cause from the error code (README, Troubleshooting), then
`POST /api/news/issues/<date>/republish` re-sends only the unfinished calls.

With `NEWS_ENABLED=true` the feed collector and the watchdog run too, with no further setting
(README, "News feeds" and "News watchdog"). About a minute after the start,
`curl -fsS "$GATEWAY_URL/api/news/feed?limit=1" -H "X-Api-Key: $API_KEY"` shows in `health` how
many feeds are enabled and failing; the container log has one `News feeds polled:` line per run.
Because the check-news fixture sits a year ahead, it never satisfies the watchdog.

## 9. Renaming to AgentGateway

The rename changes the solution, image (`agent-gateway:local`) and documentation. Everything the
running installation depends on keeps its old value: the Compose service `gateway`, the network
default `telegram-gateway`, the public hostname, `/mcp`, the OAuth issuer, client ID
`telegram-gateway` and scope `telegram:send`, the files `gateway.db`, `oauth.db` and `.secrets`
in `/app/Data`, and the `TELEGRAMGATEWAY_` variables (still read; `AGENTGATEWAY_` wins).
New tables come from an additive migration; existing rows are not touched.

The one trap is the data volume. Compose names it `<project>_gateway-data`, and the project name
is the folder name for plain `docker compose` (e.g. `telegramgateway_gateway-data`) or the stack
name in Portainer. A different project name would create a new, empty volume. Pin it:

### Before the first redeploy on the server

1. Find the real names:

   ```bash
   docker volume ls --format '{{.Name}}' | grep gateway-data
   docker ps --format '{{.Names}}  {{.Image}}' | grep gateway
   ```

   The container is called `<project>-gateway-1`; the volume `<project>_gateway-data`.
2. Back up the volume (stop the gateway for a consistent copy):

   ```bash
   docker stop <project>-gateway-1
   docker run --rm -v <volume>:/data:ro -v "$PWD":/backup alpine \
     tar czf /backup/gateway-data-$(date +%F).tgz -C /data .
   docker start <project>-gateway-1
   ```

3. Pin both names where the stack reads its variables:
   - plain Compose: in `.env` set `GATEWAY_VOLUME_NAME=<volume>` and
     `COMPOSE_PROJECT_NAME=<project>` (the project pin also makes `up` replace the old container
     instead of starting a second one next to it);
   - Portainer: keep the same stack, replace its compose file with the new `docker-compose.yml`
     (Web editor, or the Git repository with the new URL), and add the environment variable
     `GATEWAY_VOLUME_NAME=<volume>`.
4. Build the new image on that Docker host: `docker build -t agent-gateway:local .` (the compose
   file now references `agent-gateway:local`; `scripts/deploy.sh` builds it for you and refuses
   to start if the pinned volume does not exist while another `*_gateway-data` volume does).
5. Redeploy, then verify: `docker inspect <container> --format '{{range .Mounts}}{{.Name}}{{end}}'`
   prints the old volume; `/api/ready` is OK; `GET /api/deliveries/<an old id>` still answers;
   ChatGPT can still send a Telegram message without reconnecting. A startup log line lists any
   `TELEGRAMGATEWAY_` variables still in use; rename them to `AGENTGATEWAY_` when convenient.

### GitHub repository, remote and folder (after the work is merged)

1. GitHub → `VahaC/Telegram-Gateway` → Settings → General → Repository name → `AgentGateway` →
   Rename. GitHub redirects the old URL, but update every clone anyway.
2. Local clone (and the server clone, if Portainer or the server pulls from Git):

   ```bash
   git remote set-url origin https://github.com/VahaC/AgentGateway.git
   git remote -v
   git fetch
   ```

   In Portainer, update the stack's repository URL if it deploys from Git.
3. Folder: close Visual Studio/Rider, terminals and running `dotnet` processes in it, then in
   PowerShell `Rename-Item D:\Sources\TelegramGateway AgentGateway`. Run `dotnet restore` once.
   Development user-secrets keep working (the UserSecretsId did not change). If you also rename a
   server folder that runs plain Compose, step 3 above (pinned project and volume) keeps the data.

### Rolling back

- Code: check out the commit before the rename, `docker build -t telegram-gateway:local .`, and
  redeploy the old `docker-compose.yml`. The database needs nothing: the old version ignores the
  added news tables and the extra migration row. Keep `COMPOSE_PROJECT_NAME` (plain Compose) or the
  stack name (Portainer) as before, because the old compose file has no `GATEWAY_VOLUME_NAME`.
- Settings: `GATEWAY_VOLUME_NAME`, `COMPOSE_PROJECT_NAME` and the `NEWS_*`/`MCP_OAUTH_CLIENT_1_*`
  variables can stay; the old version ignores them. Remove client 1 only if you want to revoke the
  claude.ai connector (the new version deletes unconfigured clients at startup).
- Data: if anything went wrong with the volume, stop the gateway and restore the backup:
  `docker run --rm -v <volume>:/data -v "$PWD":/backup alpine sh -c 'rm -rf /data/* /data/.[!.]* ; tar xzf /backup/gateway-data-<date>.tgz -C /data'`.
- Names: GitHub → Settings → rename back to `Telegram-Gateway`;
  `git remote set-url origin https://github.com/VahaC/Telegram-Gateway.git`;
  `Rename-Item D:\Sources\AgentGateway TelegramGateway`.
