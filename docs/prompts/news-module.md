# Prompt — AgentGateway: rename and news module

Hand this to a fresh Claude Code session opened in `D:\Sources\TelegramGateway`. It is
self-contained. Written 2026-11-13.

---

Turn **TelegramGateway** into **AgentGateway**: the same gateway, renamed, which keeps
delivering Telegram notifications exactly as today **and** gains a news module. The news
module receives a finished daily tech-news issue from a Claude cloud routine, keeps every news
item it has ever accepted as the duplicate registry, and publishes the issue to Vasyl's
WordPress site (vahac.com) — every news item as its own post with the picture from the original
site — where the theme shows it in a private `/news/` section.

## What Vasyl will see when this works

- Every morning a Claude routine sends one issue (5–40 news, about 35 on a normal day, + releases + CVE) to the gateway.
  About a minute later Telegram shows one line, `📰 Техдайджест 14.11.2026 — 38 новин:
  https://vahac.com/news/2026-11-14/`. That page — visible only to him while logged in — lists
  the day; each news opens as its own post (`/news/2026-11-14/proxmox-ve-9-1-release/`) with
  the original article's image. The news texts themselves never go to Telegram.
- If the routine tries to include something already published on any earlier day — the same
  URL with `?utm_source=…`, or the same event from another site under the same `eventKey` —
  the gateway refuses the issue with `409` and lists exactly which items clash and in which
  earlier issue they appeared, so the routine replaces them and resubmits.
- `GET /api/news/history?days=30` (and the MCP tool `get_news_history`) give the routine a
  compact list of everything published, so it can skip known events **before** researching
  them in depth.
- ChatGPT keeps sending Telegram messages through the same hostname, the same `/mcp`, the same
  OAuth client and the same API key, with no change on its side.

## Read first

1. The `vahac-app` skill — Vasyl's house rules for .NET apps (layout, errors, time, tests,
   Docker, docs, CHANGELOG, versioning, commits). Follow it.
2. `README.md`, `docs/integrations.md`, `docs/adr/0001-*.md`, `docs/adr/0002-*.md`,
   `BUSINESS_REQUIREMENTS.md`, `INSTALL.md`, `docker-compose.yml`, `.env.example`.
3. The code: `src/TelegramGateway.Api` (endpoints, `Mcp/GatewayTools.cs`, `Services/Delivery`,
   `Auth`, migrations), `src/TelegramGateway.Core`, `src/TelegramGateway.Infrastructure`, tests.

## Shared contract: news issue v1

> This section is **identical** in two prompts: `TelegramGateway/docs/prompts/news-module.md`
> (the gateway) and `vahac-terminal/docs/prompts/news-module.md` (the WordPress theme).
> If you have to change it, stop and tell Vasyl — the other side must change the same way.

### Roles

```text
Claude cloud routine (researches news every morning)
  -> gateway: POST /api/news/issues            (whole issue, one call)
  -> gateway: stores every item = the dedup registry (source of truth)
  -> gateway worker, one call at a time:
       PUT  /wp-json/vahac/v1/news/days/{date}          the day: order, TOP-5, releases, CVE, stats
       PUT  /wp-json/vahac/v1/news/items/{eventKey}     one call per news item (+ its image)
       GET  /wp-json/vahac/v1/news/days/{date}          verify every item is there
  -> theme: one private post per news item  -> /news/{date}/{eventKey}/
            one private day post per issue  -> /news/{date}/
            the latest news, newest first   -> /news/
  -> gateway: ONE Telegram message with the link to the day page
```

The researcher never talks to WordPress. WordPress never calls the gateway. The news texts
are **not** sent to Telegram: once the whole issue is on the site, the gateway sends a single
line, e.g. `📰 Техдайджест 14.11.2026 — 38 новин: https://vahac.com/news/2026-11-14/`
(setting `News:NotifyTelegramOnPublish`, default **on**).

### The issue document (JSON, UTF-8, camelCase)

```jsonc
{
  "contractVersion": 1,
  "date": "2026-11-14",            // issue date, Europe/Kyiv calendar day; one issue per date
  "title": "Техдайджест 14.11.2026",// <= 200 chars
  "language": "uk",
  "intro": "…",                     // optional, <= 1000 chars, plain text
  "items": [ /* NewsItem, 1..80 */ ],
  "releases": [ /* ReleaseNote, 0..40 */ ],
  "securityAlerts": [ /* SecurityAlert, 0..40 */ ],
  "research": {                     // optional, shown in the issue footer
    "mode": "6 subagents",          // <= 100 chars
    "searchPasses": 7,
    "candidates": 64,
    "duplicatesRemoved": 9,
    "windowHours": 48
  }
}
```

