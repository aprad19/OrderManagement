# Order Management API - .NET 8

Prototype REST API for an order-management rewrite. The implementation focuses on idempotency, concurrent stock deduction, optimistic concurrency for status changes, transactional consistency, consistent errors, correlation IDs, and concurrency tests.

## Stack

- .NET 8 / ASP.NET Core Web API
- Entity Framework Core 8
- SQL Server 2022
- Swagger/OpenAPI
- xUnit + FluentAssertions + Testcontainers (test project)

## Why SQL Server?

SQL Server is selected because this service is transaction-heavy and the assignment focuses on concurrency. SQL Server provides transactions, rowversion-based optimistic concurrency, atomic conditional updates, unique constraints, and strong EF Core support.

## Concurrency strategy

### 1. Duplicate create / idempotency

Every `POST /api/orders` requires `Idempotency-Key`.

The key is stored in `IdempotencyKeys` with a unique database index. The key is reserved inside the same transaction **before stock is touched**. This ordering is important: if two identical requests arrive at the same time, one transaction owns the key; the other receives a unique-key conflict and then reads the already-created order.

A SHA-256 request hash is also stored. Reusing an existing key with a different payload returns `409 Conflict`.

### 2. Concurrent stock deduction

Stock is never implemented as read-check-write. The service executes an atomic conditional update:

```sql
UPDATE Products
SET StockQuantity = StockQuantity - @quantity
WHERE Id = @productId
  AND StockQuantity >= @quantity;
```

If affected rows are zero, the request receives `409 Conflict`. A database check constraint also guarantees `StockQuantity >= 0`.

### 3. Concurrent status update

`Orders.RowVersion` is configured as SQL Server `rowversion`. EF Core automatically includes the original rowversion in updates. If another admin has already changed the order, `DbUpdateConcurrencyException` is converted to `409 Conflict`.

### 4. Cancel transaction

Cancellation and stock restoration are performed inside one database transaction. The order row's rowversion protects against another status update winning concurrently.

## Additional race conditions identified

1. **Same idempotency key with different payload** - prevented by unique key plus SHA-256 request hash.
2. **Two requests deducting the same product** - prevented by atomic conditional stock update.
3. **Cancel vs status update** - protected by transaction + order rowversion.
4. **Duplicate product IDs inside one request** - quantities are aggregated before stock deduction and a unique `(OrderId, ProductId)` index provides an additional data-integrity guard.

## API

- `POST /api/orders`
- `GET /api/orders/{id}`
- `GET /api/orders?status=Pending&customerId={guid}&fromDate=2026-10-01&toDate=2026-10-05&page=1&pageSize=20`
- `PATCH /api/orders/{id}/status`
- `POST /api/orders/{id}/cancel`

### Valid status transitions

```text
Pending   -> Confirmed | Cancelled
Confirmed -> Shipped   | Cancelled
Shipped   -> Delivered
Delivered -> terminal
Cancelled -> terminal
```

## Error format

Errors use a consistent Problem Details-like JSON structure:

```json
{
  "type": "https://api.example.com/errors/409",
  "title": "Conflict",
  "status": 409,
  "detail": "Insufficient stock for product 'Product B'.",
  "traceId": "correlation-id"
}
```

## Correlation ID

Clients can send `X-Correlation-ID`. If absent, the API generates one and returns it in the response header. The same value is included in logging scope.

## Run SQL Server

```bash
docker compose up -d sqlserver
```

Wait until SQL Server is healthy, then:

```bash
dotnet restore
dotnet run
```

In Development, the application calls `EnsureCreated` and seeds the sample products automatically. For a production deployment, replace this with EF Core migrations (`dotnet ef migrations add InitialCreate` followed by `dotnet ef database update`).

Swagger is available at the URL printed by ASP.NET Core, normally:

```text
https://localhost:xxxx/swagger
```

## Seed data

Development startup automatically creates the schema and seeds the sample products. If you want to seed manually, use: Sample IDs:

- Product A: `11111111-1111-1111-1111-111111111111`, stock 100
- Product B: `22222222-2222-2222-2222-222222222222`, stock 15
- Product C: `33333333-3333-3333-3333-333333333333`, stock 50

Example:

```sql
INSERT INTO Products (Id, Name, Price, StockQuantity)
VALUES
('11111111-1111-1111-1111-111111111111', 'Product A', 100000, 100),
('22222222-2222-2222-2222-222222222222', 'Product B', 250000, 15),
('33333333-3333-3333-3333-333333333333', 'Product C', 50000, 50);
```

## Example create order

```http
POST /api/orders
Idempotency-Key: demo-001
Content-Type: application/json
X-Correlation-ID: demo-correlation-001
```

```json
{
  "customerId": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
  "shippingAddress": "Jakarta Selatan",
  "items": [
    {
      "productId": "22222222-2222-2222-2222-222222222222",
      "quantity": 10
    }
  ]
}
```

## Testing

The recommended test project should use a real SQL Server container rather than EF InMemory for concurrency tests. InMemory does not reproduce SQL Server locking, unique constraints, rowversion, or atomic SQL behavior accurately enough for this assignment.

The most valuable integration tests are:

1. Two different idempotency keys concurrently buying 10 units while stock is 15: exactly one succeeds and final stock is 5.
2. Same idempotency key concurrently creating the same order: exactly one order is created and stock is deducted once.
3. Two admins concurrently changing Confirmed to Shipped and Cancelled: exactly one succeeds and the other receives 409.

## Production improvements

For a production rewrite, add authentication/authorization, customer validation against the source of truth, idempotency-key expiration/cleanup, structured log sink such as Seq/Application Insights/ELK, distributed tracing/OpenTelemetry, metrics, rate limiting, API versioning, outbox/event publishing, and stronger integration-test isolation.

## Run concurrency tests

Docker must be running because the tests start a real SQL Server container:

```bash
dotnet test ../OrderManagement.Tests/OrderManagement.Tests.csproj
```

These tests intentionally do not use EF Core InMemory because the assignment is specifically about database concurrency behavior.
