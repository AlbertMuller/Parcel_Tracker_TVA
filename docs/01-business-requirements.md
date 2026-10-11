# Business Requirements Analysis (BRA)
## Order Intake & Tracking

| Field | Value |
|---|---|
| Document | Business Requirements Analysis |
| Project | Order Intake & Tracking (OfferZen take-home, Senior .NET + Angular) |
| Author | Albert Muller |
| Version | 1.1 |
| Date | 2026-10-11 |
| Status | Signed off as built (changes in §17) |

---

## 1. Purpose

This document defines **what** the Order Intake & Tracking system must do, and **why**. It does not cover how the system is built (see `02-design-document.md`). It is the reference for scope decisions, acceptance testing, and the demo walkthrough.

## 2. Background & Problem Statement

A sales team records customer purchase orders and tracks their progress. Today, reps are often unsure whether a submission went through, so they submit it again. This causes **duplicate orders**, which lead to:

- Double fulfilment or double invoicing risk.
- Manual clean-up effort and reduced trust in the order list.
- Unclear status for both reps and customers.

The business wants a **simple, reliable internal tool** that:

1. Captures orders.
2. Prevents accidental duplicates when the same client reference is resubmitted.
3. Shows what has been submitted and the current status of each order.

## 3. Business Objectives

| ID | Objective | Measure of success |
|---|---|---|
| BO-1 | Eliminate accidental duplicate orders | Resubmitting the same client reference never creates a second order (0 duplicates in tests, including concurrent submissions) |
| BO-2 | Give reps confidence that an order was captured | After any submit (first or repeat), the rep sees the stored order and a clear message |
| BO-3 | Ensure order values are correct | Line totals, subtotal and total are always calculated by the server; the client's figures are never trusted |
| BO-4 | Make order progress visible | Reps can list orders (newest first), open any order, and see its current status |
| BO-5 | Keep status changes sensible | Only valid status transitions are allowed; invalid ones get a clear, human-readable explanation |
| BO-6 | Make the system low-friction to run and evaluate | A reviewer can run backend and frontend locally in a few documented steps |

## 4. Stakeholders & Users

| Stakeholder | Role | Interest |
|---|---|---|
| Sales rep (primary user) | Submits orders, checks status | Fast entry, certainty that the order is captured, no duplicates |
| Sales manager / ops (secondary user) | Reviews orders, moves them through the flow | Accurate list, correct totals, controlled status changes |
| Assessor (OfferZen / hiring team) | Evaluates the solution | Correctness, design quality, tests, documentation, clarity of trade-offs |

## 5. Scope

### 5.1 In scope

- Submitting a new purchase order with customer details and one or more line items.
- Duplicate prevention based on the **client-provided external reference**.
- Server-side calculation of line totals, subtotal and total.
- Retrieving a single order.
- Listing orders, most recent first.
- Changing order status under defined rules, with helpful feedback on invalid changes.
- Angular UI to submit, list, and view orders.
- Unit tests for critical backend business rules.
- README (run instructions) and SOLUTION (design and trade-offs).
- Video demo (5–10 min).
- *Added in v1.1:* re-pricing a Pending order in another currency at live exchange rates (§7.7).
- *Added in v1.1:* one-command local run with Docker Compose, including a PostgreSQL database.

### 5.2 Optional (if time permits)

- Changing status from the UI.
- Client-side filtering of the order list (by status, reference, customer).
- Status change history on the order.
- Frontend unit tests.

### 5.3 Out of scope

- Authentication, authorisation, and user management.
- Multi-tenancy (references are unique across the whole system).
- Tax, discounts or shipping. *(Currency conversion moved into scope in v1.1, see §7.7.)*
- Product catalogue / SKU validation against master data.
- Editing an order's line items or customer after submission.
- Deleting orders (cancellation is a status, not a deletion).
- Payments, invoicing, notifications, integrations with ERP/CRM.
- Production deployment, scaling, and high availability.

## 6. Business Concepts (Glossary)

