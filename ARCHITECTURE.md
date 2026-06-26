# SmsHubNext — Application Architecture

> **Status:** **Living draft — under active revision.** The *data model* is locked (see `README.md`); this *application* architecture is agreed in direction but expected to evolve. Implementation has not started (pre-Phase 0).
> **Optimize for (highest → lowest):** Simplicity · Developer productivity · Readability · Maintainability · Performance · Extensibility. Architectural elegance is **not** a goal — minimizing cognitive load for future developers is.

---

## 1. Context & constraints

On-prem **Enterprise SMS Platform**, .NET 8 / SQL Server. It is, and will remain:

- a **single deployable application** — no microservices, no modular-monolith boundaries, no independent module deployment;
- maintained by a **small team**, optimized for fast feature work and easy onboarding;
- high-throughput (billion-row fact tables; hand-tuned SQL, partitioning, columnstore).

Every project, abstraction, interface, or layer must justify itself with concrete practical value. **Prefer deleting code over adding abstractions** unless they clearly reduce future complexity.

---

## 2. Decisions (summary)

| # | Decision | One-line rationale |
|---|---|---|
| 1 | **Single deployable, feature-organized (vertical-slice) monolith** | Related code lives together; no cross-project hopping per feature. |
| 2 | **Dapper + colocated SQL; no Generic Repository / Unit-of-Work / persistence layer / repository interfaces** | These hide the hand-tuned SQL that is the whole point and add zero swappability. |
| 3 | **Keep only the abstractions that pay for themselves** | `ISmsProvider` (real polymorphism), a thin `Domain/` shared kernel, separate test projects, raw-SQL migrations. |
| 4 | **No .NET Aspire** | Its value (distributed orchestration, cloud deploy) doesn't apply to an on-prem monolith; take OpenTelemetry + health checks + Polly à la carte. |
| 5 | **Result pattern for expected failures; exceptions for the unexpected** | Predictable control flow + one Result→HTTP mapping at the edge. |
| 6 | **Hosting: Windows + IIS** (API) **+ a Windows Service worker** (background jobs) | Idiomatic on-prem Windows; IIS recycling makes in-process background work fragile. |
| 7 | **Async dispatch: DB-backed outbox, no RabbitMQ** | Messages already persist; a resumable outbox gives reliability with zero new infrastructure. |
| 8 | **Secrets: provider credentials encrypted in SQL Server** (`ProviderCredential`) | App-side Data Protection (DPAPI key, outside the DB); connection string plaintext in config *for now*. |
| 9 | **Migrations: DbUp** · **Logging: Serilog + OpenTelemetry** · **Result: hand-rolled** | Least ceremony; forward-only raw SQL; no extra dependency. |

---

## 3. Solution structure

**3 projects total:** one application + two test projects.

