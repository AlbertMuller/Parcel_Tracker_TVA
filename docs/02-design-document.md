# Design Document
## Order Intake & Tracking

| Field | Value |
|---|---|
| Document | Technical Design |
| Project | Order Intake & Tracking (OfferZen take-home) |
| Author | Albert Muller |
| Version | 1.2 |
| Date | 2026-10-11 |
| Status | Signed off as built; §0 lists where the build differs; §17 describes the DDD / Clean Architecture restructure |
| Related | `01-business-requirements.md`, `03-todo-backlog.md` |

---

## 0. As-built summary (v1.1)

Sections 1–13 are the original design. The delivered system follows it closely for the business logic (duplicate
key and fingerprint, decision table, race handling, status machine, server-side totals, API shape). It differs
in the places below. Sections 14–16 describe the parts that were built differently or added.

| Area | Original design | As built | Where |
|---|---|---|---|
| Platform | .NET 8, Angular 18 | .NET 10 (SDK 10.0.401 pinned), Angular 21 (zoneless, Vitest) | — |
| Projects | `OrderIntake.Domain` + `OrderIntake.Api`, two test projects | Clean Architecture: `OrderTracker.Domain`, `.Application`, `.Infrastructure`, `.Api`; tests split into Domain, Application, Integration and Architecture projects (§17) | §2.2, §2.3 |
| Storage | SQLite file, `EnsureCreated`, owned types | PostgreSQL 17, EF Core migrations, separate persistence entities, least-privilege roles | §6 → §14 |
| Running | `dotnet run` + `npm start`, optional scripts | The same for development (with `docker compose up -d db`), or the full stack with `docker compose up` | §11 → §16 |
| Validation | FluentValidation + endpoint filter | Hand-written `SubmitOrderValidator` in Application, reusing the value objects' rules; same JSON-path keys | §5.7 |
| Business errors | `Result`/`Error` with codes | As designed: `Result`/`Error` in the domain; every ProblemDetails carries a stable `code` (§17) | §3.4, §5.6 |
| Same-status request | `409 order.status.unchanged` | `200`, no change | §3.4 |
| Concurrency | Optional client `expectedVersion` + token | Version token only; the service re-reads and re-checks the rules on conflict | §5.5 |
| Currencies | Fixed ZAR, USD, EUR, GBP; `/api/meta/currencies` | Any ISO 4217 code; `/api/currencies` and `/api/exchange-rates` from live ECB rates; conversion of Pending orders | §5.1 → §15 |
| Request field names | `lines` | `lineItems` | §5.2 |
| List paging | Default 20, max 100, 400 above | Default 50, max 200, clamped; the UI uses 25 per page | §5.4 |
| By-reference lookup | `GET /api/orders/by-reference/{reference}` | Not built (search covers it) | §5.1 |
| Frontend structure | `core/`, `features/`, toasts, interceptor, not-found page, lazy routes | `orders/` and `shared/`; inline alerts; `toProblem` helper; redirect for unknown routes; eager routes; sidebar with dark mode | §9 |
| Tests | xUnit + Shouldly + FakeTimeProvider; SQLite in-memory | xUnit `Assert` + `ManualTimeProvider`; domain and use-case unit tests (in-memory store); real PostgreSQL via Testcontainers for persistence and API tests; NetArchTest architecture tests | §10 |

## 1. Overview

A two-part system:

- **Backend**: ASP.NET Core Web API on **.NET 8 (LTS)**. Exposes a REST API for submitting, listing, viewing and transitioning orders. Persists to a local **SQLite** file via EF Core.
- **Frontend**: **Angular 18** standalone-component SPA. Submits orders, lists them newest first, shows details, and changes status.

Design goals, in order:

1. **Correctness** of the business rules (no duplicates, server totals, valid transitions).
2. **Clarity**: code a reviewer can read in a 10-minute video.
3. **Zero-friction setup**: `dotnet run` and `npm start`, nothing else to install beyond SDKs.
4. **Proportionate architecture**: clean separation where it helps testing, no ceremony beyond that.

## 2. Architecture

### 2.1 High-level

```
┌────────────────────────┐   HTTP/JSON    ┌─────────────────────────────────────────┐
│  Angular 18 SPA        │ ─────────────▶ │  ASP.NET Core API (.NET 8)              │
│  (localhost:4200)      │ ◀───────────── │  (localhost:5080)                       │
│                        │  /api proxy    │                                         │
│  - Orders list         │                │  Endpoints ─▶ OrderService ─▶ Domain    │
│  - New order form      │                │                    │                    │
│  - Order detail        │                │                    ▼                    │
└────────────────────────┘                │             OrderIntakeDbContext        │
                                          │                    │                    │
                                          └────────────────────┼────────────────────┘
                                                               ▼
                                                     orders.db (SQLite file)
```

### 2.2 Backend layering

> *v1.1: built as one API project with a `Domain/` folder and one test project; see §0.*

| Project | Responsibility | Depends on |
|---|---|---|
| `OrderIntake.Domain` | Entities, value objects, business rules (totals, status transitions, reference normalisation, payload fingerprint). Pure C#, no framework dependencies. | — |
| `OrderIntake.Api` | HTTP endpoints, request/response DTOs, validation, application service (`OrderService`), EF Core persistence, error mapping, DI composition. | Domain |
| `OrderIntake.Domain.Tests` | Unit tests for domain rules. | Domain |
| `OrderIntake.Api.Tests` | Unit tests for `OrderService` and validators; a small set of integration tests via `WebApplicationFactory`. | Api, Domain |