| Term | Definition |
|---|---|
| **Order** | A customer's purchase order captured by a rep. |
| **External reference** | The reference supplied by the client/rep (e.g. the customer's PO number). It identifies a submission for duplicate detection. |
| **Order ID** | The system-generated unique identifier of an order. |
| **Customer** | The buyer on the order: name and email. |
| **Line item** | One product on an order: SKU, name, quantity, unit price. |
| **Line total** | Quantity × unit price, calculated by the server. |
| **Subtotal** | Sum of all line totals. |
| **Total** | Amount payable. In this version, total = subtotal (no tax/discounts). |
| **Currency** | ISO 4217 three-letter code for all amounts on the order (one currency per order). |
| **Status** | Where the order is in the sales flow: Pending, Confirmed, Fulfilled, Cancelled. |
| **Repeat submission** | Submitting an order with an external reference that already exists. |

## 7. Functional Requirements

Priority uses MoSCoW: **M**ust, **S**hould, **C**ould, **W**on't (this release).

### 7.1 Order submission

| ID | Requirement | Priority |
|---|---|---|
| FR-01 | The user can submit an order with: external reference, customer name, customer email, currency, optional notes, and one or more line items. | M |
| FR-02 | Each line item has SKU, product name, quantity, and unit price. | M |
| FR-03 | On a successful first submission, the system stores the order with status **Pending**, a system-generated ID, and created/updated timestamps. | M |
| FR-04 | The system returns the stored order, including server-calculated totals, to the user. | M |
| FR-05 | The order must have at least one line item. | M |
| FR-06 | Notes are optional free text with a sensible maximum length. | S |

### 7.2 Duplicate prevention (repeat submissions)

| ID | Requirement | Priority |
|---|---|---|
| FR-10 | An external reference can be associated with **at most one order**. | M |
| FR-11 | When an order is submitted with a reference that already exists **and the same content**, the system does **not** create a new order. It returns the existing order and tells the user it was already captured. | M |
| FR-12 | When an order is submitted with a reference that already exists **but different content**, the system does **not** create a new order and does **not** overwrite the existing one. It rejects the submission with a clear message that the reference is already in use for a different order, and identifies the existing order. | M |
| FR-13 | Duplicate prevention holds even when the same reference is submitted at nearly the same time (e.g. double-click, two tabs, network retry). | M |
| FR-14 | Reference matching ignores leading/trailing whitespace and letter case (e.g. `po-1001 ` and `PO-1001` are the same reference). | S |
| FR-15 | A repeat submission is answered consistently regardless of the order's current status (e.g. still recognised as a duplicate after it is Confirmed or Cancelled). | M |
| FR-16 | The UI prevents accidental double submission (submit button disabled while a submission is in progress). | S |

### 7.3 Totals calculation

| ID | Requirement | Priority |
|---|---|---|
| FR-20 | Line total = quantity × unit price, calculated on the server. | M |
| FR-21 | Subtotal = sum of line totals; total = subtotal. Both calculated on the server. | M |
| FR-22 | Any totals sent by the client are ignored. | M |
| FR-23 | Amounts are rounded to 2 decimal places using a consistent, documented rounding rule. | M |
| FR-24 | The UI may show an estimated total while typing, but the saved order displays server-calculated values. | C |

### 7.4 Viewing orders

| ID | Requirement | Priority |
|---|---|---|
| FR-30 | The user can list orders, sorted by creation time, **newest first**. | M |
| FR-31 | The list shows at least: external reference, customer, status, total with currency, and created date. | M |
| FR-32 | The user can open a single order and see all details: customer, line items with line totals, subtotal, total, currency, status, notes, timestamps. | M |
| FR-33 | The user can look up an order by its external reference (to answer "did it go through?"). *(v1.1: not built as a dedicated lookup; the list search finds references. Open item.)* | S |
| FR-34 | The list supports paging so it stays usable as orders grow. | S |
| FR-35 | The user can filter the list by status and search by reference or customer. | C |

### 7.5 Status management

| ID | Requirement | Priority |
|---|---|---|
| FR-40 | Orders support statuses: **Pending, Confirmed, Fulfilled, Cancelled**. | M |
| FR-41 | Only the following transitions are allowed: Pending → Confirmed; Pending → Cancelled; Confirmed → Fulfilled; Confirmed → Cancelled. | M |
| FR-42 | Fulfilled and Cancelled are final; no further changes allowed. | M |
| FR-43 | When a transition is not allowed, the system rejects it with a helpful message stating the current status, the requested status, and which statuses are allowed from the current one (or that the order is final). | M |
| FR-44 | Requesting a change to the status the order already has is rejected with a clear "already in this status" message. *(v1.1: changed. It's accepted as a no-op and returns the order unchanged, so a double-click isn't an error.)* | S |
| FR-45 | A successful status change updates the order's "last updated" timestamp. | M |
| FR-46 | A cancellation can include an optional reason. *(v1.1: not built.)* | C |
| FR-47 | The system records a history of status changes (from, to, when, reason). *(v1.1: built without the reason.)* | C |
| FR-48 | The UI only offers status changes that are currently allowed. | C |
| FR-49 | If two users change the same order's status at the same time, one succeeds and the other gets a clear "order was changed by someone else, please refresh" message. *(v1.1: one always succeeds; the other is re-checked against the new state and gets that rule's message, e.g. "already fulfilled".)* | S |

