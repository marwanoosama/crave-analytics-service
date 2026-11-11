# API Contracts — `analytics-service`

All endpoints are mounted under `/api`. Authentication via JWT in cookie `access_token`
(same token core-service issues), with `Authorization: Bearer` as fallback. This service
is **read-only** — every endpoint is a `GET`.

---

## 0. Conventions

### Headers (in)

| Header                       | Required where                | Notes                                        |
| ---------------------------- | ----------------------------- | -------------------------------------------- |
| `Cookie: access_token`       | All authenticated endpoints   | Issued by core-service `/api/auth/login`     |
| `Authorization: Bearer <jwt>`| Same — fallback if no cookie  | Checked second, after the cookie             |
| `X-CorrelationId`            | Optional                       | Propagated to core-service RBAC calls        |

### Headers (out)

| Header             | Always present                                |
| ------------------ | ---------------------------------------------- |
| `X-CorrelationId`  | Request id (echoed if provided, generated otherwise) |

### Response envelope

```jsonc
// success
{ "success": true, "data": <DTO> }

// error
{ "error": "<message>" }
```

HTTP status codes:

- `200 OK` — success
- `400` — validation error (query params, date range)
- `401` — not authenticated (missing/invalid token)
- `403` — not authorized (wrong restaurant, role lacks `analytics:read`)
- `500` — unhandled server error (`"Something went wrong"`)
- `503` — upstream unavailable (core-service RBAC lookup failed and role not cached)

### Money

Always integer **minor units** (cents/piasters) with a sibling `currency` field
(e.g. `"EGP"`, `"SAR"`).

### Timestamps & dates

- `date` fields: `YYYY-MM-DD` strings, **UTC**.
- Timestamps elsewhere: ISO 8601 UTC strings.

### IDs

`restaurantId` is core-service's numeric restaurant id, serialized as a JSON number.

---

## 1. Restaurant daily rollup

### 1.1 `GET /api/analytics/restaurants/{restaurantId}/days?from=&to=`

Per-day order/revenue rollup for one restaurant. The only analytics endpoint in v1.

**Auth chain**

1. `Authenticate` — cookie `access_token` first, then `Authorization: Bearer` (401 on failure).
2. `RequireRestaurantMember` — `system_admin` passes; `restaurant_user` must carry a
   `restaurantId` claim equal to `{restaurantId}` (403 otherwise).
3. `Rbac("analytics:read")` — `system_admin` passes; `restaurant_user` checked against
   their `restaurantRole`'s permissions (resolved from core's internal RBAC endpoint,
   cached 5 min). Other roles (customer, delivery_agent) ⇒ 403.

**Query params**

| Param  | Type   | Required | Rules                                                       |
| ------ | ------ | -------- | ----------------------------------------------------------- |
| `from` | date   | yes      | `YYYY-MM-DD` (UTC)                                           |
| `to`   | date   | yes      | `YYYY-MM-DD` (UTC), `from <= to`, range **≤ 366 days**        |

**200 — success**

```json
{
  "success": true,
  "data": [
    {
      "date": "2026-11-08",
      "ordersCount": 12,
      "revenueMinor": 150000,
      "currency": "EGP",
      "avgOrderMinor": 12500
    },
    {
      "date": "2026-11-09",
      "ordersCount": 9,
      "revenueMinor": 108750,
      "currency": "EGP",
      "avgOrderMinor": 12083
    }
  ]
}
```

- Rows are ordered by `date` ascending.
- Only days with at least one `order.placed` event appear (no zero-fill).
- `revenueMinor` sums order **`subtotal`** (food value; excludes delivery fee + service fee).
- `avgOrderMinor = floor(revenueMinor / ordersCount)` — derived per row, never stored.
- Max 366 rows per request (the range cap).
- Unknown `restaurantId` is **not** an error — analytics has no restaurant registry;
  absence of data yields `200` with `data: []`.

**Errors**