**Why not a full Clean Architecture (Application / Infrastructure projects)?** For a 4-hour scope, four or five projects add navigation cost without adding testability. The domain is isolated (that is where the rules live and get tested); everything else stays in one API project with clear folders. SOLUTION.md notes how it would split as the system grows.

### 2.3 Repository layout

> *v1.1: as built the layout is `backend/src/OrderTracker.Api` (`Domain/`, `Application/`, `Infrastructure/Persistence/`, `Infrastructure/ExchangeRates/`, `Contracts/`, `Endpoints/`), `backend/tests/OrderTracker.Tests`, `frontend/`, `docker/postgres/init/`; see README "Project layout".*

```
order-intake/
├── README.md
├── SOLUTION.md
├── docs/
│   ├── 01-business-requirements.md
│   ├── 02-design-document.md
│   └── 03-todo-backlog.md
├── backend/
│   ├── OrderIntake.sln
│   ├── Directory.Build.props          # nullable, warnings-as-errors, LangVersion
│   ├── src/
│   │   ├── OrderIntake.Domain/
│   │   │   ├── Orders/
│   │   │   │   ├── Order.cs
│   │   │   │   ├── OrderLine.cs
│   │   │   │   ├── OrderStatus.cs
│   │   │   │   ├── OrderStatusTransitions.cs
│   │   │   │   ├── OrderStatusChange.cs
│   │   │   │   ├── CustomerInfo.cs
│   │   │   │   ├── ExternalReference.cs
│   │   │   │   └── OrderFingerprint.cs
│   │   │   ├── Common/
│   │   │   │   ├── Money.cs            # rounding helper
│   │   │   │   ├── Currency.cs         # supported ISO codes
│   │   │   │   ├── DomainException.cs
│   │   │   │   └── Result.cs
│   │   └── OrderIntake.Api/
│   │       ├── Program.cs
│   │       ├── Endpoints/OrderEndpoints.cs
│   │       ├── Contracts/              # request/response DTOs
│   │       ├── Validation/             # FluentValidation validators
│   │       ├── Application/OrderService.cs
│   │       ├── Persistence/
│   │       │   ├── OrderIntakeDbContext.cs
│   │       │   └── Configurations/
│   │       ├── Errors/                 # ProblemDetails mapping, error codes
│   │       └── appsettings.json
│   └── tests/
│       ├── OrderIntake.Domain.Tests/
│       └── OrderIntake.Api.Tests/
└── frontend/
    └── order-intake-web/               # Angular 18 workspace
```

## 3. Domain Model

### 3.1 Entities

```
Order (aggregate root)
├── Id                     Guid (v7, time-ordered)
├── ExternalReference      string  (as entered, trimmed)
├── NormalizedReference    string  (trimmed + upper-invariant; UNIQUE)
├── Customer               CustomerInfo (owned)
│     ├── Name             string
│     └── Email            string  (trimmed, lower-invariant)
├── Currency               string  (ISO 4217, e.g. "ZAR")
├── Notes                  string?
├── Lines                  List<OrderLine>
│     ├── LineNumber       int (1-based, preserves entry order)
│     ├── Sku              string
│     ├── Name             string
│     ├── Quantity         int     (≥ 1)
│     ├── UnitPrice        decimal (≥ 0, 2 dp)
│     └── LineTotal        decimal (computed)
├── Subtotal               decimal (computed)
├── Total                  decimal (computed; = Subtotal)
├── Status                 OrderStatus
├── StatusHistory          List<OrderStatusChange>
│     ├── From             OrderStatus?   (null for creation)
│     ├── To               OrderStatus
│     ├── Reason           string?
│     └── ChangedAtUtc     DateTimeOffset
├── PayloadFingerprint     string  (SHA-256 hex of canonical submission)
├── CreatedAtUtc           DateTimeOffset
├── UpdatedAtUtc           DateTimeOffset
└── Version                int     (optimistic concurrency token)
```

### 3.2 Key design choices

| Choice | Decision | Rationale |
|---|---|---|
| Customer | Snapshot (owned value) on the order, not a separate managed entity | No customer management in scope. A snapshot captures who the order was for at submission time and avoids surprising cross-order updates. Trade-off: no customer-level reporting; noted for future. |
| IDs | `Guid.CreateVersion7()` (.NET 9+) or a small v7 helper on .NET 8 | Time-ordered, safe to expose, no sequential leakage. |
| Money | `decimal`, single currency per order, `Math.Round(x, 2, MidpointRounding.AwayFromZero)` | Avoids floating-point errors; away-from-zero matches common commercial expectations. Banker's rounding listed as alternative. |
| Computed totals | Computed in the `Order` factory and stored | Stored for cheap listing/sorting; always derived by domain code, never from input. |
| Immutability | Lines and customer can't change after creation | Matches assumption A-3; simplifies duplicate semantics. |

### 3.3 Order creation (factory)

```csharp
public static Order Create(
    ExternalReference reference,
    CustomerInfo customer,
    string currency,
    string? notes,
    IReadOnlyList<NewOrderLine> lines,
    string fingerprint,
    TimeProvider clock)
```

- Guards invariants again (quantity ≥ 1, price ≥ 0, ≥ 1 line). DTO validation catches user errors first; the domain guards make invalid states impossible regardless of entry point.
- Computes `LineTotal = Round(Quantity × UnitPrice)`, `Subtotal = Σ LineTotal`, `Total = Subtotal`.
- Sets `Status = Pending`, adds initial `StatusHistory` entry, timestamps from injected `TimeProvider` (testable).

