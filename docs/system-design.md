# System Design — `analytics-service`

How the analytics service fits into the Crave platform, its sync/async interactions, the
event pipeline, failure modes, and the reasoning behind the dedupe design.

---

## 1. Position in the platform

```
                      ┌──────────────────────────────────────┐
                      │           clients (web / app)         │
                      └────┬──────────────┬─────────────┬────┘
                           │              │             │
             JWT (cookie) │      JWT      │     JWT (read-only)
                           ▼              ▼             ▼
              ┌─────────────────┐ ┌─────────────────┐ ┌────────────────────────┐
              │  core-service   │ │  order-service   │ │ analytics-service ★    │
              │  Node/TS + PG   │ │  Node/TS + PG   │ │ .NET 10 + MongoDB      │
              │                 │ │  (sharded) +    │ │                        │
              │  users          │ │  Redis          │ │  no Redis · no outbox  │
              │  restaurants    │ │                 │ │  no operational writes │
              │  products       │ │  orders         │ │                        │
              │  RBAC catalog   │ │  payments       │ │  per-day rollups       │
              │                 │ │  deliveries     │ │  GET /api/analytics/*  │
              └────────────────┘ └────────────────┘ └────────────────────────┘
                      ▲                  │    ▲                  ▲
                      │                  │    │                  │
                 sync HTTP          transactional    sync HTTP        consume
                 (api-key)          outbox drain    (api-key)       order.#, payment.#
```

| Edge                       | Transport                       | Purpose                                             |
| -------------------------- | ------------------------------- | --------------------------------------------------- |
| client → core / order      | HTTPS, JWT cookie               | operational reads/writes                             |
| client → analytics          | HTTPS, JWT cookie               | **read-only** analytics queries                      |
| order → core                | HTTP, `api-key` header          | branch/product/address lookups, RBAC perms (cached) |
| analytics → core            | HTTP, `api-key` header          | RBAC permissions by role (5 min in-process cache)   |
| core → RabbitMQ             | `core.events` exchange          | cache-invalidation events → order-service queue      |
| order → RabbitMQ            | `order.events` exchange         | `order.*` / `payment.*` events → analytics queue ★  |

**Why async for the rollup feed:** order placement's latency budget must not include
analytics, and analytics' availability must not depend on order-service's request path.
The transactional outbox on order-service guarantees the domain write and the event row
commit together; analytics consumes at its own pace.

---

## 2. The event pipeline (zoomed)

```
order-service                     RabbitMQ                       analytics-service
─────────────                     ────────                       ─────────────────
domain trx
  ├─ write order
  └─ insert events_outbox ─────►  exchange: order.events
        (same DB trx)              (topic, durable)
                                  queue: analytics-service.order-events
                                  bindings: order.#, payment.#
                                  DLX: order.events.dlx
                                    └─ DLQ: analytics-service.order-events.dlq
                                        (poison / handler-failure messages)
                                        │
                                        │  at-least-once · manual ack · prefetch 32
                                        ▼
                                  ┌──────────────────────────────────────┐
                                  │ OrderEventsConsumerService            │
                                  │                                        │
                                  │  1. parse envelope (JSON)              │
                                  │     └─ invalid / missing ids ──► DLQ    │
                                  │  2. EventDeduper.MarkSeen(eventId)     │
                                  │     └─ E11000 duplicate ──► ack + skip │
                                  │  3. registry lookup by eventType       │
                                  │     └─ unknown type ──► log + ack      │
                                  │  4. run handler                        │
                                  │     ├─ ok ───────────► ack             │
                                  │     └─ fail (after retries) ► DLQ       │
                                  └──────────────┬───────────────────────┘
                                                 ▼
                                  ┌──────────────────────────────────────┐
                                  │ MongoDB — crave_analytics              │
                                  │                                        │
                                  │  event_ids                             │
                                  │    unique index on event_id            │
                                  │    TTL index on received_at (7 days)   │
                                  │                                        │
                                  │  agg_restaurant_day                    │
                                  │    upsert on (restaurant_id, date)      │
                                  │    $inc { orders_count: 1,             │
                                  │            revenue_sum: subtotal }     │
                                  │    $set { currency, updated_at }       │
                                  └──────────────────────────────────────┘
```

### Envelope (published by order-service's outbox drain)

```jsonc
{
  "eventId": "<uuid>",                 // dedupe key — unique insert into event_ids
  "eventType": "order.placed",         // routing key == registry key
  "occurredAt": "2026-11-09T13:42:11Z",
  "aggregateType": "order",
  "aggregateId": "<order publicId>",
  "region": "eg",
  "payload": { /* order.placed payload — see docs/api-contracts.md §3 */ }
}
```

Full payload contract: `docs/api-contracts.md` §3. v1 uses only `restaurantId`,
`subtotal`, `currency`, and `placedAt` (→ UTC day key) from the payload; the rest is
carried for forward compatibility.

---

## 3. Sync vs async — who talks to whom, when

### Sync (request/response, on the hot read path)

