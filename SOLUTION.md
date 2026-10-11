# Solution notes

## The core problem: duplicate submissions

Sales reps resubmit when they aren't sure an order went through. The order's **client-provided
external reference** (`externalReference`) is the idempotency key.

**What counts as the same reference.** References are compared after trimming and upper-casing,
so `po-1001 ` and `PO-1001` are the same order. The reference as first submitted is stored for display.

**What happens on resubmit.** The server compares a fingerprint (SHA-256) of the normalized
submission with the one stored on the existing order:

| Situation | Response | Why |
|-----------|----------|-----|
| New reference | `201 Created` + order | Normal path. |
| Same reference, same details | `200 OK` + the **existing** order, header `Idempotent-Replay: true` | This is the "did it go through?" retry. The rep sees exactly what they'd have seen the first time, plus a notice that it already existed. Nothing is stored. |
| Same reference, different details | `409 Conflict` with `existingOrderId` | Probably a reused or mistyped reference. Silently returning the old order would hide that the new details were dropped, and overwriting would be worse. The UI links to the existing order and offers a fresh reference. |

The fingerprint is computed from normalized values, so harmless formatting differences
(`10` vs `10.00`, email case, extra whitespace) still count as the same submission.

A resubmit still returns the existing order after its status has changed (for example after it was
confirmed or cancelled). The reference stays used for good.

**Checked in the database, in two layers.**
1. `SubmitOrderHandler` first looks the normalized reference up through the repository.
   This catches the common case, a rep resubmitting seconds or days later.
2. Two identical requests arriving at the same moment can both pass that lookup. So the insert relies on the
   **unique index** `ux_orders_normalized_reference`. The losing request's unit of work gets a unique-violation
   error (`23505` on that index) and raises `DuplicateReferenceException`. The handler then reads the winning order
   and answers as a replay (or a conflict, if the content differs). The rep sees exactly what a later resubmission
   would show.

Tests cover this against real PostgreSQL (20 parallel submissions through the handler, 20 parallel HTTP POSTs,
and the unit of work reporting the index violation), plus deterministic unit tests of the race path.
In the Docker stack, 25 simultaneous `curl` requests produced one `201`, twenty-four `200`s and one row.

**The frontend helps too.** The form generates a reference (`PO-YYYYMMDD-XXXX`) when it opens and
keeps it across failed attempts, so pressing *Submit* again after a timeout is always safe. The
submit button is disabled while a request is in flight. If the network fails, the message says that
retrying with the same reference won't create a duplicate.

## Totals and money

- All money is `decimal`, never `double`, so there's no binary floating-point drift (there's a test for `0.10 + 0.20`).
- `lineTotal = quantity × unitPrice`, `subtotal = Σ lineTotal`, rounded to 2 decimals, half away from zero.
  `total = subtotal` because tax, shipping and discounts are out of scope. `Pricing.Total` is the single place to add them.
- Totals are computed only on the server, and any client-sent totals would be ignored (the request contract has no total fields).
  The form shows a live estimate labelled as such. In the browser check, the estimate matched the server's total.
- Unit prices may have at most 2 decimal places, so line totals are exact. Currencies with 0 or 3 minor
  units (JPY, KWD) aren't specially handled. That's a known simplification.

## Validation

- Quantities must be positive whole numbers and prices non-negative. Sanity caps: quantity ≤ 1,000,000,
  price ≤ 1,000,000,000 and at most 100 lines, so totals can't overflow.
- Request DTO fields are nullable, and quantity is a `decimal` on the wire, so missing or fractional values
  get specific messages ("Quantity must be a positive whole number") instead of a generic deserialization 400.
- Errors are returned as `ValidationProblemDetails`, keyed by JSON path (`lineItems[1].unitPrice`), and the UI
  shows each message under the matching input. The client runs the same basic checks for fast feedback,
  but the server is the authority.
- Malformed JSON returns a `400` problem response, not a `500`.

## Status changes

A small state machine in `OrderStatusPolicy`, enforced by the `Order` aggregate:

```
Pending ──► Confirmed ──► Fulfilled
   │            │
   └────────────┴──► Cancelled
```

- Fulfilled and Cancelled are final. Nothing moves back to Pending, and you can't fulfil an unconfirmed order.
- A rejected change returns `409` with a specific explanation and the allowed next statuses, e.g.
  *"This order must be confirmed before it can be fulfilled. Allowed next statuses: Confirmed, Cancelled."*
- Requesting the status the order is already in returns `200` and changes nothing, so a double-clicked
  *Confirm* doesn't become an error.