### 3.4 Status transitions

Rules live in one table-driven class:

```csharp
public static class OrderStatusTransitions
{
    private static readonly Dictionary<OrderStatus, OrderStatus[]> Allowed = new()
    {
        [OrderStatus.Pending]   = [OrderStatus.Confirmed, OrderStatus.Cancelled],
        [OrderStatus.Confirmed] = [OrderStatus.Fulfilled, OrderStatus.Cancelled],
        [OrderStatus.Fulfilled] = [],
        [OrderStatus.Cancelled] = [],
    };

    public static IReadOnlyList<OrderStatus> AllowedFrom(OrderStatus from) => Allowed[from];
    public static bool IsFinal(OrderStatus s) => Allowed[s].Length == 0;
}
```

`Order.ChangeStatus(OrderStatus target, string? reason, TimeProvider clock)` returns a `Result` with one of:

| Outcome | Error code | Example message |
|---|---|---|
| Success | — | — |
| Same status | `order.status.unchanged` | "Order is already Confirmed." |
| From final state | `order.status.final` | "Order is Fulfilled, which is final. No further status changes are allowed." |
| Not allowed | `order.status.invalid_transition` | "Cannot change status from Pending to Fulfilled. Allowed next statuses: Confirmed, Cancelled." |

The API response for every order includes `allowedTransitions`, so the UI never has to duplicate the rules.

**Why a `Result` and not exceptions?** Invalid transitions are an expected business outcome, not an exceptional failure. A `Result` keeps control flow explicit and makes tests read cleanly. Exceptions are reserved for broken invariants (programming errors).

## 4. Duplicate Prevention (Idempotency) Design

This is the core of the assignment, so it gets its own section.

### 4.1 Identity of a submission

- The **external reference** is the idempotency key.
- **Normalisation**: trim, then `ToUpperInvariant()`. Stored in `NormalizedReference` with a **unique index**. Original casing kept in `ExternalReference` for display.

### 4.2 Detecting "same" vs "different" content

On every submission the service builds a **canonical fingerprint** of the business-relevant payload:

- Normalised reference, customer name (trimmed), customer email (lower-cased), currency (upper-cased), notes (trimmed, null ≡ empty), and lines in submitted order: SKU (trimmed, upper), name (trimmed), quantity, unit price (formatted `0.00`, invariant culture).
- Serialised deterministically, then hashed with **SHA-256** → hex string stored on the order.

Line order is **significant** (an order with lines reversed counts as different). This is the simplest defensible rule; noted as a trade-off.

### 4.3 Decision table

| Situation | Outcome | HTTP | Body |
|---|---|---|---|
| Reference not seen before | Create order | `201 Created` + `Location` | Order |
| Reference exists, fingerprint matches | Return existing order, create nothing | `200 OK` + header `Idempotent-Replay: true` | Order (with `isReplay: true` in envelope, see 5.3) |
| Reference exists, fingerprint differs | Reject, change nothing | `409 Conflict` | ProblemDetails, code `order.reference.conflict`, includes `existingOrderId` |
| Request invalid | Reject | `400 Bad Request` | ValidationProblemDetails |

Status of the existing order does not matter (FR-15): a replay of a Cancelled order still returns the Cancelled order with a 200.

**Why not just return the existing order for any reuse of the reference?** That would hide a real mistake (a rep reusing a PO number for a different order) behind a success response. **Why not overwrite?** That silently mutates confirmed business data. The fingerprint approach mirrors established idempotency-key patterns (e.g. payment APIs that reject a reused key with a different body).

### 4.4 Concurrency (two submissions at once)

The check-then-insert pattern alone has a race window. The flow is:

```
1. Validate request.
2. Compute normalised reference + fingerprint.
3. Look up by NormalizedReference.
   ├─ found → compare fingerprint → 200 replay or 409 conflict.
   └─ not found → build Order, INSERT.
        ├─ success → 201.
        └─ unique-constraint violation (SQLite error 19 / SQLITE_CONSTRAINT_UNIQUE)
             → another request won the race.
             → detach entity, re-read by NormalizedReference, go to step 3 "found" branch.
```

The **database unique index is the source of truth**. The lookup is an optimisation that yields a friendly response in the common case. Covered by a test that fires N parallel identical submissions and asserts exactly one order exists and N−1 responses are replays.

### 4.5 Client-side support

- Submit button disabled while a request is in flight.
- The rep types the customer's PO number as the reference, so a resubmission naturally reuses it.
- On `200` replay: banner "Already submitted on {createdAt}. No duplicate was created." and navigate to the existing order.
- On `409`: inline error on the reference field with a link to the existing order.

## 5. API Design

Base path: `/api`. JSON, camelCase. Enums serialised as strings. OpenAPI document via Swashbuckle (or `Microsoft.AspNetCore.OpenApi`) with Swagger UI in Development.

### 5.1 Endpoints