```
SmsHubNext/
├─ SmsHubNext.sln
├─ src/
│  └─ SmsHubNext/                      # THE single deployable app (ASP.NET Core, .NET 8)
│     ├─ Program.cs                    # host, DI, middleware, endpoint mapping
│     │
│     ├─ Common/                       # genuinely cross-cutting, kept tiny
│     │   ├─ Data/                     # IDbConnectionFactory (Dapper), type handlers
│     │   ├─ Auth/                     # API-key hashing (SHA-256) + auth middleware
│     │   ├─ Time/                     # IClock, Jalali calendar helpers
│     │   └─ Results/                  # Result / Result<T> / Error + Result→HTTP mapping
│     │
│     ├─ Domain/                       # SHARED KERNEL — schema types + pure logic only
│     │   ├─ Entities/                 # row/record types (Message, MessageBatch, …)
│     │   ├─ Enums/                    # SendStatus, DeliveryStatus, BatchStatus, Encoding…
│     │   ├─ ValueObjects/             # MobileNumber, Money(IRR), JalaliDate
│     │   └─ Sms/                      # segment counting, encoding detection, cost calc
│     │
│     ├─ Features/                     # VERTICAL SLICES (endpoint + DTO + validation + SQL + handler)
│     │   ├─ Messages/                 # send, status, recipient/bill history
│     │   ├─ Batches/                  # accept batch, batch status, batch events
│     │   ├─ DeliveryReports/          # DLR ingestion worker → append + DeliveryStatus projection
│     │   ├─ Billing/                  # balance debit/refund/top-up, ledger
│     │   ├─ Reports/                  # success rate, cost-by-geo/provider, trends, dispatch durations
│     │   ├─ Tariffs/                  # tariff resolution + admin
│     │   ├─ ApiKeys/                  # issue/rotate/revoke; auth lookup
│     │   ├─ Providers/
│     │   │   ├─ ISmsProvider.cs        # ← the one justified abstraction
│     │   │   └─ Magfa/                 # MagfaSmsProvider, MagfaOptions, status-code map
│     │   └─ ReferenceData/            # GeoSection, SenderLine, MessageType, Customer admin
│     │
│     ├─ Migrations/                   # raw SQL (DbUp / FluentMigrator): partition func/scheme,
│     │                                #   columnstore, filtered indexes, OPTIMIZE_FOR_SEQUENTIAL_KEY
│     └─ appsettings*.json             # provider URLs; secrets via user-secrets / secret store
│
└─ tests/
   ├─ SmsHubNext.UnitTests/            # segment counting, cost calc, status mapping, Jalali, Result flow
   └─ SmsHubNext.IntegrationTests/     # Dapper + SQL Server (Testcontainers), Magfa via WireMock
```

**Dependency flow:** `Features/` → `Domain/` + `Common/`. Features never reference each other. `Domain/` and `Common/` reference nothing outward.

---

## 4. Per-folder justification

| Folder | Why it exists / what it holds | Why not a separate project |
|---|---|---|
| **`Common/`** | Things used *everywhere* and external: DB connection factory, API-key hashing, clock/Jalali, `Result` types. | Tiny; a project boundary buys nothing for a single deployable. |
| **`Domain/`** | **Shared kernel**, not a Clean-Arch layer: one definition of each schema row-type/enum/VO + the **pure, testable SMS logic** (segment counting, cost calc) reused across slices. | Compile-time "no outward deps" is low value for a small team; the *logic* matters, the *assembly* doesn't. |
| **`Features/`** | One folder per capability, each owning its endpoints, DTOs, validation, handler, and **its own SQL**. 90% of feature work happens here. | Splitting features into projects is the cross-project navigation we're avoiding. |
| **`Features/Providers/`** | `ISmsProvider` + `Magfa/` adapter. | Adapter stays a folder until a provider drags heavy/conflicting NuGet deps — then split *that* one. |
| **`Migrations/`** | Hand-written DDL — partitioning, columnstore, filtered indexes can't be auto-generated. | Belongs with the app it migrates. |
| **`tests/*`** | The two boundaries that genuinely earn their own assemblies (distinct deps: xUnit, Testcontainers, WireMock). | — |

### What was removed from the original Clean-Architecture proposal, and why
- **Domain / Application / Infrastructure projects → folders** in the single app. The only thing separate assemblies bought was compile-time dependency-direction enforcement — low value for this team, high friction.
- **Providers.Abstractions / Providers.Magfa projects → `Features/Providers/`.** Pluggability is real; a per-provider *project* is not (no independent deployment).
- **Generic Repository, Unit-of-Work, separate Persistence layer, Repository interfaces → deleted.** See §5.

---

## 5. Data access — Dapper, no ceremony

With Dapper hitting a real SQL Server and integration tests covering it:

| Pattern | Verdict | Why |
|---|---|---|
| Generic Repository | **Removed** | Hides the hand-tuned SQL that is the point; can't generically operate a partitioned columnstore fact; zero swappability (we're not swapping SQL Server). |
| Unit-of-Work abstraction | **Removed** | Atomic multi-writes are `using var tx = conn.BeginTransaction()` in the handler. |
| Separate Persistence layer | **Removed** | SQL lives in the feature; the only shared piece is `IDbConnectionFactory` in `Common/Data`. |
| Repository interfaces | **Removed** | They mostly enable mocking SQL — which we don't do; we integration-test against real SQL Server. |

