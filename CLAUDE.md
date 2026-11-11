# CLAUDE.md — Analytics Service Guidelines

These rules apply to the **`analytics-service`** microservice of the **Crave (QuickBite)** platform.
It is the third service, alongside `core-service` and `order-service` (both Node + TypeScript +
Postgres + Redis). This service is the deliberate **.NET analogue** of those conventions:

> Mirror the pattern, find the .NET counterpart. When in doubt, look at how `order-service`
> does it and follow that exact pattern. Deviate **only** where a deviation is documented here.

---

## 1. Mission of this service

This service owns **per-day rollups of order/payment/delivery activity**:

- **Consumes** events from `order-service` via RabbitMQ (topic exchange `order.events`).
- **Upserts** day-grained aggregates into MongoDB (`agg_restaurant_day`).
- **Serves** read-only HTTP endpoints under `/api/analytics/...`.

It does **not** own users, restaurants, products, orders, payments. It does **not** write to any
operational data store. It does **not** emit events — **inbound async only, forever** (no outbox,
no publisher). It is an analytics/insight sidecar: correctness matters, latency is secondary.

---

## 2. Tech stack (locked — do not deviate)

| Concern            | Library / Tool                                                |
| ------------------ | ------------------------------------------------------------- |
| Runtime            | **.NET 10 (LTS)**, C#, nullable enabled                       |
| HTTP framework    | ASP.NET Core **Minimal APIs** (endpoint groups + filters)      |
| Validation         | Inline (one GET endpoint — query params parsed at the edge)   |
| DI                | Built-in `Microsoft.Extensions.DependencyInjection`            |
| Env validation    | Hand-rolled fail-fast `Env.cs` (zod `env.ts` convention)        |
| DB driver          | **`MongoDB.Driver` 3.x** — official driver, POCOs + `[BsonElement]` |
| Messaging          | **`RabbitMQ.Client` 7.x** (async API), consumer only           |
| Auth              | `Microsoft.IdentityModel.JsonWebTokens` — HS256, shared `ACCESS_SECRET` |
| Logging            | `ILogger<T>` + JSON console formatter (same shape as Node Logger) |
| IDs                | `restaurantId`/`branchId` are core's numeric ids; `eventId` is a UUID |

**Do not introduce new packages** without justification. Explicitly **forbidden**:

- ❌ EF Core or any ORM/mapper over Mongo (the driver is the "Knex query builder, not an ORM" analogue).
- ❌ **Redis** — deliberately absent. Dedupe is Mongo `event_ids`; there is no cache layer.
- ❌ MediatR / CQRS ceremony — one module, one endpoint.
- ❌ FluentValidation / validation frameworks — inline parsing for one endpoint.
- ❌ Mongock or any migration framework — Mongo is schemaless; indexes are ensured on boot (§7).
- ❌ Any outbound messaging — this service never publishes.

---

## 3. Folder structure — mirrors the Node services' `app/ lib/ pkg/`

The Node services enforce layering by convention; .NET enforces it **with project references**:

```
backend/src/
  Crave.Analytics.Api/             # "app/"  — host + business modules
  Crave.Analytics.Infrastructure/  # "lib/"  — env-aware wiring, no business rules
  Crave.Analytics.Common/          # "pkg/"  — app-agnostic, env-agnostic, framework-free
```

```
       Api ("app")           ── may import Infrastructure, Common
       Infrastructure ("lib") ── may import Common; may NOT import Api
       Common ("pkg")        ── NO imports from lib or app, NO env, NO global singletons,
                                 NO app-specific knowledge
```