| Method | Path | Purpose | Success | Errors |
|---|---|---|---|---|
| `POST` | `/api/orders` | Submit an order (idempotent on reference) | `201` new / `200` replay | `400`, `409` |
| `GET` | `/api/orders` | List orders, newest first, paged, filterable | `200` | `400` (bad query) |
| `GET` | `/api/orders/{id}` | Get one order | `200` | `404` |
| `GET` | `/api/orders/by-reference/{reference}` | Find by external reference | `200` | `404` |
| `PATCH` | `/api/orders/{id}/status` | Change status | `200` | `400`, `404`, `409` |
| `GET` | `/api/meta/currencies` | Supported currencies (for the UI dropdown) | `200` | — |
| `GET` | `/health` | Liveness | `200` | — |

### 5.2 Submit order

**Request**

```json
POST /api/orders
{
  "externalReference": "PO-1001",
  "customer": { "name": "Thandi Nkosi", "email": "thandi@example.co.za" },
  "currency": "ZAR",
  "notes": "Deliver to loading bay 3",
  "lines": [
    { "sku": "WID-001", "name": "Widget", "quantity": 3, "unitPrice": 19.99 },
    { "sku": "GAD-042", "name": "Gadget", "quantity": 1, "unitPrice": 250.00 }
  ]
}
```

Unknown properties (e.g. a client-sent `total`) are ignored; totals are never read from input.

**Response (201 / 200)**

```json
{
  "id": "0192c3e4-...",
  "externalReference": "PO-1001",
  "customer": { "name": "Thandi Nkosi", "email": "thandi@example.co.za" },
  "currency": "ZAR",
  "notes": "Deliver to loading bay 3",
  "lines": [
    { "lineNumber": 1, "sku": "WID-001", "name": "Widget", "quantity": 3, "unitPrice": 19.99, "lineTotal": 59.97 },
    { "lineNumber": 2, "sku": "GAD-042", "name": "Gadget", "quantity": 1, "unitPrice": 250.00, "lineTotal": 250.00 }
  ],
  "subtotal": 309.97,
  "total": 309.97,
  "status": "Pending",
  "allowedTransitions": ["Confirmed", "Cancelled"],
  "statusHistory": [
    { "from": null, "to": "Pending", "reason": null, "changedAtUtc": "2026-10-11T12:02:11Z" }
  ],
  "createdAtUtc": "2026-10-11T12:02:11Z",
  "updatedAtUtc": "2026-10-11T12:02:11Z",
  "version": 1
}
```

### 5.3 Signalling a replay

- HTTP status `200` (vs `201`) and header `Idempotent-Replay: true`.
- The body is the same order shape in both cases, so clients that ignore the signal still behave correctly.
- CORS exposes the `Idempotent-Replay` and `Location` headers so Angular can read them.

### 5.4 List orders

```
GET /api/orders?status=Pending&search=po-10&page=1&pageSize=20
```

| Param | Type | Default | Notes |
|---|---|---|---|
| `status` | enum | none | Optional filter |
| `search` | string | none | Case-insensitive contains on reference, customer name, customer email |
| `page` | int ≥ 1 | 1 | |
| `pageSize` | int 1–100 | 20 | |

Sort: `CreatedAtUtc DESC, Id DESC` (deterministic tiebreaker; v7 GUIDs are time-ordered).

```json
{
  "items": [
    { "id": "...", "externalReference": "PO-1001", "customerName": "Thandi Nkosi",
      "status": "Pending", "currency": "ZAR", "total": 309.97,
      "lineCount": 2, "createdAtUtc": "2026-10-11T12:02:11Z" }
  ],
  "page": 1,
  "pageSize": 20,
  "totalCount": 1
}
```

> SQLite note: EF Core cannot `ORDER BY` a `DateTimeOffset` on SQLite natively. Store `CreatedAtUtc` with a value converter to UTC ticks (`long`) or as `DateTime` UTC. Decision: convert to `long` ticks for correct sorting and comparison.

### 5.5 Change status

```json
PATCH /api/orders/{id}/status
{ "status": "Confirmed", "reason": null, "expectedVersion": 1 }
```

- `expectedVersion` is optional. If supplied and it does not match → `409` code `order.concurrency_conflict` ("This order was changed by someone else. Refresh and try again.").
- Regardless, EF Core concurrency token on `Version` protects against lost updates between read and save.
- Success returns the full updated order.

### 5.6 Error format

> *v1.2: implemented. Every error carries a stable `code`; the as-built list is in §17 and SOLUTION.md ("Error codes").*

All errors use **RFC 9457 ProblemDetails** with an extra `code` field (stable, machine-readable) and optional extensions.

```json
{
  "type": "https://order-intake.local/errors/order.status.invalid_transition",
  "title": "Status change not allowed",
  "status": 409,
  "detail": "Cannot change status from Pending to Fulfilled. Allowed next statuses: Confirmed, Cancelled.",
  "code": "order.status.invalid_transition",
  "currentStatus": "Pending",
  "requestedStatus": "Fulfilled",
  "allowedTransitions": ["Confirmed", "Cancelled"],
  "traceId": "00-..."
}
```

Validation errors (400) use `ValidationProblemDetails` with `errors` keyed by JSON path, e.g. `"lines[1].quantity": ["Quantity must be a whole number of at least 1."]`.

| Code | HTTP | When |
|---|---|---|
| `validation_failed` | 400 | Request fails validation |
| `order.not_found` | 404 | Unknown id / reference |
| `order.reference.conflict` | 409 | Reference reused with different content |
| `order.status.invalid_transition` | 409 | Transition not allowed |
| `order.status.final` | 409 | Order in final state |
| `order.status.unchanged` | 409 | Target equals current status |
| `order.concurrency_conflict` | 409 | Version mismatch |
| `internal_error` | 500 | Unhandled exception (no stack trace leaked) |

