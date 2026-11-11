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

insuring that when the app is run, mongodb indexes are created (indexes in mongo pretty much represents the whole schema, collections are not needed to say that we have a mongo schema, so ensuring the indexes are created is like ensuring migrations are commited in sql)

Goal: connection, typed documents, repositories, idempotent index bootstrap. — ✅ done

1. ~~`Common/Mongo/MongoConnectionFactory.cs`~~ — dropped: the official driver is already
   app-agnostic; `AddMongo` wraps `MongoClient` directly.
2. ✅ `Common/Mongo/IndexEnsurer.cs` — generic `EnsureIndexesAsync(database, collectionName,
   models)`; idempotent (createIndex is a no-op for identical indexes).
3. ✅ `Api/Modules/Analytics/Documents/RestaurantDayDocument.cs` — POCO,
   `[BsonElement]` snake_case (`restaurant_id`, `date`, `currency`, `orders_count`,
   `revenue_sum`, `delivery_ms_sum`, `delivery_ms_count`, `updated_at`).
4. ✅ `Api/Modules/Analytics/Documents/EventIdDocument.cs` — `event_id`, `received_at`.
5. ✅ `Api/Modules/Analytics/Repositories/Indexes.cs` — all declarations:
   - `agg_restaurant_day`: unique `(restaurant_id, date)`; range `(date, restaurant_id)`.
   - `event_ids`: unique `event_id`; TTL `received_at` (`EVENT_DEDUPE_TTL_DAYS`).
   Each with its query-path comment.
6. ✅ `Infrastructure/Mongo/MongoServiceCollectionExtensions.cs` — `AddMongo()` registering
   `IMongoDatabase` (singleton) from `MONGO_URL`/`MONGO_DATABASE`.
7. ✅ `Api/Program.cs` — ensure indexes on boot **before** starting the consumer.
8. ✅ `Api/Health/HealthEndpoints.cs` — `GET /api/health` pinging Mongo (`200 {ok:true}` / 503,
   2 s ping timeout).

**Acceptance:** ✅ verified — boot against local Mongo created both collections with all four
indexes (confirmed via `mongosh`: unique `restaurant_id_1_date_1`, `date_1_restaurant_id_1`,
unique `event_id_1`, TTL `received_at` `expireAfterSeconds:604800`); second boot was a no-op
(health 200, zero error-level logs); `GET /api/health` → `200 {"ok":true}` with Mongo up and
`503 {"ok":false}` when Mongo died mid-run (throwaway mongod on port 27019, killed after boot);
dead Mongo at boot → loud process exit (failure mode #2 in system-design.md — crash loop is the
documented behavior).

---

## Phase 2 — RabbitMQ consumer (dedupe + DLQ)

Goal: the `order-service` inbound consumer ported to .NET — the exact
`lib/core-events/consumer.ts` flow. — ✅ done

1. ✅ `Common/Messaging/AmqpConsumer.cs` — `RabbitMQ.Client` v7 async wrapper: connect,
   declare topology (exchange, durable queue, bindings, DLX, DLQ, `x-dead-letter-exchange`
   arg), prefetch, manual ack/nack. **No event types, no envelope shapes — raw bytes +
   ack/nack callbacks only.** Mid-run reconnects are the client's native
   `AutomaticRecoveryEnabled`/`TopologyRecoveryEnabled` (reconnects re-assert topology —
   no hand-rolled loop); boot-time unreachability is the caller's retry loop.
2. ✅ `Common/Messaging/ConsumerOptions.cs` — record (url, exchange, queue, bindings, dlx,
   dlq, prefetch); constructed from `Env` in `Program.cs` (Common stays env-agnostic).
3. ✅ `Infrastructure/Events/EventEnvelope.cs` — record + `TryParse` (System.Text.Json,
   case-insensitive); false when unparseable or missing `eventId`/`eventType`.
4. ✅ `Infrastructure/Events/IEventDeduper.cs` + `IEventHandler.cs` + `EventHandlerRegistry.cs`
   — `MarkSeen` funnel, unknown-type log+ack-skip, handler dispatch.
5. ✅ `Infrastructure/Events/OrderEventsConsumerService.cs` — `BackgroundService` hosting
   `AmqpConsumer` with the consumer loop: parse → invalid ⇒ DLQ · `MarkSeen` ⇒ dup ⇒
   ack-skip · no handler ⇒ log+ack-skip · handler ⇒ ack on success / DLQ on failure.
   5 s boot-retry backoff, non-fatal.
6. ✅ `Api/Modules/Analytics/Repositories/EventDeduper.cs` — `event_ids` insert;
   E11000 ⇒ seen.
7. ✅ `Api/Program.cs` — `ConsumerOptions` from env, `AmqpConsumer` + `IEventDeduper` +
   `EventHandlerRegistry` + `AddHostedService<OrderEventsConsumerService>`.
8. ✅ `scripts/publish-test-event.sh` — publishes a full `order.placed` envelope via the
   RabbitMQ management API (fresh uuid each run).

**Acceptance:** ✅ verified — publish valid envelope → `{"routed":true}` → "No handler for
order.placed, acking" log + dedupe row in `event_ids` (empty registry acks, per CLAUDE.md
§Idempotency); republish same `eventId` → "Duplicate event ack-skipped", still 1 row;
payload `this is not json` → "Unparseable envelope" → message landed in
`analytics-service.order-events.dlq` (routing key `order.placed` preserved);
`RABBITMQ_URL` pointing at a dead port → app boots healthy, 3 retry warnings at 5 s backoff,
health stays 200 (non-fatal at boot, per system-design.md failure mode #1). Mid-run broker
bounce could not be exercised locally (rabbitmqctl lacks node access, sudo requires
password) — covered by the client's native automatic recovery; to verify by hand:
`sudo systemctl restart rabbitmq-server` while the app runs, then watch for re-consumption.

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