The compiler enforces this: `Common` references nothing; `Infrastructure` references `Common`;
`Api` references both. If you need a type from `Api` inside `Infrastructure` — **stop and refactor**.
The only sanctioned composition point is `Api/Program.cs` (the .NET composition root, the exact
analogue of the Node services' "only `container.ts` may see app/" exception).

- **`Common`** knows how to connect (Mongo connection factory, idempotent index-ensure helper,
  RabbitMQ consumer wrapper with topology/DLQ/prefetch/manual-ack, retry/money/time utils). It must
  **never** contain collection names, event type strings, or envelope shapes.
- **`Infrastructure`** wires `Common` to the environment: `Env.cs`, correlation + error-handling
  middleware, response envelopes, JWT auth, RBAC filters, core-client, the order-events consumer
  host. Knows ASP.NET Core and env; contains no business rules.
- **`Api`** contains `Program.cs`, `Health/`, and business modules under `Modules/<module>/`.
  **All Mongo collection names and index declarations live here.**

### Module skeleton (every module under `Modules/<module>/`)

```
Modules/<module>/
  Endpoints/<Module>Endpoints.cs     # routes.ts analogue: endpoint group + filter chain
  Services/<Thing>Service.cs         # business logic, throws AppException
  Services/<Event>Handler.cs         # event handler registered into the consumer registry
  Repositories/<Thing>Repository.cs  # typed IMongoCollection<T> data access
  Repositories/Indexes.cs            # ALL index declarations for this module's collections
  Documents/<Thing>Document.cs      # POCO + [BsonId]/[BsonElement] mappings
  Dtos/<Thing>ResponseDto.cs        # response payload shapes
  Errors.cs                         # stable AppException instances
```

See `docs/folder-structure.md` for the annotated target tree.

---

## 4. Naming conventions

### Files
- `PascalCase.cs`, file name matches the primary type (`AnalyticsService.cs`).

### C#
- `PascalCase` types, methods, properties, constants.
- `camelCase` locals and parameters; `_camelCase` private fields.
- `UPPER_SNAKE` environment variable names (mirrors the Node services' `.env`).

### MongoDB
- Collections: `snake_case` (`agg_restaurant_day`, `event_ids`).
- Document fields: `snake_case`, mapped from PascalCase C# properties via `[BsonElement("...")]`.
- Indexes: named in `Indexes.cs` as `uq_<collection>_<cols>` / `idx_<collection>_<cols>`.

### Routes
- Plural resource nouns: `/api/analytics/restaurants/{restaurantId}/days`.
- Mounted under `/api` like the siblings (no `v1` segment — platform consistency).

### Events
- Event types: `<aggregate>.<past-tense-verb>` (`order.placed`), constants only — no magic strings.

### Money
- `long` minor units (cents/piasters) + a sibling `currency` field. **Never `decimal`/`double`.**

---

## 5. Response DTOs — every HTTP payload is shaped

- Response payloads are DTO records in `Dtos/` — never raw documents, never ad-hoc anonymous
  objects (except the temporary scaffold root endpoint).
- Money fields are integer **minor units** with a `currency` sibling. No server-side formatting.
- Timestamps are ISO 8601 UTC strings; `date` is `YYYY-MM-DD` UTC.
- Derived fields (e.g. `avgOrderMinor`) are computed in the service layer — **never stored**.
- Internal fields (`delivery_ms_sum`, `delivery_ms_count`) never leave the service; the DTO is the
  single source of truth for the wire format (`docs/api-contracts.md`).

---

## 6. Module conventions

1. **`Documents/`** — plain POCOs, `[BsonId]`/`[BsonElement]` snake_case mapping,
   `[BsonIgnoreExtraElements]`. No behavior, no repository knowledge.
2. **`Repositories/Indexes.cs`** — one file declaring **every** index model for the module's
   collections. Index declarations are app knowledge — they never live in `Common`.
3. **`Repositories/`** — constructor-injected `IMongoDatabase`, typed `GetCollection<T>`,
   explicit `Builders<T>.Filter`/`Projection` (no "select all"). Repos never throw domain errors.
4. **`Services/`** — business logic; composes repositories; throws `AppException`.
5. **`Services/*Handler.cs`** — event handlers: `(payload) => Task`, registered in the consumer's
   handler registry at boot. Handlers must be idempotent; duplicates are already filtered by the
   `EventDeduper` funnel.
6. **`Endpoints/`** — validate input → call service → map to response DTO → `SendSuccess`.
   No business logic. Endpoint filter chain mirrors the Node `routes.ts` middleware wiring:
   `Authenticate` → `RequireRestaurantMember` → `Rbac("analytics:read")`.
7. **`Errors.cs`** — exported `AppException` instances (not classes). Stable wording, stable codes.

Inline record/class declarations for shared shapes inside services/endpoints/repositories are
**forbidden** — the shape belongs in `Dtos/` or `Documents/`. Tiny one-shot locals inside a single
method are fine.

---

## 7. Database rules (MongoDB)

- **No DDL migrations.** Mongo is schemaless. The only schema-like artifact is indexes.
- **Indexes are declared once** — in the owning module's `Repositories/Indexes.cs` — and created
  **idempotently on boot** via `EnsureIndexesAsync` (Mongo `createIndex` is a no-op when an
  identical index exists). Boot order: connect → ensure indexes → start consumer → serve HTTP.
- Each declared index carries a one-line comment naming the query path it supports
  (e.g. `// supports GET /…/days?from=&to=`). **No speculative indexes.**
- **Aggregates store sums + counts, never pre-computed averages** — `delivery_ms_sum/count`,
  `revenue_sum/orders_count` — so per-day rows merge associatively across event replays.
- **Upserts are atomic**: `FindOneAndUpdate` with `$inc` + `$setOnInsert`/`$set`, `IsUpsert=true`.
  Never read-modify-write. Competing consumers are safe.
- **Dedupe** lives in `event_ids`: unique `event_id` + TTL `received_at` (7 days default,
  `EVENT_DEDUPE_TTL_DAYS`). `InsertOne` duplicate-key (E11000) ⇒ the event was seen.
- Explicit projections on reads — never materialize whole documents for the API.
- This service **never writes** any database other than its own Mongo, and never writes
  operational (core/order) data.

### The one aggregate collection (v1)

```
agg_restaurant_day
  { _id, restaurant_id, date "YYYY-MM-DD" UTC, currency,
    orders_count, revenue_sum, delivery_ms_sum, delivery_ms_count, updated_at }
```

- `revenue_sum` accumulates `subtotal` (food value; excludes delivery fee + service fee).
- `avgOrderMinor = floor(revenue_sum / orders_count)` — derived in the service layer.
- Unique `(restaurant_id, date)`; range `(date, restaurant_id)`.

`agg_branch_day`, `agg_product_day`, `agg_platform_day` are future extensions following the exact
same pattern — **out of scope for v1** (§13).

---

## 8. Cross-cutting infra

### Env — `Infrastructure/Config/Env.cs` (fail-fast at boot)

All env vars (UPPER_SNAKE, mirroring siblings; `.env.example` is the contract):

| Var                                | Default                            |
| ---------------------------------- | ---------------------------------- |
| `PORT`                             | `5000`                             |
| `ASPNETCORE_ENVIRONMENT`           | `development`                      |
| `ACCESS_SECRET`                    | required — MUST match core/order    |
| `CORS_ORIGINS`                     | `http://localhost:3000`             |
| `MONGO_URL`                        | `mongodb://localhost:27017`        |
| `MONGO_DATABASE`                   | `crave_analytics`                   |
| `RABBITMQ_URL`                     | required                           |
| `RABBITMQ_ORDER_EVENTS_EXCHANGE`    | `order.events`                     |
| `RABBITMQ_ORDER_EVENTS_QUEUE`       | `analytics-service.order-events`   |
| `RABBITMQ_ORDER_EVENTS_BINDINGS`    | `order.#,payment.#`                |
| `RABBITMQ_ORDER_EVENTS_DLX`         | `order.events.dlx`                  |
| `RABBITMQ_ORDER_EVENTS_DLQ`         | `analytics-service.order-events.dlq`|
| `RABBITMQ_PREFETCH`                | `32`                               |
| `CORE_SERVICE_BASE_URL`            | required                           |
| `CORE_INTERNAL_API_KEY`            | required                           |
| `RBAC_CACHE_TTL_SEC`               | `300`                              |
| `EVENT_DEDUPE_TTL_DAYS`            | `7`                                |

Env comes from the process environment / docker-compose `env_file`. No dotenv library, no
appsettings secrets. Missing required var ⇒ crash at boot with a clear message.

### Auth

- JWT **HS256** with `ACCESS_SECRET` (same secret core/order use). Token from cookie
  `access_token` **first**, then `Authorization: Bearer`.
- Claims: `userId` (number), `role`, `email`, `restaurantId?`, `restaurantRole?`, `branchIds?`.
  Verify with `MapInboundClaims = false` so raw claim names survive (no `ClaimTypes` remapping).
- Failures ⇒ `401 {error: "User not authenticated"}`. The authenticated principal is attached to
  `HttpContext.Items` — the `req.user` analogue.

### RBAC

- This service has **no permissions catalog**. It resolves role permissions via core's
  `GET /api/internal/rbac/permissions?role=...` with header `api-key: <CORE_INTERNAL_API_KEY>`.
- In-process cache by role, TTL default 5 min (`RBAC_CACHE_TTL_SEC`).
- Endpoint authorization: `system_admin` bypasses; `restaurant_user` checked against the cached
  role permissions (`analytics:read`); everyone else ⇒ `403`.

### Idempotency (inbound events)

Every handler funnels through **`EventDeduper.MarkSeen(eventId)`** before dispatching:

- Unique insert into `event_ids` succeeded ⇒ fresh ⇒ dispatch handler ⇒ ack.
- Duplicate key (E11000) ⇒ **ack-and-skip**.
- **Unknown event types ⇒ log + ack-skip** (bindings `order.#,payment.#` are wider than the
  v1 handler set — do not DLQ traffic a future handler will need).

### RabbitMQ (consumer)

- Topology (declared idempotently on every connect by the `AmqpConsumer`):
  - Topic exchange `order.events` (durable — declared by order-service, asserted here).
  - Durable queue `analytics-service.order-events`, bound with `order.#`, `payment.#`.
  - DLX `order.events.dlx` + DLQ `analytics-service.order-events.dlq` (bound `#`).
- Delivery semantics: **at-least-once**, manual ack after the handler commits, prefetch 32,
  persistent messages.
- Routing: unparseable envelope / missing `eventId`/`eventType` ⇒ DLQ. Handler failure ⇒ DLQ.
- The consumer runs as a `BackgroundService` with a reconnect/backoff loop (the .NET analogue of
  amqp-connection-manager). Reconnects re-assert topology.
- Publisher-side reliability is order-service's responsibility (its transactional outbox). This
  service publishes nothing.

### Error handling

- `AppException(message, statusCode = 500, isOperational = true)` — the `AppError` analogue.
- Global error-handling middleware logs (level, message, statusCode, path, correlationId) and
  responds `{error: message}` for operational errors, `{error: "Something went wrong"}` (500)
  otherwise. Never leak stacks.

### Response envelopes

```jsonc
// success          { "success": true, "data": <DTO> }
// error            { "error": "<message>" }
```

Same envelope as core/order (`SendSuccess` helper in `Infrastructure/Http`).

### Correlation

`X-CorrelationId` middleware: echo incoming or generate UUID; set on response; propagate to
core-client calls.

### Health

`GET /api/health` — pings Mongo; `200 {"ok":true}` / `503`. Same shape idea as order's health
route (per-shard ping collapsed to a single Mongo ping).

---

## 9. Performance & scale rules

This is **not** the hot path, but the rules still hold:

1. Every read is backed by an index (§7 comments name the query path).
2. GET endpoint p95 **< 200 ms** served from Mongo; response capped at **366 rows**.
3. Event handler p95 **< 50 ms** — exactly two Mongo ops (dedupe insert + aggregate upsert).
4. No N+1: the GET endpoint is a single query; handlers never fan out per-item.
5. Explicit projections — never read fields you don't use.
6. `prefetch 32` with manual acks keeps the consumer fair under burst.
7. No synchronous fan-out to core/order in the read path — RBAC cache absorbs it (5 min TTL).

---

## 10. Code style — what to avoid

- ❌ EF Core / any ORM, mapper, or "repository framework" over the Mongo driver.
- ❌ Redis (deliberately absent — see `docs/system-design.md` §"Why Mongo dedupe").
- ❌ MediatR, CQRS, validators, mapping profiles — ceremony for one module.
- ❌ Business logic in endpoints. Endpoints do: validate → service → DTO → respond.
- ❌ `throw new Exception(...)` in services — `AppException` and stable `Errors.cs` instances.
- ❌ Collection names or event type strings anywhere outside `Api/Modules/*`.
- ❌ `.Result` / `.Wait()` — async all the way.
- ❌ `Console.WriteLine` — `ILogger<T>` only.
- ❌ Magic strings for routes/event types — constants.
- ❌ New env vars without adding them to `Env.cs` **and** `.env.example`.
- ❌ `decimal`/`double` for money — `long` minor units.
- ❌ Writing to any operational data store, or emitting any event. **Never.**
- ❌ Storing averages — sums + counts only, so replays merge associatively.

---

## 11. When implementing a new module / extending one

Follow this exact order — never skip ahead:

1. `Documents/` POCO(s) with Bson mappings.
2. `Repositories/Indexes.cs` — declare indexes (with query-path comments).
3. `Repositories/` data access.
4. `Dtos/` response shapes.
5. `Errors.cs` stable error instances.
6. `Services/` business logic (register in DI from `Program.cs`).
7. `Services/*Handler.cs` if consuming events (register in the handler registry).
8. `Endpoints/` + filter chain (mount in the endpoint map).
9. Smoke test manually before moving on (see `scripts/publish-test-event.sh`).

One module end-to-end before the next. There is exactly one module today: `Analytics`.

---

## 12. Reference docs (in `docs/`)

- `docs/system-design.md` — platform ASCII diagram, sync/async flows, failure-mode table,
  why-Mongo-dedupe-not-Redis-SETNX.
- `docs/folder-structure.md` — annotated tree, layer rules.
- `docs/api-contracts.md` — endpoint request/response shapes + every error code, plus the
  inbound `order.placed` event contract.
- `plan.md` — phased build order with acceptance checks.
- Sibling conventions: `crave-order-service/backend/CLAUDE.md` (Node reference for every pattern
  this service mirrors).

---

## 13. Out of scope (do not build)

- `agg_branch_day`, `agg_product_day`, `agg_platform_day` — future, same pattern as v1.
- Any handler beyond `order.placed` in v1 (`order.cancelled` decrement, `order.delivered`
  delivery-duration rollup → `delivery_ms_sum/count` fields exist and stay zero until then).
- Redis, read-result caching, ETL exports, dashboards.
- Write endpoints of any kind; any outbound event; any outbox.
- Multi-document Mongo transactions (documented upgrade path, not needed at this volume).
- Admin/platform-level analytics (agg_platform_day territory).
- DevOps/deploy/observability stack, benchmark harness.
- WebSocket/realtime pushes of analytics.
