# 3. Retry WordPress news publication, unlike Telegram writes

Status: accepted

## Context

The news module receives one finished issue per day (35–80 news items, releases and security
alerts) and publishes it to the WordPress theme on vahac.com: one day post and one post per item,
each item with the picture of its source. The theme's REST routes (`/wp-json/vahac/v1/news`) are
specified by the shared contract "news issue v1" (`docs/prompts/news-module.md`).

Telegram writes are not idempotent, so [ADR 0001](0001-stop-after-uncertain-telegram-writes.md)
stops after any uncertain outcome and asks a human to inspect the chat. Applying the same rule to
40 WordPress calls a day would turn every slow image download into a manual review.

## Decision

**Every WordPress call is an upsert keyed by date or eventKey and carrying a revision, so the
gateway retries uncertain outcomes automatically.** A greater revision replaces the post; the same
or a lower one returns the stored post unchanged. A lost answer is therefore safe to repeat.

- The SQLite rows are the outbox (Queue/Worker, as for Telegram). One worker publishes one issue
  at a time and makes strictly one call at a time, `News:CallIntervalMilliseconds` apart:
  `PUT /days/{date}`, then `PUT /items/{eventKey}` for every item not yet published at the current
  revision (in number order), then `DELETE /items/{eventKey}` for items a replacement dropped, then
  `GET /days/{date}` to compare `expectedItems`, `publishedItems` and `missing` with what was sent.
  Only a matching verification makes the issue `Published`.
- Each item's state is saved after its call, so a restart or a later republish sends only the
  remaining calls. An issue left in `Publishing` by a crash returns to `Pending` on startup.
- 5xx, 408, 429, timeouts, network errors and malformed success bodies are retried with
  exponential backoff (`News:RetryBaseMilliseconds` × 2ⁿ) up to `News:MaxAttempts` (default 6).
  Other 4xx answers are final at once. Results are stable codes (`wp_401`, `wp_403`, `wp_400`,
  `wp_404`, `wp_5xx`, `wp_timeout`, `wp_network`, `wp_invalid_response`, `wp_missing`,
  `verify_mismatch`); vendor messages and the Application Password are never stored or logged.
- 401/403 stop the whole issue (`Failed`): every further call would be refused the same way.
  An item that fails for good leaves the issue `PartiallyPublished`; republish re-queues only it.
- Item calls make WordPress download the image first, so they get `News:ItemTimeoutSeconds`
  (default 60); the other calls get 30 seconds. An image WordPress could not fetch is recorded on
  the item (`image: failed` + code) and does not fail the item or the issue.
- Bodies are built from the stored, canonical issue document, so a retry or republish sends the
  same bytes for the same revision.
- When the issue becomes `Published` (or `PartiallyPublished`), one ordinary Telegram message with
  the day URL WordPress returned is queued through the existing message service, idempotency key
  `news-published-{date}-r{revision}`. That message keeps ADR 0001 semantics.

## Compared with ADR 0001

| | Telegram (ADR 0001) | WordPress news (this ADR) |
|---|---|---|
| Write semantics | `sendMessage` creates a new message every time | Upsert by key + revision |
| Lost answer / timeout | Stop, `requiresReview`, human checks the chat | Retry the same call |
| Crash during a call | Attempt becomes Ambiguous | Issue returns to Pending, call is repeated |
| Proof of success | Recorded message IDs | Per-item answers plus a verification read of the day |

## Alternatives

| Approach | Reason for rejection |
|---|---|
| Treat WordPress like Telegram (stop on uncertainty) | Daily manual reviews for an operation that is safe to repeat |
| One bulk call with the whole issue | One slow image download times out the whole issue; no per-item resume |
| Parallel item calls | Overloads a small WordPress host and its image downloads |
| Trust the 2xx answers without a verification read | A silently missing post would only be noticed by a reader |

## Consequences

Publication heals itself after timeouts, restarts and short outages, and an operator only acts on
`Failed`/`PartiallyPublished` (fix the cause, then `POST /api/news/issues/{date}/republish`).
The correctness of the retries depends on the theme honouring the revision rule of the contract.
`NewsPublicationHttpTests` pins the call order, bodies, retries, restart resume, verification,
replacement and the single notice per revision against a recording fake of the theme.
