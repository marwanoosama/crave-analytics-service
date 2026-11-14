# Crave Analytics Service

Builds restaurant/day reporting aggregates from order events. It is a read-only reporting
service, not an order or payment source of truth.

**Stack:** .NET 10, RabbitMQ and MongoDB.

- [System design](docs/system-design.md)
- [API contracts](docs/api-contracts.md)
- [Folder structure](docs/folder-structure.md)

Copy `.env.example` to `.env` and export its values (`set -a; source .env; set +a`). Then
run from `backend/` with `dotnet run --project src/Crave.Analytics.Api`.