**Why 409 for transition errors (not 400/422)?** The request is well-formed; it conflicts with the current state of the resource. 422 is a reasonable alternative and is noted in SOLUTION.md.

### 5.7 Validation

> *v1.1: rules implemented in a hand-written `CreateOrderValidator` rather than FluentValidation. Differences: currency is any ISO 4217 code, notes ≤ 1,000, email ≤ 320, the reference character pattern isn't enforced, and quantity/price are nullable decimals so `2.5` gets a friendly message.*

FluentValidation validators for request DTOs, wired via an endpoint filter. Rules per BRA §7.6:

| Field | Rule |
|---|---|
| `externalReference` | Required; trimmed length 1–64; regex `^[A-Za-z0-9\-_/.]+$` |
| `customer.name` | Required; 1–200 |
| `customer.email` | Required; ≤ 254; valid email |
| `currency` | Required; in supported set {ZAR, USD, EUR, GBP} (case-insensitive) |
| `notes` | Optional; ≤ 2000 |
| `lines` | 1–100 items |
| `lines[i].sku` | Required; 1–64 |
| `lines[i].name` | Required; 1–200 |
| `lines[i].quantity` | Integer; 1 – 1,000,000 |
| `lines[i].unitPrice` | 0 – 10,000,000; max 2 decimal places |

**Whole-number quantity:** the DTO types `quantity` as `int`. A JSON value like `2.5` fails model binding; that error is mapped to a friendly validation message instead of the raw System.Text.Json error.

**Price precision:** `unitPrice` as `decimal`; validator checks `value == Math.Round(value, 2)`.

## 6. Persistence

> *v1.1: superseded by §14 (PostgreSQL). Kept as the record of the original decision.*

| Aspect | Decision |
|---|---|
| Engine | **SQLite** file (`orders.db`) via `Microsoft.EntityFrameworkCore.Sqlite` |
| Schema creation | `Database.EnsureCreated()` on startup (no migrations needed for the exercise; noted as trade-off) |
| Config | Connection string in `appsettings.json`; DB file under `backend/src/OrderIntake.Api/data/` (git-ignored) |
| Unique key | Unique index on `Orders.NormalizedReference` |
| Owned types | `Customer` as owned columns; `Lines` and `StatusHistory` as owned collections (separate tables) |
| Money columns | `decimal` with value converter to `TEXT` or `(18,2)`. SQLite has no decimal type, so use EF's default (TEXT) to preserve precision; sorting by total not required |
| Timestamps | `DateTimeOffset` → UTC ticks `long` converter (sortable on SQLite) |
| Concurrency | `Version` as concurrency token, incremented in `ChangeStatus` |
| Seed data | Optional dev seed (5–6 orders in mixed statuses) behind a config flag, for the demo |
| Reset | Delete `orders.db` or run with `--reset-db` flag |

**Why SQLite over in-memory?**

| Option | Pros | Cons |
|---|---|---|
| `ConcurrentDictionary` in memory | Fastest to build, no packages | Data lost on restart; uniqueness must be hand-coded; doesn't resemble production |
| EF Core InMemory provider | Easy | **Does not enforce unique indexes**: the race safeguard would be untestable |
| **SQLite file** (chosen) | Real unique constraints and transactions, survives restarts, zero install, same EF code works with PostgreSQL/SQL Server | Minor SQLite quirks (decimal, DateTimeOffset) handled with converters |

Repository abstraction: not introduced. `OrderService` uses `DbContext` directly (it is already a unit of work and repository). Tests use SQLite in-memory mode (`DataSource=:memory:` with an open connection), which keeps real constraint behaviour.

## 7. Application Service

`OrderService` (scoped) orchestrates use cases:

```csharp
Task<SubmitOrderResult> SubmitAsync(SubmitOrderRequest request, CancellationToken ct);
Task<OrderDto?>         GetByIdAsync(Guid id, CancellationToken ct);
Task<OrderDto?>         GetByReferenceAsync(string reference, CancellationToken ct);
Task<PagedResult<OrderSummaryDto>> ListAsync(OrderQuery query, CancellationToken ct);
Task<Result<OrderDto>>  ChangeStatusAsync(Guid id, ChangeStatusRequest request, CancellationToken ct);
```

`SubmitOrderResult` is a discriminated outcome: `Created(order)`, `Replayed(order)`, `Conflict(existingOrderId)`. Endpoints map outcomes to HTTP, keeping HTTP concerns out of the service.

## 8. Cross-cutting Concerns

| Concern | Approach |
|---|---|
| Error handling | `IExceptionHandler` → ProblemDetails 500; `AddProblemDetails()` for consistent shape; business outcomes mapped explicitly |
| Logging | Built-in `ILogger` with structured messages; log replays (info), conflicts (warning), rejected transitions (info). Optionally Serilog console sink |
| Time | `TimeProvider` injected (`TimeProvider.System` in prod, `FakeTimeProvider` in tests) |
| CORS | Allow `http://localhost:4200` in Development; expose `Location`, `Idempotent-Replay` |
| JSON | `JsonStringEnumConverter`; camelCase; ignore unknown members |
| OpenAPI | Swagger UI at `/swagger` in Development, with example payloads |
| Health | `/health` via `AddHealthChecks()` (+ DB check) |
| Config | `appsettings.json` / `appsettings.Development.json`; ports fixed in `launchSettings.json` |
| Code quality | Nullable enabled, warnings as errors, `.editorconfig`, `dotnet format` |

