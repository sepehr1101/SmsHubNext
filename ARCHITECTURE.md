# SmsHubNext — Application Architecture

> **Status:** **Living draft — decision-complete.** The *data model* is locked (`README.md`); this *application* design is agreed. Implementation not started (pre-Phase 0).
> **Optimize for (in order):** **1. Simplicity · 2. Readability · 3. Developer productivity · 4. Maintainability.** A developer should open a feature folder and find almost everything for that business capability in one place.

---

## 1. How the code is organized — by feature

This is a small-to-medium **monolithic backend service** that should **stay simple for years**. Code is organized **by business feature, not by technical layer** — the inspiration is the coding style and project organization of **DntSite**, nothing more.

**We are not implementing any named architecture** — not Clean, Onion, Hexagonal, CQRS, "Vertical Slice", or microservices, and no MediatR or FluentValidation. There is no pattern to satisfy. We just keep the code for one capability together and keep it readable.

- **Everything for a feature lives together** — its endpoints, requests, handler/service, validation, Dapper SQL, models, and mapping, in one folder.
- **No global technical buckets** — no `Services/`, `Repositories/`, `DTOs/`, `Validators/`, or `Interfaces/` folders pulling unrelated code together.
- **Shared code stays small** — only genuinely cross-cutting things.
- **Features don't call each other** — anything shared moves into `Shared/`.
- **Explicit over generic** — straightforward code a developer grasps in a few minutes beats a clever abstraction.
- **One project, one deployable.** A "feature" is just a folder.

**Abstractions:** if an abstraction (interface/wrapper/base class) genuinely makes the code **simpler**, use it. If it mainly exists to satisfy a pattern or a "best practice," don't — write the concrete code. When several solutions are valid, pick the simpler one.

---

## 2. Decisions (summary)

| # | Decision | One-line rationale |
|---|---|---|
| 1 | **Organized by feature; one deployable** | Related code lives together; no cross-folder hopping per feature. |
| 2 | **Dapper + feature-local SQL; no Repository / Unit-of-Work / persistence layer / repo interfaces** | They hide the hand-tuned SQL that is the point and add zero swappability. |
| 3 | **ASP.NET Core Controllers; no MediatR, no FluentValidation, no AutoMapper** | Endpoints call plain feature handlers; validation and mapping are explicit, local code. |
| 4 | **Very few abstractions, each justified** | `ISmsProvider` (≥2 impls) and BCL `TimeProvider` (test determinism). That's essentially it. |
| 5 | **Result pattern for expected failures; exceptions for the unexpected** | Predictable control flow + one Result→HTTP mapping at the edge. |
| 6 | **Hosting: Windows + IIS, in-process background work** | One deployable; resumable outbox tolerates app-pool recycles. |
| 7 | **Reliable dispatch via SQL-backed jobs; transport is swappable (built-in BackgroundService), not baked in** | Feature job-logic stays scheduler-agnostic. |
| 8 | **Secrets: provider credentials encrypted in SQL Server** (`ProviderCredential`) | App-side Data Protection (DPAPI key, outside the DB). |
| 9 | **Migrations: DbUp** · **Logging: Serilog (no OpenTelemetry)** · **Result: hand-rolled** | Least ceremony; forward-only raw SQL; structured logs without OTel weight. |

---

## 3. Solution structure

**3 projects:** one application + two test projects. Within the app, **`Features/` dominates**; everything else is small.

```
SmsHubNext/
├─ SmsHubNext.slnx
├─ src/
│  └─ SmsHubNext/                      # THE single deployable (ASP.NET Core, .NET 10)
│     ├─ Program.cs                    # minimal composition root (builder → extensions → run)
│     ├─ Extensions/                   # composition root wiring: DI, HTTP pipeline, DB bootstrap
│     │
│     ├─ Features/                     # ← business capabilities (the bulk of the code)
│     │   ├─ Sending/                  # accept + send messages (the core path)
│     │   ├─ Batches/                  # batch status, batch events
│     │   ├─ DeliveryReports/          # DLR ingestion + DeliveryStatus projection
│     │   ├─ Billing/                  # balance debit/refund/top-up, ledger
│     │   ├─ Reports/                  # cost/success-rate/trend queries
│     │   ├─ Tariffs/                  # tariff resolution + admin
│     │   ├─ ApiKeys/                  # issue/rotate/revoke; auth lookup
│     │   ├─ Providers/                # ISmsProvider + Magfa/ (the one real seam)
│     │   └─ ReferenceData/            # GeoSection, SenderLine, MessageType, Customer admin
│     │
│     ├─ Shared/                       # SMALL, truly cross-cutting only
│     │   ├─ Database/                 # connection helper, Dapper setup/type handlers
│     │   ├─ Results/                  # Result / Result<T> / Error + Result→HTTP mapping
│     │   ├─ Security/                 # API-key hashing (SHA-256)
│     │   ├─ Sms/                      # pure: segment counting, encoding detection, cost calc
│     │   └─ Enums/                    # cross-feature enums (SendStatus, DeliveryStatus, BatchStatus…)
│     │
│     ├─ Migrations/                   # raw SQL (DbUp): partitioning, columnstore, filtered indexes
│     └─ appsettings*.json            # provider URLs; connection string (plaintext for now)
│
└─ tests/
   ├─ SmsHubNext.UnitTests/            # Shared/Sms calc, status mapping, Result flow, Jalali
   └─ SmsHubNext.IntegrationTests/     # Dapper + SQL Server (Testcontainers), Magfa via WireMock
```

