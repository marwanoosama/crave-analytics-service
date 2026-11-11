# Implementation Plan — `analytics-service`

Sequenced build order. Every phase ends with something runnable and manually verifiable.
The layering rules, naming, and conventions live in `CLAUDE.md`; this file is only the
order of work.

**Parallel work on `core-service`.** This service depends on one core-service change: the
`analytics:read` permission seed (Phase 6). Everything else (JWT secret, internal RBAC
endpoint, order-service's `order.placed` emission) already exists.

Stack prerequisites: .NET 10 SDK, MongoDB, RabbitMQ (with management plugin for smoke
tests), core-service and order-service running for end-to-end checks.

---

## Phase 0 — Scaffolding (no business logic) — ✅ done

Goal: a runnable ASP.NET Core app with the conventions of the Node siblings and the
multi-project layering (`Api` → `Infrastructure` → `Common`), plus every cross-cutting
utility later phases need, so they only write business code.

### 0.1 Project bootstrap — ✅ done

1. ✅ `backend/Crave.Analytics.slnx` — solution.
2. ✅ `Crave.Analytics.Common` (pkg) — `MongoDB.Driver` 3.x, `RabbitMQ.Client` 7.x.
3. ✅ `Crave.Analytics.Infrastructure` (lib) — `Microsoft.IdentityModel.JsonWebTokens`,
   `Microsoft.Extensions.Http`, `Microsoft.Extensions.Hosting.Abstractions`,
   `Microsoft.Extensions.Logging.Abstractions`.
4. ✅ `Crave.Analytics.Api` (app) — `Microsoft.NET.Sdk.Web`, Minimal APIs.
5. ✅ Project references: `Api → Infrastructure → Common` (layering compiler-enforced).
6. ✅ Folder tree (`Common/{Mongo,Messaging,Utils}`,
   `Infrastructure/{Config,Http,Auth,CoreClient,Events}`,
   `Api/{Health,Modules/Analytics/{Endpoints,Services,Repositories,Documents,Dtos}}`).
7. ✅ Minimal `Program.cs` (PORT-aware, `{success, data}` envelope).
8. ✅ `.env.example`, `.gitignore`, docs skeletons (`CLAUDE.md`, `plan.md`, `docs/*`).

### 0.2 Cross-cutting infra (copy-the-pattern from order-service) — ✅ done

9. ✅ `Infrastructure/Config/Env.cs` — fail-fast UPPER_SNAKE parser producing the typed `Env`
   singleton (all vars from CLAUDE.md §8 table). Crash with a clear message when a
   required var is missing.
10. ✅ `Infrastructure/Http/ApiResponse.cs` — `SendSuccess` envelope helper
    (`{success:true, data}`).
11. ✅ `Infrastructure/Http/AppException.cs` — `(message, statusCode, isOperational)`.
12. ✅ `Infrastructure/Http/ErrorHandlerMiddleware.cs` — logs with correlation id; maps
    `AppException` → `{error}`; non-operational → 500 `"Something went wrong"`.
13. ✅ `Infrastructure/Http/CorrelationIdMiddleware.cs` — echo/generate `X-CorrelationId`.
14. ✅ JSON console logging — configured in `Program.cs` via `AddJsonConsole`
    (mirrors the Node Logger output shape: level/message/timestamp as JSON).
15. ✅ `Api/Program.cs` full composition — CORS (credentials, `CORS_ORIGINS`),
    correlation → error middleware pipeline, `Env` bootstrap, graceful shutdown
    (`SIGTERM`/`SIGINT` → stop consumer → close Mongo).

**Acceptance:** ✅ verified — `dotnet run` (PORT=5000) → `/` responds
`{"success":true,"data":"crave-analytics-service"}` with `X-CorrelationId` (generated,
and echoed when supplied); missing required env → boot fails naming the offending var
(`Missing required environment variable: ACCESS_SECRET`); SIGTERM stops the host
cleanly.

---

## Phase 1 — Mongo persistence + indexes

Goal: connection, typed documents, repositories, idempotent index bootstrap.

1. `Common/Mongo/MongoConnectionFactory.cs` — `IMongoClient` from a connection string.
   **No collection names. No env.**
2. `Common/Mongo/IndexEnsurer.cs` — generic `EnsureIndexesAsync(database, collectionName,
   models)`; idempotent (createIndex is a no-op for identical indexes).
3. `Api/Modules/Analytics/Documents/RestaurantDayDocument.cs` — POCO,
   `[BsonElement]` snake_case (`restaurant_id`, `date`, `currency`, `orders_count`,
   `revenue_sum`, `delivery_ms_sum`, `delivery_ms_count`, `updated_at`).
4. `Api/Modules/Analytics/Documents/EventIdDocument.cs` — `event_id`, `received_at`.
5. `Api/Modules/Analytics/Repositories/Indexes.cs` — all declarations:
   - `agg_restaurant_day`: unique `(restaurant_id, date)`; range `(date, restaurant_id)`.
   - `event_ids`: unique `event_id`; TTL `received_at` (`EVENT_DEDUPE_TTL_DAYS`).
   Each with its query-path comment.
6. `Infrastructure` — `AddMongo(...)` extension registering `IMongoDatabase` from
   `MONGO_URL`/`MONGO_DATABASE`.
7. `Api/Program.cs` — ensure indexes on boot **before** starting the consumer.
8. `Api/Health/HealthEndpoints.cs` — `GET /api/health` pinging Mongo (`200 {ok:true}` / 503).

**Acceptance:** boot against local Mongo creates both collections with the four indexes;
second boot is a no-op (no error); `/api/health` reflects Mongo availability.

---

## Phase 2 — RabbitMQ consumer (dedupe + DLQ)

Goal: the `order-service` inbound consumer ported to .NET — the exact
`lib/core-events/consumer.ts` flow.

1. `Common/Messaging/AmqpConsumer.cs` — `RabbitMQ.Client` v7 async wrapper: connect,
   declare topology (exchange, durable queue, bindings, DLX, DLQ, `x-dead-letter-exchange`
   arg), prefetch, manual ack/nack, auto-reconnect with backoff. **No event types, no
   envelope shapes — raw bytes + ack/nack callbacks only.**
2. `Infrastructure/Events/EventEnvelope.cs` — `{eventId, eventType, occurredAt,
   aggregateType, aggregateId, region, payload}` (System.Text.Json, case-insensitive).
3. `Infrastructure/Events/IEventDeduper.cs` + `IEventHandler` + `EventHandlerRegistry`
   — `MarkSeen` funnel, unknown-type log+ack-skip, handler dispatch.
4. `Infrastructure/Events/OrderEventsConsumerService.cs` — `BackgroundService` hosting
   `AmqpConsumer` with the consumer loop:
   parse → invalid ⇒ DLQ · `MarkSeen` ⇒ dup ⇒ ack-skip · no handler ⇒ log+ack-skip ·
   handler ⇒ ack on success / DLQ on failure.
5. `Api/Modules/Analytics/Repositories/EventDeduper.cs` — `event_ids` insert; E11000 ⇒ seen.
6. `Api/Program.cs` — register consumer + handlers, start hosted service (non-fatal if
   Rabbit is down at boot — keep retrying).
7. `scripts/publish-test-event.sh` — publish a raw envelope via the RabbitMQ management
   API for smoke tests.

**Acceptance:** publish a valid envelope → consumed (dedupe row inserted, acked);
republish same `eventId` → ack-skip (no second row); unparseable body → routed to
`analytics-service.order-events.dlq`; killing Rabbit → consumer reconnects when it's back.

---

## Phase 3 — `order.placed` handler + aggregate upsert

Goal: the single write path — atomic day-grained rollup.

1. `Api/Modules/Analytics/Services/OrderPlacedHandler.cs` — validates the payload shape
   (restaurantId, subtotal, currency, placedAt), derives `date` (UTC `YYYY-MM-DD` from
   `placedAt`), delegates to the repository.
2. `Api/Modules/Analytics/Repositories/RestaurantDayRepository.cs` —
   `FindOneAndUpdate` upsert: `$inc {orders_count:1, revenue_sum:subtotal}` +
   `$set {currency, updated_at}`, filter `(restaurant_id, date)`, `IsUpsert = true`
   (atomic — competing consumers safe).
3. Register handler for `order.placed` in the registry.

**Acceptance:** publish an `order.placed` event → `agg_restaurant_day` row appears/merges
with correct sums; replay the same event (same `eventId`) → counts unchanged; two events
same restaurant+day → single row, incremented twice.

---

## Phase 4 — Auth + RBAC + core-client

Goal: same JWT contract and permission resolution as order-service.

1. `Infrastructure/Auth/JwtUser.cs` — typed claims record (`userId, role, email,
   restaurantId?, restaurantRole?, branchIds?`).
2. `Infrastructure/Auth/JwtVerifier.cs` — HS256 `ACCESS_SECRET`,
   `MapInboundClaims = false`, cookie `access_token` first then `Authorization: Bearer`.
3. `Infrastructure/Auth/AuthenticateFilter.cs` — 401 on failure; stores `JwtUser` in
   `HttpContext.Items`.
4. `Infrastructure/Auth/RequireRestaurantMemberFilter.cs` — `system_admin` bypass;
   `restaurant_user.restaurantId` must equal the route param (else 403).
5. `Infrastructure/CoreClient/CoreClient.cs` — `IHttpClientFactory` + `api-key` header +
   retry (3 attempts, backoff — mirrors `pkg/utils/retry.ts`); propagates
   `X-CorrelationId`.
6. `Infrastructure/CoreClient/RbacClient.cs` — `GET /api/internal/rbac/permissions?role=`.
7. `Infrastructure/Auth/PermissionCacheService.cs` — in-process role→permissions cache,
   TTL `RBAC_CACHE_TTL_SEC` (5 min default).
8. `Infrastructure/Auth/RbacFilter.cs` — `system_admin` bypass; `restaurant_user` →
   cached permission check `analytics:read`; else 403. Core unreachable ⇒ 503.

**Acceptance:** no token → 401; valid token + `system_admin` → passes; `restaurant_user`
of another restaurant → 403; role without `analytics:read` → 403; core down → 503
(cached permissions keep serving after first success).

---

## Phase 5 — GET endpoint end-to-end

Goal: the single read endpoint, full contract.

1. `Api/Modules/Analytics/Dtos/RestaurantDayResponseDto.cs` —
   `{date, ordersCount, revenueMinor, currency, avgOrderMinor}`.
2. `Api/Modules/Analytics/Services/AnalyticsService.cs` — validates `from`/`to`
   (required, `YYYY-MM-DD`, `from <= to`, range ≤ 366 days), queries by
   `(restaurant_id, date range)` ascending, derives `avgOrderMinor =
   floor(revenue_sum / orders_count)` — never stored.
3. `Api/Modules/Analytics/Repositories/RestaurantDayRepository.cs` — add the range read
   with an explicit projection backed by the `(date, restaurant_id)` index.
4. `Api/Modules/Analytics/Endpoints/AnalyticsEndpoints.cs` —
   `GET /api/analytics/restaurants/{restaurantId}/days?from=&to=` wired with the filter
   chain from Phase 4; `Api/Modules/Analytics/Errors.cs` stable errors.
5. Finalize `docs/api-contracts.md` against the real implementation.

**Acceptance:** seeded events → endpoint returns documented payload (ascending dates,
derived averages); missing/invalid `from`/`to` → 400; `from > to` / > 366 days → 400;
unknown restaurant → `200` with empty `data` (analytics has no restaurant registry —
absence is not an error); RBAC denials per Phase 4.

---

## Phase 6 — Cross-service change + docs final pass

1. **core-service migration** — `seed_analytics_permissions`: insert permission
   `('analytics','read')` + grant to `owner` and `branch_manager`
   (`ON CONFLICT DO NOTHING`, mirroring
   `20260418000001_seed_order_service_permissions.ts`). Run core's migrations.
2. Verify the full happy path: place a real order (COD and online) against
   core+order services → `order.placed` lands → aggregate upserted → GET returns it.
3. Docs final pass: `CLAUDE.md`, `plan.md` (tick phases), `docs/folder-structure.md`,
   `docs/system-design.md`, `docs/api-contracts.md` — must match the shipped code.
4. Local run book: `.env.example` values + `docker compose` dependencies (Mongo,
   RabbitMQ) documented in `docs/system-design.md` §Local run.

**Acceptance:** everything above passes; a fresh clone + `dotnet run` boots green with
only `.env` filled in.

---

## Build order summary

```
Phase 0  scaffold + cross-cutting infra          (runnable app)
Phase 1  mongo + indexes                        (persistence ready)
Phase 2  rabbitmq consumer + dedupe + DLQ       (events flowing)
Phase 3  order.placed handler + upsert          (aggregates filling)
Phase 4  auth + rbac + core-client              (endpoint secured)
Phase 5  GET endpoint                           (service complete)
Phase 6  core permission seed + docs            (platform complete)
```
