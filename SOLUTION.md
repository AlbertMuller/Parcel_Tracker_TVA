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

**Concurrency.** Two identical requests arriving at the same moment must not both create an order.
The check and the insert happen as a single atomic repository operation
(`AddIfReferenceIsNew`), which is the in-memory equivalent of a unique index on the normalized
reference. A test fires 50 parallel submissions and asserts exactly one order exists.

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

A small state machine in `OrderStatusRules`:

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
- Concurrent changes use **optimistic concurrency**: orders are immutable records with a `Version`. An update
  succeeds only if the version is unchanged, and otherwise the service re-reads and re-validates. A test races
  *Fulfil* against *Cancel* and checks that exactly one wins and the history stays consistent.

## Architecture

```
Endpoints (HTTP)  →  OrderService (use cases)  →  IOrderRepository
                           │
                    Domain: Order, OrderStatusRules, Pricing, OrderDraft (pure C#, no ASP.NET)
```

- **Minimal APIs** in one project, with folders as layers. For an app this size, separate assemblies would be ceremony.
- The domain logic is pure static functions or immutable records, so the important rules are unit-testable without mocks.
- `TimeProvider` is injected, so tests control the clock (ordering and timestamp tests are deterministic).
- Listing sorts by `createdAt` descending, with the sequential order number as tie-breaker. Orders created
  in the same tick still come out newest first.
- **Customer** has no registry in scope. A customer is identified by normalized email, and `customer.id` is
  derived deterministically from it (`cus_` + hash prefix), so the same email always gets the same id.
- **Angular**: standalone components, signals for state, typed reactive forms, zoneless (the v21 default).
  The UI is deliberately plain CSS with no component library.

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
| In-memory store | Zero setup, but data is lost on restart and there's a single instance only (so the API must not be scaled to more than one container). | EF Core + PostgreSQL/SQL Server behind the same `IOrderRepository`, with a **unique index** on the normalized reference and a rowversion column for concurrency. The service code wouldn't change. |
| Reference is unique forever | Simple, predictable rule for reps. | Probably scope uniqueness per customer or per sales team. |
| Single global lock in the repository | Trivially correct, and fine for an internal tool's load. | Handled by the database. |
| 409 on reference reuse with different data | Safer than silently ignoring changes, but the rep must pick a new reference. | Possibly an explicit "amend order" flow while it's still Pending. |
| No auth | Out of scope for the timebox. | Company SSO (OIDC), recording who submitted and who changed status. |
| Server-side search + status filter, max 200 per page | Enough for the demo. The UI loads one page. | Pagination controls in the UI and date-range filters. |
| Frontend tests are light | The backend tests cover the business rules. The Angular tests cover the API client's 200/201 handling and error mapping. | Component tests for the form and a Playwright E2E test of the resubmit flow. |

## Testing

`dotnet test` runs 90 tests:

- **OrderStatusRulesTests**: every allowed and rejected transition, final states, messages, strict status parsing.
- **CreateOrderValidatorTests**: quantity, price and decimal rules, required fields, error paths, normalization.
- **PricingTests**: line totals, exact decimal sums, rounding mode.
- **OrderServiceTests**: totals on create, duplicate vs conflict, case and whitespace normalization, parallel
  submissions creating exactly one order, newest-first listing (including ties), filtering and paging,
  status flow and history, no-op status changes, racing status changes.
- **CurrencyTests**: conversion maths and rounding, converting back to the base currency, the Pending-only rule,
  missing rates and provider outages, the cache window and stale fallback, and parsing of Frankfurter responses
  (with a stubbed HTTP handler, so there are no network calls).
- **OrdersApiTests**: the HTTP contract end to end with `WebApplicationFactory` (201/200/409/400/404,
  the replay header, problem details, malformed JSON).