**NewsItem**

| Field | Type | Rules |
|---|---|---|
| `number` | int | 1..N, contiguous, unique in the issue. Items with `isTop: true` are numbers 1..5 |
| `category` | string | one of `dotnet`, `homelab`, `smarthome`, `hardware`, `security`, `ai` |
| `isTop` | bool | exactly 5 items are `true` (TOP-5) when the issue has >= 5 items |
| `importance` | int | 1..5 |
| `title` | string | 1..200 chars |
| `summary` | string | 1..2000 chars, **plain text**, `\n\n` separates paragraphs; no HTML, no Markdown |
| `whyItMatters` | string | 1..600 chars, plain text ("Чому цікаво") |
| `publishedOn` | date | `yyyy-MM-dd`, publication date of the source |
| `eventOn` | date? | `yyyy-MM-dd`, when the event happened if it differs |
| `releaseStatus` | string? | `stable`, `beta`, `preview`, `rc`, `rfc`, `announced`, `research`, `incident`, `other` |
| `version` | string? | <= 50 chars |
| `eventKey` | string | `^[a-z0-9]+(-[a-z0-9]+)*$`, 3..100 chars, chosen by the researcher to name the *event*, e.g. `proxmox-ve-9-1-release`; the same event from two sites gets the same key |
| `source` | Source | the primary, direct URL of the material (never a home page, tag page or archive) |
| `extraSources` | Source[] | 0..3 independent confirmations |
| `image` | Image? | the picture of the original material, see below; optional but wanted |
| `tags` | string[] | 0..8, each <= 40 chars |