**Interface bar (project-wide rule):** an interface must have **≥2 real implementations** *or* wrap a **genuinely external/non-deterministic dependency** (e.g. `ISmsProvider`, `IClock`). Otherwise it is deleted.

**One way to access data:** `IDbConnectionFactory` + Dapper + SQL colocated in the feature. No second pattern creeps in.

---

## 6. Observability & infra — à la carte, not Aspire

.NET Aspire targets distributed/cloud-native systems; this is an on-prem monolith, so it would add projects and a framework layer for value we don't use. Instead:

- **Observability:** OpenTelemetry + ASP.NET Core health checks (≈ a few lines in `Program.cs`).
- **Resilience:** `Microsoft.Extensions.Http.Resilience` (Polly) on the Magfa `HttpClient`.
- **Local infra:** `docker-compose.yml` (SQL Server; later RabbitMQ) — universally understood, nothing new to teach.

*(Revisit only if the team already standardizes on Aspire elsewhere and wants the dev dashboard — then a dev-only AppHost, deployed conventionally on-prem.)*

---

## 7. Error handling — Result pattern

- **`Result` / `Result<T>` for *expected* outcomes** — validation, business-rule rejections (insufficient balance, unknown line), not-found, idempotency hits, **provider send rejections**.
- **Exceptions for the *unexpected*** — DB down, misconfig, programming errors → handled by middleware.
- **Flow:** feature handlers and `ISmsProvider.SendAsync` return `Result<T>` (never throw for expected failures); **endpoints own the single `Result → ProblemDetails` mapping** (error category → HTTP status).
- **Shape (lean):** `Result`, `Result<T>`, `Error(Code, Message, ErrorType)` where `ErrorType ∈ Validation/NotFound/Conflict/Unauthorized/Provider/Unexpected` drives the status code. Hand-rolled (~1 small file) to avoid a dependency.
- **Guardrails:** don't wrap pure helpers that can't fail; no nested `Result<Result<T>>`; errors are typed by category, not free-form strings.

---

## 8. Cross-cutting conventions

- **No MediatR / AutoMapper by default** — plain handler classes injected into minimal-API endpoints; hand-mapped DTOs. Add a pipeline only after feeling the pain.
- **Slices don't call slices.** Cross-feature needs are promoted into `Domain/` or `Common/`.
- **`Domain/` and `Common/` stay thin.** If they bloat, they're absorbing feature logic — push it back out.
- **Secrets:** provider credentials stored **encrypted in SQL Server** (`ProviderCredential`), decrypted in-app via Data Protection (key outside the DB) — see §10.3. Never in plaintext in code or DB.
- **Naming** follows the data model: table PK is `Id`, FKs are `<Table>Id`.

---

## 9. Tech stack

| Area | Choice |
|---|---|
| Runtime / language | .NET 8 (LTS), C# |
| Web | ASP.NET Core minimal APIs |
| Hosting | **Windows + IIS** (API) **+ Windows Service** (background worker) |
| Data access | **Dapper** + raw SQL |
| Database | SQL Server (2019+) |
| Async dispatch | **DB-backed outbox** (no RabbitMQ) |
| Migrations | **DbUp** (forward-only raw SQL) |
| Secrets | `ProviderCredential` (SQL Server, encrypted) via ASP.NET **Data Protection** (DPAPI) |
| Validation | FluentValidation → `Result` |
| Resilience | Polly via `Microsoft.Extensions.Http.Resilience` |
| Result | hand-rolled (no dependency) |
| Logging / observability | **Serilog** (+ Seq, Windows Event Log) · **OpenTelemetry** · health checks |
| Tests | xUnit · Testcontainers (SQL Server) · WireMock.Net (Magfa) |