## 9. Frontend Design (Angular 18)

### 9.1 Approach

- **Standalone components**, no NgModules.
- **Signals** for component state; `HttpClient` (with `provideHttpClient(withFetch())`) for API calls returning observables, converted with `toSignal` where convenient.
- **Reactive forms** (typed) with a `FormArray` for line items.
- Styling: plain SCSS with a small set of utility classes, or Angular Material for speed. Decision: **plain SCSS + native elements** to keep the bundle and setup minimal; Material is a fine alternative.
- Dev proxy (`proxy.conf.json`) forwards `/api` → `http://localhost:5080`, so no CORS in dev and the base URL stays relative.

### 9.2 Routes

| Path | Component | Purpose |
|---|---|---|
| `/` | redirect → `/orders` | |
| `/orders` | `OrderListComponent` | Newest-first table, status filter, search, paging |
| `/orders/new` | `OrderFormComponent` | Submit new order |
| `/orders/:id` | `OrderDetailComponent` | Full details, status actions, history |
| `**` | `NotFoundComponent` | |

### 9.3 Structure

> *v1.1: as built: `orders/` (list, form, detail, models, API client) and `shared/` (currency API, currency picker, problem helper, status badge); see §0.*

```
src/app/
├── app.config.ts            # providers: router, http, interceptors
├── app.routes.ts
├── core/
│   ├── api/orders-api.service.ts     # typed HTTP client
│   ├── api/models.ts                 # TS interfaces mirroring API contracts
│   ├── errors/problem-details.ts     # parsing ProblemDetails / validation errors
│   └── notifications/toast.service.ts
├── features/orders/
│   ├── order-list/
│   ├── order-form/
│   │   └── line-items-editor/
│   ├── order-detail/
│   │   └── status-actions/
│   └── shared/
│       ├── status-badge.component.ts
│       └── money.pipe.ts             # formats using currency code
└── shared/ui/                         # button, alert, spinner
```

### 9.4 Screens

**Order list**
- Columns: Reference · Customer · Status (badge) · Total (currency) · Lines · Created (local time).
- Controls: status dropdown, search box (debounced 300 ms), pager, "New order" button.
- Filtering is done **server-side** via query params (scales, and paging stays correct). Filter state reflected in the URL query string.
- Empty state and loading state.

**New order form**
- Fields: Reference (hint: "Use the customer's PO number"), customer name, email, currency (from `/api/meta/currencies`), notes.
- Line items editor: add/remove rows; SKU, name, quantity (step 1, min 1), unit price (min 0, step 0.01); live **estimated** line total and order total, labelled "Estimate. Final totals are calculated on save".
- Client validation mirrors server rules; server validation errors mapped back to controls by JSON path (`lines[1].quantity` → `lines.at(1).quantity`).
- Submit button disabled while invalid or submitting.
- Outcomes:
  - `201` → toast "Order created", navigate to detail.
  - `200` replay → navigate to detail with banner "Already submitted on … No duplicate was created."
  - `409` conflict → error on reference field: "Reference already used by a different order." + link.
  - `400` → field errors.
  - Network error → generic alert with retry (retry is safe because the endpoint is idempotent).

**Order detail**
- Header: reference, status badge, created/updated.
- Customer block, notes.
- Lines table with line totals; subtotal and total in footer.
- Status actions: one button per `allowedTransitions` entry ("Confirm", "Mark fulfilled", "Cancel"). Cancel asks for confirmation and optional reason. Sends `expectedVersion`.
- Final-state orders show "This order is {status} and can no longer change."
- 409 responses shown as alert with the server's `detail` message; on concurrency conflict, offer "Reload".
- Status history timeline.

### 9.5 Error handling in UI

- An HTTP interceptor normalises errors into a `ApiError` type (`status`, `code`, `detail`, `fieldErrors`).
- Components decide presentation; the server's `detail` text is shown as-is because it is written for users.

## 10. Testing Strategy

### 10.1 Backend unit tests (required)

Framework: **xUnit**, assertions via **Shouldly** (or xUnit `Assert`), `Microsoft.Extensions.TimeProvider.Testing` for `FakeTimeProvider`.

| Area | Test cases |
|---|---|
| Totals | single line; multiple lines; zero price; rounding (e.g. 3 × 0.335 → 1.01 with away-from-zero); large quantity × price; total = subtotal |
| Domain guards | quantity 0 / negative rejected; negative price rejected; no lines rejected |
| Reference normalisation | trims; case-insensitive equality; preserves display casing |
| Fingerprint | identical payload → same hash; whitespace/case differences in reference/email/currency → same hash; different qty/price/sku/notes → different hash; line order change → different hash; price `19.9` vs `19.90` → same hash |
| Status transitions | every cell of the 4×4 matrix (theory data): allowed ones succeed and update `UpdatedAtUtc` + history + version; disallowed return correct code and message listing allowed statuses |
| Validators | each rule's boundary values (min/max lengths, 0/1 quantity, 2 vs 3 decimals, unsupported currency, invalid email, 0 and 101 lines) |
| OrderService.Submit | new → Created; same payload → Replayed (one row); different payload → Conflict (original unchanged); replay after status change still Replayed; client totals ignored |
| OrderService concurrency | 20 parallel identical submissions → exactly 1 row, 1 Created, 19 Replayed |
| OrderService.List | newest first; tiebreak deterministic; paging; status filter; search |
| OrderService.ChangeStatus | not found; version mismatch → concurrency conflict |