**Image**: `{ "url": "https://…/cover.jpg", "alt": "≤ 200 chars" }` — a direct image URL taken
from the source page itself (its `og:image`, `twitter:image` or the article's lead image), never
a search-engine thumbnail or a stock photo. `url` absolute `https`, <= 2000 chars. The theme
downloads it; if that fails the item is still published, without a picture.

**Source**: `{ "url": "https://…", "name": "Microsoft .NET Blog" }` — `url` absolute `https`
(or `http`), <= 2000 chars; `name` 1..100 chars.

**ReleaseNote**: `{ "name": "ESPHome", "version": "2026.11.1", "date": "2026-11-13",
"url": "https://…", "note": "≤ 300 chars plain text" }`

**SecurityAlert**: `{ "id": "CVE-2026-12345", "product": "…", "severity": "critical|high|medium|low",
"cvss": 9.8 /* optional */, "date": "2026-11-13", "url": "https://…", "note": "≤ 300 chars" }`

Releases and security alerts are **not** news items and do not count toward any quota.

### Canonical URL (dedup key)

The gateway computes it (WordPress does not need it): lower-case scheme and host, drop a leading `www.`, drop the
default port, drop the fragment, drop query parameters `utm_*`, `fbclid`, `gclid`, `mc_cid`,
`mc_eid`, `ref`, `source`, sort the remaining query parameters, drop a trailing `/` except for
the root path. `http` and `https` versions of the same URL are the same canonical URL.

### Editorial gate (checked by the gateway, configurable)

- at least `MinItems` items (default **5**), at most `MaxItems` (default **40**; the contract allows 1..80);
- every one of the 6 categories has at least `MinPerCategory` items (default **0**: no category is mandatory);
- `ai` items <= `MaxAiShare` of all items (default 0.30);
- exactly 5 TOP items, numbered 1..5;
- no `source.url` canonical URL and no `eventKey` that already exists in **any earlier issue**,
  and no duplicates inside the issue; `extraSources` must not point to an earlier item's
  primary URL either.

### Gateway → WordPress

Base: `https://vahac.com/wp-json/vahac/v1/news`. Every call:

- Auth: WordPress **Application Password** of a dedicated user (Basic auth over HTTPS). The
  user has the role `vahac_news_publisher`, whose only real capability is `vahac_publish_news`.
- Carries `"gateway": { "issueId": "uuid", "revision": 1, "submittedAt": "2026-11-14T05:12:03Z" }`.
- Is an **upsert with a revision**: a greater `revision` than the stored one replaces the
  content; the same or a lower one returns the stored post unchanged with `200`. So every call
  is safe to repeat after a timeout.
- Errors use the WordPress REST shape: `400` `{ "code": "...", "message": "...",
  "data": { "errors": ["items[3].title: ..."] } }`; `401/403` auth; `413` body above 1 MiB.

**`PUT /days/{date}`** — the day post. Body: the issue document **without the item bodies**:
`date`, `title`, `language`, `intro`, `releases`, `securityAlerts`, `research`, `gateway`, and
`items` replaced by `itemOrder: [{ "number": 1, "eventKey": "…", "category": "…", "isTop": true }]`.
Response `201`/`200`: `{ "postId": 120, "url": "https://vahac.com/news/2026-11-14/",
"status": "private", "revision": 1 }`.

**`PUT /items/{eventKey}`** — one news post. Body: one NewsItem plus `issueDate` and `gateway`.
The post's slug is the `eventKey`, its URL `/news/{issueDate}/{eventKey}/`. When `image` is
present and differs from the stored one, the theme downloads it and sets it as the featured
image; the same image on a later revision is not downloaded again. Response `201`/`200`:
`{ "postId": 121, "url": "https://vahac.com/news/2026-11-14/proxmox-ve-9-1-release/",
"status": "private", "revision": 1, "image": "attached" | "unchanged" | "none" | "failed",
"imageError": "http_404" /* only when failed */ }`. A failed image is **not** an error of the
call.

**`DELETE /items/{eventKey}`** — moves the news post to the trash (used when a replaced issue no
longer contains that item). `200` `{ "deleted": true }`; `404` when it does not exist, which the
gateway treats as done.

**`GET /days/{date}`** — `{ "postId", "url", "status", "revision", "expectedItems": 38,
"publishedItems": 38, "missing": ["event-key", …] }` or `404`. **`GET /items/{eventKey}`** —
`{ "postId", "url", "status", "revision", "image" }` or `404`.

## Part 1 — Rename without breaking the running installation

The gateway is live: ChatGPT calls it over MCP with OAuth, behind Vasyl's reverse proxy /
Cloudflare. The rename must not cost him data or reconfiguration.

- Rename solution, projects, namespaces, assembly names, Docker image (`agent-gateway`), docs
  and the product name to **AgentGateway**. Use `git mv` so history follows the files.
- **Do not change anything the outside world or the data volume depends on**, unless the
  default stays the old value:
  - the Compose **project name and volume**: renaming the folder later changes Compose's default
    project name, which silently creates a new empty volume. Pin it: top-level `name:` and/or an
    explicit volume `name:` equal to what the running stack uses today. You cannot see the
    server: the name depends on how it was deployed (a Portainer stack uses the stack name, a
    plain `docker compose` uses the folder name, e.g. `telegramgateway_gateway-data`). Make the
    name a variable whose default keeps today's behaviour, and tell Vasyl to read the real name
    with `docker volume ls` before redeploying;
  - the Docker network default `telegram-gateway`, the service name the proxy points to, the
    public hostname, `/mcp`, the OAuth issuer/client ID `telegram-gateway`, scope `telegram:send`;
  - the SQLite file names inside `/app/Data`, `oauth.db`, `.secrets`. New tables come from
    additive EF migrations; existing rows stay untouched.
- Configuration prefix: read `AGENTGATEWAY_` **and** the old `TELEGRAMGATEWAY_`; the new one
  wins; log once at startup (names only, never values) which old keys are still in use. Keep
  `TELEGRAM_BOT_TOKEN`, `TELEGRAM_CHAT_ID`, `API_KEY` as they are.
- Telegram stays exactly as it is: `/api/messages`, `send_telegram_message`, delivery statuses,
  ADR 0001 semantics. `/api/digests` and `send_technology_digest` keep working but are marked
  **deprecated** in the docs (news now goes to the website).
- Vasyl renames the folder `D:\Sources\TelegramGateway` and the GitHub repository himself after
  the work is merged. At the end, give him the exact steps (GitHub rename, `git remote set-url`,
  folder rename, how to redeploy in Portainer without losing the volume) and how to undo.

## Part 2 — News module

### Storage (same `ApplicationDbContext`, new migration)

- `NewsIssue`: id (uuid), date (unique), title, language, intro, research stats, payload SHA-256,
  revision (starts at 1), submittedAt, and publication state: `Pending → Publishing → Published`
  or `PartiallyPublished` / `Failed`, attempts, last safe error code, day `wpPostId`/`wpUrl`,
  `publishedAt`, the Telegram notice's delivery id.
- `NewsItem`: id, issue id, number, category, isTop, importance, title, summary, whyItMatters,
  publishedOn, eventOn, releaseStatus, version, eventKey, source url/name, **canonical url**,
  image url/alt, tags, and its own publication state: published revision, `wpPostId`, `wpUrl`,
  image result (`attached`/`unchanged`/`none`/`failed` + code), attempts, last error. Unique
  index on canonical URL and on eventKey across all issues.
- `NewsSource` (extra sources) with canonical URL; `NewsRelease`, `NewsSecurityAlert` with
  canonical URL (deduplicated against earlier releases/alerts, not against news items).
- Store the exact accepted document too, so a republish sends the same bytes.

### HTTP API (all with `X-Api-Key`, same error shape `{ "error": "..." }` as today)

| Method | Route | Behavior |
|---|---|---|
| POST | `/api/news/issues` | Validate structure (400), editorial gate (422 with `violations: [...]`), duplicates (409 with `duplicates: [{ path, canonicalUrl or eventKey, issueDate, itemNumber, title }]`). On success `202` + `Location`, queue publication. Identical repeat for the same date → `200` with the existing issue. Different content for an existing date → `409` (use PUT) |
| PUT | `/api/news/issues/{date}` | Replace an existing issue (fixes after the fact). Same validation; dedup ignores the issue's own items. Bumps `revision`, re-queues publication |
| GET | `/api/news/issues/{date}` | Status: counts per category, publication state, day URL, per item: number, eventKey, state, URL, image result, error; the Telegram notice's delivery state |
| POST | `/api/news/issues/{date}/republish` | Re-queue a `Failed`/`PartiallyPublished` publication (only the unfinished calls); no-op for `Published` |
| GET | `/api/news/history?days=30` | Compact registry, newest first, max 180 days: `{ date, number, category, title, eventKey, canonicalUrl }` plus releases/alerts as `{ date, kind, name/id, canonicalUrl }` |
| POST | `/api/news/check` | Body `{ "urls": [...], "eventKeys": [...] }` (≤ 200 each) → `{ "known": [{ value, kind, issueDate, itemNumber, title }] }` |

Raise the request-body limit for the news routes only (a 45-item Ukrainian issue is ≈150–250
KiB); keep 128 KiB elsewhere. Gate values come from options `News:MinItems`, `News:MaxItems`,
`News:MinPerCategory`, `News:MaxAiShare`.

### Publication worker

- A background worker in the style of the existing delivery worker, using the SQLite rows as the
  outbox. Per issue, strictly one call at a time (`News:CallIntervalMilliseconds`, default 500):
  `PUT /days/{date}`, then `PUT /items/{eventKey}` for every item not yet published at the
  current revision, then on a replaced issue `DELETE /items/{eventKey}` for items that were
  dropped, then `GET /days/{date}` and compare `publishedItems`/`missing` with what was sent.
  Only then the issue is `Published`.
- Each item call is tracked separately, so a restart or a failed item resumes with the
  remaining calls only. Items that end in `Failed` leave the issue `PartiallyPublished`.
- Unlike Telegram, these writes are **idempotent** (upsert by key + revision), so a timeout or
  lost response is retried safely: exponential backoff, bounded attempts (`News:MaxAttempts`,
  default 6), then `Failed` with a safe code (`wp_401`, `wp_403`, `wp_400`, `wp_404`, `wp_5xx`,
  `wp_timeout`, `wp_network`). 4xx other than 408/429 are not retried. Item calls download an
  image on the WordPress side, so give them a longer timeout (`News:ItemTimeoutSeconds`,
  default 60). Write this down as ADR 0003 and contrast it with ADR 0001.
- An image that WordPress could not fetch is recorded on the item (`image: failed`, code) and
  shown in the status, but it does not fail the item or the issue.
- Options: `News:WordPressBaseUrl` (`https://vahac.com`), `News:WordPressUser`,
  `News:WordPressAppPassword` (secret, env only, never logged), `News:Enabled`.
- `News:NotifyTelegramOnPublish` (default **true**): when the issue becomes `Published` (or
  `PartiallyPublished`, then the line says how many are on the site), queue **one** ordinary
  Telegram message through the existing message service, idempotency key
  `news-published-{date}-r{revision}`:
  `📰 Техдайджест 14.11.2026 — 38 новин: https://vahac.com/news/2026-11-14/`
  The URL is the one WordPress returned for the day post, never built by hand. Store the
  delivery id on the issue; the Telegram delivery keeps ADR 0001 semantics.

### MCP tools

`get_news_history(days)`, `check_news_candidates(urls, eventKeys)`, `submit_news_issue(issue)`,
`replace_news_issue(date, issue)`, `get_news_issue_status(date)` — thin adapters over the same
services, with descriptions that tell an AI caller the workflow: read history first, check
candidates, submit once, on 409/422 fix exactly the listed items and resubmit, then poll status
until `Published`.

> Added 2026-11-15 (ADR 0004): `get_news_feed(sinceHours, category, query, excludePublished, limit,
> cursor)` and `get_news_feed_entry(id)` give the routine entries the gateway collected from the
> publishers' own RSS/Atom feeds, and a watchdog alerts on Telegram when no issue arrived. See
> README, "News feeds" and "News watchdog".

OAuth: add scope `news:publish`. Telegram tools need `telegram:send`, news tools need
`news:publish`; the API key grants both. ChatGPT's existing tokens keep working for Telegram.

**Several OAuth clients.** Today exactly one confidential client is configured. Add support for
a list (`McpOAuth:Clients:0:ClientId`, `…:ClientSecret`, `…:RedirectUris:0`, `…:Scopes:0`) while
the current single-client keys keep working as client 0. The Claude cloud routine will use the
gateway as a claude.ai custom connector — look up Claude's current documentation for its exact
OAuth callback URL and put it in the docs and `.env.example` as the example for client 1.

## Tests (real HTTP + real SQLite, as the project already does)

- Rename: the app starts with only old-prefix variables and with only new-prefix ones; existing
  message/digest tests still pass unchanged.
- Canonical URL normalisation: a table of inputs → outputs, including the `utm_`/`www`/port/
  trailing slash/fragment/sorted-query cases from the contract.
- Gate: each violation separately (4 items, 41 items, a missing category with `MinPerCategory` 1,
  AI share 32.5%, 4 TOP items, TOP numbered 3..7, gaps in numbering) → 422 with the right
  `violations`; 5 items with an empty category pass the default gate.
- Dedup: same URL with tracking params on an earlier day → 409 naming that day and item; same
  eventKey with a different URL → 409; duplicates inside one issue → 409; PUT of the same issue
  does not clash with itself; history and check endpoints return what was stored.
- Publication against a recording fake WordPress handler: the call order (day, items in number
  order, verify) and the bodies match the contract (the day body has `itemOrder`, no item
  texts); all 201 → Published with the day URL and one Telegram notice with exactly that URL;
  500 then 201 on item 7 → Published after a retry; timeout then 200 → Published (the fake
  sees the same revision twice); 401 → Failed `wp_401` without retry; item 12 fails for good →
  PartiallyPublished, the notice says `37 з 38`, republish sends only item 12; `image: failed`
  is recorded but the issue is Published; restart after item 20 resumes at item 21; verify
  reports `missing` → not Published; PUT of a replaced issue sends revision 2, re-PUTs every
  item and DELETEs a dropped one; the notice is sent once per revision, never twice.
- MCP: each news tool, and scope enforcement (`telegram:send` token cannot submit news).
- OAuth: two clients with different callbacks both complete the flow.

Add a `examples/news-sample-issue.json` fixture (36 clearly fake items, every field used,
`"title": "FIXTURE — not real news"`, eventKeys starting with `fixture-`, images pointing to a
few stable public test images, one deliberately broken image URL). Vasyl's theme uses a copy
of the same file.

## Docs

README (architecture now has two outputs: Telegram and WordPress), `docs/integrations.md`
(news workflow for AI callers, MCP tools, the claude.ai connector setup), INSTALL (WordPress
Application Password: create the user with role `vahac_news_publisher` in the theme, generate
the password, put it into `.env`; Cloudflare must not challenge requests to `/wp-json/vahac/`),
`.env.example`, BUSINESS_REQUIREMENTS, ROADMAP, ADR 0003, CHANGELOG.

## Live check (Vasyl runs it; it needs secrets you never type)

Write `scripts/check-news.sh` that prompts (hidden input) for the gateway URL and API key, sends
`examples/news-sample-issue.json` under today's date + 1 year (so it never collides with a real
issue), polls the status until `Published`, `PartiallyPublished` or `Failed`, prints the day URL
and the per-item table (state, image result), then sends the same file under another date and
shows the 409 duplicate report. Vasyl should also see the one-line notice in Telegram. Tell him how to delete the fixture post in
WordPress and the fixture rows in the gateway afterwards (or give the script a `--cleanup` that
calls a guarded admin endpoint — your call, explain it).

## Done means

- `dotnet build -c Release` with warnings as errors, all tests green, Docker smoke green.
- A short report in Ukrainian for Vasyl: what changed, the exact `.env` additions, the deploy
  steps that keep the existing volume, the rename steps, and how to roll back.
- **Do not commit or push.** Vasyl commits himself after reviewing.
