# Fleet Maintenance & Logistics (FML)

A single ASP.NET Core (.NET 8) Web API monolith for a marine fleet operator. It covers
asset health and maintenance on one side and supply plus fleet insights on the other,
with an auth/user module used by both. Everything lives in one project, one solution,
one `DbContext` and one PostgreSQL connection string — this is the source system for a
later microservices migration exercise, so the current coupling is deliberate and
documented rather than hidden.

## Running it locally

```bash
docker compose up -d                     # PostgreSQL 16 on localhost:5432 (fml/fml/fml)
dotnet run --project src/FML.Api         # http://localhost:5080
dotnet test                              # 38 baseline tests
```

The database schema is created on startup (`EnsureCreated`) and seeded with the sample
fleet when it is empty. Swagger UI is at `/swagger`, a liveness probe at `/health` and
the Prometheus scrape endpoint at `/metrics`. `requests.http` exercises every endpoint;
seeded users log in with `<username>-pw` (e.g. `planner` / `planner-pw`).

Configuration lives in `appsettings.json` under `ConnectionStrings:Fml` and `Fml:*`
(fleet name, telemetry thresholds, maintenance interval, reorder policy, auth settings).

## Business journeys

### 1. Asset Health & Maintenance

| Module | Folder | Responsibility |
| --- | --- | --- |
| Asset Registry | `src/FML.Api/AssetRegistry` | Vessels, engines, cranes, pumps and generators: specs, home port, operating hours, service status. |
| Telemetry Ingestion | `src/FML.Api/Telemetry` | Sensor readings per asset (engine temperature, vibration, oil pressure, fuel rate, …), threshold evaluation, a simulator for demos. |
| Maintenance Scheduling | `src/FML.Api/MaintenanceScheduling` | Work orders with priority, trigger, assignee and part lines; condition-based work orders raised from telemetry breaches; a preventive sweep over assets past their service interval. |

The journey runs end to end: a reading is ingested, `FmlConfig` decides whether it
breached a threshold, a critical condition-based work order is raised for the asset and
assigned to an available technician, starting it moves the asset to `UnderMaintenance`,
and completing it issues the parts, restocks what dropped below its reorder point and
puts the asset back `InService`.

### 2. Supply & Fleet Insights

| Module | Folder | Responsibility |
| --- | --- | --- |
| Parts & Inventory | `src/FML.Api/Inventory` | Parts catalogue, stock on hand, stock movements, purchase orders; reorder logic driven by both stock levels and open work-order demand. |
| Reporting | `src/FML.Api/Reporting` | Cross-journey aggregates: fleet health, maintenance cost, inventory pressure, per-asset dossier. |

### Cross-cutting

| Module | Folder | Responsibility |
| --- | --- | --- |
| Auth / Users | `src/FML.Api/Auth` | `AuthService`: salted-hash login returning an opaque demo token, technician lookup for work-order assignment. The `User`/`UserRole` model and the login/user contracts come from `Fml.Common`. |
| Shared plumbing | `src/FML.Api/Common`, `src/FML.Api/Data` | `FmlConfig` static configuration and threshold rules, the shared `FmlDbContext`, `SeedData`. The activity source, counters and configuration readers come from `Fml.Common`. |

### Shared library

