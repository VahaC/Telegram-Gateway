# 1. Stop after uncertain Telegram writes

Status: accepted for the development implementation

## Context

The gateway must persist daily idempotency and resume a partial multi-message digest. Telegram
[sendMessage](https://core.telegram.org/bots/api#sendmessage) returns a message ID on success,
but exposes no idempotency-key parameter. Sending is not part of the local SQLite transaction.
A network response or local write can fail after Telegram has accepted a message.

## Decision

**Persist an attempt before each external write, persist observed IDs immediately, and stop
automatic replay when the outcome cannot be established.**

The durable SQLite outbox is the queue. A conditional status update claims Pending work; one
process owns each local data volume using an exclusive lock. Startup reconciliation examines
Sending deliveries. An uncompleted attempt becomes Ambiguous and sets RequiresReview. If all
parts have confirmed IDs, the delivery becomes Delivered; if a safe interruption occurred
between known attempts, undelivered work returns to Pending.

Known failed parts may resume through a repeat of the identical payload/key. Completed parts
are skipped. A different payload using the same key gets 409. Daily dates are unique even when
callers choose different keys; additional notifications use the separate message endpoint.

Explicit rate-limit responses honor retry_after. Explicit 5xx responses and transport failures
known to occur before sending use a bounded retry budget. Generic connection drops, timeouts,
malformed success responses, and local crashes are treated as uncertain. Only stable local
error codes are retained, never vendor error descriptions or credentials.

The observed flood-control deadline is also persisted and applies to every subsequent send to
the configured chat, including other deliveries and restarted workers. A resubmission cannot
bypass that deadline. `FloodControlHttpTests.Restart_retry_after_persists_and_blocks_other_deliveries_until_due`
pins the same-volume application restart behavior.

## Alternatives

| Approach | Reason for rejection |
|---|---|
| Blind retry after every timeout | Can duplicate messages when only the acknowledgement was lost |
| In-memory keys | Loses duplicate prevention on restart |
| Claim exactly-once using SQLite | Cannot atomically commit Telegram and SQLite together |
| Add Redis/broker | Does not solve the external write's uncertain outcome and adds infrastructure |
| Automatically delete/reset failed rows | Removes idempotency and obscures unresolved attempts |

## Consequences

Operators must occasionally inspect the private chat. No unsafe reset/delete endpoint is
included. Reconcile uncertain attempts only against actual verified message IDs, with the
worker stopped and a backup retained. Keys and dates cannot silently be reused for new content.
Known duplicate prevention is strong; exactly-once delivery remains outside the Telegram API
contract, including potentially side-effecting upstream 5xx responses.

`DeliveryHttpTests.Post_partial_failure_retries_only_undelivered_parts`,
`DeliveryHttpTests.Post_ambiguous_outcome_blocks_automatic_resubmission`,
`DeliveryHttpTests.Post_concurrent_duplicate_requests_create_one_delivery`, and
`PersistenceHttpTests.Restart_pending_digest_is_resumed_and_inflight_attempt_requires_review`
pin this behavior. Container smoke separately verifies the persistent ledger after an actual
container restart without reaching Telegram.

## Setup-send correction (development)

A bot token contains a colon. An unprefixed `bot{token}/sendMessage` string was parsed as an
absolute URI with a `bot<number>` scheme, rejected by the real HTTP handler before sending.
The adapter now prefixes the relative path with `./`; the resolved destination is HTTPS.
`TelegramClientTests.Send_token_colon_stays_in_https_path_and_delivery_is_recorded` pins the
actual outbound URI, and container smoke exercises the real handler on a network without egress.
Unexpected worker failures log only their exception type. Existing uncertain rows are preserved;
the URL correction does not authorize automatic replay of historical attempts.

MCP clients follow the same rule: reuse the daily key and content on a retry, and confirm
Delivered rather than accepting queue submission as completed delivery.
`get_delivery_status` adapts the existing delivery service's UUID lookup for notifications
and digests; the digest-date lookup remains available. Neither status query mutates the ledger
or retries Telegram writes. HTTP/MCP tests verify pending-to-delivered transitions and
failed/uncertain outcomes without additional outbound requests.
Private client-file configuration changes how credentials are supplied, not delivery semantics.