| Call                              | Trigger                                       | Failure behavior                     |
| --------------------------------- | --------------------------------------------- | ------------------------------------ |
| `client → GET /api/analytics/restaurants/{id}/days` | dashboard render        | 401/403/400/503 per `api-contracts.md` |
| `analytics → core GET /api/internal/rbac/permissions?role=` | RBAC cache miss (≤1 per role per 5 min) | 503 to the caller if core is down and the role isn't cached |

Sync calls to core carry the shared `api-key` header and the propagated `X-CorrelationId`.
No other service-to-service sync exists. **Analytics never calls order-service.**

### Async (event-driven, the write path)

| Direction        | Mechanism                                                        | Semantics                          |
| ---------------- | ---------------------------------------------------------------- | ---------------------------------- |
| order → analytics | `order.events` topic exchange, persistent messages, durable queue | **at-least-once** delivery          |
| analytics side   | dedupe (`event_ids` unique index) before dispatch                | effectively **exactly-once** apply |
| poison           | DLX `order.events.dlx` → DLQ `analytics-service.order-events.dlq`| never lost, never blocks the queue  |

Event types currently flowing: `order.placed`, `order.accepted`, `order.rejected`,
`order.delivered`, `order.cancelled`, `payment.completed`, `payment.failed`. Analytics
v1 registers a handler for **`order.placed` only**; the rest are logged and acked
(bindings `order.#, payment.#` are deliberately wider than the handler set so future
handlers need no topology change).

---

## 4. Data model (MongoDB, `crave_analytics`)

### `agg_restaurant_day`

```jsonc
{
  "_id": "<ObjectId>",
  "restaurant_id": 42,          // core's numeric restaurant id (logical ref)
  "date": "2026-11-09",        // UTC day key "YYYY-MM-DD", derived from placedAt
  "currency": "EGP",           // minor units' currency
  "orders_count": 12,          // sum-style counters…
  "revenue_sum": 150000,       //   …so replays merge associatively
  "delivery_ms_sum": 0,        // populated by a future order.delivered handler
  "delivery_ms_count": 0,      //   (kept in v1 for forward compatibility)
  "updated_at": "2026-11-09T13:42:12Z"
}
```

- `revenue_sum` accumulates **`subtotal`** (food value, excludes delivery fee + service fee).
- Averages are **never stored** — `avgOrderMinor = floor(revenue_sum / orders_count)` is
  derived in the service layer.
- Writes are a single atomic `FindOneAndUpdate` upsert (`$inc` + `$set`), so competing
  consumers and redeliveries can never interleave a read-modify-write.

| Index                              | Supports                                            |
| ---------------------------------- | --------------------------------------------------- |
| unique `(restaurant_id, date)`      | the upsert identity — one row per restaurant per day |
| range `(date, restaurant_id)`       | `GET …/days?from=&to=` (date range, ascending scan) |

### `event_ids`

```jsonc
{ "_id": "<ObjectId>", "event_id": "<uuid>", "received_at": "<ISO>" }
```

| Index                        | Supports                                        |
| ---------------------------- | ----------------------------------------------- |
| unique `event_id`            | `EventDeduper.MarkSeen` — E11000 ⇒ already seen |
| TTL `received_at` (7 days)   | automatic cleanup; matches realistic redelivery/replay horizon |

---

## 5. Failure-mode table

| # | Failure                                | What happens                                                                                                                   | Blast radius                        | Recovery                                                                    |
| - | -------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------ | ----------------------------------- | --------------------------------------------------------------------------- |
| 1 | RabbitMQ unreachable                   | Consumer reconnects with backoff. Events accumulate in order-service's `events_outbox` (its drain fails) or the broker queue.   | Aggregates go stale. Reads fine.     | Automatic when broker returns; outbox retains nothing-lost guarantee.        |
| 2 | Mongo unreachable at boot              | `EnsureIndexes` fails → process exits loudly (crash loop).                                                                     | Service down (visible).             | Fix Mongo; process restarts and re-ensures idempotently.                    |
| 3 | Mongo unreachable mid-run              | Handler fails after in-handler retries → message DLQ'd; reads fail health (503).                                               | Missed events parked in DLQ.        | Ops replays DLQ after Mongo is back — safe within the 7-day dedupe window.  |
| 4 | Poison message (unparseable envelope)  | Immediately DLQ'd, logged with queue-level detail. Never blocks the queue.                                                      | That one message.                   | Manual inspection of DLQ.                                                   |
| 5 | Duplicate delivery (redelivery)        | `MarkSeen` hits E11000 → **ack-and-skip**. No double-count.                                                                    | None.                               | —                                                                           |
| 6 | Unknown event type                     | Logged (`warn`) + ack-skip — never DLQ'd (future handlers need this traffic).                                                   | None.                               | —                                                                           |
| 7 | Handler failure (bug, bad payload)     | Retries with backoff inside the handler, then DLQ. Aggregate not applied for that event.                                        | That event.                         | Fix → replay from DLQ within 7 days = exact-once via `event_ids`.           |
| 8 | Crash between `MarkSeen` and upsert    | Event marked seen but not counted (at-most-once window, milliseconds wide).                                                     | Undercount by one order.            | Documented residual risk; upgrade path = multi-document Mongo transaction.  |
| 9 | DLQ replayed **after** 7-day TTL       | Dedupe key expired → event reapplied → double count.                                                                           | That event's sums.                  | Ops rule: replay within the window, or rebuild aggregates from order data.  |
| 10 | Core down during RBAC cache miss       | 503 to that request. `system_admin` unaffected; cached roles keep serving until TTL.                                            | Restaurant-user reads only.         | Core recovery; TTL bounds staleness at 5 min.                               |
| 11 | JWT missing/invalid                    | 401 `{error}` envelope.                                                                                                        | That request.                        | —                                                                           |
| 12 | order-service publisher outage        | Staleness only — no loss: events sit in `events_outbox` with `attempts++`.                                                     | Aggregates lag.                     | Outbox drains when broker/publisher recovers.                               |
| 13 | Bad index definition deployed          | `createIndex` conflicts with existing → boot fails loudly.                                                                       | Service down (visible).             | Fix forward; indexes are the only schema-like artifact.                     |
| 14 | Meal-hour event burst                  | Prefetch 32 + manual ack keeps the consumer fair; durable queue absorbs depth; handler ≈ 2 Mongo ops (≈50ms).                   | Slight lag.                         | Scales horizontally — competing consumers, atomic upserts.                  |