- Each order response includes `allowedTransitions`. The UI only offers valid actions and doesn't duplicate the rules.
- Every change is recorded in `statusHistory` with a timestamp.
- Concurrent changes use **optimistic concurrency**: every change bumps the aggregate's `Version`. A save
  succeeds only if the stored version is unchanged, and otherwise the handler re-loads and re-validates. A test races
  *Fulfil* against *Cancel* and checks that exactly one wins and the history stays consistent.

## Architecture: DDD with Clean Architecture

```
            ┌──────────────────────────────────────────────┐
            │ Api            endpoints, request contracts,  │  composition root
            │                Error → ProblemDetails (code)   │
            └───────────────┬──────────────────┬───────────┘
                            │                  │
            ┌───────────────▼───────┐  ┌───────▼──────────────────────────┐
            │ Application           │◄─┤ Infrastructure                   │
            │ use-case handlers,    │  │ EF Core + PostgreSQL (repository, │
            │ validator, DTOs,      │  │ unit of work, read queries,       │
            │ ports, event handlers │  │ migrations), Frankfurter client   │
            └───────────┬───────────┘  └──────────────────────────────────┘
                        │
            ┌───────────▼───────────┐
            │ Domain                │  depends on nothing
            │ Order aggregate,      │
            │ value objects, events │
            └───────────────────────┘
```

Dependencies point inwards only, and `OrderTracker.ArchitectureTests` enforces it: the Domain references
nothing (not even `Microsoft.Extensions`), the Application has no EF, ASP.NET or HTTP code, and the endpoints
never touch persistence.

**Domain (`OrderTracker.Domain`)**, the tactical DDD building blocks:
- **Aggregate root `Order`.** All changes go through its methods (`Submit`, `ChangeStatus`, `ConvertCurrency`),
  which keep the invariants true: totals are always derived from the lines, status follows
  `OrderStatusPolicy`, the currency only changes while Pending and always re-prices from the submitted prices,
  and every change appends history and bumps `Version`. Setters are private (an architecture test checks this).
- **Entities inside the aggregate:** `OrderLine` (keeps current and base price), `OrderStatusChange`,
  `OrderCurrencyChange`. They're created and changed only by the order.
- **Value objects** with validating factories: `OrderId` (strongly typed), `ExternalReference` (display value
  plus normalized key, equal when the keys match), `EmailAddress`, `CustomerInfo`, `CurrencyCode`, `ExchangeRate`.
- **Domain events:** `OrderSubmitted`, `OrderStatusChanged`, `OrderCurrencyConverted`. They're raised by the
  aggregate and dispatched only after a successful commit.
- **Expected failures are values, not exceptions:** `Result`/`Result<T>` with an `Error` that carries a
  stable `code` (see below). `DomainException` is reserved for programming errors (e.g. a line with
  quantity 0 that bypassed validation).
- **`IOrderRepository`** lives with the aggregate it stores.

**Application (`OrderTracker.Application`)**: one handler per use case (`SubmitOrderHandler`,
`ChangeOrderStatusHandler`, `ConvertOrderCurrencyHandler`, `GetOrderHandler`, `ListOrdersHandler`, plus
currency and rate queries), each behind `ICommandHandler`/`IQueryHandler`.
- **Ports** for the outside world: `IUnitOfWork`, `IOrderQueries` (read side), `IExchangeRateProvider`.
- **CQRS-lite:** commands load and save whole aggregates through the repository; queries read DTOs straight
  from the store, with the list projected in SQL.
- **Validation:** `SubmitOrderValidator` reports every field error at once, keyed by JSON path. It reuses the
  value objects' rules, so each rule is written once.
- **Event handling:** `OrderActivityLogger` handles the domain events, and the handlers log replays,
  conflicts, lost races and rejected transitions.
- MediatR isn't used: it's commercially licensed now, and a handler per use case injected directly is just as clear.

**Infrastructure (`OrderTracker.Infrastructure`)**: EF Core maps the aggregate directly. Value objects become
owned columns or converted values, and child entities become owned tables, so there's no separate persistence
model to keep in sync. The schema is identical to before the restructure, and EF confirmed no migration was needed.
- `UnitOfWork` commits, translates `23505` on the reference index into `DuplicateReferenceException` and a
  stale `version` into `ConcurrencyConflictException`, then dispatches domain events.

**Api (`OrderTracker.Api`)**: endpoints translate the request, call a handler and map the `Result`. A single
`ToProblem()` turns every `Error` into ProblemDetails with its `code`, so the error format is consistent by
construction.