The cross-cutting primitives are no longer in this repo: they live in
[`ipsorakis/fleet-maintenance-logistics-common`](https://github.com/ipsorakis/fleet-maintenance-logistics-common)
and are consumed as the `Fml.Common` NuGet package (`0.1.0`, semantic versioning, published
to GitHub Packages). What moved:

| From | To |
| --- | --- |
| `FML.Api.Auth.User` / `UserRole` | `FML.Common.Auth` |
| `LoginRequest`, `LoginResponse`, `CreateUserRequest` | `FML.Common.Auth` |
| password hashing and token issuing in `AuthService` | `FML.Common.Auth.PasswordHasher` / `AuthTokenFactory` (called by `AuthService` with the configured salt) |
| `FML.Api.Common.FmlTelemetry` | `FML.Common.Observability.FmlTelemetry` |
| `FmlConfig`'s private `IConfiguration` readers | `FML.Common.Configuration.ConfigurationReader` |

`AuthService`, `FmlDbContext`, `FmlConfig`'s thresholds and every module service stayed here —
they are service-specific. `nuget.config` adds the GitHub Packages feed; set
`GITHUB_PACKAGES_USER` and `GITHUB_PACKAGES_TOKEN` (a token with `read:packages`) to restore
locally.

## Entity relationships

```
User 1 ──── * WorkOrder            (AssignedToUserId, ON DELETE SET NULL)

Asset 1 ─── * TelemetryReading     (cascade)
Asset 1 ─── * WorkOrder            (restrict — assets with work orders cannot be deleted)

WorkOrder 1 ─ * WorkOrderPart * ─ 1 Part        (join with QuantityRequired / QuantityIssued)
WorkOrder 0..1 ── TelemetryReading              (TriggeringReadingId — why the work order exists)

Part 1 ──── * StockMovement        (signed quantity: negative issued, positive received)
Part 1 ──── * PurchaseOrder        (Draft → Placed → Received)
StockMovement 0..1 ── WorkOrder    (which job consumed the stock)
```

Tables are snake_case (`assets`, `telemetry_readings`, `work_orders`, `work_order_parts`,
`parts`, `stock_movements`, `purchase_orders`, `users`); `assets.Code` and `parts.Sku`
are unique.

## Seed data

Five users, ten assets (3 vessels, 3 engines, 2 cranes, a pump, a generator), 28
telemetry readings including engine-temperature, vibration and oil-pressure breaches,
five work orders across manual/preventive/condition-based/corrective triggers, eight
parts with matching stock movements and two purchase orders. A few parts are seeded
below or close to their reorder point so `reorder-candidates` and the inventory
pressure report are non-trivial.

## API surface

| Area | Endpoints |
| --- | --- |
| Auth | `POST /api/auth/login`, `GET/POST /api/auth/users`, `GET /api/auth/users/{id}`, `POST /api/auth/users/{id}/deactivate` |
| Assets | `GET /api/assets` (filter by status/type), `GET /api/assets/{id}`, `GET /api/assets/by-code/{code}`, `POST /api/assets`, `PUT /api/assets/{id}`, `DELETE /api/assets/{id}`, `POST /api/assets/{id}/status`, `GET /api/assets/due-for-maintenance` |
| Telemetry | `GET /api/telemetry/readings`, `POST /api/telemetry/readings`, `POST /api/telemetry/readings/batch`, `POST /api/telemetry/simulate/{assetId}`, `GET /api/telemetry/assets/{assetId}/summary`, `GET /api/telemetry/breaches`, `DELETE /api/telemetry/readings/{id}` |
| Work orders | `GET /api/work-orders`, `GET /api/work-orders/{id}`, `POST /api/work-orders`, `PUT /api/work-orders/{id}`, `POST /api/work-orders/{id}/start`, `/complete`, `/cancel`, `/parts`, `POST /api/work-orders/preventive-sweep`, `GET /api/work-orders/overdue` |
| Inventory | `GET/POST /api/inventory/parts`, `GET/PUT/DELETE /api/inventory/parts/{id}`, `GET /api/inventory/reorder-candidates`, `POST /api/inventory/parts/{id}/reorder`, `POST /api/inventory/reorder-run`, `GET /api/inventory/purchase-orders`, `POST /api/inventory/purchase-orders/{id}/receive`, `GET /api/inventory/movements`, `GET /api/inventory/stock-value` |
| Reporting | `GET /api/reports/fleet-health`, `/maintenance-cost`, `/inventory-pressure`, `/assets/{assetId}/dossier` |

## Observability

OpenTelemetry is wired in `Program.cs`: ASP.NET Core and HttpClient instrumentation for
traces plus the `fml-monolith` activity source (spans such as `Telemetry.Ingest`,
`Maintenance.CompleteWorkOrder`, `Reporting.FleetHealth`), exported to the console by
default. Metrics use the same instrumentation plus the `fml-monolith` meter with
`fml.telemetry.readings_ingested`, `fml.maintenance.work_orders_created`,
`fml.inventory.parts_reordered` and `fml.reporting.reports_generated`, exposed for
Prometheus at `/metrics`.

## Tests

`tests/FML.Tests` is an xUnit baseline over an in-memory database, wiring the real
service graph (`FmlTestContext`) so cross-module behavior is exercised rather than
mocked away: asset CRUD and filtering, telemetry ingestion and breach-driven work-order
creation, maintenance scheduling/start/complete and the preventive sweep, inventory
reorder logic including work-order demand, report generation, and auth/login. These
tests are the reference for verifying behavior is preserved after a future migration.

## Known coupling

This is the interesting part for a decomposition pass. Nothing below is a bug — it is
what the monolith actually looks like today, and it is what a split would have to break.

- **One database, one context.** `FmlDbContext` maps every module's entities and is
  injected into every service, so any query can join across module boundaries and a
  single `SaveChangesAsync` commits changes spanning several modules atomically.
- **One shared static config.** `FmlConfig` is a static class loaded once at startup.
  Telemetry thresholds (`FmlConfig.IsBreach`), the maintenance due window, the reorder
  multiplier, the stock-value alert and the password salt all live there and are read
  directly by whichever module needs them.
- **Telemetry → Maintenance.** `TelemetryService.IngestAsync` calls
  `MaintenanceService.CreateFromTelemetryAsync` synchronously inside the ingest request,
  so raising a work order is part of the same transaction as storing a reading.
- **Maintenance → Assets, Inventory, Auth.** `MaintenanceService` validates assets and
  mutates their status through `AssetService`, issues and reorders parts through
  `InventoryService`, and picks an assignee through `AuthService.FindAvailableTechnicianAsync`.
  `CompleteWorkOrderAsync` alone touches maintenance, inventory and asset state.
- **Inventory → Maintenance tables.** `InventoryService.GetPartsBelowReorderPointAsync`
  queries `WorkOrderParts` and filters on `WorkOrder.Status` to fold open work-order
  demand into the reorder decision, without going through `MaintenanceService`.
- **Reporting → everything.** `ReportService` reads `Assets`, `WorkOrders`,
  `TelemetryReadings`, `Parts` and `PurchaseOrders` straight from the shared context and
  also calls `MaintenanceService`, `InventoryService` and `TelemetryService` for their
  logic — the reports are only correct because all the data happens to be co-located.
- **Entity navigations cross modules.** `WorkOrder` navigates to `Asset`, `User` and
  `Part`; `TelemetryReading` navigates to `Asset`; `StockMovement` navigates to
  `WorkOrder`. Cascade/restrict rules span modules too: an asset cannot be deleted while
  a work order references it.
- **Seed data spans modules.** `SeedData` constructs users, assets, readings, work
  orders, parts, movements and purchase orders in one unit of work and calls
  `AuthService.HashPassword` for the user rows.
- **Shared observability primitives.** Every module emits spans and counters through the
  single `FmlTelemetry` activity source and meter, now provided by the `Fml.Common` package.
- **One deployment unit.** All modules share the same host, DI container, JSON settings
  and startup path in `Program.cs`, so they scale, deploy and fail together.