---

## 6. Why Mongo dedupe, not Redis `SETNX`

`order-service` dedupes its inbound `core.events` with Redis `SETNX` (24h TTL). This
service deliberately does **not**, for five reasons:

1. **No other Redis dependency.** Order-service needs Redis (caches, idempotency keys,
   presence, WS fan-out). Analytics needs none of that. Adding Redis for a single
   `SETNX` means a whole extra stateful system to deploy, monitor, and fail — for zero
   functionality. `docs` principle: fewest moving parts that satisfy the requirement.
2. **Loss semantics differ.** Order-service's handlers are idempotent cache
   *invalidations* — losing a dedupe key is harmless (worst case a redundant `DEL`).
   Analytics handlers mutate durable **sums** — a lost dedupe key means a redelivered
   `order.placed` double-counts revenue. A Mongo unique index is enforced by the
   database on every insert, survives restarts, and cannot be evicted under memory
   pressure. Redis keys can (eviction, restart without persistence).
3. **Co-located with the write it guards.** The dedupe insert and the aggregate upsert
   hit the same database: one system to reason about, one backup, one latency budget,
   and (documented upgrade path) the option of wrapping both in a single multi-document
   transaction later. Splitting them across Redis + Mongo gives a two-system crash
   window instead of a one-system one.
4. **TTL cleanup for free.** The TTL index on `received_at` expires dedupe rows after
   7 days with no cron, no script, no extra protocol.
5. **Platform precedent.** Order-service already dedupes *money-critical* deliveries
   (Kashier webhooks) with a unique constraint at the SQL boundary
   (`payment_webhook_events`) rather than Redis — unique-index dedupe for durable writes
   is the established pattern; we are consistent with it.

**Trade-off acknowledged:** Redis `SETNX` is faster (sub-ms vs ~1–2ms). This service is
not hot-path; the handler budget is 50ms for two Mongo ops. If analytics ever becomes a
high-volume consumer, fronting Mongo with Redis is the documented evolution — the
`EventDeduper` seam (`IEventDeduper`) exists precisely so that swap is one class.

### Replay-safety model (why sums + counts)

`orders_count`/`revenue_sum`/`delivery_ms_sum` are **additive counters**, so per-day
documents merge associatively: applying N events in any order, in any batch grouping,
yields the same document. Combined with `event_ids` dedupe, the effective semantics are:

```
delivery:      at-least-once  (broker)
per-event apply: exactly-once  (unique event_id insert guards dispatch)
per-day merge:  associative    (sums + counts, never averages)
```

The only residual gaps are #8 (crash inside the mark-seen→upsert window) and #9 (replay
after TTL expiry) — both in the failure-mode table with their mitigations.

---

## 7. Local run

Prereqs: .NET 10 SDK, Docker (Mongo + RabbitMQ), and for full end-to-end: core-service
and order-service running with matching `ACCESS_SECRET` / internal API key.

```bash
# 1. infrastructure
docker run -d --name crave-mongo -p 27017:27017 mongo:7
docker run -d --name crave-rabbit -p 5672:5672 -p 15672:15672 rabbitmq:3-management

# 2. env
cp .env.example .env          # fill ACCESS_SECRET, CORE_INTERNAL_API_KEY

# 3. run (env vars come from the process environment — docker-compose env_file or export)
set -a; source .env; set +a   # or: export $(grep -v '^#' .env | xargs)
cd backend && dotnet run --project src/Crave.Analytics.Api

# 4. smoke
curl -s localhost:5000/api/health
./scripts/publish-test-event.sh    # Phase 2: publish a raw order.placed envelope
```

The consumer is non-fatal at boot when RabbitMQ is down (it keeps retrying with
backoff), mirroring the Node siblings' startup resilience.