### 10.2 Integration tests (should)

`WebApplicationFactory<Program>` with SQLite in-memory:

- POST → 201 + Location; repeat → 200 + `Idempotent-Replay: true`; mutated repeat → 409 ProblemDetails.
- Invalid body → 400 with field paths.
- PATCH invalid transition → 409 with `code` and `allowedTransitions`.
- GET list ordering.

### 10.3 Frontend tests (could)

- `OrderFormComponent`: estimated totals; required validation; maps server field errors.
- `StatusActionsComponent`: renders only allowed transitions.
- `OrdersApiService`: reads `Idempotent-Replay` header.

### 10.4 Commands

```bash
cd backend && dotnet test
cd frontend/order-intake-web && npm test   # optional
```

## 11. Developer Experience

> *v1.1: see §16 and the README for the commands as built.*

| Item | Detail |
|---|---|
| Prerequisites | .NET 8 SDK, Node 20 LTS+, npm |
| Backend run | `cd backend && dotnet run --project src/OrderIntake.Api` → `http://localhost:5080`, Swagger at `/swagger` |
| Frontend run | `cd frontend/order-intake-web && npm ci && npm start` → `http://localhost:4200` |
| One-liner (optional) | Root `run.ps1` / `run.sh` that starts both; or `docker compose up` as optional extra |
| Seed data | `Seed:Enabled=true` in Development |
| Sample requests | `backend/src/OrderIntake.Api/OrderIntake.http` (VS / Rider / VS Code REST Client) covering replay and conflict |
| CI (optional) | GitHub Actions: `dotnet test` + `npm ci && npm run build` |

## 12. Trade-offs Summary (for SOLUTION.md)

| Decision | Chosen | Alternative | Why |
|---|---|---|---|
| Duplicate key | Client external reference | Separate `Idempotency-Key` header | Brief specifies the client reference; it is meaningful to reps (their PO number) |
| Replay with different body | 409 Conflict | Return existing order / overwrite | Surfaces real mistakes; never mutates silently |
| Replay signal | 200 vs 201 + header | Same 201 always | Lets UI tell the rep "already captured" while staying backward compatible |
| Storage | ~~SQLite file~~ PostgreSQL in Docker *(v1.1)* | In-memory / SQLite | Real unique constraint and race handling, persistence; production-like and still one command (§14) |
| Schema | ~~`EnsureCreated`~~ EF Core migrations *(v1.1)* | `EnsureCreated` | Real schema evolution; applied by a one-shot `migrate` service |
| Architecture | Domain + API (2 projects) | Full Clean Architecture | Proportionate; rules isolated and tested |
| Business errors | `Result` type | Exceptions | Expected outcomes, explicit flow |
| Transition error status | 409 | 422 / 400 | State conflict semantics |
| Customer | Snapshot on order | Customer entity with upsert | No customer management in scope |
| Totals | Stored + computed by domain | Computed on read | Cheap listing; still never client-supplied |
| Rounding | Away from zero, per line | Banker's / round only at total | Commercial expectation; documented and tested |
| Filtering | Server-side | Client-side | Correct with paging |
| Auth | None | JWT/Keycloak | Out of scope; would be first addition |

## 13. Future Considerations

- Authentication/authorisation (e.g. Keycloak/OIDC) and per-user audit (who changed status).
- Multi-tenant reference uniqueness (`TenantId + Reference`).
- Tax, discounts, multiple price lists.
- ~~PostgreSQL with migrations~~ (done in v1.1); outbox pattern for publishing `OrderCreated` / `OrderStatusChanged` events.
- Cursor-based paging for large lists.
- Order amendments with versioning instead of cancel-and-resubmit.
- E2E tests (Playwright).

## 14. Persistence as built (v1.1)

| Aspect | Decision |
|---|---|
| Engine | PostgreSQL 17 (`postgres:17-alpine`) via EF Core 10 + Npgsql, snake_case naming |
| Model | Persistence entities (`OrderEntity`, `OrderLineEntity`, `OrderStatusChangeEntity`, `OrderCurrencyChangeEntity`) mapped to and from the immutable domain `Order` by `OrderMapping` |
| Tables | `orders` (current state), `order_lines` (current and base unit price, line total), `order_status_changes` and `order_currency_changes` (append-only) |
| Duplicate guard | Unique index `ux_orders_normalized_reference`. The repository looks the reference up first; if an insert hits `23505` on that index, it clears the unit of work, reads the winner and returns it as a replay |
| Concurrency | `orders.version` is an EF concurrency token (`UPDATE … WHERE version = @expected`); new history rows are in the same transaction, so a losing writer's rows roll back |
| Integrity | Check constraints: quantity > 0, prices ≥ 0, status in the enum, 3-letter currencies, rate > 0, version ≥ 1 |
| Money and time | `numeric(20,2)` prices, `numeric(28,2)` totals, `numeric(20,8)` rates; `timestamptz`, truncated to microseconds before saving |
| Ids and numbers | Version 7 GUIDs (time-ordered); `number` is an identity column (gaps possible, display only) |
| Migrations | Committed under `Infrastructure/Persistence/Migrations`; `dotnet ef` as a local tool. Applied by `dotnet OrderTracker.Api.dll migrate` (Docker `migrate` service) or on startup in Development |
| Roles | Created by `docker/postgres/init/01-create-roles.sh` on first start: `ordertracker_migrator` owns the schema; `ordertracker_app` has `SELECT`, `INSERT`, `UPDATE` only (no DDL, no `DELETE`) |
| Health | `/health` includes `AddDbContextCheck<OrdersDbContext>` |
| Tests | Testcontainers PostgreSQL, a fresh migrated database per test class; tagged `Category=Database` |