Other choices:
- `TimeProvider` is injected, so tests control the clock (ordering and timestamp tests are deterministic).
- Listing sorts by `createdAt` descending, with the sequential order number as tie-breaker. Orders created
  in the same tick still come out newest first.
- **Customer** has no registry in scope. A customer is identified by normalized email, and `customer.id` is
  derived deterministically from it (`cus_` + hash prefix), so the same email always gets the same id.
- **Angular**: standalone components, signals for state, typed reactive forms, zoneless (the v21 default).
  The UI is deliberately plain CSS with no component library.

## Error codes

Every error response is RFC 9457 ProblemDetails with a stable `code`, so clients can branch on it without
parsing messages:

| Code | HTTP | When |
|---|---|---|
| `validation_failed` | 400 | One or more fields invalid (`errors` keyed by JSON path) |
| `request.malformed` | 400 | Body isn't valid JSON |
| `order.not_found` | 404 | Unknown order id |
| `order.reference.conflict` | 409 | Reference reused with different details (`existingOrderId`, `existingOrderNumber`) |
| `order.status.invalid_transition` | 409 | Move not allowed (`currentStatus`, `requestedStatus`, `allowedTransitions`) |
| `order.status.final` | 409 | Order is Fulfilled or Cancelled |
| `order.currency.locked` | 409 | Currency change on a non-Pending order |
| `exchange_rate.not_available` | 422 | No rate for that currency pair |
| `exchange_rate.unavailable` | 503 | Rate source down and no usable cached rates |

## Persistence (PostgreSQL)

- **EF Core 10 + Npgsql**, with snake_case naming. EF maps the `Order` aggregate directly with fluent
  configuration in Infrastructure (owned value objects, owned child tables, backing fields), so the domain has
  no EF attributes or references.
- **Schema.** `orders` has one row per order with its current state. `order_lines` keeps each line's current and
  original (base) price. `order_status_changes` and `order_currency_changes` are append-only history.
  - Order numbers come from an identity column. A failed insert can use up a number, so gaps are possible; that's
    fine because numbers are only for humans.
  - Money is `numeric`, never floating point.
  - Check constraints repeat the key rules (quantity > 0, prices ≥ 0, a valid status, 3-letter currency codes),
    so bad data can't get in even around the API.
- **Optimistic concurrency.** `orders.version` is an EF concurrency token, so updates run as
  `UPDATE … WHERE id = @id AND version = @expected`. If another request committed first, the update affects no
  rows, EF throws, and the unit of work reports a conflict. The handler then re-loads and re-checks the rules.
  New history rows are written in the same transaction, so a losing writer's history is rolled back too.
