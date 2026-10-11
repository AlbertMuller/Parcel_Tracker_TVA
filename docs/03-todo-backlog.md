# To-Do Backlog (Ticket-Ready)
## Order Intake & Tracking

| Field | Value |
|---|---|
| Document | Implementation backlog |
| Version | 1.1 (signed off) |
| Date | 2026-10-11 |
| Status | Delivery reviewed and signed off; open items listed below |
| Related | `01-business-requirements.md` (BRA), `02-design-document.md` (DD) |

---

## Epics

| Epic | Name | Goal |
|---|---|---|
| E1 | Repository & Scaffolding | Runnable empty backend + frontend in one repo |
| E2 | Domain Model | Business rules in pure C#: totals, references, fingerprint, status flow |
| E3 | Persistence | Database storage with real uniqueness and concurrency (built on PostgreSQL instead of SQLite, see OIT-045) |
| E4 | Backend API | REST endpoints, validation, error format, idempotent submit |
| E5 | Backend Tests | Unit and integration coverage of critical rules |
| E6 | Frontend | Angular submit, list, detail, status flow |
| E7 | Developer Experience & Docs | README, SOLUTION, scripts, CI |
| E8 | Demo | Video walkthrough |
| E9 | Added during delivery | Work done beyond the original backlog (added at sign-off) |

## Sign-off (2026-10-11)

Every ticket below was checked against the code, the test runs and the running Docker stack. Each one now has a
**Status** line, ticked tasks where the work exists, notes where it was built differently, and any **Remaining** work.

### Evidence

| Check | Result |
|---|---|
| Backend tests (`cd backend && dotnet test`) | **102 passed**: 79 unit tests, 23 against real PostgreSQL (Testcontainers) |
| Frontend (`npm test -- --watch=false`, `ng build`) | **23 passed**; build clean, no warnings |
| Docker (`docker compose up --build -d` on an empty volume) | db healthy → migrate exits 0 → api healthy (incl. database check) → web healthy |
| Duplicate prevention under load | 25 simultaneous identical `POST`s: one `201`, twenty-four `200`s, one row |
| Database least privilege | The API's role is denied `CREATE`, `DROP` and `DELETE`; the migrator owns the schema |
| Persistence | Orders, history and conversions survive `docker compose down` / `up` |
| Browser | Scripted Playwright runs of every user story against the Docker stack; no unexpected console errors |

### Result

| Status | Tickets |
|---|---|
| ✅ Done (31; most with recorded changes) | 001–009, 012, 013, 015, 016, 018–020, 022, 023, 026–034, 036–039 |
| 🔁 Superseded (1) | 010, delivered as OIT-045 (PostgreSQL) |
| 🟡 Partly done (8) | 011, 014, 017, 024, 025, 035, 041, 042 |
| ⬜ Not done (2) | 021, 040 |
| ✅ Added and done (9) | 043–051 (epic E9) |

All Must-have business requirements are met: no duplicates (including concurrent submissions), server-side totals,
newest-first listing, detail view, and sensible status changes with helpful messages. Must-priority tickets that are only
partly done are about error codes (014), lookup by reference (017), extra test depth (024, 025) and submission
admin (041, 042). They're listed under [Open items](#open-items-after-sign-off).

### Accepted changes from the plan

These were deliberate and are signed off as built. The requirements and design docs (v1.1) record them too.

| Planned | As built | Why |
|---|---|---|
| SQLite file, `EnsureCreated` | PostgreSQL 17 in Docker, EF Core migrations, separate migrator and app roles | Production-like constraints, concurrency and least privilege; one `docker compose up` |
| Separate Domain project | Superseded by OIT-051: separate Domain, Application, Infrastructure and Api projects | Clean Architecture with enforced boundaries |
| FluentValidation | Hand-written validator reusing the domain value objects | No extra dependency; each rule written once |
| Fixed currency list (ZAR, USD, EUR, GBP) | Any ISO 4217-format code; UI lists the ~30 currencies with live rates | Needed for currency conversion (OIT-046) |
| Same status → `409 order.status.unchanged` | `200`, no change | A double-clicked button shouldn't be an error |
| Client sends `expectedVersion` | Server-side version token with re-read and re-check | Lost updates prevented without client bookkeeping |
| Toasts, not-found page, lazy routes | Inline alerts, redirect to the list, eager routes | Simpler for a three-page app |
| `run.ps1` / `run.sh` | Docker Compose for the full stack | One cross-platform command |
| Limits: notes 2,000, email 254, `pageSize` 20/100 → 400 | Notes 1,000, email 320, `pageSize` 50/200 clamped | As implemented; harmless for the use case |
| .NET 8, Angular 18 | .NET 10 (SDK 10.0.401), Angular 21 | Current LTS and current Angular (OIT-043) |

## Summary

| Key | Title | Epic | Type | Pri | Est (h) | Depends on | Status |
|---|---|---|---|---|---|---|---|
| OIT-001 | Create repository structure and conventions | E1 | Task | M | 0.5 | — | Done* |
| OIT-002 | Scaffold .NET solution and projects | E1 | Task | M | 0.5 | 001 | Done* |
| OIT-003 | Scaffold Angular 18 workspace with API proxy | E1 | Task | M | 0.5 | 001 | Done* |
| OIT-004 | Domain common primitives (Result, Money, Currency) | E2 | Story | M | 0.5 | 002 | Done* |
| OIT-005 | ExternalReference value object and normalisation | E2 | Story | M | 0.5 | 004 | Done* |
| OIT-006 | CustomerInfo and OrderLine domain types | E2 | Story | M | 0.5 | 004 | Done* |
| OIT-007 | Order aggregate with server-side totals | E2 | Story | M | 1.0 | 005, 006 | Done* |
| OIT-008 | Order status state machine and history | E2 | Story | M | 1.0 | 007 | Done* |
| OIT-009 | Order payload fingerprint | E2 | Story | M | 0.75 | 005, 006 | Done* |
| OIT-010 | EF Core DbContext and SQLite mappings | E3 | Story | M | 1.0 | 007, 008 | Superseded |
| OIT-011 | Database initialisation, config and demo seed | E3 | Task | S | 0.5 | 010 | Partly |
| OIT-012 | API contracts (DTOs) and mapping | E4 | Story | M | 0.75 | 007 | Done* |
| OIT-013 | Request validation with friendly messages | E4 | Story | M | 1.0 | 012 | Done* |
| OIT-014 | Global error handling and ProblemDetails codes | E4 | Story | M | 0.75 | 002 | Partly |
| OIT-015 | OrderService submit with idempotency and race handling | E4 | Story | M | 1.5 | 009, 010, 012 | Done* |
| OIT-016 | POST /api/orders endpoint | E4 | Story | M | 0.5 | 013, 014, 015 | Done* |
| OIT-017 | GET order by id and by reference | E4 | Story | M | 0.5 | 012, 010 | Partly |
| OIT-018 | GET orders list (newest first, paging, filter, search) | E4 | Story | M | 1.0 | 012, 010 | Done* |
| OIT-019 | PATCH order status with optimistic concurrency | E4 | Story | M | 1.0 | 008, 010, 014 | Done* |
| OIT-020 | API plumbing: JSON, CORS, OpenAPI, health, logging, meta | E4 | Task | M | 0.75 | 002 | Done* |
| OIT-021 | HTTP request samples file | E4 | Task | S | 0.25 | 016–019 | Not done |
| OIT-022 | Tests: totals and domain guards | E5 | Test | M | 0.75 | 007 | Done* |
| OIT-023 | Tests: status transition matrix | E5 | Test | M | 0.5 | 008 | Done* |
| OIT-024 | Tests: reference normalisation and fingerprint | E5 | Test | M | 0.5 | 005, 009 | Partly |
| OIT-025 | Tests: request validators | E5 | Test | M | 0.75 | 013 | Partly |
| OIT-026 | Tests: OrderService (idempotency, concurrency, listing, status) | E5 | Test | M | 1.5 | 015, 018, 019 | Done* |
| OIT-027 | Tests: API integration (WebApplicationFactory) | E5 | Test | S | 1.0 | 016–019 | Done* |
| OIT-028 | Frontend core: models, API client, error interceptor, toasts | E6 | Story | M | 1.0 | 003, 016 | Done* |
| OIT-029 | App shell, routing and shared UI components | E6 | Story | M | 0.75 | 003 | Done* |
| OIT-030 | Order list page (newest first, paging) | E6 | Story | M | 1.0 | 028, 029, 018 | Done* |
| OIT-031 | New order form with line items editor | E6 | Story | M | 1.5 | 028, 029 | Done* |
| OIT-032 | Submit outcome handling (created / replay / conflict / errors) | E6 | Story | M | 1.0 | 031, 016 | Done* |
| OIT-033 | Order detail page | E6 | Story | M | 1.0 | 028, 029, 017 | Done* |
| OIT-034 | Status change actions in UI | E6 | Story | C | 1.0 | 033, 019 | Done* |
| OIT-035 | List filtering and search in UI | E6 | Story | C | 0.75 | 030 | Partly |
| OIT-036 | Frontend unit tests | E6 | Test | C | 1.0 | 031, 034 | Done* |
| OIT-037 | README.md | E7 | Docs | M | 0.5 | 016, 030 | Done* |
| OIT-038 | SOLUTION.md | E7 | Docs | M | 1.0 | most | Done* |
| OIT-039 | Run scripts (and optional Docker Compose) | E7 | Task | C | 0.5 | 037 | Done* |
| OIT-040 | GitHub Actions CI | E7 | Task | C | 0.5 | 026 | Not done |
| OIT-041 | End-to-end manual QA pass | E7 | Test | M | 0.5 | all M | Partly |
| OIT-042 | Demo script and video recording | E8 | Task | M | 1.0 | 041 | Partly |
| OIT-043 | Upgrade to .NET 10 and Angular 21 | E9 | Task | S | 0.5 | 002, 003 | Done |
| OIT-044 | Docker Compose for API and UI | E9 | Task | S | 1.0 | 039 | Done |
| OIT-045 | PostgreSQL persistence with migrations and Docker init | E9 | Story | M | 3.0 | 015, 044 | Done |
| OIT-046 | Live exchange rates and currency conversion | E9 | Story | S | 2.5 | 008, 033 | Done |
| OIT-047 | "Show totals in" on the order list | E9 | Story | C | 0.5 | 046, 030 | Done |
| OIT-048 | Searchable currency picker | E9 | Story | C | 1.0 | 046 | Done |
| OIT-049 | UI redesign | E9 | Story | C | 2.0 | 029, 030 | Done |
| OIT-050 | Database tests with Testcontainers | E9 | Test | M | 1.5 | 045 | Done |
| OIT-051 | Restructure the backend as DDD with Clean Architecture | E9 | Story | S | 6.0 | 045 | Done |

*Done\* = done, with differences from the plan recorded on the ticket.*

**Totals:** Must ≈ 25 h · Should ≈ 1.75 h · Could ≈ 4.75 h.

> The brief targets about 4 hours. The estimates above describe a polished senior-level submission. For a strict 4-hour cut, see **Appendix A**.

### Suggested build order

1. **Foundation:** 001 → 002 → 003 → 020 → 014
2. **Domain:** 004 → 005 → 006 → 007 → 008 → 009 (write 022–024 alongside)
3. **Persistence:** 010 → 011
4. **API:** 012 → 013 → 015 → 016 → 017 → 018 → 019 → 021 (write 025–027 alongside)
5. **Frontend:** 028 → 029 → 030 → 031 → 032 → 033 → 034 → 035
6. **Wrap-up:** 037 → 038 → 041 → 042 (039, 040, 036 if time)

---

# E1 — Repository & Scaffolding

### OIT-001 — Create repository structure and conventions

- **Epic:** E1 · **Type:** Task · **Priority:** Must · **Estimate:** 0.5 h
- **Depends on:** —
- **Refs:** DD §2.3, BRA §15
- **Status:** ✅ Done (with changes)

**Description**
Create the single GitHub repository with the agreed folder layout, shared conventions, and the planning documents.