## 15. Currencies and exchange rates (v1.1)

- `IExchangeRateProvider` → `FrankfurterExchangeRateProvider` (ECB reference rates via api.frankfurter.dev, no key, 5 s timeout). Network, timeout, 5xx and bad-JSON failures become `ExchangeRatesUnavailableException`.
- `ExchangeRateService` caches rates per base currency for 1 hour and the currency list for 24 hours. If a refresh fails, it serves the last good rates for up to 3 days, flagged `isStale`.
- Endpoints: `GET /api/currencies`, `GET /api/exchange-rates?base=XXX`, `PATCH /api/orders/{id}/currency` (`200`, or `409` when not Pending, `422` with no rate, `503` when rates are unavailable, `400` for a bad code).
- `Order.ConvertCurrency` re-prices from `BaseLineItems`: `unit price = round(base price × rate, 2)`, line totals and totals recomputed by `Pricing`. Converting back to `BaseCurrency` restores the exact submitted prices. Each conversion is appended to `CurrencyHistory`.
- Duplicate protection still uses the original submission's fingerprint, so resubmitting the original payload after a conversion returns the converted order as a replay.
- UI: a searchable `CurrencySelect` (ARIA combobox) on the form, the Convert action and "Show totals in"; a preview with rate, date and new total before converting; an Original price column on converted orders.

## 16. Deployment and developer experience as built (v1.1)

| Item | Detail |
|---|---|
| Full stack | `docker compose up --build -d` → db (healthy) → migrate (exits 0) → api (healthy) → web; UI at http://localhost:8080, Swagger at http://localhost:8081/swagger |
| Images | API: multi-stage, SDK 10.0.401 build, `aspnet:10.0` runtime, non-root. Web: Node 24 build, unprivileged nginx, SPA fallback, `/api` proxied to `API_UPSTREAM` |
| Configuration | Dev defaults in `docker-compose.yml`, overridable via `.env` (see `.env.example`); `docker compose down -v` resets data |
| Local development | `docker compose up -d db`, then `dotnet run --project src/OrderTracker.Api` (applies migrations) and `npm start` (proxy to 5080) |
| Tests | `dotnet test` (needs Docker for the database tests); `dotnet test --filter "Category!=Database"` without Docker; `npm test -- --watch=false` |

## 17. Domain-Driven Design and Clean Architecture (v1.2)

The backend was restructured into four projects. Dependencies point inwards and are enforced by
`OrderTracker.ArchitectureTests`.

| Project | Contains | Depends on |
|---|---|---|
| `OrderTracker.Domain` | `Order` aggregate root (with `OrderLine`, `OrderStatusChange`, `OrderCurrencyChange`), value objects (`OrderId`, `ExternalReference`, `EmailAddress`, `CustomerInfo`, `CurrencyCode`, `ExchangeRate`), `OrderStatusPolicy`, `SubmissionFingerprint`, domain events, `Result`/`Error`, `IOrderRepository` | nothing |
| `OrderTracker.Application` | Use-case handlers (submit, change status, convert currency, get, list, currencies, rates), `SubmitOrderValidator`, DTOs, ports (`IUnitOfWork`, `IOrderQueries`, `IExchangeRateProvider`), `ExchangeRateService`, domain-event handlers | Domain |
| `OrderTracker.Infrastructure` | `OrdersDbContext` mapping the aggregate directly (owned value objects and child tables), `OrderRepository`, `UnitOfWork` (translates database errors, dispatches events after commit), `OrderQueries` (SQL projection), migrations, `FrankfurterExchangeRateProvider` | Application |
| `OrderTracker.Api` | Endpoints, request contracts, `Error` → ProblemDetails, composition root | Application, Infrastructure |

**Key decisions**
- **Mutable aggregate, private setters.** Every change goes through an aggregate method that keeps the
  invariants and bumps `Version`. EF maps it through backing fields, so the domain has no EF references.
- **Duplicate prevention is unchanged:** the handler looks the reference up, and the unique index catches races.
  The unit of work turns the `23505` into `DuplicateReferenceException` and the handler answers from the winner.
- **Schema unchanged.** The new mapping produces an identical schema. EF confirmed it, so no migration was added;
  only the model snapshot was regenerated. The fingerprint format is unchanged, and a test pins it, so existing
  orders still replay.
- **CQRS-lite.** Commands use the repository and unit of work; queries return DTOs, and the list is projected in SQL.
- **Domain events** (`OrderSubmitted`, `OrderStatusChanged`, `OrderCurrencyConverted`) are dispatched after
  commit; `OrderActivityLogger` writes structured logs (NFR-09).
- **Error codes** (NFR-05): `validation_failed`, `request.malformed`, `order.not_found`, `order.reference.conflict`,
  `order.status.invalid_transition`, `order.status.final`, `order.currency.locked`,
  `exchange_rate.not_available`, `exchange_rate.unavailable`.
- **No MediatR**: handlers are plain classes behind `ICommandHandler`/`IQueryHandler`, injected directly.
- The API's JSON contract is unchanged apart from the added `code` (and `requestedStatus` on transition errors),
  so the frontend needed no changes.