| Status | `{error}` message                          | When                                            |
| ------ | ------------------------------------------ | ------------------------------------------------ |
| 400    | `"from and to are required (YYYY-MM-DD)"`  | either param missing or unparseable              |
| 400    | `"from must be before or equal to to"`     | inverted range                                   |
| 400    | `"date range must not exceed 366 days"`     | range too wide                                   |
| 401    | `"User not authenticated"`                  | missing/invalid JWT                              |
| 403    | `"Permission denied"`                       | not a member of the restaurant, or role lacks `analytics:read` |
| 503    | `"Core service unavailable"`                | RBAC lookup failed and role not cached           |
| 500    | `"Something went wrong"`                    | unhandled error (never leaks internals)          |

---

## 2. Health

### 2.1 `GET /api/health`

No auth. Pings MongoDB.

```jsonc
// 200
{ "ok": true }

// 503 (Mongo unreachable)
{ "ok": false }
```

---

## 3. Inbound event contract (consumer side)

Analytics consumes from RabbitMQ — this section is the contract order-service already
implements (its outbox drain publishes exactly this envelope; see
`crave-order-service/backend/src/lib/events/outbox-drain.ts`).

**Topology** (declared idempotently by analytics on every connect):

| Object                                            | Value                                    |
| ------------------------------------------------- | ---------------------------------------- |
| Exchange (topic, durable)                          | `order.events`                            |
| Queue (durable, `x-dead-letter-exchange`)         | `analytics-service.order-events`           |
| Bindings                                          | `order.#`, `payment.#`                     |
| Dead-letter exchange                              | `order.events.dlx`                         |
| Dead-letter queue (bound `#`)                     | `analytics-service.order-events.dlq`      |
| Prefetch                                          | 32                                        |
| Ack mode                                          | manual (after handler commits)            |

**Delivery semantics:** at-least-once. Duplicates expected; deduped via the Mongo
`event_ids` unique index. Unknown event types are acked and skipped. Unparseable
envelopes and handler failures (after retries) are dead-lettered.

**Envelope**

```jsonc
{
  "eventId": "3f9c2a1e-...",       // uuid — the dedupe key
  "eventType": "order.placed",    // routing key
  "occurredAt": "2026-11-09T13:42:11.000Z",
  "aggregateType": "order",
  "aggregateId": "<order publicId>",
  "region": "eg",
  "payload": { /* per event type below */ }
}
```

### 3.1 `order.placed` payload

Emitted by order-service in the same DB transaction as the order write — for COD at
placement, and for online orders when the payment capture moves the order
`pending_payment → placed`. Published only when `status = "placed"`.

```jsonc
{
  "orderId": "5b0d4a2e-...",        // order publicId (uuid)
  "region": "eg",
  "countryCode": "EG",
  "restaurantId": 42,
  "branchId": 7,
  "customerId": 1234,
  "status": "placed",
  "paymentMethod": "cod",          // "cod" | "online"
  "subtotal": 150000,              // minor units — what analytics accumulates
  "deliveryFee": 2000,
  "serviceFee": 1000,
  "total": 153000,
  "currency": "EGP",
  "items": [
    { "productId": 9, "quantity": 2, "unitPrice": 5000, "lineTotal": 10000 }
  ],
  "placedAt": "2026-11-09T13:42:11.123Z"
}
```

**v1 analytics uses:** `restaurantId`, `subtotal`, `currency`, `placedAt` (→ UTC day
key). All other fields are carried for forward compatibility (branch/product rollups,
delivery durations) and currently ignored.

**Effect on the aggregate** — one atomic upsert on `agg_restaurant_day` keyed
`(restaurant_id, date)`:

```
orders_count += 1
revenue_sum  += subtotal
currency      = payload.currency        ($set)
updated_at    = now                     ($set)
```

**Limitation (documented):** v1 counts orders at placement. A later `order.cancelled`
does not decrement — see `CLAUDE.md` §13 (future handler).
