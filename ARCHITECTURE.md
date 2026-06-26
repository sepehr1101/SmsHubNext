# SmsHubNext — Application Architecture

> **Status:** **Living draft — decision-complete.** The *data model* is locked (`README.md`); this *application* architecture is agreed. Implementation not started (pre-Phase 0).
> **Optimize for (highest → lowest):** Simplicity · Developer productivity · Readability · Maintainability · Performance · Extensibility. The goal is **minimal cognitive load for the next developer** — not architectural purity.

---

## 1. Philosophy — a feature-first, pragmatic monolith

Organize around **business features**, not technical layers. Inspiration is the *organization and developer experience* of the **DntSite** project (feature cohesion, low ceremony) — not its exact tech or folders.

**This project is NOT** Clean/Onion Architecture, CQRS, MediatR, FluentValidation, or microservices. We do not add abstraction layers or patterns unless they earn their place.

- **Features come first.** Each feature is as self-contained as practical — its endpoints, requests, handler/service, validation, Dapper SQL, models, and mapping live **together** in one folder.
- **No global technical buckets.** No `Services/`, `Repositories/`, `DTOs/`, `Validators/`, or `Interfaces/` folders collecting unrelated code from different features.
- **Shared code stays small** and holds only *truly* cross-cutting concerns.
- **High cohesion, low coupling.** Features don't call each other; shared needs move into `Shared/`.
- **Explicit over generic.** Straightforward code a developer can grasp in a few minutes beats a clever abstraction.
- **"Modular monolith"** here means feature *modules as folders* inside **one project / one deployable** — not enforced module boundaries or separate assemblies.

**Rule for any abstraction (interface/wrapper/base class): justify it.** It must have **≥2 real implementations** *or* wrap a **genuinely external/non-deterministic dependency**. Otherwise, delete it and write the concrete code.

---

## 2. Decisions (summary)

| # | Decision | One-line rationale |
|---|---|---|
| 1 | **Feature-first monolith, one deployable** | Related code lives together; no cross-folder hopping per feature. |
| 2 | **Dapper + feature-local SQL; no Repository / Unit-of-Work / persistence layer / repo interfaces** | They hide the hand-tuned SQL that is the point and add zero swappability. |
| 3 | **Minimal APIs; no MediatR, no FluentValidation, no AutoMapper** | Endpoints call plain feature handlers; validation and mapping are explicit, local code. |
| 4 | **Very few abstractions, each justified** | `ISmsProvider` (≥2 impls) and BCL `TimeProvider` (test determinism). That's essentially it. |
| 5 | **Result pattern for expected failures; exceptions for the unexpected** | Predictable control flow + one Result→HTTP mapping at the edge. |
| 6 | **Hosting: Windows + IIS, in-process background work** | One deployable; resumable outbox tolerates app-pool recycles. |
| 7 | **Reliable dispatch via SQL-backed jobs; transport is swappable (Hangfire a candidate), not baked in** | Feature job-logic stays scheduler-agnostic. |
| 8 | **Secrets: provider credentials encrypted in SQL Server** (`ProviderCredential`) | App-side Data Protection (DPAPI key, outside the DB). |
| 9 | **Migrations: DbUp** · **Logging: Serilog (no OpenTelemetry)** · **Result: hand-rolled** | Least ceremony; forward-only raw SQL; structured logs without OTel weight. |

---

## 3. Solution structure

**3 projects:** one application + two test projects. Within the app, **`Features/` dominates**; everything else is small.