### 7.6 Validation rules

| ID | Rule | Priority |
|---|---|---|
| VR-01 | External reference is required, 1–64 characters after trimming. Allowed characters: letters, digits, `-`, `_`, `/`, `.`. *(v1.1: length enforced; the character list isn't (only control characters are rejected). Open decision.)* | M |
| VR-02 | Customer name is required, 1–200 characters. | M |
| VR-03 | Customer email is required and must be a valid email format (max 254 characters). *(v1.1: max 320.)* | M |
| VR-04 | Currency is required and must be a supported ISO 4217 code (initially ZAR, USD, EUR, GBP). *(v1.1: any 3-letter ISO 4217 code is accepted; the UI offers the ~30 currencies that have live rates.)* | M |
| VR-05 | At least 1 and at most 100 line items. | M |
| VR-06 | SKU is required, 1–64 characters. Product name is required, 1–200 characters. | M |
| VR-07 | Quantity must be a **positive whole number** (≥ 1), with a sensible upper limit (e.g. 1,000,000). | M |
| VR-08 | Unit price must be **non-negative** (≥ 0) with at most 2 decimal places and a sensible upper limit. | M |
| VR-09 | Notes are optional, max 2,000 characters. *(v1.1: max 1,000.)* | S |
| VR-10 | All validation errors are returned together, each tied to the field it relates to, in plain language. | M |
| VR-11 | The UI validates the same rules before submitting, but the server remains the authority. | S |

### 7.7 Currency conversion *(added in v1.1)*

| ID | Requirement | Priority |
|---|---|---|
| FR-60 | The user can re-price a **Pending** order in another currency at the latest available exchange rate. | S |
| FR-61 | Once an order is Confirmed (or final), its currency and prices are locked; a conversion attempt is rejected with a message saying why. | S |
| FR-62 | Conversions always start from the prices as submitted, so converting back to the original currency restores them exactly and rounding never accumulates. | S |
| FR-63 | Each conversion is recorded with the rate, the rate date, the rate source and when it happened. | S |
| FR-64 | Before converting, the user sees the rate and the new total. | C |
| FR-65 | The order list can show all totals converted into one chosen currency, for display only, labelled with the rate date. | C |
| FR-66 | If live rates can't be fetched, orders can still be taken; conversion is paused with a clear message, and recently fetched rates may be used (flagged as not live). | S |

Rates are the European Central Bank's daily reference rates (mid-market), which suit an internal sales tool but not payment settlement.

## 8. Business Rules Summary

1. **One reference, one order.** A client reference can never produce more than one order.
2. **Same reference + same content = "already captured"**, not an error and not a new order.
3. **Same reference + different content = conflict**, never a silent overwrite.
4. **Server owns the money.** Totals are always calculated on the server.
5. **Quantities ≥ 1, whole numbers. Prices ≥ 0.**
6. **Status moves forward only**, along the allowed transitions; Fulfilled and Cancelled are final.
7. **Newest first** in all order lists.
8. **Every rejection explains itself** in language a sales rep understands.

## 9. Status Flow

```
          ┌──────────┐   confirm   ┌───────────┐   fulfil   ┌───────────┐
  new ──▶ │ Pending  │ ──────────▶ │ Confirmed │ ─────────▶ │ Fulfilled │ (final)
          └────┬─────┘             └─────┬─────┘            └───────────┘
               │ cancel                  │ cancel
               ▼                         ▼
          ┌─────────────────────────────────┐
          │            Cancelled            │ (final)
          └─────────────────────────────────┘
```

| From \ To | Pending | Confirmed | Fulfilled | Cancelled |
|---|---|---|---|---|
| **Pending** | ✖ (already) | ✔ | ✖ (must confirm first) | ✔ |
| **Confirmed** | ✖ (no going back) | ✖ (already) | ✔ | ✔ |
| **Fulfilled** | ✖ final | ✖ final | ✖ final | ✖ final |
| **Cancelled** | ✖ final | ✖ final | ✖ final | ✖ final |

## 10. User Stories & Acceptance Criteria

### US-01 Submit a new order
*As a sales rep, I want to capture a customer's purchase order so that it can be processed.*

- **Given** a valid order with a new reference, **when** I submit it, **then** it is saved as Pending, I see the saved order with server-calculated totals, and a success message.
- **Given** an order with invalid fields, **when** I submit it, **then** nothing is saved and I see each problem next to the relevant field.

### US-02 Resubmit safely
*As a sales rep who is unsure whether my order went through, I want to submit it again without creating a duplicate.*

- **Given** an order with reference `PO-1001` exists, **when** I submit the same order again, **then** no new order is created, and I see the existing order with a message such as "This order was already submitted on 11 Oct 2026 at 14:02. No duplicate was created."
- **Given** `PO-1001` exists, **when** I submit `PO-1001` with different line items, **then** nothing is created or changed, and I see "Reference PO-1001 is already used by a different order" with a link to that order.
- **Given** I double-click submit, **then** only one order exists.

### US-03 See recent orders
*As a sales rep, I want to see the latest orders first so I can quickly confirm what was captured.*

- **Given** several orders, **when** I open the order list, **then** the most recently created order appears at the top, with reference, customer, status, total and date.

### US-04 Verify an order's details
*As a sales rep, I want to open an order to check its items, totals and status.*

- **Given** an order, **when** I open it, **then** I see each line with quantity, unit price and line total, plus subtotal, total, currency, status and timestamps.

### US-05 Move an order through the flow
*As a sales manager, I want to change an order's status so the team knows where it stands.*

- **Given** a Pending order, **when** I confirm it, **then** its status becomes Confirmed and "last updated" changes.
- **Given** a Fulfilled order, **when** I try to cancel it, **then** the change is rejected with "Order is Fulfilled, which is final. No further status changes are allowed."
- **Given** a Pending order, **when** I try to fulfil it, **then** the change is rejected with "Cannot change from Pending to Fulfilled. Allowed: Confirmed, Cancelled."

### US-06 Find orders (optional)
*As a sales rep, I want to filter the list by status or search by reference/customer.*

- **Given** a list of orders, **when** I pick "Confirmed", **then** only Confirmed orders show, still newest first.

## 11. Non-Functional Requirements

| ID | Category | Requirement |
|---|---|---|
| NFR-01 | Technology | Backend: C#, ASP.NET Core, .NET 8 LTS or later. Frontend: Angular 18+, TypeScript 5+. |
| NFR-02 | Setup | Runs locally with minimal steps and no cloud services. *(v1.1: one `docker compose up` starts the PostgreSQL database, migrations, API and UI. Live exchange rates need internet access; everything else works offline.)* Documented in README. |
| NFR-03 | Reliability | No duplicate orders under concurrent repeat submissions. |
| NFR-04 | Data integrity | Money is stored and calculated with decimal precision (no floating-point). |
| NFR-05 | Consistency | Error responses follow one consistent format with machine-readable codes and human-readable messages. *(v1.1: one format (ProblemDetails) with readable messages; stable `code` values are an open item.)* |
| NFR-06 | Testability | Core business rules (totals, validation, duplicates, transitions) covered by automated unit tests that run with one command. |
| NFR-07 | Usability | UI is minimal, clear and focused on the submit → list → detail → status flow. |
| NFR-08 | Performance | Typical API responses under 200 ms locally for up to a few thousand orders. |
| NFR-09 | Observability | Structured request logging; duplicate submissions and rejected transitions are logged. *(v1.1: framework logging only; business-event logging is an open item.)* |
| NFR-10 | Documentation | README (run), SOLUTION (design, trade-offs), and API documentation (OpenAPI/Swagger). |
| NFR-11 | Time | Target effort about 4 hours for the core; optional items only if time allows. |

## 12. Assumptions

- A-1: The external reference is chosen by the rep (typically the customer's PO number) and is reused when the rep resubmits.
- A-2: References are unique system-wide (single sales team, no tenants).
- A-3: Order content is not editable after submission; corrections are done by cancelling and submitting a new order with a new reference.
- A-4: One currency per order at a time. *(v1.1: a Pending order can be re-priced into another currency, see §7.7.)*
- A-5: No authentication; any user can perform any action.
- A-6: Local single-instance deployment is sufficient for the assignment.
- A-7: Timestamps are stored in UTC and displayed in the user's local time.

## 13. Constraints

- C-1: Mandated technologies (see NFR-01).
- C-2: Single GitHub repository with README.md and SOLUTION.md.
- C-3: 5–10 minute video demo with voice-over.

## 14. Risks

| Risk | Impact | Mitigation |
|---|---|---|
| Ambiguous "consistent behaviour" for duplicates | Reviewer disagrees with chosen behaviour | Document the decision and alternatives in SOLUTION.md; cover with tests |
| Race conditions on concurrent submits | Duplicates slip through | Database unique constraint as final safeguard, plus handling of the constraint violation; concurrency test |
| Decimal/rounding errors | Wrong totals | `decimal` throughout, explicit rounding rule, unit tests with edge cases |
| Scope creep | Exceeds timebox | MoSCoW priorities; optional items clearly separated |
| Setup friction for reviewer | Poor first impression | One-command Docker Compose setup, clear README *(v1.1: replaces zero-dependency SQLite storage)* |

## 15. Deliverables

1. GitHub repository with all source code.
2. `README.md`: prerequisites and local run instructions.
3. `SOLUTION.md`: design decisions and trade-offs.
4. Backend unit tests for critical business rules.
5. Video demo (5–10 minutes): working flow, architecture, decisions, notable implementations.

## 16. Traceability

| Objective | Requirements |
|---|---|
| BO-1 | FR-10 – FR-16 |
| BO-2 | FR-04, FR-11, FR-33 |
| BO-3 | FR-20 – FR-24, VR-07, VR-08 |
| BO-4 | FR-30 – FR-35 |
| BO-5 | FR-40 – FR-49 |
| BO-6 | NFR-02, NFR-10 |

## 17. Change Log

### v1.1 (2026-10-11): signed off as built

Reviewed against the delivered system. Full ticket-level evidence is in `03-todo-backlog.md` (Sign-off).

**Added to scope**
- Currency conversion of Pending orders with live exchange rates, display conversion on the list (§7.7, FR-60–FR-66).
- Docker Compose with PostgreSQL as the standard way to run the system (NFR-02).

**Requirements met differently (accepted)**
- FR-44: re-requesting the current status is a harmless no-op instead of an error.
- FR-47: status history is recorded without a reason (FR-46 not built).
- FR-49: concurrent changes are resolved server-side; the slower request gets the message for the order's new state.
- VR-03, VR-04, VR-09: email max 320; any ISO 4217 currency code; notes max 1,000.
- US-05 wording: rejections read, for example, "This order has already been fulfilled, so it can no longer be cancelled." and "This order must be confirmed before it can be fulfilled. Allowed next statuses: Confirmed, Cancelled."
- US-02 wording: the replay message reads "This order was already submitted … No duplicate was created." The submitted date is shown on the order page rather than in the message.

**Open (see backlog "Open items after sign-off")**
- FR-33 lookup by reference; VR-01 reference character list (decide whether to enforce or relax); NFR-05 stable error codes; NFR-09 business-event logging.