There is **no `Domain/` / `Application/` / `Infrastructure/`** layering. Persistence models are **feature-local** (each Dapper query projects exactly the columns it needs into a small record next to the query) — so there is no shared "entities" god-folder, and features don't drift against a monolithic model.

**Composition root.** `Program.cs` stays a **minimal** composition root — create the builder, call extension methods, build and run — and nothing else. The wiring lives in `Extensions/`: `ServiceCollectionExtensions.AddApplicationServices` (DI: controllers, OpenAPI, `Db`, feature handlers, health checks), `ApplicationBuilderExtensions.ConfigurePipeline` (Serilog request logging, OpenAPI + Scalar, root endpoint, controller + health-check mapping), and `DatabaseExtensions.MigrateDatabase` (startup migrations). Why: **separation of concerns** (registration vs. pipeline vs. bootstrap are distinct), **maintainability** at the volume this platform grows to (a single ballooning `Program.cs` becomes a merge-conflict magnet), and **consistency with the feature-first monolith** (each feature adds its handler in one obvious place rather than padding the host file).

---

## 4. Anatomy of a feature

A feature folder is flat and holds *everything for that capability*. Example:

```
Features/Sending/
├─ SendMessagesController.cs     # ASP.NET Core controller actions → handler
├─ SendMessages.cs               # SendMessagesRequest + SendMessagesResponse (one use-case contract file)
├─ SendMessagesHandler.cs        # the feature logic (a plain class, injected) → Result<T>
├─ SendSql.cs                    # the Dapper SQL strings + small row records for this feature
└─ (anything else only Sending needs)
```

**Use-case contract files.** For a command or query endpoint, put its `{Operation}Request` and `{Operation}Response` (when both exist) in **one file** named after the operation — e.g. `CreateCustomer.cs`, `TopUp.cs`, `SendMessages.cs`. Type names keep the `Request`/`Response` suffix; only the file is merged. Request-only endpoints use the same naming (`AddIpRestriction.cs` holds `AddIpRestrictionRequest` only). **Do not merge:** shared read models reused across handlers (e.g. `TariffResponse`), provider HTTP DTOs under `Features/Providers/`, domain/row types, or nested item types (`SendMessageItem`).

- **Endpoint** is a thin minimal-API mapping that binds the request, calls the handler, and translates `Result` → HTTP.
- **Routes** carry **no `api/` prefix** — controllers route on the resource directly (e.g. `[Route("messages")]`, `[Route("reference-data/message-types")]`).
- **Handler/service** is a plain class (no MediatR, no base class) holding the feature's logic; dependencies are obvious constructor params.
- **Validation** is plain code (see §6).
- **Data access** is Dapper SQL **in the feature** (see §5).
- A feature may use `Shared/` and `Features/Providers/ISmsProvider`; it **must not** reference another feature.

---

## 5. Data access — Dapper, no ceremony

SQL lives **in the feature** that owns it; the only shared piece is a tiny connection helper in `Shared/Database`.

| Pattern | Verdict | Why |
|---|---|---|
| Generic Repository | **Removed** | Hides the hand-tuned SQL that is the point; can't generically operate a partitioned columnstore fact; zero swappability (not swapping SQL Server). |
| Unit-of-Work | **Removed** | Atomic multi-writes are `using var tx = conn.BeginTransaction()` in the handler. |
| Persistence layer / repo interfaces | **Removed** | They mostly enable mocking SQL — which we don't do; we integration-test against real SQL Server. |

- Open connections via a small concrete `Db` helper (reads the connection string, returns `SqlConnection`). **No interface** — there is no second implementation and we don't mock it.
- Models are **feature-local records** matching each query's projection.

---

## 6. Validation — plain, in-feature (no framework)

No FluentValidation. Each feature validates its own request **explicitly**:

- A `Validate()` method on the request (or guard checks at the top of the handler) returns a **`Result` with `Validation` errors** — never throws for bad input.
- Rules live **in the same use-case contract file as the request** (see §4), not in a global `Validators/` folder.
- The controller maps a validation `Result` to **400** via the single mapper (§7).

*Why no framework:* validation here is simple field/relationship checks; a fluent DSL + a `IValidator<T>` registry is ceremony for little gain, and it scatters rules away from the feature.

---

## 7. Error handling — Result pattern