---

## 10. Runtime, deployment & infrastructure

### 10.1 Hosting & deployment — Windows + IIS
- API hosted in **IIS** (ASP.NET Core Module → Kestrel) on Windows Server, co-located with SQL Server in the data center.
- SQL auth: prefer **Integrated Security (Windows/AD)** where available; otherwise a SQL login. Connection string in `appsettings.json` (plaintext **for now** — §10.3).

### 10.2 Async dispatch & background work — DB outbox + Windows Service worker (no RabbitMQ)
- **Transport:** a **DB-backed outbox** in SQL Server. Messages persist as `Queued`; a **claim-based, idempotent, resumable** worker dispatches them. No RabbitMQ — one fewer system to install/secure/monitor on-prem.
- **Hosting the jobs:** a **separate Windows Service** (same solution/codebase, shared DB), **not** in-process under IIS — IIS app-pool recycling/idle makes in-process background work fragile for billing-grade dispatch. Two host processes from one codebase (Api + Worker) — *not* microservices.
  - Jobs: outbox dispatch, DLR ingestion/polling, `Message.DeliveryStatus` projection, `MessageBody` purge, partition-switch retention, `MessageBatchEvent` purge, balance reconciliation.
  - *Simpler fallback (one process):* in-process `BackgroundService` under an app pool set **AlwaysRunning + idle-timeout 0 + Application Initialization**; the resumable outbox tolerates recycles. **← final choice pending confirmation.**

### 10.3 Secrets & credentials — encrypted in SQL Server
- **Provider credentials** (Magfa `username`/`domain`/`password`) live **encrypted in SQL Server** in **`ProviderCredential`** (ciphertext only), decrypted in-app via **ASP.NET Core Data Protection** with the key ring **protected by Windows DPAPI (machine-scoped)** — the key lives **outside** the DB, so a stolen connection string can't decrypt them.
- **Connection string:** plaintext in `appsettings.json` **for now** (explicitly temporary).
- *Hardening path:* SQL Server **Always Encrypted** (master key in the Windows cert store), and moving the connection string into a protected/DPAPI config section — when a security review requires it.

### 10.4 Migrations — DbUp (forward-only)
- Ordered raw-SQL scripts run by **DbUp** at deploy. Forward-only (no down-migrations); partitioning/columnstore DDL is hand-written. FluentMigrator only if rollback becomes a hard requirement.

### 10.5 Logging & observability — Serilog + OpenTelemetry
- **Serilog** (structured) → console + rolling file + **Seq** (v1) + **Windows Event Log** for service-level errors; **OpenTelemetry** traces/metrics. Feed an existing Prometheus/Grafana or ELK if the org already runs one *(open — §11)*.

### 10.6 Web, validation, errors — locked defaults
- Minimal APIs · FluentValidation → `Result` · hand-rolled `Result` · no MediatR/AutoMapper · built-in ASP.NET **rate limiter** keyed by API key (header auth) · **Testcontainers** integration tests · Jalali via BCL `PersianCalendar`.

---

## 11. Open / under revision

This document is a **living draft**. Resolved this round: hosting (Windows/IIS), async transport (DB outbox), secrets (`ProviderCredential`), migrations (DbUp), logging (Serilog/OTel), Result (hand-rolled). Still open:

- **Background-worker hosting — final nod needed.** Recommended: a **separate Windows Service**; fallback: in-process `BackgroundService` under an AlwaysRunning app pool (§10.2).
- **Logging sink:** does the org already run **ELK/Grafana/Prometheus** we should feed instead of standing up Seq?
- **Connection-string protection:** plaintext now → DPAPI/protected config + SQL **Always Encrypted** as the hardening path (§10.3) — when to do it.
- **Split `Features/Providers/Magfa`** into its own project — only once provider NuGet deps are known.
- **Result library** (e.g. ErrorOr) vs. hand-rolled — currently hand-rolled; revisit if ergonomics demand.