**Tasks**
- [x] Create GitHub repo `order-intake` (public or private with reviewer access). *(as built: `AlbertMuller/Parcel_Tracker_TVA`)*
- [x] Add folders: `backend/`, `frontend/`, `docs/`.
- [x] Add `.gitignore` covering .NET (`bin/`, `obj/`, `*.user`, `.vs/`), Node/Angular (`node_modules/`, `dist/`, `.angular/`), IDEs (`.idea/`, `.vscode/*` except shared settings), and `*.db`, `*.db-shm`, `*.db-wal`. *(SQLite patterns not needed: data lives in a PostgreSQL Docker volume)*
- [ ] Add root `.editorconfig` (C# conventions, 4-space C#, 2-space TS/JSON/HTML/SCSS, LF line endings, final newline). *(only `frontend/.editorconfig` (from the Angular CLI))*
- [x] Add `.gitattributes` (`* text=auto eol=lf`).
- [x] Copy BRA, Design Document and this backlog into `docs/`. *(in `docs/`; not yet committed (open item))*
- [x] Add placeholder `README.md` and `SOLUTION.md` at root.
- [x] Initial commit on `main`.

**Acceptance Criteria**
- Repo cloned fresh shows the layout from DD §2.3.
- `git status` is clean after building backend and frontend (ignores work).
- Docs are visible under `docs/`.

**As built:** Layout differs from DD §2.3: projects are named `OrderTracker.*`, the Angular app is in `frontend/` (not `frontend/order-intake-web/`), and the Postgres init script is in `docker/postgres/init/`.

---

### OIT-002 — Scaffold .NET solution and projects

- **Epic:** E1 · **Type:** Task · **Priority:** Must · **Estimate:** 0.5 h
- **Depends on:** OIT-001
- **Refs:** DD §2.2, §2.3, §8
- **Status:** ✅ Done (with changes)

**Description**
Create the backend solution with Domain, Api and two test projects, with shared build settings and packages.

**Tasks**
- [x] `dotnet new sln -n OrderIntake` in `backend/`. *(as built: classic `OrderTracker.sln`, which older SDKs can open)*
- [ ] `dotnet new classlib -n OrderIntake.Domain -o src/OrderIntake.Domain -f net8.0`. *(replaced by a `Domain/` folder in the API project)*
- [x] `dotnet new web -n OrderIntake.Api -o src/OrderIntake.Api -f net8.0` (minimal API template). *(as built: `OrderTracker.Api`, now `net10.0`)*
- [ ] `dotnet new xunit -n OrderIntake.Domain.Tests -o tests/OrderIntake.Domain.Tests`. *(one test project instead)*
- [x] `dotnet new xunit -n OrderIntake.Api.Tests -o tests/OrderIntake.Api.Tests`. *(as built: `OrderTracker.Tests`)*
- [x] Add all projects to the solution; add project references (Api → Domain; Domain.Tests → Domain; Api.Tests → Api, Domain). *(Tests → Api; there is no separate Domain project)*
- [ ] Add `backend/Directory.Build.props`: `Nullable=enable`, `ImplicitUsings=enable`, `TreatWarningsAsErrors=true`, `LangVersion=latest`, `AnalysisLevel=latest`. *(not added; the build has zero warnings anyway)*
- [x] Add packages: *(as built: Npgsql EF Core, EFCore.NamingConventions, Swashbuckle, Mvc.Testing, Testcontainers.PostgreSql. No FluentValidation, Shouldly or FakeTimeProvider: a hand-written validator and a small `ManualTimeProvider` are used)*
  - Api: `Microsoft.EntityFrameworkCore.Sqlite`, `FluentValidation` (+ `FluentValidation.DependencyInjectionExtensions`), `Swashbuckle.AspNetCore` (or `Microsoft.AspNetCore.OpenApi`).
  - Tests: `Shouldly`, `Microsoft.Extensions.TimeProvider.Testing`, `Microsoft.AspNetCore.Mvc.Testing` (Api.Tests), `Microsoft.EntityFrameworkCore.Sqlite`.
- [ ] Optional: `Directory.Packages.props` for central package management.
- [x] Fix ports in `launchSettings.json`: `http://localhost:5080`.
- [x] Remove template "Hello World" endpoint.
- [x] Add `public partial class Program;` at end of `Program.cs` for `WebApplicationFactory`.

**Acceptance Criteria**
- `dotnet build` in `backend/` succeeds with zero warnings.
- `dotnet test` runs (template tests may be deleted; zero tests is acceptable at this stage).
- `dotnet run --project src/OrderIntake.Api` starts on port 5080.

**As built:** One API project with a `Domain/` folder that has no ASP.NET or EF references, and one test project. The SDK is pinned in `backend/global.json` (10.0.401).

---

### OIT-003 — Scaffold Angular 18 workspace with API proxy

- **Epic:** E1 · **Type:** Task · **Priority:** Must · **Estimate:** 0.5 h
- **Depends on:** OIT-001
- **Refs:** DD §9.1, §11
- **Status:** ✅ Done (with changes)

**Description**
Create the Angular 18 standalone application and configure it to call the backend through a dev proxy.

**Tasks**
- [x] `npx @angular/cli@18 new order-intake-web --directory frontend/order-intake-web --standalone --routing --style=scss --ssr=false`. *(as built: Angular 21 in `frontend/`, plain CSS, zoneless, Vitest)*
- [x] Confirm TypeScript ≥ 5.4 and `strict: true` in `tsconfig.json`. *(TypeScript 5.9, strict)*
- [x] Add `proxy.conf.json`: `/api` → `http://localhost:5080`, `secure: false`, `changeOrigin: true`.
- [x] Reference proxy in `angular.json` `serve.options.proxyConfig`.
- [ ] Add `environment` value `apiBaseUrl: '/api'`. *(the API services use `/api` directly)*
- [ ] Set `.nvmrc` to Node 20 (or 22).
- [ ] Optional: add ESLint via `ng add @angular-eslint/schematics`.
- [x] Remove template boilerplate content from `AppComponent`.

**Acceptance Criteria**
- `npm ci && npm start` serves on `http://localhost:4200`.
- A request from the browser to `/api/health` (once OIT-020 is done) is proxied to the backend.
- `ng build` succeeds.

**As built:** Upgraded to Angular 21 (OIT-043).

---

# E2 — Domain Model

### OIT-004 — Domain common primitives (Result, Money, Currency)

- **Epic:** E2 · **Type:** Story · **Priority:** Must · **Estimate:** 0.5 h
- **Depends on:** OIT-002
- **Refs:** DD §3.2, §3.4
- **Status:** ✅ Done (with changes)

**Description**
Add shared building blocks used across the domain: a `Result` type for expected business failures, a money rounding helper, the supported currency list, and a domain exception for broken invariants.

**Tasks**
- [ ] `Common/Error.cs`: `record Error(string Code, string Message, IReadOnlyDictionary<string, object?>? Metadata = null)`.
- [ ] `Common/Result.cs` and `Result<T>`: `IsSuccess`, `Value`, `Error`, static `Success`/`Failure`. *(replaced by outcome records: `CreateOrderResult`, `ChangeStatusResult`, `ChangeCurrencyResult`)*
- [x] `Common/Money.cs`: `static decimal Round(decimal amount) => Math.Round(amount, 2, MidpointRounding.AwayFromZero)`; `static bool HasAtMostTwoDecimals(decimal value)`. *(as built: `Domain/Pricing.cs` (`Round`, `LineTotal`, `Subtotal`, `Total`); the 2-decimal check is in the validator)*
- [ ] `Common/Currency.cs`: `IReadOnlySet<string> Supported = { "ZAR", "USD", "EUR", "GBP" }` (case-insensitive comparer); `static bool IsSupported(string code)`; `static string Normalize(string code)` → upper-invariant. *(replaced: any ISO 4217-format code is accepted and the UI offers the live list (OIT-046))*
- [ ] `Common/DomainException.cs` for invariant violations. *(broken invariants throw `InvalidOperationException`)*

**Acceptance Criteria**
- Domain project has no package references.
- `Money.Round(1.005m) == 1.01m`, `Money.Round(2.345m) == 2.35m`.
- `Currency.IsSupported("zar") == true`, `Currency.IsSupported("XYZ") == false`.

---

### OIT-005 — ExternalReference value object and normalisation

- **Epic:** E2 · **Type:** Story · **Priority:** Must · **Estimate:** 0.5 h
- **Depends on:** OIT-004
- **Refs:** BRA FR-10, FR-14, VR-01 · DD §4.1
- **Status:** ✅ Done (with changes)

**Description**
Model the client-provided reference as a value object that knows its display value and its normalised (uniqueness) value.

**Tasks**
- [ ] `Orders/ExternalReference.cs` as a `sealed record` with `Value` (trimmed, display) and `Normalized` (trimmed, `ToUpperInvariant()`). *(no value object; display value and key are stored separately (`external_reference`, `normalized_reference`))*
- [x] `static Result<ExternalReference> Create(string? raw)` validating: not null/whitespace, trimmed length 1–64, regex `^[A-Za-z0-9\-_/.]+$`. *(partly: required and 1–64 characters are enforced in `CreateOrderValidator`; the character whitelist is not (only control characters are rejected))*
- [x] Equality based on `Normalized` only. *(as built: `ExternalReferences.Normalize` (trim + upper-case) is the key, backed by a unique index)*
- [x] Constants: `MaxLength = 64`, `AllowedPattern`. *(as built: `CreateOrderValidator.MaxReferenceLength`)*

**Acceptance Criteria**
- `Create(" po-1001 ")` → `Value == "po-1001"`, `Normalized == "PO-1001"`.
- `Create("PO-1001")` equals `Create("po-1001")`.
- Empty, 65-char, and `PO 1001` (space) inputs fail with code `order.reference.invalid` and a readable message.

**As built:** `PO 1001` (with a space) is currently accepted, unlike VR-01.

**Remaining:** Decide whether to enforce the VR-01 character whitelist or relax VR-01.

---

### OIT-006 — CustomerInfo and OrderLine domain types

- **Epic:** E2 · **Type:** Story · **Priority:** Must · **Estimate:** 0.5 h
- **Depends on:** OIT-004
- **Refs:** BRA VR-02, VR-03, VR-06–VR-08 · DD §3.1, §3.2
- **Status:** ✅ Done (with changes)

**Description**
Create the customer snapshot value object and the order line entity, enforcing invariants on construction.

**Tasks**
- [x] `CustomerInfo` record: `Name` (trimmed), `Email` (trimmed, lower-invariant). Factory guards non-empty values. *(as built: `Customer(Id, Email, Name)`; `Id` is derived from the email (`cus_` + hash) because there is no customer registry)*
- [x] `NewOrderLine` input record: `Sku`, `Name`, `Quantity`, `UnitPrice`. *(as built: `LineItemDraft`)*
- [x] `OrderLine` entity: `LineNumber`, `Sku` (trimmed), `Name` (trimmed), `Quantity`, `UnitPrice`, `LineTotal`. *(as built: immutable `LineItem` record; line numbers exist only in the database (`order_lines.line_no`))*
- [x] Internal factory `OrderLine.Create(int lineNumber, NewOrderLine input)`: *(rules are enforced by the validator and by database check constraints; totals by `Pricing.LineTotal`)*
  - Throws `DomainException` if `Quantity < 1`, `UnitPrice < 0`, or price has more than 2 decimals.
  - Computes `LineTotal = Money.Round(Quantity * UnitPrice)`.
- [ ] Private parameterless constructors for EF Core. *(not needed: EF maps separate persistence entities (OIT-045))*

**Acceptance Criteria**
- Quantity 3 × 19.99 → `LineTotal == 59.97`.
- Quantity 0 or −1 throws; price −0.01 throws; price 0 allowed.
- Email `" Thandi@Example.CO.ZA "` stored as `thandi@example.co.za`.

**As built:** Invalid lines can't reach the domain: the validator rejects them, and the database rejects quantity < 1 or negative prices with check constraints.

---

### OIT-007 — Order aggregate with server-side totals

- **Epic:** E2 · **Type:** Story · **Priority:** Must · **Estimate:** 1.0 h
- **Depends on:** OIT-005, OIT-006
- **Refs:** BRA FR-03, FR-05, FR-20–FR-23 · DD §3.1, §3.3
- **Status:** ✅ Done (with changes)

**Description**
Implement the `Order` aggregate root and its creation factory. Totals are always computed by the aggregate; there is no way to set them from outside.

**Tasks**
- [x] `Orders/Order.cs` with properties per DD §3.1 (private setters). *(as built: immutable `Domain/Order.cs` record; every change returns a new snapshot)*
- [x] `static Order Create(ExternalReference reference, CustomerInfo customer, string currency, string? notes, IReadOnlyList<NewOrderLine> lines, string fingerprint, TimeProvider clock)`. *(as built: `Order.Create(OrderDraft, Guid, DateTimeOffset)`)*
- [x] Guards: ≥ 1 line, ≤ 100 lines, supported currency, notes ≤ 2000. *(in `CreateOrderValidator`: 1–100 lines, ISO currency format, notes ≤ 1,000 characters)*
- [x] Assign `Id = Guid.CreateVersion7()` (on .NET 8 add a small `GuidV7` helper). *(done in `OrderService` (built into .NET 10))*
- [x] Build lines with 1-based `LineNumber` preserving input order.
- [x] `Subtotal = Σ LineTotal`; `Total = Subtotal`.
- [x] `Status = Pending`; `CreatedAtUtc = UpdatedAtUtc = clock.GetUtcNow()`; `Version = 1`.
- [x] Expose `Lines` as `IReadOnlyList<OrderLine>` backed by private list. *(as built: `IReadOnlyList<LineItem> LineItems`)*
- [x] Notes: trim; whitespace-only becomes `null`.

**Acceptance Criteria**
- Creating with lines (3 × 19.99) and (1 × 250.00) yields `Subtotal == Total == 309.97`.
- No public setter exists for `Subtotal`, `Total`, `LineTotal`, `Status`.
- Created order has status `Pending`, timestamps from the supplied `TimeProvider`, and `Version == 1`.
- Zero lines throws `DomainException`.

**As built:** Also stores `BaseCurrency` and `BaseLineItems` for currency conversion (OIT-046).

---

### OIT-008 — Order status state machine and history

- **Epic:** E2 · **Type:** Story · **Priority:** Must · **Estimate:** 1.0 h
- **Depends on:** OIT-007
- **Refs:** BRA FR-40–FR-47, §9 · DD §3.4
- **Status:** ✅ Done (with changes)

**Description**
Implement the allowed transitions table and `Order.ChangeStatus`, returning helpful errors for disallowed changes and recording history.

**Tasks**
- [x] `OrderStatus` enum: `Pending`, `Confirmed`, `Fulfilled`, `Cancelled`.
- [x] `OrderStatusTransitions` static class with the transition table, `AllowedFrom(status)`, `IsFinal(status)`, `CanTransition(from, to)`. *(as built: `OrderStatusRules` (`AllowedTransitionsFrom`, `CanTransition`, `IsFinal`, `ExplainRejection`, strict `TryParse`))*
- [x] `OrderStatusChange` entity: `From` (nullable), `To`, `Reason`, `ChangedAtUtc`. *(as built: `StatusChange(From, To, At)`, no reason)*
- [x] `Order.Create` adds initial history entry (`null → Pending`).
- [x] `Result Order.ChangeStatus(OrderStatus target, string? reason, TimeProvider clock)`: *(as built: `Order.TransitionTo` + `OrderService.ChangeStatusAsync`)*
  - Same status → `order.status.unchanged`: "Order is already {Status}." *(changed: requesting the current status is a harmless no-op (200), so a double-clicked button isn't an error)*
  - Current is final → `order.status.final`: "Order is {Status}, which is final. No further status changes are allowed."
  - Not allowed → `order.status.invalid_transition`: "Cannot change status from {From} to {To}. Allowed next statuses: {list}."
  - Error metadata includes `currentStatus`, `requestedStatus`, `allowedTransitions`. *(`currentStatus` and `allowedTransitions` are returned; `requestedStatus` is not)*
  - Success → set status, append history, set `UpdatedAtUtc`, `Version++`.
- [ ] `Reason` trimmed, max 500 chars (guard). *(no cancellation reason (FR-46))*
- [x] Expose `AllowedTransitions` read-only property on `Order` for DTO mapping.

**Acceptance Criteria**
- All four allowed transitions succeed and append one history row each.
- All other 12 combinations fail with the correct code and message (verified in OIT-023).
- Failed transitions do not change status, timestamps, version or history.

**As built:** Messages are specific, e.g. "This order must be confirmed before it can be fulfilled. Allowed next statuses: Confirmed, Cancelled." No `order.status.*` codes are returned yet (see OIT-014).

**Remaining:** Optional cancellation reason (FR-46).

---

### OIT-009 — Order payload fingerprint

- **Epic:** E2 · **Type:** Story · **Priority:** Must · **Estimate:** 0.75 h
- **Depends on:** OIT-005, OIT-006
- **Refs:** BRA FR-11, FR-12 · DD §4.2
- **Status:** ✅ Done (with changes)

**Description**
Compute a deterministic SHA-256 fingerprint of the business content of a submission, used to tell a true resubmission from a conflicting reuse of the same reference.

**Tasks**
- [x] `Orders/OrderFingerprint.cs` with `static string Compute(ExternalReference reference, CustomerInfo customer, string currency, string? notes, IReadOnlyList<NewOrderLine> lines)`. *(as built: `OrderDraft.Fingerprint()`, stored as `orders.submission_fingerprint`)*
- [x] Canonical form (invariant culture): *(SKU is trimmed but not upper-cased; prices are formatted without trailing zeros, so `19.9` and `19.90` match)*
  - `reference.Normalized`
  - customer name trimmed; email trimmed lower
  - currency upper
  - notes trimmed, null/whitespace → empty string
  - each line in order: SKU trimmed upper, name trimmed, quantity, unit price formatted `"0.00"`
- [x] Serialise with a fixed separator scheme (or `System.Text.Json` of an anonymous canonical record with fixed property order) and hash with `SHA256.HashData`; return lowercase hex. *(SHA-256, upper-case hex)*
- [x] XML doc comment explaining which fields are included and that line order is significant.

**Acceptance Criteria**
- Same input twice → identical hash.
- Differences only in reference casing/whitespace, email casing, currency casing, notes whitespace, or `19.9` vs `19.90` → identical hash.
- Different quantity, price, SKU, line count, notes text, or line order → different hash.

---

# E3 — Persistence

### OIT-010 — EF Core DbContext and SQLite mappings

- **Epic:** E3 · **Type:** Story · **Priority:** Must · **Estimate:** 1.0 h
- **Depends on:** OIT-007, OIT-008
- **Refs:** DD §6
- **Status:** 🔁 Superseded by OIT-045 (PostgreSQL); intent delivered

**Description**
Map the domain model to SQLite with EF Core, including the unique reference index, owned types, value converters and the concurrency token.

**Tasks**
- [x] `Persistence/OrderIntakeDbContext.cs` with `DbSet<Order> Orders`. *(as built: `Infrastructure/Persistence/OrdersDbContext.cs`)*
- [x] `Persistence/Configurations/OrderConfiguration.cs` (`IEntityTypeConfiguration<Order>`): *(as built: `OnModelCreating` mapping separate persistence entities: unique index `ux_orders_normalized_reference`, `numeric` money, `timestamptz` (no tick converter needed), string status, `version` concurrency token, tables `order_lines`, `order_status_changes`, `order_currency_changes`)*
  - Key `Id`.
  - `ExternalReference` required, max 64; `NormalizedReference` required, max 64, **unique index** `IX_Orders_NormalizedReference`.
  - `Customer` as owned type → columns `CustomerName`, `CustomerEmail`.
  - `Currency` max 3; `Notes` max 2000; `PayloadFingerprint` max 64.
  - `Status` stored as string (`HasConversion<string>()`).
  - `Subtotal`, `Total` decimals (default TEXT on SQLite to keep precision).
  - `CreatedAtUtc`, `UpdatedAtUtc` → `long` UTC ticks via `ValueConverter<DateTimeOffset, long>`.
  - Index on `CreatedAtUtc` (for sort).
  - `Version` as `IsConcurrencyToken()`.
  - `Lines` as `OwnsMany` → table `OrderLines` (key: OrderId + LineNumber).
  - `StatusHistory` as `OwnsMany` → table `OrderStatusChanges` (shadow `Id` key).
  - Backing field access for private collections.
- [x] Register `DbContext` in DI with connection string `ConnectionStrings:OrderIntake`. *(connection string `ConnectionStrings:Orders`)*

**Acceptance Criteria**
- `EnsureCreated()` produces tables `Orders`, `OrderLines`, `OrderStatusChanges` with a unique index on `NormalizedReference`.
- Round-trip test: save an order, reload in a new context, all fields (including decimals to 2 dp and timestamps) equal.
- Inserting a second order with the same `NormalizedReference` throws `DbUpdateException` (inner `SqliteException` with `SqliteErrorCode == 19`).
- Ordering by `CreatedAtUtc` translates to SQL (no client-side evaluation warning).

**As built:** The acceptance criteria are covered against real PostgreSQL in `PostgresOrderRepositoryTests`: every field survives a save and reload, the database rejects a duplicate reference (`23505`), and newest-first ordering runs in SQL.

---

### OIT-011 — Database initialisation, config and demo seed

- **Epic:** E3 · **Type:** Task · **Priority:** Should · **Estimate:** 0.5 h
- **Depends on:** OIT-010
- **Refs:** DD §6, §11
- **Status:** 🟡 Partly done

**Description**
Create the database automatically on startup and optionally seed demo data so a reviewer sees a populated list immediately.

**Tasks**
- [x] Connection string in `appsettings.json`: `Data Source=data/orders.db`; ensure `data/` directory is created on startup. *(as built: PostgreSQL connection strings; dev values in `appsettings.Development.json`, Docker values in `docker-compose.yml`)*
- [x] On startup: `EnsureCreated()` inside a scope. *(as built: EF Core migrations, applied on startup in Development and by the one-shot `migrate` service in Docker)*
- [ ] `Seed:Enabled` setting (true in `appsettings.Development.json`): if DB empty, insert ~6 orders spread over time and statuses (Pending, Confirmed, Fulfilled, Cancelled), using the domain factory and `ChangeStatus` (no bypassing rules).
- [ ] Support `--reset-db` command-line arg (or `Database:ResetOnStartup` setting) to delete and recreate. *(`docker compose down -v` resets the database)*
- [x] Log DB path at startup. *(applied migrations are logged)*

**Acceptance Criteria**
- Fresh clone + `dotnet run` creates `data/orders.db` and seeded orders appear in `GET /api/orders`.
- Restarting keeps data; `--reset-db` clears and reseeds.
- Seeding is idempotent (does not duplicate on restart).

**As built:** Data survives restarts (Docker volume `pgdata`).

**Remaining:** Optional demo seed data.

---

# E4 — Backend API

### OIT-012 — API contracts (DTOs) and mapping

- **Epic:** E4 · **Type:** Story · **Priority:** Must · **Estimate:** 0.75 h
- **Depends on:** OIT-007
- **Refs:** DD §5.2, §5.4, §5.5
- **Status:** ✅ Done (with changes)

**Description**
Define request/response records that form the public API, separate from domain types, plus mapping helpers.

**Tasks**
- [x] Requests: `SubmitOrderRequest`, `CustomerRequest`, `OrderLineRequest` (`Quantity` as `int`, `UnitPrice` as `decimal`), `ChangeStatusRequest` (`Status`, `Reason?`, `ExpectedVersion?`), `OrderListQuery` (`Status?`, `Search?`, `Page`, `PageSize`). *(as built: `CreateOrderRequest` with `lineItems`; `quantity`/`unitPrice` are nullable decimals so the validator can give specific messages; `ChangeStatusRequest` has only `status`; list filters are plain query parameters; plus `ChangeCurrencyRequest`)*
- [x] Responses: `OrderResponse` (full, incl. `allowedTransitions`, `statusHistory`, `version`), `OrderLineResponse`, `StatusChangeResponse`, `OrderSummaryResponse`, `PagedResponse<T>`. *(no `version` field; adds `number`, `baseCurrency`, `canChangeCurrency`, `currencyHistory`)*
- [x] `OrderMappings` static extension methods: `ToResponse(this Order)`, `ToSummary(this Order)` (or EF projection expression for summaries). *(as built: `OrderResponse.From`, `OrderSummaryResponse.From`)*
- [x] Note in XML docs that request totals are not accepted. *(the request contract has no total fields at all)*

**Acceptance Criteria**
- No domain types are exposed directly from endpoints.
- JSON shape matches DD §5.2 / §5.4 examples (verified in OIT-027).
- Enum values serialise as strings.

---

### OIT-013 — Request validation with friendly messages

- **Epic:** E4 · **Type:** Story · **Priority:** Must · **Estimate:** 1.0 h
- **Depends on:** OIT-012
- **Refs:** BRA §7.6 · DD §5.7
- **Status:** ✅ Done (with changes)

**Description**
Validate all incoming requests with FluentValidation and return every problem at once as `ValidationProblemDetails`, with plain-language messages keyed by JSON path.

**Tasks**
- [x] `SubmitOrderRequestValidator` with rules from DD §5.7 (child validators for customer and lines; `RuleForEach(x => x.Lines)`). *(as built: hand-written `CreateOrderValidator`; returns every error at once)*
- [x] `ChangeStatusRequestValidator`: status required and a defined enum value; reason ≤ 500; expectedVersion ≥ 1 if present. *(status parsed strictly (`OrderStatusRules.TryParse`); unknown values → 400)*
- [x] `OrderListQueryValidator`: page ≥ 1; pageSize 1–100; search ≤ 100. *(unknown `status` → 400; `page`/`pageSize` are clamped (1–200) instead of rejected)*
- [ ] Generic `ValidationFilter<T>` endpoint filter returning `Results.ValidationProblem(errors)`. *(not needed: validation runs in the service)*
- [x] Property names camelCased and indexed (`lines[1].quantity`) via `ValidatorOptions.Global.PropertyNameResolver` / `DisplayNameResolver`. *(e.g. `lineItems[1].quantity`)*
- [x] Handle JSON binding failures (e.g. `"quantity": 2.5`, `"quantity": "abc"`, malformed JSON): map `BadHttpRequestException` / `JsonException` to a 400 ProblemDetails with a friendly message such as "lines[0].quantity must be a whole number." *(partly: malformed JSON → 400 and `2.5` → "Quantity must be a positive whole number."; a non-numeric value such as `"abc"` still returns the serializer's message)*
- [x] Messages written for reps, e.g. "Quantity must be a whole number of at least 1.", "Unit price can't be negative.", "Unit price can have at most 2 decimal places.", "Currency must be one of: ZAR, USD, EUR, GBP." *(currency accepts any ISO 4217-format code)*

**Acceptance Criteria**
- A request with several problems returns one 400 listing all of them.
- Field keys use camelCase JSON paths.
- Non-integer quantity returns 400 with a friendly message (no raw System.Text.Json exception text).
- Validators are unit-tested in OIT-025.

**Remaining:** Friendly message for non-numeric JSON values.

---

### OIT-014 — Global error handling and ProblemDetails codes

- **Epic:** E4 · **Type:** Story · **Priority:** Must · **Estimate:** 0.75 h
- **Depends on:** OIT-002
- **Refs:** BRA NFR-05 · DD §5.6
- **Status:** 🟡 Partly done

**Description**
Give every error response one consistent ProblemDetails shape with a stable `code`, and ensure unhandled exceptions never leak internals.

**Tasks**
- [x] `builder.Services.AddProblemDetails(o => o.CustomizeProblemDetails = ...)` adding `traceId`. *(`traceId` is added by the default problem-details writer)*
- [ ] `Errors/ErrorCodes.cs` constants (table in DD §5.6).
- [ ] `Errors/ProblemFactory.cs`: build ProblemDetails from domain `Error` (title, status, detail, `code`, `type` URI, metadata as extensions).
- [x] `Errors/GlobalExceptionHandler : IExceptionHandler` → 500 `internal_error`, logs exception, generic detail. *(as built: `UseExceptionHandler` + `AddProblemDetails` return a 500 ProblemDetails without a stack trace outside Development; `BadRequestExceptionHandler` turns malformed requests into 400s)*
- [x] `app.UseExceptionHandler()`; `app.UseStatusCodePages()` for empty 404/405.
- [x] Map domain error codes to HTTP status: not_found → 404; conflicts/status → 409.

**Acceptance Criteria**
- Throwing in an endpoint (temporary test) returns 500 ProblemDetails with `code: internal_error`, no stack trace.
- Unknown route returns ProblemDetails 404.
- Every error body contains `type`, `title`, `status`, `detail`, `code`, `traceId`.

**As built:** Every error is RFC 9457 ProblemDetails with `type`, `title`, `status`, `detail` and `traceId`.

**Remaining:** A stable machine-readable `code` on every error (NFR-05, DD §5.6).

---

### OIT-015 — OrderService submit with idempotency and race handling

- **Epic:** E4 · **Type:** Story · **Priority:** Must · **Estimate:** 1.5 h
- **Depends on:** OIT-009, OIT-010, OIT-012
- **Refs:** BRA FR-10–FR-15, FR-22 · DD §4.3, §4.4, §7
- **Status:** ✅ Done (with changes)

**Description**
Implement the submit use case: create a new order, return the existing one on a true resubmission, or reject a conflicting reuse. Must be safe under concurrent submissions.

**Tasks**
- [x] `Application/SubmitOrderResult.cs`: discriminated outcome `Created(Order)`, `Replayed(Order)`, `Conflict(Guid existingOrderId, string reference)`. *(as built: `CreateOrderResult` with `Created` / `AlreadySubmitted` / `ReferenceConflict` / `Invalid`)*
- [x] `Application/OrderService.SubmitAsync(SubmitOrderRequest, CancellationToken)`: *(as built: `OrderService.CreateAsync`; lookup and insert in `PostgresOrderRepository.AddIfReferenceIsNewAsync`)*
  1. Build `ExternalReference`, `CustomerInfo`, `NewOrderLine` list from request.
  2. Compute fingerprint.
  3. Query existing by `NormalizedReference` (include lines/history).
  4. If found: fingerprint equal → `Replayed`; else → `Conflict`.
  5. If not found: `Order.Create(...)`, `Add`, `SaveChangesAsync`.
  6. Catch `DbUpdateException` where inner is `SqliteException { SqliteErrorCode: 19 }` (helper `IsUniqueViolation`): detach the entity (`ChangeTracker.Clear()`), re-query, then go to step 4. *(as built: PostgreSQL `23505` on `ux_orders_normalized_reference` only; other database errors propagate)*
- [ ] Inject `TimeProvider` and `ILogger<OrderService>`. *(`TimeProvider` yes; no logger yet)*
- [ ] Log: created (info, id + reference), replayed (info), conflict (warning), race resolved (info).
- [x] Register `OrderService` as scoped.

**Acceptance Criteria**
- New reference → `Created`, one row.
- Same reference + same content → `Replayed` returning the original order (same id, original timestamps), still one row.
- Same reference + different content → `Conflict` with the existing id; original row unchanged.
- Replay after the order was Confirmed/Cancelled still returns `Replayed`.
- Parallel identical submissions produce exactly one row (verified in OIT-026).
- Unique-violation detection does not swallow other DB errors.

**As built:** Verified: 50 parallel submits (service), 20 parallel inserts on PostgreSQL, 20 parallel HTTP POSTs, and 25 simultaneous `curl` requests against the Docker stack each produced exactly one order.

**Remaining:** Log replays, conflicts and race resolutions (NFR-09).

---

### OIT-016 — POST /api/orders endpoint

- **Epic:** E4 · **Type:** Story · **Priority:** Must · **Estimate:** 0.5 h
- **Depends on:** OIT-013, OIT-014, OIT-015
- **Refs:** BRA US-01, US-02 · DD §5.1–§5.3
- **Status:** ✅ Done (with changes)

**Description**
Expose order submission over HTTP and map service outcomes to the agreed status codes and headers.

**Tasks**
- [x] `Endpoints/OrderEndpoints.cs` with `MapOrderEndpoints(this IEndpointRouteBuilder)` and a `/api/orders` route group (tag "Orders").
- [x] `POST /` with `ValidationFilter<SubmitOrderRequest>`. *(validation runs in the service)*
- [x] Map outcomes:
  - `Created` → `201` + `Location: /api/orders/{id}` + body.
  - `Replayed` → `200` + header `Idempotent-Replay: true` + body.
  - `Conflict` → `409` ProblemDetails `order.reference.conflict`, detail "Reference {ref} is already used by a different order. Use a new reference, or open the existing order.", extension `existingOrderId`. *(detail names the existing order number; extensions `existingOrderId` and `existingOrderNumber`; no `code` yet)*
- [x] OpenAPI metadata: `Produces<OrderResponse>(201)`, `Produces<OrderResponse>(200)`, `ProducesValidationProblem()`, `ProducesProblem(409)`; example request.

**Acceptance Criteria**
- Behaviour matches DD §4.3 decision table end to end (OIT-027).
- Swagger shows the endpoint with all response codes.
- A body containing `total`/`lineTotal` fields is accepted but those values are ignored.

**As built:** Client-sent totals are ignored because the contract has no total fields (unknown JSON members are skipped); there is no dedicated test for it.

---

### OIT-017 — GET order by id and by reference

- **Epic:** E4 · **Type:** Story · **Priority:** Must · **Estimate:** 0.5 h
- **Depends on:** OIT-010, OIT-012
- **Refs:** BRA FR-32, FR-33 · DD §5.1
- **Status:** 🟡 Partly done

**Description**
Allow fetching a single order by system id, and by external reference to answer "did my order go through?".

**Tasks**
- [x] `OrderService.GetByIdAsync` and `GetByReferenceAsync` (normalise input via `ExternalReference.Create`; invalid format → not found), `AsNoTracking`, include lines and history. *(`GetAsync` by id only)*
- [x] `GET /api/orders/{id:guid}` → 200 / 404 `order.not_found` ("No order found with id …").
- [ ] `GET /api/orders/by-reference/{reference}` → 200 / 404 ("No order found with reference …").
- [x] Lines ordered by `LineNumber`; history ordered by `ChangedAtUtc`. *(lines by `line_no`, history by insertion order)*

**Acceptance Criteria**
- Existing id returns full `OrderResponse` incl. `allowedTransitions`.
- Lookup by `po-1001` finds order created as `PO-1001`.
- Unknown id/reference returns ProblemDetails 404 with code `order.not_found`.
- Non-GUID id returns 404 (route constraint) not 500.

**As built:** The list's search box finds an order by reference today.

**Remaining:** Lookup by reference (FR-33).

---

### OIT-018 — GET orders list (newest first, paging, filter, search)

- **Epic:** E4 · **Type:** Story · **Priority:** Must · **Estimate:** 1.0 h
- **Depends on:** OIT-010, OIT-012
- **Refs:** BRA FR-30, FR-31, FR-34, FR-35 · DD §5.4
- **Status:** ✅ Done (with changes)

**Description**
List orders sorted newest first with paging and optional status filter and text search.

**Tasks**
- [x] `OrderService.ListAsync(OrderListQuery, ct)`:
  - `AsNoTracking()`.
  - Filter by `Status` if provided.
  - Search (trimmed, case-insensitive) across `NormalizedReference` (contains upper search), customer name, customer email. Use `EF.Functions.Like` with escaped wildcards, or `ToUpper().Contains` (SQLite translates). *(PostgreSQL `ILIKE`, with `%`, `_` and `\` escaped)*
  - `OrderByDescending(CreatedAtUtc).ThenByDescending(Id)`. *(as built: `created_at DESC, number DESC`, backed by index `ix_orders_created_at_number`)*
  - `CountAsync` for `totalCount`; `Skip/Take`.
  - Project to `OrderSummaryResponse` in SQL (incl. `LineCount`). *(loads the page of orders and maps in memory; fine at this scale)*
- [x] `GET /api/orders` binding query via `[AsParameters]` with validation filter. *(plain query parameters)*
- [x] Defaults: page 1, pageSize 20. *(as built: default 50, maximum 200; the UI asks for 25 per page)*

**Acceptance Criteria**
- Orders created at t1 < t2 < t3 are returned t3, t2, t1.
- Orders with identical `CreatedAtUtc` are returned in a stable order.
- `status=Confirmed` returns only Confirmed orders, still newest first.
- `search=thandi` matches by customer name; `search=po-10` matches reference `PO-1001`.
- `pageSize=101` → 400.
- Response includes `page`, `pageSize`, `totalCount`.

**As built:** A `pageSize` above the maximum is clamped to 200 rather than rejected with 400.

---

### OIT-019 — PATCH order status with optimistic concurrency

- **Epic:** E4 · **Type:** Story · **Priority:** Must · **Estimate:** 1.0 h
- **Depends on:** OIT-008, OIT-010, OIT-014
- **Refs:** BRA FR-41–FR-49, US-05 · DD §5.5, §5.6
- **Status:** ✅ Done (with changes)

**Description**
Change an order's status through the domain state machine, return helpful errors, and protect against lost updates.

**Tasks**
- [x] `OrderService.ChangeStatusAsync(Guid id, ChangeStatusRequest, ct)` returning `Result<Order>`: *(returns `ChangeStatusResult`)*
  - Load tracked order; not found → `order.not_found`.
  - If `ExpectedVersion` supplied and ≠ `order.Version` → `order.concurrency_conflict` ("This order was changed by someone else. Refresh and try again."). *(not built: clients don't send a version (see notes))*
  - Call `order.ChangeStatus(...)`; on failure return the domain error.
  - `SaveChangesAsync`; catch `DbUpdateConcurrencyException` → `order.concurrency_conflict`. *(as built: the repository update is `WHERE version = @expected`; on a conflict the service re-reads and re-checks the rules (up to 5 times))*
- [x] `PATCH /api/orders/{id:guid}/status` with validation filter; success → 200 full `OrderResponse`; errors → ProblemDetails via `ProblemFactory` (409 with `currentStatus`, `requestedStatus`, `allowedTransitions` extensions). *(409 extensions: `currentStatus`, `allowedTransitions`)*
- [ ] Log rejected transitions at info level.

**Acceptance Criteria**
- Pending → Confirmed returns 200, status Confirmed, version incremented, history has 2 entries, `updatedAtUtc` changed.
- Pending → Fulfilled returns 409 `order.status.invalid_transition` with message listing Confirmed, Cancelled.
- Any change on a Fulfilled/Cancelled order returns 409 `order.status.final`.
- Same status returns 409 `order.status.unchanged`.
- Stale `expectedVersion` returns 409 `order.concurrency_conflict`.
- Invalid status value (e.g. "Shipped") returns 400.

**As built:** A request that loses a race gets the normal rule-based answer for the order's new state, e.g. "This order has already been fulfilled…". Same-status requests return 200 (see OIT-008).

**Remaining:** Log rejected transitions (NFR-09).

---

### OIT-020 — API plumbing: JSON, CORS, OpenAPI, health, logging, meta

- **Epic:** E4 · **Type:** Task · **Priority:** Must · **Estimate:** 0.75 h
- **Depends on:** OIT-002
- **Refs:** DD §5.1, §8
- **Status:** ✅ Done (with changes)

**Description**
Configure cross-cutting API concerns so endpoints behave consistently and are easy to explore.

**Tasks**
- [x] JSON: `ConfigureHttpJsonOptions` → camelCase, `JsonStringEnumConverter`, ignore unknown members (default).
- [x] CORS policy `Frontend` allowing `http://localhost:4200`, any header/method, `WithExposedHeaders("Location", "Idempotent-Replay")` (Development). *(origins from `Cors:AllowedOrigins`; rarely needed because both the dev server and nginx proxy `/api`)*
- [x] OpenAPI + Swagger UI at `/swagger` in Development; API title/version; XML comments included. *(also enabled in Docker via `Swagger__Enabled=true`)*
- [x] `AddHealthChecks().AddDbContextCheck<OrderIntakeDbContext>()`; map `/health`. *(`/health` includes the database check)*
- [x] `GET /api/meta/currencies` → `["ZAR","USD","EUR","GBP"]`. *(as built: `GET /api/currencies` (live list) and `GET /api/exchange-rates`)*
- [x] `TimeProvider.System` registered as singleton.
- [ ] Request logging (`UseHttpLogging` with minimal fields, or Serilog request logging) with structured console output.

**Acceptance Criteria**
- `/swagger` lists all endpoints with schemas.
- `/health` returns `Healthy`.
- Angular dev server (via proxy) and direct browser calls from `localhost:4200` both work; `Idempotent-Replay` is readable by the client.
- Enum values appear as strings in responses.

---

### OIT-021 — HTTP request samples file

- **Epic:** E4 · **Type:** Task · **Priority:** Should · **Estimate:** 0.25 h
- **Depends on:** OIT-016 – OIT-019
- **Refs:** DD §11
- **Status:** ⬜ Not done

**Description**
Provide a ready-to-run `.http` file that walks through every scenario, useful for reviewers and the demo.

**Tasks**
- [ ] `backend/src/OrderIntake.Api/OrderIntake.http` with `@baseUrl = http://localhost:5080`.
- [ ] Requests: create order; resubmit identical (expect 200 replay); resubmit with changed qty (expect 409); invalid body (expect 400 with multiple errors); get by id; get by reference (different casing); list; list filtered; confirm; invalid transition; fulfil; cancel fulfilled (expect 409).
- [ ] Comments above each request describing expected result.

**Acceptance Criteria**
- Running the file top to bottom against a fresh DB yields the expected status codes for each request.

**As built:** The README has `curl` examples for create and replay.

**Remaining:** `.http` samples file.

---

# E5 — Backend Tests

### OIT-022 — Tests: totals and domain guards

- **Epic:** E5 · **Type:** Test · **Priority:** Must · **Estimate:** 0.75 h
- **Depends on:** OIT-007
- **Refs:** BRA FR-20–FR-23, VR-07, VR-08 · DD §10.1
- **Status:** ✅ Done (with changes)

**Description**
Unit-test server-side total calculation and the invariants that protect it.

**Tasks**
- [x] `OrderTotalsTests`: *(as built: `PricingTests` and `OrderServiceTests` (half-away-from-zero rounding, `0.10 + 0.20`, 1,000,000 × 999.99))*
  - Single line total.
  - Multiple lines sum to subtotal; total == subtotal.
  - Zero unit price → line total 0.
  - Rounding cases via `[Theory]` (e.g. 3 × 0.335 = 1.005 → 1.01; 7 × 0.125 = 0.875 → 0.88).
  - Large values (1,000,000 × 9,999,999.99) don't overflow and compute exactly.
- [x] `OrderGuardTests`: quantity 0/−1 throws; negative price throws; 3-decimal price throws; empty lines throws; unsupported currency throws; >100 lines throws. *(as built: `CreateOrderValidatorTests` and the database check-constraint test)*
- [x] `OrderCreationTests`: status Pending; timestamps from `FakeTimeProvider`; version 1; initial history entry; line numbers 1..n in input order. *(in `OrderServiceTests`, using `ManualTimeProvider`)*

**Acceptance Criteria**
- All tests pass; each rule in BRA §7.3 has at least one test.
- Tests use `FakeTimeProvider` (no `DateTime.Now`).

---

### OIT-023 — Tests: status transition matrix

- **Epic:** E5 · **Type:** Test · **Priority:** Must · **Estimate:** 0.5 h
- **Depends on:** OIT-008
- **Refs:** BRA §9, FR-41–FR-45 · DD §3.4
- **Status:** ✅ Done (with changes)

**Description**
Exhaustively test the 4×4 transition matrix and the error messages users see.

**Tasks**
- [x] Helper to create an order in any given status via valid transitions.
- [x] `[Theory]` over all 16 (from, to) pairs with expected outcome (success / unchanged / final / invalid). *(`OrderStatusRulesTests`: 4 allowed and 8 rejected pairs; the 4 same-status pairs are no-ops, tested in `OrderServiceTests`)*
- [x] On success assert: status, `UpdatedAtUtc` advanced (advance fake clock), version +1, new history entry with from/to/reason.
- [x] On failure assert: error code, message text contains current and requested status and allowed list; state unchanged.
- [x] Test `AllowedTransitions` per status.

**Acceptance Criteria**
- 16 matrix cases + allowed-transitions cases pass.
- Message assertions protect the user-facing wording.

---

### OIT-024 — Tests: reference normalisation and fingerprint

- **Epic:** E5 · **Type:** Test · **Priority:** Must · **Estimate:** 0.5 h
- **Depends on:** OIT-005, OIT-009
- **Refs:** BRA FR-11, FR-12, FR-14 · DD §4.1, §4.2
- **Status:** 🟡 Partly done

**Description**
Lock down the rules that decide whether two submissions are "the same".

**Tasks**
- [x] `ExternalReferenceTests`: trimming; case-insensitive equality; display value preserved; invalid inputs (empty, whitespace, too long, illegal chars) with expected code. *(covered in validator and service tests (trimming, case-insensitive matching))*
- [x] `OrderFingerprintTests` `[Theory]`s: *(partly: formatting variants match (`Formatting_differences_…`) and changed content conflicts; no dedicated fingerprint tests)*
  - Equivalent variants → same hash (reference case/space, email case, currency case, notes whitespace/null vs empty, `19.9` vs `19.90`, SKU case).
  - Material differences → different hash (qty, price, SKU, name, notes text, extra line, swapped line order, different customer, different currency).
- [ ] Hash is 64 lowercase hex chars.

**Acceptance Criteria**
- All cases pass; trade-off "line order is significant" is covered by an explicit, well-named test.

**Remaining:** Dedicated fingerprint tests, including line order and SKU casing.

---

### OIT-025 — Tests: request validators

- **Epic:** E5 · **Type:** Test · **Priority:** Must · **Estimate:** 0.75 h
- **Depends on:** OIT-013
- **Refs:** BRA §7.6 · DD §5.7
- **Status:** 🟡 Partly done

**Description**
Boundary-test every validation rule using FluentValidation's `TestValidate`.

**Tasks**
- [x] Valid baseline request builder (`SubmitOrderRequestBuilder`) for readable tests. *(`TestData.ValidRequest`)*
- [ ] Reference: empty, 64 chars OK, 65 fails, illegal char fails. *(only empty is tested)*
- [x] Customer name/email: empty, too long, invalid email. *(empty name and invalid email)*
- [x] Currency: lower-case `zar` OK, `XYZ` fails. *(`usd` normalised, `dollars` rejected)*
- [ ] Lines: 0 fails, 1 OK, 100 OK, 101 fails. *(only 0 is tested)*
- [x] Quantity: 0 fails, 1 OK, 1,000,000 OK, 1,000,001 fails. *(0, −1 and 1.5; no upper-bound test)*
- [x] Unit price: −0.01 fails, 0 OK, 12.34 OK, 12.345 fails, above max fails. *(−0.01, 0 and 1.005; no upper-bound test)*
- [ ] Notes: 2000 OK, 2001 fails.
- [x] Error property paths are camelCase indexed (`lines[2].quantity`).
- [x] `ChangeStatusRequestValidator` and `OrderListQueryValidator` boundaries. *(status parsing in `OrderStatusRulesTests`; bad list filter in `OrdersApiTests`)*

**Acceptance Criteria**
- Each rule in BRA §7.6 has passing and failing boundary tests.
- Multiple simultaneous errors are all reported.

**Remaining:** Length and maximum boundary tests (reference 64/65, 100/101 lines, quantity and price maximums, notes).

---

### OIT-026 — Tests: OrderService (idempotency, concurrency, listing, status)

- **Epic:** E5 · **Type:** Test · **Priority:** Must · **Estimate:** 1.5 h
- **Depends on:** OIT-015, OIT-018, OIT-019
- **Refs:** BRA FR-10–FR-15, FR-30, FR-49 · DD §4.4, §10.1
- **Status:** ✅ Done (with changes)

**Description**
Test the application service against a real SQLite (in-memory) database so unique constraints and concurrency tokens behave as in production.

**Tasks**
- [x] Test fixture: `SqliteConnection("DataSource=:memory:")` kept open; `EnsureCreated`; factory method for fresh `DbContext` instances sharing the connection. For the concurrency test use a shared-cache named in-memory DB or temp file so parallel contexts see each other. *(as built: service tests use a fast in-memory fake repository; repository tests use real PostgreSQL via Testcontainers, with a fresh migrated database per test class)*
- [x] Submit tests:
  - New → `Created`, 1 row.
  - Identical resubmission → `Replayed`, same id, 1 row.
  - Changed content → `Conflict` with existing id; original unchanged (qty/total compare).
  - Reference differing only by case/whitespace → `Replayed`.
  - Replay after Confirm and after Cancel → `Replayed`.
- [x] **Concurrency test**: 20 tasks submitting the same payload via separate scopes/contexts with `Task.WhenAll` → exactly 1 row; exactly 1 `Created`; 19 `Replayed`; no exceptions. *(50 parallel submits (service), 20 parallel inserts on PostgreSQL, 20 parallel HTTP POSTs)*
- [x] Race-path unit test: simulate unique violation (pre-insert a row between lookup and insert using a test hook or by inserting via a second context) → returns `Replayed`/`Conflict` correctly. *(a raw insert that bypasses the repository proves the unique index rejects it; the concurrent tests exercise the catch-and-replay path)*
- [x] List tests: newest first (advance fake clock between creates); tiebreak stable; paging counts; status filter; search on reference/name/email.
- [x] ChangeStatus tests: not found; stale `ExpectedVersion` → concurrency conflict; two contexts updating same order → second gets concurrency conflict. *(not found, stale version rejected, racing fulfil vs cancel (service and PostgreSQL))*

**Acceptance Criteria**
- All tests pass reliably (run 10× locally without flakiness).
- Concurrency test demonstrates no duplicate rows.

**As built:** The database tests passed on 4 consecutive runs with no flakiness.

---

### OIT-027 — Tests: API integration (WebApplicationFactory)

- **Epic:** E5 · **Type:** Test · **Priority:** Should · **Estimate:** 1.0 h
- **Depends on:** OIT-016 – OIT-019
- **Refs:** DD §10.2
- **Status:** ✅ Done (with changes)

**Description**
Verify HTTP contracts end to end: status codes, headers, error shapes and JSON format.

**Tasks**
- [x] `ApiFactory : WebApplicationFactory<Program>` overriding the DbContext to SQLite in-memory and disabling seed. *(runs against its own migrated PostgreSQL database (Testcontainers); only the exchange-rate provider is faked)*
- [x] POST new → 201, `Location` header, body totals correct.
- [x] POST identical → 200 + `Idempotent-Replay: true`, same id.
- [x] POST changed → 409, `code == order.reference.conflict`, `existingOrderId` present. *(`existingOrderId` checked; no `code` yet)*
- [x] POST invalid → 400, `errors` keyed by `lines[0].quantity` etc.
- [ ] POST with `quantity: 2.5` → 400 friendly message. *(covered by validator tests, not over HTTP)*
- [ ] POST with client-supplied `total: 1` → server total used.
- [x] GET list → newest first, `totalCount`.
- [ ] GET by reference with different casing → 200. *(no by-reference endpoint (OIT-017))*
- [x] PATCH invalid transition → 409 with `allowedTransitions`.
- [x] Unknown id → 404 ProblemDetails.

**Acceptance Criteria**
- All integration tests pass via `dotnet test`.
- Assertions check the JSON contract (camelCase, string enums).

**As built:** Also covers the currency endpoints, 20 simultaneous POSTs and the database health check.

---

# E6 — Frontend

### OIT-028 — Frontend core: models, API client, error interceptor, toasts

- **Epic:** E6 · **Type:** Story · **Priority:** Must · **Estimate:** 1.0 h
- **Depends on:** OIT-003, OIT-016
- **Refs:** DD §9.3, §9.5
- **Status:** ✅ Done (with changes)

**Description**
Build the typed data layer the feature components use.

**Tasks**
- [x] `core/api/models.ts`: `OrderStatus` union type, `Order`, `OrderLine`, `StatusChange`, `OrderSummary`, `PagedResult<T>`, `SubmitOrderRequest`, `ChangeStatusRequest`, `OrderListQuery`. *(as built: `orders/order.models.ts`)*
- [x] `core/api/orders-api.service.ts` (`providedIn: 'root'`, `inject(HttpClient)`): *(as built: `orders/orders-api.ts` (`submit` returns `{ order, alreadySubmitted }`) and `shared/currency-api.ts`; no `getByReference`)*
  - `submit(req): Observable<SubmitOutcome>` using `observe: 'response'`; returns `{ kind: 'created' | 'replayed', order }` based on status/`Idempotent-Replay` header.
  - `getById(id)`, `getByReference(ref)`, `list(query)` (HttpParams), `changeStatus(id, req)`, `getCurrencies()`.
- [x] `core/errors/api-error.ts`: `ApiError { status, code?, title?, detail?, fieldErrors: Record<string,string[]>, extensions }` + `toApiError(HttpErrorResponse)` parsing ProblemDetails/ValidationProblemDetails; network error (status 0) → friendly message. *(as built: `shared/problem.ts` (`toProblem`))*
- [ ] Functional HTTP interceptor converting errors to `ApiError`. *(components call `toProblem` directly)*
- [ ] `core/notifications/toast.service.ts`: signal-based queue; `ToastContainerComponent` with auto-dismiss. *(replaced by inline success and info alerts)*
- [x] `app.config.ts`: `provideHttpClient(withFetch(), withInterceptors([...]))`. *(no interceptors)*

**Acceptance Criteria**
- Calling `submit` twice with the same payload yields `created` then `replayed`.
- Error objects from 400/404/409/500/network are normalised into `ApiError`.
- No `any` types in core.

---

### OIT-029 — App shell, routing and shared UI components

- **Epic:** E6 · **Type:** Story · **Priority:** Must · **Estimate:** 0.75 h
- **Depends on:** OIT-003
- **Refs:** DD §9.2, §9.3
- **Status:** ✅ Done (with changes)

**Description**
Create the app layout, routes (lazy-loaded), and reusable presentational pieces.

**Tasks**
- [x] `app.routes.ts` per DD §9.2 with `loadComponent` for each page; `withComponentInputBinding()` so `:id` binds to an input. *(eager routes (the app is small); `withComponentInputBinding()` yes)*
- [x] Shell: header with app name and "New order" link, `<router-outlet>`, toast container. *(as built: sidebar with Orders and New order plus a dark-mode toggle (OIT-049); no toast container)*
- [x] `StatusBadgeComponent` (input `status`), distinct colour per status, accessible text.
- [x] `MoneyPipe` / use Angular `CurrencyPipe` with order currency code and `en-ZA` locale (register locale data). *(`CurrencyPipe` with each order's currency; default locale)*
- [x] Shared UI: `AlertComponent` (info/success/warning/error), `SpinnerComponent`, button styles. *(CSS classes (`alert`, `btn`, `badge`) rather than components)*
- [x] Global SCSS: CSS variables, base typography, table and form styles, responsive container. *(plain CSS with light and dark theme variables)*
- [ ] `NotFoundComponent`. *(unknown routes redirect to the order list)*

**Acceptance Criteria**
- Navigating to `/`, `/orders`, `/orders/new`, `/orders/<id>`, `/nope` renders the right component.
- Status badge renders all four statuses distinctly.
- Layout usable at 375 px width.

---

### OIT-030 — Order list page (newest first, paging)

- **Epic:** E6 · **Type:** Story · **Priority:** Must · **Estimate:** 1.0 h
- **Depends on:** OIT-028, OIT-029, OIT-018
- **Refs:** BRA FR-30, FR-31, FR-34, US-03 · DD §9.4
- **Status:** ✅ Done (with changes)

**Description**
Show orders newest first so reps can quickly confirm what was captured.

**Tasks**
- [x] `OrderListComponent` with signals: `query`, `result`, `loading`, `error`.
- [ ] Load on init and whenever query changes; sync `page` with URL query params. *(loads on init and on every change; the page is not kept in the URL)*
- [x] Table columns: Reference (link to detail) · Customer · Status (badge) · Total (currency) · Lines · Created (local date/time via `DatePipe`). *(plus the order number and an optional converted-total column)*
- [x] Pager: previous/next, "Showing X–Y of Z". *(25 per page)*
- [x] Empty state ("No orders yet" + New order button), loading state, error state with retry. *(first-order and no-match states; Refresh button)*
- [x] Do not re-sort client-side (server order is authoritative).

**Acceptance Criteria**
- With seeded data, newest order appears first.
- Clicking a reference opens the detail page.
- Paging updates the URL and survives refresh.
- Error from API shows an alert with retry.

**Remaining:** Keep the page in the URL (with OIT-035).

---

### OIT-031 — New order form with line items editor

- **Epic:** E6 · **Type:** Story · **Priority:** Must · **Estimate:** 1.5 h
- **Depends on:** OIT-028, OIT-029
- **Refs:** BRA FR-01–FR-06, FR-16, FR-24, VR-11, US-01 · DD §9.4
- **Status:** ✅ Done (with changes)

**Description**
Typed reactive form to capture an order with dynamic line items, client-side validation mirroring the server, and an estimated total.

**Tasks**
- [x] `OrderFormComponent` with typed `FormGroup`: `externalReference`, `customer.name`, `customer.email`, `currency` (select from `getCurrencies()`, default ZAR), `notes`, `lines: FormArray`. *(currency is a searchable picker of live currencies (OIT-048), default USD)*
- [x] `LineItemsEditorComponent`: rows with SKU, name, quantity (`type=number`, `step=1`, `min=1`), unit price (`step=0.01`, `min=0`); add row; remove row (disabled when only one row); starts with one empty row. *(an inline `FormArray` table in the form, not a separate component)*
- [x] Validators mirroring BRA §7.6 incl. custom `integer` and `maxDecimals(2)` validators and reference pattern. *(required, email, whole number, ≥ 1, ≥ 0, 2 decimals, max lengths; no reference character pattern)*
- [x] Inline error messages under each field after touch/submit.
- [x] Computed signal for estimated line totals and order total (rounded away from zero like the server), labelled "Estimate. Final totals are calculated by the server."
- [x] Reference field hint: "Use the customer's PO number. Resubmitting the same reference won't create a duplicate." *(pre-filled with a generated `PO-YYYYMMDD-XXXX` reference, plus a New button)*
- [x] Submit button disabled when form invalid or `submitting()`; shows spinner. *(disabled while submitting; an invalid form shows every error on click instead)*
- [x] Map form value → `SubmitOrderRequest` (trim strings; numbers as numbers). *(the server trims strings)*
- [x] Cancel button back to list.

**Acceptance Criteria**
- Cannot submit with zero lines, quantity 0/decimal, negative price, or invalid email (button disabled, messages shown).
- Estimated total updates live as quantities/prices change.
- Double-clicking submit results in a single HTTP request.

---

### OIT-032 — Submit outcome handling (created / replay / conflict / errors)

- **Epic:** E6 · **Type:** Story · **Priority:** Must · **Estimate:** 1.0 h
- **Depends on:** OIT-031, OIT-016
- **Refs:** BRA FR-11, FR-12, US-02 · DD §4.5, §9.4
- **Status:** ✅ Done (with changes)

**Description**
Make every submission outcome clear to the rep, especially resubmissions.

**Tasks**
- [x] `created` → success toast "Order PO-1001 created." and navigate to `/orders/:id`. *(success alert on the detail page instead of a toast)*
- [x] `replayed` → navigate to `/orders/:id` with router state `{ replayed: true }`; detail page shows info alert "This order was already submitted on {createdAt}. No duplicate was created." *(the notice says the order was already submitted and no duplicate was created; the submitted date is on the page, not in the message)*
- [x] 409 `order.reference.conflict` → set `externalReference` control error `conflict`, message "This reference is already used by a different order." with a router link to `existingOrderId`; form values preserved. *(alert above the form with "View order #N" and "Use a new reference"; form values kept)*
- [x] 400 → map `fieldErrors` keys (`lines[1].quantity`, `customer.email`) to form controls (`setErrors({ server: msg })`); unmapped errors shown in a summary alert.
- [x] Network/500 → error alert with "Try again" (safe because submission is idempotent; text says so).
- [x] Server error on a control clears when the user edits it.

**Acceptance Criteria**
- Submitting the same order twice shows the "already submitted" message on the second attempt and no duplicate appears in the list.
- Changing a line and resubmitting with the same reference shows the conflict message with a working link.
- Server validation errors appear next to the correct fields.

---

### OIT-033 — Order detail page

- **Epic:** E6 · **Type:** Story · **Priority:** Must · **Estimate:** 1.0 h
- **Depends on:** OIT-028, OIT-029, OIT-017
- **Refs:** BRA FR-32, US-04 · DD §9.4
- **Status:** ✅ Done (with changes)

**Description**
Show everything a rep needs to verify an order's totals and status.

**Tasks**
- [x] `OrderDetailComponent` with `id` input (route binding); load via `getById`; loading/404/error states.
- [x] Header: reference, status badge, created and last updated (local time), order id (small, copyable). *(order number as the title; the order id isn't shown)*
- [x] Replay info alert when navigated with `replayed` state.
- [x] Customer section (name, email), currency, notes (if any).
- [x] Lines table: #, SKU, name, qty, unit price, line total; footer subtotal and total, formatted in order currency. *(no # column; an Original price column appears for converted orders)*
- [x] Status history list (from → to, when, reason). *(from → to and when; no reason)*
- [x] Back to list link.
- [x] Placeholder region for status actions (OIT-034). *(status actions and a Currency panel are built in)*

**Acceptance Criteria**
- Totals displayed match the API response exactly (no client recalculation).
- Unknown id shows "Order not found" with link back.
- Page works on direct load/refresh of `/orders/:id`.

---

### OIT-034 — Status change actions in UI

- **Epic:** E6 · **Type:** Story · **Priority:** Could · **Estimate:** 1.0 h
- **Depends on:** OIT-033, OIT-019
- **Refs:** BRA FR-43, FR-46, FR-48, FR-49, US-05 · DD §9.4
- **Status:** ✅ Done (with changes)

**Description**
Let users move an order through the flow from the detail page, using only transitions the server says are allowed.

**Tasks**
- [x] `StatusActionsComponent` inputs: `order`; output: `changed(order)`. *(built into the detail page, not a separate component)*
- [x] Render one button per `allowedTransitions` with friendly labels: Confirmed → "Confirm order", Fulfilled → "Mark as fulfilled", Cancelled → "Cancel order" (danger style).
- [x] Cancel opens confirm dialog (native `<dialog>`) with optional reason field. *(native `confirm()`, no reason field)*
- [x] Call `changeStatus` with `expectedVersion: order.version`; disable buttons while in flight. *(no `expectedVersion`; buttons are disabled while a request is in flight)*
- [x] On success: update detail view, toast "Order marked as Confirmed." *(the view updates in place; no toast)*
- [x] On 409 status errors: show server `detail` in an alert.
- [x] On `order.concurrency_conflict`: alert with "Reload" button that refetches. *(after a rejected change the page reloads the order automatically)*
- [x] Final state: show "This order is {status} and can no longer change."
- [ ] Optional: quick action in list rows.

**Acceptance Criteria**
- A Pending order shows Confirm and Cancel only; Confirmed shows Mark fulfilled and Cancel; final orders show no buttons.
- Status change reflects immediately with updated history.
- Changing the same order in two tabs: the second tab gets the concurrency message and can reload.

---

### OIT-035 — List filtering and search in UI

- **Epic:** E6 · **Type:** Story · **Priority:** Could · **Estimate:** 0.75 h
- **Depends on:** OIT-030
- **Refs:** BRA FR-35, US-06 · DD §9.4
- **Status:** 🟡 Partly done

**Description**
Filter the list by status and search by reference/customer using the server-side query parameters.

**Tasks**
- [x] Status `<select>` (All + four statuses).
- [x] Search input with 300 ms debounce (`toObservable` + `debounceTime` or `effect` with timer). *(250 ms)*
- [x] Changing filter resets to page 1.
- [ ] Persist `status` and `search` in URL query params; restore on load.
- [x] "Clear filters" button; result count shown.
- [x] Empty result state ("No orders match your filters"). *("No matching orders" with Clear filters)*

**Acceptance Criteria**
- Selecting Confirmed shows only Confirmed orders, newest first.
- Typing `po-10` narrows to matching references after the debounce, with one request per pause.
- Refreshing the page keeps the filters.

**As built:** A "Show totals in" option converts every total into one currency for display (OIT-047).

**Remaining:** Keep status, search and page in the URL so refreshes and shared links keep them.

---

### OIT-036 — Frontend unit tests

- **Epic:** E6 · **Type:** Test · **Priority:** Could · **Estimate:** 1.0 h
- **Depends on:** OIT-031, OIT-034
- **Refs:** DD §10.3
- **Status:** ✅ Done (with changes)

**Description**
Cover the UI logic most likely to regress.

**Tasks**
- [x] `OrdersApiService`: `HttpTestingController` verifies `created` vs `replayed` from status/header; error mapping. *(`orders-api.spec.ts` and `problem.spec.ts`)*
- [ ] `OrderFormComponent`: estimate calculation incl. rounding; required/integer/decimals validators; add/remove line; server field errors mapped to controls.
- [ ] `StatusActionsComponent`: renders only allowed transitions; emits on success.
- [x] Ensure `ng test --watch=false` passes headless (ChromeHeadless or switch to Jest/Vitest if preferred). *(Vitest (Angular 21 default): 23 tests in 6 files, including the currency picker, list paging and the theme toggle)*

**Acceptance Criteria**
- `npm test -- --watch=false` passes locally.

**Remaining:** Order form component tests.

---

# E7 — Developer Experience & Docs

### OIT-037 — README.md

- **Epic:** E7 · **Type:** Docs · **Priority:** Must · **Estimate:** 0.5 h
- **Depends on:** OIT-016, OIT-030
- **Refs:** BRA §15 · DD §11
- **Status:** ✅ Done (with changes)

**Description**
Clear, copy-pasteable instructions so a reviewer can run everything locally in minutes.

**Tasks**
- [ ] Overview (2–3 sentences) and screenshot/GIF of the UI. *(overview yes, no screenshot)*
- [x] Prerequisites with versions (.NET 8 SDK, Node 20+). *(.NET SDK 10.0.401, Node, Docker)*
- [x] Quick start: backend run, frontend run, URLs (app, API, Swagger). *(local run and Docker Compose)*
- [x] Running tests (backend, optional frontend).
- [x] Configuration: DB location, seed toggle, `--reset-db`. *(PostgreSQL connection strings, `.env` overrides, `docker compose down -v` to reset)*
- [x] API summary table and link to Swagger and `.http` file. *(no `.http` file (OIT-021))*
- [x] "Try the duplicate scenario" walkthrough (3 steps).
- [x] Project structure tree.
- [ ] Troubleshooting (port in use, SQLite file locked, Node version).
- [x] Links to `SOLUTION.md` and `docs/`. *(both linked)*

**Acceptance Criteria**
- Someone following only the README on a clean machine (with SDKs installed) can run app and tests without asking questions.

**Remaining:** Screenshot and a short troubleshooting section.

---

### OIT-038 — SOLUTION.md

- **Epic:** E7 · **Type:** Docs · **Priority:** Must · **Estimate:** 1.0 h
- **Depends on:** most implementation tickets
- **Refs:** DD §12, §13
- **Status:** ✅ Done (with changes)

**Description**
Concise explanation of design decisions and trade-offs for reviewers (and the basis of the demo narration).

**Tasks**
- [x] Architecture overview with diagram. *(text diagram)*
- [x] Duplicate handling: key, normalisation, fingerprint, decision table, race handling, why 409 not overwrite.
- [x] Totals and rounding rule.
- [x] Status machine and error messaging; why `Result` over exceptions; why 409.
- [x] Storage choice (SQLite vs in-memory vs EF InMemory) and `EnsureCreated` trade-off. *(now PostgreSQL with migrations (section "Persistence (PostgreSQL)"))*
- [x] Validation strategy (client mirrors server; server authoritative).
- [x] API conventions (ProblemDetails codes, replay header).
- [x] Testing approach and what's covered.
- [x] Trade-offs table (from DD §12) and what was deliberately left out.
- [x] "With more time" list (DD §13).
- [ ] Time spent (honest breakdown).

**Acceptance Criteria**
- Every major decision in DD §12 is explained with the alternative considered.
- Readable in under 10 minutes.

---

### OIT-039 — Run scripts (and optional Docker Compose)

- **Epic:** E7 · **Type:** Task · **Priority:** Could · **Estimate:** 0.5 h
- **Depends on:** OIT-037
- **Refs:** DD §11
- **Status:** ✅ Done (with changes)

**Description**
One command to start both apps.

**Tasks**
- [ ] `run.ps1` (Windows) and `run.sh` (macOS/Linux): restore, start API in background, start Angular, stop both on exit. *(replaced by `docker compose up --build -d`)*
- [x] Optional `docker-compose.yml`: API (SDK image build) + frontend (node build served by nginx with `/api` proxy). *(full stack: db, migrate, api, web (OIT-044, OIT-045))*
- [x] Document in README.

**Acceptance Criteria**
- `./run.sh` / `.\run.ps1` brings up both apps reachable at documented URLs.

---

### OIT-040 — GitHub Actions CI

- **Epic:** E7 · **Type:** Task · **Priority:** Could · **Estimate:** 0.5 h
- **Depends on:** OIT-026
- **Refs:** DD §11
- **Status:** ⬜ Not done

**Description**
Show the build and tests passing on every push.

**Tasks**
- [ ] `.github/workflows/ci.yml` with two jobs:
  - backend: `actions/setup-dotnet@v4` (8.0.x), `dotnet restore`, `dotnet build --no-restore`, `dotnet test --no-build --logger trx`.
  - frontend: `actions/setup-node@v4` (20, npm cache), `npm ci`, `npm run build`, optional `npm test -- --watch=false --browsers=ChromeHeadless`.
- [ ] CI badge in README.

**Acceptance Criteria**
- Workflow runs green on `main`.

**As built:** When it's built: the database tests need Docker (GitHub-hosted Ubuntu runners have it); `dotnet test --filter "Category!=Database"` runs the unit tests only.

**Remaining:** GitHub Actions CI.

---

### OIT-041 — End-to-end manual QA pass

- **Epic:** E7 · **Type:** Test · **Priority:** Must · **Estimate:** 0.5 h
- **Depends on:** all Must tickets
- **Refs:** BRA §10
- **Status:** 🟡 Partly done

**Description**
Run through every user story on a fresh clone before recording and submitting.

**Tasks**
- [ ] Fresh clone into a new folder; follow README exactly. *(still to do, before submission)*
- [x] US-01: create order; check totals.
- [x] US-02: resubmit same (replay message, no duplicate); resubmit changed (conflict); double-click submit (one order).
- [x] US-03: list newest first; paging.
- [x] US-04: detail totals match.
- [x] US-05: valid transitions; invalid transition message; final state message.
- [x] US-06 (if built): filters.
- [x] Validation errors display properly (server and client).
- [x] `dotnet test` green; `ng build` clean; no console errors in browser.
- [ ] Check repo for secrets, stray `.db` files, TODO comments. *(still to do; the dev-only database passwords in `docker-compose.yml` and `.env.example` are intentional)*

**Acceptance Criteria**
- All checks pass; any defects fixed or recorded as known limitations in SOLUTION.md.

**As built:** User stories were exercised with scripted browser runs (Playwright) against the Docker stack after each feature: create, replay, conflict, validation, status changes, currency conversion, list filters and totals, the currency picker, and the PostgreSQL-backed stack. The only browser error was the expected 409 log entry.

**Remaining:** Fresh-clone pass following only the README; secrets check.

---

# E8 — Demo

### OIT-042 — Demo script and video recording

- **Epic:** E8 · **Type:** Task · **Priority:** Must · **Estimate:** 1.0 h
- **Depends on:** OIT-041
- **Refs:** BRA §15 (Deliverable 2)
- **Status:** 🟡 Partly done

**Description**
Record a 5–10 minute Loom (or similar) with voice-over covering the working app, architecture, decisions and notable code.

**Tasks**
- [x] Write a script with timings: *(`DEMO_SCRIPT.md`, about 9 minutes (kept out of the repo))*
  1. **0:00–0:45** Problem and what was built.
  2. **0:45–3:30** Live demo: list (seeded, newest first) → create order → resubmit (replay) → change line and resubmit (conflict) → detail totals → status changes incl. invalid transition message → filter.
  3. **3:30–6:30** Code walkthrough: repo layout; `Order` aggregate and totals; `OrderStatusTransitions`; `OrderFingerprint`; `OrderService.SubmitAsync` race handling; ProblemDetails mapping; Angular form + outcome handling.
  4. **6:30–8:00** Tests: run `dotnet test`; show concurrency test and transition matrix.
  5. **8:00–9:30** Trade-offs and what you'd do next.
- [ ] Reset DB before recording (`--reset-db`); close unrelated tabs/notifications; increase editor font size. *(`docker compose down -v`)*
- [ ] Record, review once, re-record if over 10 minutes.
- [ ] Add video link to README and submission.

**Acceptance Criteria**
- Video is 5–10 minutes, audio clear, covers all four required topics (demo, architecture, decisions/trade-offs, notable implementations).
- Link is accessible to reviewers (permissions checked in a private window).

**Remaining:** Record the video and add the link to the README.

---

# E9 — Added during delivery

Work that wasn't in the original backlog. Recorded at sign-off so the backlog matches the delivered system.

### OIT-043 — Upgrade to .NET 10 and Angular 21

- **Epic:** E9 · **Type:** Task · **Priority:** Should · **Estimate:** 0.5 h
- **Depends on:** OIT-002, OIT-003
- **Refs:** BRA NFR-01
- **Status:** ✅ Done

**Tasks**
- [x] Install .NET SDK 10.0.401; retarget API and tests to `net10.0`; pin the SDK in `backend/global.json` (`rollForward: latestPatch`).
- [x] Update packages to their .NET 10 versions (Swashbuckle 10, Mvc.Testing 10, test SDK).
- [x] Angular 21 workspace (zoneless, Vitest), TypeScript 5.9.

**Acceptance Criteria**
- Build has zero warnings; all tests pass on `net10.0`; Swagger still loads.

---

### OIT-044 — Docker Compose for API and UI

- **Epic:** E9 · **Type:** Task · **Priority:** Should · **Estimate:** 1.0 h
- **Depends on:** OIT-039
- **Refs:** BRA NFR-02 · DD §16
- **Status:** ✅ Done

**Tasks**
- [x] `backend/Dockerfile`: multi-stage (SDK 10.0.401 build, `aspnet:10.0` runtime), non-root, health check on `/health`, optional `test` stage (unit tests only).
- [x] `frontend/Dockerfile`: Node 24 build, unprivileged nginx on 8080, SPA fallback, long caching for hashed assets, `/api` proxied to `API_UPSTREAM`.
- [x] `docker-compose.yml` with health-ordered start-up; ports configurable (`WEB_PORT`, `API_PORT`, `DB_PORT`).
- [x] README "Run with Docker" section.

**Acceptance Criteria**
- `docker compose up --build -d` serves the UI on http://localhost:8080 and Swagger on http://localhost:8081/swagger.
- Both containers run as non-root and report healthy.

---

### OIT-045 — PostgreSQL persistence with migrations and Docker init

- **Epic:** E9 · **Type:** Story · **Priority:** Must · **Estimate:** 3.0 h
- **Depends on:** OIT-015, OIT-044
- **Refs:** BRA FR-10, FR-13, FR-49, NFR-03 · DD §14
- **Status:** ✅ Done (supersedes OIT-010 and the storage part of OIT-011)

**Tasks**
- [x] EF Core 10 + Npgsql, snake_case names; persistence entities separate from the immutable domain records (`OrderMapping`).
- [x] Tables `orders`, `order_lines`, `order_status_changes`, `order_currency_changes`; `numeric` money; check constraints (quantity > 0, prices ≥ 0, status, currency format, version ≥ 1).
- [x] Unique index `ux_orders_normalized_reference`; repository looks up first, then catches `23505` on that index and returns the winning order.
- [x] `orders.version` as concurrency token; append-only history written in the same transaction.
- [x] Initial migration committed; `dotnet-ef` as a local tool.
- [x] `docker/postgres/init/01-create-roles.sh`: migrator role (owns schema) and app role (`SELECT`, `INSERT`, `UPDATE` only).
- [x] One-shot `migrate` service (`dotnet OrderTracker.Api.dll migrate`); the API starts only after it succeeds. Migrations also run on startup in Development.
- [x] `/health` includes a database check; GSS encryption disabled in connection strings (the slim image has no Kerberos library).
- [x] Repository, service and endpoints made async; in-memory store moved to the test project as a fake.

**Acceptance Criteria**
- 20 parallel inserts of one reference store exactly one row; a raw duplicate insert fails on the unique index.
- A stale-version update is rejected and changes nothing; racing updates let exactly one win.
- Data survives restarts; the app role can't change the schema or delete data.

---

### OIT-046 — Live exchange rates and currency conversion

- **Epic:** E9 · **Type:** Story · **Priority:** Should · **Estimate:** 2.5 h
- **Depends on:** OIT-008, OIT-033
- **Refs:** BRA §7.7 (FR-60–FR-66) · DD §15
- **Status:** ✅ Done

**Tasks**
- [x] `FrankfurterExchangeRateProvider` (ECB reference rates, no API key) behind `IExchangeRateProvider`; 5 s timeout.
- [x] `ExchangeRateService`: 1-hour cache per base currency; serves the last good rates (flagged stale) for up to 3 days if the provider is down.
- [x] `GET /api/currencies`, `GET /api/exchange-rates?base=…`.
- [x] `PATCH /api/orders/{id}/currency`: Pending orders only; prices converted from the original (base) prices; each conversion recorded with rate, date and source.
- [x] Clear errors: `409` (not Pending), `422` (no rate), `503` (rates unavailable), `400` (bad code).
- [x] Detail page Currency panel: live-rate preview, Convert, conversion history, Original price column.

**Acceptance Criteria**
- Converting back to the base currency restores the exact submitted prices.
- Repeated conversions round once, not cumulatively.
- A provider outage never blocks taking orders.

---

### OIT-047 — "Show totals in" on the order list

- **Epic:** E9 · **Type:** Story · **Priority:** Could · **Estimate:** 0.5 h
- **Depends on:** OIT-046, OIT-030
- **Refs:** BRA FR-65
- **Status:** ✅ Done

**Tasks**
- [x] Optional display currency; converted column and total of listed orders; rate date and source shown; orders without a rate are left out and counted.

**Acceptance Criteria**
- Display-only: no order changes; "Order currency" switches it off.

---

### OIT-048 — Searchable currency picker

- **Epic:** E9 · **Type:** Story · **Priority:** Could · **Estimate:** 1.0 h
- **Depends on:** OIT-046
- **Refs:** DD §15
- **Status:** ✅ Done

**Tasks**
- [x] `CurrencySelect` component (ARIA combobox) filtering by code or name, code matches first; keyboard support (↑/↓, Enter, Escape, Tab).
- [x] Works with reactive forms and two-way binding; optional empty option.
- [x] Used on the order form, the Convert action and "Show totals in".
- [x] Fix: a click inside a wrapping `<label>` no longer reopens the list (regression test).

**Acceptance Criteria**
- "rand" finds ZAR, "yen" finds JPY; mouse and keyboard picks both stick.

---

### OIT-049 — UI redesign

- **Epic:** E9 · **Type:** Story · **Priority:** Could · **Estimate:** 2.0 h
- **Depends on:** OIT-029, OIT-030
- **Refs:** BRA NFR-07
- **Status:** ✅ Done

**Tasks**
- [x] Sidebar navigation with a skip link; light and dark themes with a toggle (remembers the choice, follows the system by default).
- [x] Redesigned list with order directory panel, paging (25 per page), Clear filters, first-order and no-match states.
- [x] Footer and sidebar tagline removed.
- [x] Tests for the theme toggle and list paging.

**Acceptance Criteria**
- Usable at desktop and phone widths; theme persists across reloads.

---

### OIT-050 — Database tests with Testcontainers

- **Epic:** E9 · **Type:** Test · **Priority:** Must · **Estimate:** 1.5 h
- **Depends on:** OIT-045
- **Refs:** BRA NFR-03, NFR-06 · DD §10
- **Status:** ✅ Done

**Tasks**
- [x] Shared PostgreSQL container per run; fresh migrated database per test class.
- [x] `PostgresOrderRepositoryTests` (10 tests): save and reload, duplicates, 20-way insert race, unique index and check constraints, stale and racing updates, listing, search escaping.
- [x] `OrdersApiTests` moved onto PostgreSQL, plus a 20-way HTTP race and the health check.
- [x] Tests tagged `Category=Database`, so `--filter "Category!=Database"` runs without Docker.

**Acceptance Criteria**
- Stable across repeated runs.

---

### OIT-051 — Restructure the backend as DDD with Clean Architecture

- **Epic:** E9 · **Type:** Story · **Priority:** Should · **Estimate:** 6.0 h
- **Depends on:** OIT-045
- **Refs:** DD §17 · BRA NFR-05, NFR-09
- **Status:** ✅ Done (2026-10-11)

**Tasks**
- [x] Projects `OrderTracker.Domain`, `.Application`, `.Infrastructure`, `.Api`; shared `Directory.Build.props` with warnings as errors.
- [x] Domain: `Order` aggregate root with private setters; `OrderLine` and history entities; value objects `OrderId`, `ExternalReference`, `EmailAddress`, `CustomerInfo`, `CurrencyCode`, `ExchangeRate`; `OrderStatusPolicy`; domain events; `Result`/`Error` with stable codes; `IOrderRepository`.
- [x] Application: one handler per use case; `SubmitOrderValidator` reusing value-object rules; DTOs; ports `IUnitOfWork`, `IOrderQueries`, `IExchangeRateProvider`; `ExchangeRateService`; `OrderActivityLogger` for domain events.
- [x] Infrastructure: EF maps the aggregate directly (owned types, backing fields); `UnitOfWork` translates `23505` and concurrency failures and dispatches events after commit; `OrderQueries` projects the list in SQL; migrations moved here.
- [x] Api: thin endpoints; one `Error` → ProblemDetails mapper adding `code`; composition root.
- [x] Schema verified identical (EF reports no pending model changes); fingerprint format pinned by a test.
- [x] Tests split into Domain (73), Application (47), Integration (26) and Architecture (7) projects plus a shared TestKit: 153 in total.
- [x] Dockerfile, solution file, README, SOLUTION.md and DD §17 updated.

**Acceptance Criteria**
- Architecture tests enforce the dependency rule (with a control test).
- The existing database works unchanged: old orders load, and resubmitting their original payloads returns `200` replays.
- The API JSON is unchanged apart from the added `code`; the frontend works without changes (browser flows re-run).
- 25 simultaneous identical submissions still produce exactly one order.

**As built:** Also closes open items 4 (error codes) and 8 (business-event logging) and 9 (fingerprint tests), and most of 10 (validator boundaries). A side effect: locale-dependent number formatting in validation messages was fixed (found by the new boundary tests).

---

## Open items after sign-off

| # | Item | From | Priority |
|---|---|---|---|
| 1 | Record the demo video and add the link to the README | OIT-042 | Must |
| 2 | Fresh-clone pass following only the README; secrets check | OIT-041 | Must |
| 3 | Commit the `docs/` folder | OIT-001 | Must |
| 4 | ~~Stable machine-readable `code` on every error response~~ **Done in OIT-051** | OIT-014, 016, 019 | Should |
| 5 | Look up an order by reference (FR-33) | OIT-017 | Should |
| 6 | Decide VR-01: enforce the reference character whitelist, or relax the requirement | OIT-005 | Should |
| 7 | Friendly message for non-numeric JSON values (e.g. `"quantity": "abc"`) | OIT-013 | Should |
| 8 | ~~Log replays, conflicts and rejected transitions (NFR-09)~~ **Done in OIT-051** | OIT-015, 019 | Should |
| 9 | ~~Dedicated fingerprint tests, including line order and SKU casing~~ **Done in OIT-051** | OIT-024 | Should |
| 10 | Validator boundary tests: **mostly done in OIT-051** (reference 64/65, 100/101 lines, price maximum, notes); quantity maximum still untested | OIT-025 | Should |
| 11 | `.http` request samples | OIT-021 | Should |
| 12 | Keep filters and page in the URL | OIT-030, 035 | Could |
| 13 | GitHub Actions CI | OIT-040 | Could |
| 14 | Demo seed data | OIT-011 | Could |
| 15 | Optional cancellation reason (FR-46) | OIT-008, 034 | Could |
| 16 | Order form component tests | OIT-036 | Could |
| 17 | README screenshot and troubleshooting section | OIT-037 | Could |

---

## Appendix A — Strict 4-hour cut

*Not used: the full scope was built (see Sign-off). Kept for reference.*

If you want to stay close to the brief's ±4 h estimate, build this subset with simplified scope:

| Ticket(s) | Simplification |
|---|---|
| 001, 002, 003 | As specified, minimal |
| 004–009 | Merge into one "Domain model" ticket; skip status history (FR-47) |
| 010 | SQLite as specified (keeps unique constraint) |
| 012–019 | Skip by-reference lookup (017 part) and search (018 part); keep paging + status filter |
| 020 | As specified |
| 022–026 | Keep totals, transitions, fingerprint, submit + concurrency tests; drop validator boundary exhaustiveness |
| 028–033 | Keep list, form, outcomes, detail; plain styling |
| 037, 038 | Shorter versions |
| 042 | Required |

Defer: 011 (seed), 021, 027, 034–036, 039–040.

## Appendix B — Jira import notes

- **Issue types:** Epic → Epic; Story/Task/Test/Docs → Story or Task (map Test/Docs to Task with a label `test` / `docs`).
- **Labels:** `backend`, `frontend`, `domain`, `api`, `tests`, `docs`, `idempotency` (apply `idempotency` to 005, 009, 015, 016, 024, 026, 032).
- **Story points:** 0.25–0.5 h → 1 · 0.75–1 h → 2 · 1.5 h → 3.
- **Links:** "Depends on" column → "is blocked by" issue links.
- **Description:** paste Description + Tasks (as checklist) + Acceptance Criteria; Refs as a "Requirements" field or in the description footer.