- **Timestamps** are truncated to microseconds before saving (PostgreSQL's precision), so an order returned
  straight after a write is identical to the same order read back later.
- **Ids** are version 7 GUIDs, which are time-ordered so the primary-key index stays compact.
- **Migrations** are generated by EF Core and committed. In Docker they run in a separate one-shot `migrate`
  service before the API starts. That service connects as a schema-owning role, while the API connects as a role
  that can only `SELECT`, `INSERT` and `UPDATE`, so a bug or injection in the API can't drop or delete anything.
  In local development the API applies migrations itself on startup (`Database:MigrateOnStartup`).
- **Health.** `/health` includes a database check, so Compose only starts the UI once the API can reach PostgreSQL.

## Currencies and exchange rates

**Where the rates come from.** `IExchangeRateProvider` has one implementation, `FrankfurterExchangeRateProvider`. It
reads the European Central Bank's daily reference rates from the free Frankfurter API, with no key and about 30
currencies. `ExchangeRateService` wraps it:

- Rates are cached per base currency for an hour (ECB publishes once per working day).
- If the provider is down, the last good rates are served for up to 3 days, flagged `isStale` so the UI can say so.
  Past that, or with nothing cached, the API returns `503` with a clear message instead of guessing.
- Provider failures (network, timeout, 5xx, bad JSON) all become one `ExchangeRatesUnavailableException`,
  so callers handle a single failure type.

**Changing an order's currency** (`PATCH /api/orders/{id}/currency`):

- Only allowed while the order is **Pending**. Once confirmed, the customer has agreed to the prices, so they're locked.
- The order keeps its **base currency and the line items as submitted**. Every conversion re-prices from those
  base prices (`unit price = round(base price × rate, 2)`), never from the previous conversion. So USD → EUR → GBP
  rounds once, not twice, and converting back to USD restores the exact submitted prices. Totals are then
  recomputed by the same `Pricing` functions as always.
- Each conversion is recorded with the rate, the rate date, the source and the time, so you can always see where a
  price came from.
- Duplicate protection still keys on the original submission. Resubmitting the original payload after a
  conversion returns the (converted) existing order, the same as after a status change.

**Display-only conversion.** The order list's "Show totals in" option converts each total into one currency so
mixed-currency orders can be compared and summed. It's labelled as approximate, with the rate date, and never
changes an order.

**Trade-offs.**
- ECB rates are mid-market reference rates updated once a day, which is fine for an internal sales tool but not for
  settling payments. A paid feed (and a margin or spread) would slot in behind `IExchangeRateProvider`.
- Converted prices are rounded to 2 decimals for every currency, which matches the existing simplification (JPY
  would ideally round to 0).
- Order creation still accepts any ISO-format code, so a provider outage can never block taking orders. The form
  offers the supported list, but an order in an unsupported currency just can't be converted (`422`).

## Trade-offs and what I'd do next

| Choice | Trade-off | Next step for production |
|--------|-----------|--------------------------|
| PostgreSQL in Docker with dev-default credentials | One command brings everything up. Passwords are development defaults (overridable via `.env`). | A managed database, secrets from a vault, TLS to the database, backups and point-in-time recovery. |
| Reference is unique forever | Simple, predictable rule for reps. | Probably scope uniqueness per customer or per sales team. |
| Migrations run by a one-shot container on deploy | Simple and safe for a single deployment. | Run the migration step from the CI/CD pipeline (an EF migration bundle), with backward-compatible migrations for zero-downtime deploys. |
| Database tests need Docker | They use real PostgreSQL (Testcontainers) instead of an in-memory imitation, so the unique index and concurrency behaviour are actually tested. | Run them in CI on a Docker-enabled agent. |
| 409 on reference reuse with different data | Safer than silently ignoring changes, but the rep must pick a new reference. | Possibly an explicit "amend order" flow while it's still Pending. |
| No auth | Out of scope for the timebox. | Company SSO (OIDC), recording who submitted and who changed status. |
| Server-side search + status filter, max 200 per page | Enough for the demo. The UI loads one page. | Pagination controls in the UI and date-range filters. |
| Clean Architecture with four projects | Clear boundaries the compiler and architecture tests enforce; more projects to navigate for a small app. | Split further by bounded context (e.g. pricing, fulfilment) only when a second context appears. |
| Frontend tests are light | The backend tests cover the business rules. The Angular tests cover the API client's 200/201 handling and error mapping. | Component tests for the form and a Playwright E2E test of the resubmit flow. |

## Testing

`dotnet test` runs 153 tests in four projects (Docker is needed for the 23 PostgreSQL tests):

- **Domain.UnitTests (73)**: the `Order` aggregate (totals, history, events, guards that throw on invalid
  lines, status changes and their coded errors, currency conversion, no compounding rounding, locking),
  `OrderStatusPolicy` (every transition), value objects, pricing and rounding, and the submission fingerprint.
  The fingerprint tests include a pinned value, computed independently, that guarantees stored fingerprints keep
  matching, plus each material difference (quantity, price, line order, SKU case, notes, currency, customer).
- **Application.UnitTests (47)**: every use case against an in-memory store (`TestKit`):
  - submit outcomes, case and whitespace matching, formatting-only differences;
  - **the race path** (another request wins between lookup and save) for both identical and different content;
  - status changes, including retry after a concurrency conflict;
  - currency conversion (rates, outages, missing rates, locking);
  - listing (newest first, ties, filters, paging, clamping);
  - the exchange-rate cache and stale fallback;
  - validator boundaries (lengths, 100/101 lines, price and quantity limits).
- **IntegrationTests (26)**:
  - **PersistenceTests:** the real DI container on PostgreSQL. An aggregate saved and reloaded equals what the
    API returned. 20 concurrent submissions store one row. The unit of work reports the unique-index violation.
    Check constraints hold. A stale version is rejected. Racing fulfil and cancel let exactly one win with
    consistent history. Listing, case-insensitive search and literal `%`/`_`.
  - **ApiTests:** the HTTP contract including the stable codes, the exact JSON property names the frontend uses,
    20 simultaneous POSTs and the health check.
  - **FrankfurterExchangeRateProviderTests:** with a stubbed HTTP handler.
- **ArchitectureTests (7)**: the dependency rule for each layer (with a control test, so the rules can't pass
  vacuously) and no public setters in the domain.

`dotnet test --filter "Category!=Database"` runs everything except the PostgreSQL tests, without Docker.
