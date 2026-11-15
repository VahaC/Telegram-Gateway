# 4. The gateway collects publishers' RSS/Atom feeds; the cloud agent does not fetch their sites

Status: accepted

## Context

The daily Ukrainian tech digest is written by a Claude cloud routine. A story counts only when its
date and facts come from the publisher's own material, so the routine used to open every source
page with `WebFetch`. On 2026-11-15 it failed: after two issues (45 and 43 items) the 7-day pool
of stories it could verify was exhausted, while the editorial gate demanded at least 35 items and
one per category. Many of the most relevant sites (CNX Software, TechPowerUp, Phoronix, Tom's
Hardware, VideoCardz with 402) refuse requests from the cloud agent's data-centre addresses.

The same publishers syndicate their own news as RSS or Atom. A feed carries the publisher's own
title, link, publication date, text and usually a picture, and reading it is exactly what a
personal feed reader does. The gateway runs in the owner's homelab on a residential connection.

## Decision

**The gateway polls a committed catalog of publishers' feeds and hands the entries to the agent
through MCP (`get_news_feed`, `get_news_feed_entry`); the agent does not fetch those sites.** The
gateway behaves like an ordinary feed reader and never works around a refusal.

- The catalog `src/AgentGateway.Api/NewsFeeds/news-feeds.json` (id, name, https URL, 1–3 category
  hints, kind, language) ships in the image next to the DLL. At startup it is synced into the
  `NewsFeeds` table: new feeds are added, changed ones updated, removed ones disabled; their
  entries stay until they age out but leave the list at once. A broken entry is skipped with a reason; an unreadable file
  leaves the table as it was.
- `NewsFeedWorker` polls shortly after start and then every `News:FeedPollMinutes` (60). Hosts are
  polled in parallel (`News:FeedParallelism`, 4) but each host sees one request at a time: a third
  of the catalog is github.com. One plain GET per feed with the gateway's own User-Agent
  (`News:FeedUserAgent`, startup refuses a browser token), conditional on the last ETag and
  Last-Modified, 20 s timeout, 16 MiB cap, redirects followed except https→http.
- 402, 403, 429 and every other failure are recorded on the feed (`LastError`,
  `ConsecutiveFailures`) and nothing else happens: no retry, no other User-Agent, no proxy, no cache
  or archive copy. After a 429 the rest of that host's feeds wait for the next run
  (`skipped_host_429`).
- XML is read with `DtdProcessing.Prohibit` and no resolver. RSS 2.0, RSS 1.0/RDF and Atom are
  parsed; RFC 822 dates (with zone abbreviations) and ISO 8601 dates are understood. An entry keeps
  the feed's own title, link, date (null when the feed has none), plain-text summary of at most
  4000 characters and an https picture named by the feed (media:content, media:thumbnail, an image
  enclosure or the first `img`). Article pages are never fetched.
- Entries are unique by canonical URL across all feeds and are deleted 14 days after the gateway
  first saw them; entries already older than that are not stored. `get_news_feed` reads a 1–168 hour
  window newest first, filters by category hint, by up to 20 `|`-separated terms and, by default,
  by the news registry (the set `check_news_candidates` reports), pages with an opaque snapshot
  cursor and keeps a page under 25 KB, because cloud agents truncate large tool results.
- The tool descriptions tell the agent that the date comes from the publisher's own feed, and that
  facts beyond the feed text need the source page or another source.

## Alternatives

| Approach | Reason for rejection |
|---|---|
| Keep fetching every page from the cloud agent | Key publishers block data-centre addresses; the routine starves on thin weeks |
| Let the gateway fetch article pages for the agent | Scraping pages the publisher did not syndicate; heavier, and closer to working around blocks |
| Browser User-Agent, proxies or archive copies for blocked sites | Pretends to be someone else; the owner's rule is never to get around a refusal |
| A hosted feed service (Feedly, Inoreader) | Another account, API key and vendor between the routine and its sources |
| Push full entries into the prompt | Thousands of entries a week exceed any tool result; paging and filters keep it usable |

## Consequences

The routine gets dated, first-party leads even from sites that block it, and can narrow them to
the reader's stack in one call. The gateway now makes a few hundred outbound requests an hour and
stores about 7,000 entries a week (14-day retention); `get_news_feed`'s `health` block and one
summary log line per run show how many feeds fail. A publisher that refuses feed readers simply
stays failed until the owner removes it from the catalog. Switching the collector off
(`News:FeedsEnabled=false`) makes both tools answer with an error naming that setting.
`NewsFeedHttpTests` pins sync, conditional GET, failure recording without retry, per-host
politeness, retention, filters, paging and the page size; `NewsFeedParserTests` pins parsing and
the DTD refusal; `NewsFeedCatalogTests` checks the shipped catalog.