```
SmsHubNext/
├─ SmsHubNext.sln
├─ src/
│  └─ SmsHubNext/                      # THE single deployable (ASP.NET Core, .NET 8)
│     ├─ Program.cs                    # host, DI, middleware, maps each feature's endpoints
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

---

## 4. Anatomy of a feature

A feature folder is flat and holds *everything for that capability*. Example:

```
Features/Sending/
├─ SendMessagesEndpoint.cs     # minimal-API endpoint(s): MapPost(...) → handler
├─ SendMessagesRequest.cs      # request model + a plain Validate() returning errors
├─ SendMessagesResponse.cs     # response model
├─ SendMessagesHandler.cs      # the feature logic (a plain class, injected) → Result<T>
├─ SendSql.cs                  # the Dapper SQL strings + small row records for this feature
└─ (anything else only Sending needs)
```

- **Endpoint** is a thin minimal-API mapping that binds the request, calls the handler, and translates `Result` → HTTP.
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
- Rules live **next to the request** they validate, not in a global `Validators/` folder.
- The endpoint maps a validation `Result` to **400** via the single mapper (§7).

*Why no framework:* validation here is simple field/relationship checks; a fluent DSL + a `IValidator<T>` registry is ceremony for little gain, and it scatters rules away from the feature.

---

## 7. Error handling — Result pattern

- **`Result` / `Result<T>` for *expected* outcomes** — validation, business-rule rejections (insufficient balance, unknown line), not-found, idempotency hits, **provider send rejections**.
- **Exceptions for the *unexpected*** — DB down, misconfig, bugs → handled by middleware.
- **Flow:** feature handlers and `ISmsProvider.SendAsync` return `Result<T>` (never throw for expected failures); **the endpoint owns the single `Result → ProblemDetails` mapping** (error category → HTTP status).
- **Shape (lean, hand-rolled ~1 file):** `Result`, `Result<T>`, `Error(Code, Message, ErrorType)` where `ErrorType ∈ Validation/NotFound/Conflict/Unauthorized/Provider/Unexpected` drives the status code.

---

## 8. Abstractions we keep (and what we deliberately don't)

| Abstraction | Keep? | Justification |
|---|---|---|
| **`ISmsProvider`** | ✅ | **≥2 real implementations** (Magfa now, more later) — genuine polymorphism behind one seam. |
| **`TimeProvider`** (BCL) | ✅ | Test-determinism for Jalali/time logic — and it's the **built-in** .NET 8 type, so **zero custom abstraction**. |
| DB connection helper | concrete class, **no interface** | One implementation; integration tests use a real DB. |
| Repositories / UoW / `IValidator` / generic service interfaces | ❌ removed | No second impl, no real seam — pure ceremony. |

Prefer **composition over inheritance**; avoid base classes unless they remove real duplication.

---

## 9. Background processing — reliable, implementation-agnostic

Sending is async (accept → background dispatch → status). The **job *logic*** (outbox dispatch, DLR ingestion, `MessageBody` purge, partition-switch retention, `MessageBatchEvent` purge, balance reconciliation) lives in the relevant **features** as plain classes. The **scheduling/hosting mechanism is a thin, swappable host concern** — features don't reference it.

- **Reliability comes from SQL, not the scheduler:** messages persist as `Queued`; dispatch is **claim-based, idempotent, and resumable**, so any host restart/recycle just resumes.
- **Candidate hosts:** **Hangfire** (SQL-Server-backed jobs with retries + dashboard; survives IIS recycles via its own server — attractive on Windows/IIS) **or** plain in-process `BackgroundService`s with a polling claimer. The architecture does **not** depend on either — the choice is wired in `Program.cs`, not in features.
- **One deployable:** whichever host, it runs in-process with the API (app pool AlwaysRunning); a separate Windows Service remains the documented upgrade only if recycle pauses ever prove unacceptable.

---

## 10. Runtime, deployment & infrastructure

- **Hosting:** **Windows + IIS** (ASP.NET Core Module → Kestrel), co-located with SQL Server. SQL auth prefers **Integrated Security** where available.
- **Secrets:** provider credentials **encrypted in SQL Server** (`ProviderCredential`, ciphertext only), decrypted in-app via **ASP.NET Core Data Protection** with the key ring **protected by Windows DPAPI** — key lives **outside** the DB. Connection string plaintext in `appsettings.json` **for now** (temporary; hardening path = SQL **Always Encrypted** + protected config).
- **Migrations:** **DbUp** — ordered, forward-only raw-SQL scripts run at deploy; partitioning/columnstore DDL is hand-written.
- **Logging:** **Serilog** (structured) → console + rolling file (+ Seq optional, Windows Event Log for service errors). **OpenTelemetry intentionally omitted** (simpler is better) — add tracing later only if a real need appears. ASP.NET Core **health checks** for IIS probes.
- **Resilience:** Polly via `Microsoft.Extensions.Http.Resilience` on the Magfa `HttpClient`.
- **Auth:** API key in header; built-in ASP.NET **rate limiter** keyed by API key.

---

## 11. Tech stack

| Area | Choice |
|---|---|
| Runtime / language | .NET 8 (LTS), C# |
| Web | ASP.NET Core **minimal APIs** |
| Hosting | **Windows + IIS**, in-process background work (one deployable) |
| Data access | **Dapper** + feature-local raw SQL |
| Database | SQL Server (2019+) |
| Background jobs | implementation-agnostic; **Hangfire** a candidate (SQL-backed) |
| Migrations | **DbUp** (forward-only raw SQL) |
| Secrets | `ProviderCredential` (encrypted) via ASP.NET **Data Protection** (DPAPI) |
| Validation | **plain in-feature code** (no framework) |
| Result | **hand-rolled** |
| Time | BCL **`TimeProvider`** |
| Resilience | Polly (`Microsoft.Extensions.Http.Resilience`) |
| Logging | **Serilog** → console + rolling file (Seq optional) · health checks · **no OpenTelemetry** |
| Tests | xUnit · Testcontainers (SQL Server) · WireMock.Net (Magfa) |

---

## 12. Open / revisit later

Decision-complete; nothing load-bearing remains. Revisit only on a concrete trigger:

- **Background host:** confirm Hangfire vs. plain hosted services when we build dispatch (both fit; impl-agnostic until then).
- **Connection-string protection:** plaintext now → DPAPI/Always Encrypted when a security review requires it.
- **Worker → separate Windows Service:** only if in-process recycle pauses ever prove unacceptable.
- **Split `Features/Providers/Magfa`** into its own project — only once provider NuGet deps are known.
- **Logging viewer:** Seq vs. an existing ELK/Grafana, if the org already runs one.