- **`Result` / `Result<T>` for *expected* outcomes** — validation, business-rule rejections (insufficient balance, unknown line), not-found, idempotency hits, **provider send rejections**.
- **Exceptions for the *unexpected*** — DB down, misconfig, bugs → handled by middleware.
- **Flow:** feature handlers and `ISmsProvider.SendAsync` return `Result<T>` (never throw for expected failures); **the controller owns the single `Result → ProblemDetails` mapping** (error category → HTTP status).
- **Shape (lean, hand-rolled ~1 file):** `Result`, `Result<T>`, `Error(Code, Message, ErrorType)` where `ErrorType ∈ Validation/NotFound/Conflict/Unauthorized/Provider/Unexpected` drives the status code.

---

## 8. Abstractions we keep (and what we deliberately don't)

| Abstraction | Keep? | Justification |
|---|---|---|
| **`ISmsProvider`** | ✅ | **≥2 real implementations** (Magfa now, more later) — genuine polymorphism behind one seam. |
| **`TimeProvider`** (BCL) | ✅ | Test-determinism for Jalali/time logic — and it's the **built-in** .NET 10 type, so **zero custom abstraction**. |
| DB connection helper | concrete class, **no interface** | One implementation; integration tests use a real DB. |
| Repositories / UoW / `IValidator` / generic service interfaces | ❌ removed | No second impl, no real seam — pure ceremony. |

Prefer **composition over inheritance**; avoid base classes unless they remove real duplication.

---

## 9. Background processing — reliable, in-process

Sending is asynchronous (accept → background dispatch → status).

Background work is implemented using the built-in ASP.NET Core hosting infrastructure (`BackgroundService`). Each worker is responsible only for hosting and scheduling execution; all business logic remains inside ordinary feature classes.

Examples include SMS dispatch, delivery-report polling, cleanup, retention and balance reconciliation.

Workers remain intentionally thin. They do not contain business logic, retry rules or SQL. Reliability comes from SQL Server:

- Messages are persisted as `Queued`.
- Workers claim messages atomically.
- Processing is idempotent.
- Status transitions are persisted.
- After an application restart or IIS recycle, workers resume processing from the database.

No external job scheduler is required. The application depends only on the built-in .NET hosting infrastructure.

## 10. Runtime, deployment & infrastructure

- **Hosting:** **Windows + IIS** (ASP.NET Core Module → Kestrel), co-located with SQL Server. SQL auth prefers **Integrated Security** where available.
- **Secrets:** provider credentials **encrypted in SQL Server** (`ProviderCredential`, ciphertext only), decrypted in-app via **ASP.NET Core Data Protection** with the key ring **protected by Windows DPAPI** — key lives **outside** the DB. Connection string plaintext in `appsettings.json` **for now** (temporary; hardening path = SQL **Always Encrypted** + protected config).
- **Migrations:** **DbUp** — ordered, forward-only raw-SQL scripts run at deploy; partitioning/columnstore DDL is hand-written.
- **Logging:** **Serilog** (structured) → console + rolling file (+ Seq optional, Windows Event Log for service errors). **OpenTelemetry intentionally omitted** (simpler is better) — add tracing later only if a real need appears. ASP.NET Core **health checks** for IIS probes.
- **Resilience:** Polly via `Microsoft.Extensions.Http.Resilience` on the Magfa `HttpClient`.
- **Auth:** API key in header; built-in ASP.NET **rate limiter** keyed by API key.
- **API docs:** **OpenAPI** document (built-in `Microsoft.AspNetCore.OpenApi`) at `/openapi/v1.json`, rendered by **Scalar** at `/scalar/v1` — **Development only** (not exposed in production).

---

## 11. Tech stack

| Area | Choice |
|---|---|
| Runtime / language | .NET 10 (LTS), C# |
| Web | ASP.NET Core **ASP.NET Core Controllers** |
| Hosting | **Windows + IIS**, in-process background work (one deployable) |
| Data access | **Dapper** + feature-local raw SQL |
| Database | SQL Server (2019+) |
| Background jobs | ASP.NET Core BackgroundService |
| Migrations | **DbUp** (forward-only raw SQL) |
| Secrets | `ProviderCredential` (encrypted) via ASP.NET **Data Protection** (DPAPI) |
| Validation | **plain in-feature code** (no framework) |
| Result | **hand-rolled** |
| Time | BCL **`TimeProvider`** |
| Resilience | Polly (`Microsoft.Extensions.Http.Resilience`) |
| Logging | **Serilog** → console + rolling file (Seq optional) · health checks · **no OpenTelemetry** |
| API docs | **OpenAPI** (`Microsoft.AspNetCore.OpenApi`) + **Scalar** UI · Development only |
| Tests | xUnit · Testcontainers (SQL Server) · WireMock.Net (Magfa) |

---

## 12. Open / revisit later

Decision-complete; nothing load-bearing remains. Revisit only on a concrete trigger:

- **Background host:** confirm Hangfire vs. plain hosted services when we build dispatch (both fit; impl-agnostic until then).
- **Connection-string protection:** plaintext now → DPAPI/Always Encrypted when a security review requires it.
- **Worker → separate Windows Service:** only if in-process recycle pauses ever prove unacceptable.
- **Split `Features/Providers/Magfa`** into its own project — only once provider NuGet deps are known.
- **Logging viewer:** Seq vs. an existing ELK/Grafana, if the org already runs one.
