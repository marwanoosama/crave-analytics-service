# Folder Structure — `analytics-service`

Mirrors the layering of `core-service` / `order-service` (`app/ lib/ pkg/`), expressed the
.NET way: **three projects whose references encode the import rules**. The tree below is the
target end state (Phase 6). Files marked *(Phase N)* land in that phase — see `plan.md`.

```
crave-analytics-service/
├── CLAUDE.md                          # every architectural rule (greppable)
├── plan.md                            # phased build order + acceptance checks
├── docs/
│   ├── api-contracts.md               # endpoint shapes + error codes + order.placed contract
│   ├── folder-structure.md            # this file
│   └── system-design.md               # platform diagram, flows, failure modes, dedupe rationale
├── .env.example                       # env contract (mirrors the Node siblings)
├── .gitignore
├── scripts/
│   └── publish-test-event.sh          # (Phase 2) publish a raw envelope via RabbitMQ mgmt API
└── backend/
    ├── Crave.Analytics.slnx           # solution (.NET 10 XML format)
    └── src/
        │
        ├── Crave.Analytics.Common/            # "pkg/" — app-agnostic, env-agnostic
        │   ├── Crave.Analytics.Common.csproj # refs: NOTHING (only MongoDB.Driver, RabbitMQ.Client)
        │   ├── Mongo/
        │   │   ├── MongoConnectionFactory.cs   # (P1) IMongoClient from a connection string
        │   │   └── IndexEnsurer.cs             # (P1) generic idempotent EnsureIndexesAsync
        │   ├── Messaging/
        │   │   ├── AmqpConsumer.cs             # (P2) topology declare, DLQ, prefetch, manual ack,
        │   │   │                               #      reconnect — raw bytes only
        │   │   └── ConsumerOptions.cs          # (P2) queue/exchange/bindings/dlx/dlq/prefetch
        │   └── Utils/
        │       ├── Retry.cs                    # (P4) retry with backoff (retry.ts analogue)
        │       ├── Money.cs                    # minor-units helpers (avg = floor(sum/count))
        │       └── Time.cs                     # UTC day-key helpers ("YYYY-MM-DD")
        │
        ├── Crave.Analytics.Infrastructure/    # "lib/" — env wiring, no business rules
        │   ├── Crave.Analytics.Infrastructure.csproj  # refs: Common (+ auth, http, hosting pkgs)
        │   ├── Config/
        │   │   └── Env.cs                      # (P0) fail-fast UPPER_SNAKE env parser
        │   ├── Http/
        │   │   ├── ApiResponse.cs              # (P0) SendSuccess envelope helper
        │   │   ├── AppException.cs             # (P0) AppError analogue
        │   │   ├── ErrorHandlerMiddleware.cs   # (P0) {error} envelope, operational/unknown split
        │   │   └── CorrelationIdMiddleware.cs  # (P0) X-CorrelationId echo/generate
        │   ├── Auth/
        │   │   ├── JwtUser.cs                  # (P4) typed claims record
        │   │   ├── JwtVerifier.cs              # (P4) HS256, cookie-then-bearer, MapInboundClaims=false
        │   │   ├── AuthenticateFilter.cs       # (P4) 401 on failure, stores JwtUser
        │   │   ├── RequireRestaurantMemberFilter.cs  # (P4) restaurantId claim == route param
        │   │   ├── RbacFilter.cs               # (P4) analytics:read via PermissionCacheService
        │   │   └── PermissionCacheService.cs   # (P4) role → permissions, TTL cache
        │   ├── CoreClient/
        │   │   ├── CoreClient.cs               # (P4) IHttpClientFactory + api-key + retry
        │   │   └── RbacClient.cs               # (P4) GET /api/internal/rbac/permissions?role=
        │   └── Events/
        │       ├── EventEnvelope.cs            # (P2) wire envelope (System.Text.Json)
        │       ├── IEventDeduper.cs            # (P2) MarkSeen abstraction (impl lives in Api)
        │       ├── IEventHandler.cs           # (P2) handler contract
        │       ├── EventHandlerRegistry.cs    # (P2) type → handler; unknown ⇒ ack-skip
        │       └── OrderEventsConsumerService.cs  # (P2) BackgroundService: consumer loop + reconnect
        │
        └── Crave.Analytics.Api/              # "app/" — host + business modules
            ├── Crave.Analytics.Api.csproj    # refs: Infrastructure (→ Common)
            ├── Program.cs                    # composition root: Env, DI, pipeline, consumer
            │                                 #   host, EnsureIndexes on boot, graceful shutdown
            ├── Health/
            │   └── HealthEndpoints.cs        # (P1) GET /api/health — Mongo ping
            └── Modules/
                └── Analytics/                # the one bounded context (v1)
                    ├── Endpoints/
                    │   └── AnalyticsEndpoints.cs     # (P5) GET …/restaurants/{id}/days + filters
                    ├── Services/
                    │   ├── AnalyticsService.cs       # (P5) read path, derives avgOrderMinor
                    │   └── OrderPlacedHandler.cs    # (P3) order.placed → upsert
                    ├── Repositories/
                    │   ├── RestaurantDayRepository.cs  # (P3/P5) $inc upsert + indexed range read
                    │   ├── EventDeduper.cs             # (P2) event_ids insert, E11000 ⇒ seen
                    │   └── Indexes.cs                  # (P1) ALL index declarations (app knowledge)
                    ├── Documents/
                    │   ├── RestaurantDayDocument.cs    # (P1) agg_restaurant_day POCO, BsonElement
                    │   └── EventIdDocument.cs          # (P1) event_ids POCO
                    ├── Dtos/
                    │   └── RestaurantDayResponseDto.cs # (P5) wire shape
                    └── Errors.cs                      # (P5) stable AppException instances
```

## Layer rules (compiler-enforced)

```
       Api ("app")           ── may import Infrastructure, Common
       Infrastructure ("lib") ── may import Common; may NOT import Api
       Common ("pkg")        ── NO imports from lib or app, NO env, NO global singletons,
                                NO app-specific knowledge
```

| Node (core/order)            | .NET here                                        |
| ---------------------------- | ------------------------------------------------ |
| `app/<module>/`              | `Crave.Analytics.Api/Modules/<Module>/`          |
| `lib/`                       | `Crave.Analytics.Infrastructure/`                |
| `pkg/`                       | `Crave.Analytics.Common/`                        |
| `lib/di/container.ts` (only place lib sees app) | `Api/Program.cs` composition root |
| convention-enforced imports | project references (compiler-enforced)           |
| tsyringe DI tokens           | built-in DI constructor injection                 |

## What deliberately does **not** exist here (vs the Node siblings)

- No `outbox` anything — this service never publishes events.
- No Redis, no cache layer, no idempotency-keys table — dedupe is Mongo `event_ids`.
- No migrations folder — Mongo is schemaless; indexes are ensured on boot (§ `Indexes.cs`).
- No WebSocket layer, no payment/assignment/finance modules — analytics only.
- No controllers directory — Minimal API endpoint groups replace them.
- No `play/` (Node scratch folder) — smoke scripts live in `scripts/`.
