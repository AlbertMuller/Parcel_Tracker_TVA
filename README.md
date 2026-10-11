# Order Tracker

A small internal app for recording customer purchase orders and tracking their status. Submitting the
same order reference twice never creates a duplicate.

- **Backend:** ASP.NET Core minimal API (.NET 10), in-memory storage, xUnit tests
- **Frontend:** Angular 21 (standalone components, signals, reactive forms)

See [SOLUTION.md](SOLUTION.md) for design decisions and trade-offs.

## Prerequisites

| Tool | Version |
|------|---------|
| .NET SDK | 10.0.401 or a later 10.0.4xx patch (pinned in [backend/global.json](backend/global.json)) |
| Node.js | 20.19+, 22.12+ or 24+ |
| npm | 10+ |

## Run locally

Open two terminals from the repository root.

**1. API** (http://localhost:5080, Swagger UI at http://localhost:5080/swagger)

```bash
cd backend
dotnet run --project src/OrderTracker.Api
```

**2. Web app** (http://localhost:4200)

```bash
cd frontend
npm install
npm start
```

Open http://localhost:4200. The Angular dev server proxies `/api` to the API on port 5080
([proxy.conf.json](frontend/proxy.conf.json)), so no CORS setup is needed.

Data is kept in memory and resets when the API restarts.

## Run with Docker

Requires Docker with Compose v2. From the repository root:

```bash
docker compose up --build -d
```

| URL | What |
|-----|------|
| http://localhost:8080 | Web app (nginx serving the Angular build, proxying `/api` to the API container) |
| http://localhost:8081/swagger | API Swagger UI (`/health` for the health check) |

Change the host ports with `WEB_PORT` / `API_PORT`, e.g. `WEB_PORT=9000 docker compose up -d`.
Stop with `docker compose down`. Data is in memory, so it resets when the API container restarts.

The setup:

- **API** ([backend/Dockerfile](backend/Dockerfile)): built with the .NET SDK 10.0.401 image and run
  on `aspnet:10.0` as a non-root user. It has a health check on `/health`, and Swagger is turned on via `Swagger__Enabled=true`.
- **Web** ([frontend/Dockerfile](frontend/Dockerfile)): built with Node 24, served by unprivileged nginx on port 8080.
  It handles SPA deep links and caches hashed assets long-term. The API address is set by `API_UPSTREAM` (default `http://api:8080`).
- The web container starts only after the API reports healthy. The browser talks to a single origin, so no CORS is needed.
- Run the backend tests inside the build image with `docker build --target test backend`.

## Run the tests

```bash
# Backend: unit tests for business rules + HTTP integration tests
cd backend
dotnet test

# Frontend (optional): Vitest unit tests
cd frontend
npm test -- --watch=false
```

## Try the duplicate protection

In the UI, create an order, then open **New order** again, enter the same reference and the same
details, and submit. You land on the original order with a notice that it was already submitted.
Change the line items and submit with that reference again: you get a conflict message with a link
to the existing order.

The same from the command line:

```bash
BODY='{"externalReference":"PO-1001","customer":{"email":"buyer@acme.com","name":"Acme Ltd"},
       "currency":"USD","lineItems":[{"sku":"BOLT-10","name":"Bolt","quantity":100,"unitPrice":0.15}]}'

curl -i -X POST localhost:5080/api/orders -H 'Content-Type: application/json' -d "$BODY"   # 201 Created
curl -i -X POST localhost:5080/api/orders -H 'Content-Type: application/json' -d "$BODY"   # 200 OK, Idempotent-Replay: true
```

## API

| Method | Path | Description |
|--------|------|-------------|
| `POST` | `/api/orders` | Submit an order. `201` new, `200` same reference and same details already submitted (header `Idempotent-Replay: true`), `409` reference already used for different details, `400` validation errors. |
| `GET` | `/api/orders` | List orders, newest first. Query: `status`, `search` (reference, customer name or email), `page`, `pageSize` (max 200). |
| `GET` | `/api/orders/{id}` | Order details, including line totals, subtotal, total, allowed next statuses and status history. |
| `PATCH` | `/api/orders/{id}/status` | Body `{ "status": "Confirmed" }`. `200` on success (or if already in that status), `409` with an explanation if the change isn't allowed, `400` unknown status, `404` unknown order. |

| `PATCH` | `/api/orders/{id}/currency` | Body `{ "currency": "EUR" }`. Re-prices a **Pending** order at the latest exchange rate. `200` on success, `409` if the order is no longer Pending, `422` if no rate exists for that pair, `503` if rates can't be fetched, `400` bad code. |
| `GET` | `/api/currencies` | Supported currencies (`{ "EUR": "Euro", … }`). |
| `GET` | `/api/exchange-rates?base=USD` | Latest rates for a base currency, with the rate date, the source and whether they're stale. |

Exchange rates are the European Central Bank's daily reference rates via [Frankfurter](https://frankfurter.dev)
(free, no API key). The API needs outbound internet access to fetch them. Everything else works offline, and the
form falls back to a built-in currency list. The source URL and cache timings can be changed under `ExchangeRates` in
[appsettings.json](backend/src/OrderTracker.Api/appsettings.json) or through environment variables (e.g. `ExchangeRates__BaseUrl`).

Errors use RFC 7807 problem details. Validation errors are keyed by JSON path, e.g. `lineItems[0].quantity`.

Status flow:

```
Pending ──► Confirmed ──► Fulfilled
   │            │
   └────────────┴──► Cancelled        (Fulfilled and Cancelled are final)
```

## Project layout

```
backend/
  OrderTracker.sln
  src/OrderTracker.Api/
    Domain/           Order, status rules, pricing, normalization  (pure, no ASP.NET)
    Application/      OrderService (use cases), CreateOrderValidator
    Infrastructure/   In-memory repository, bad-request handler
    Contracts/        Request / response DTOs
    Endpoints/        HTTP mapping (status codes, problem details)
  tests/OrderTracker.Tests/
frontend/
  src/app/orders/     list, form and detail pages, API client, models
  src/app/shared/     status badge, error helper
```
