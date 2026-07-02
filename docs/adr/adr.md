# SmsHubNext — Architecture Decision Record (ADR)

> This document records the reasoning behind the architectural decisions made for SmsHubNext.
> It complements `ARCHITECTURE.md` by explaining **why** each decision was made, the alternatives considered, and under what conditions the decision may be revisited.

---

# Decision Philosophy

This project optimizes for:

1. Simplicity
2. Readability
3. Maintainability
4. Long-term stability
5. Predictable performance

The project intentionally avoids architectural patterns or frameworks that primarily add abstraction without solving an actual problem.

Whenever multiple valid solutions exist, the simplest solution that satisfies the requirements is preferred.

---

# ADR-001
## Feature-first organization

### Decision

Organize code by business capability rather than technical layers.

### Alternatives

- Clean Architecture
- Onion Architecture
- Hexagonal Architecture
- Vertical Slice
- Layered Architecture

### Why

Developers work on features — not repositories, validators, DTOs and services separately.

Keeping everything for one feature together dramatically reduces navigation and cognitive load.

### Consequences

Pros

- Easier onboarding
- Less folder hopping
- Better locality

Cons

- Requires discipline to avoid feature coupling

---

# ADR-002
## Dapper instead of Entity Framework

### Decision

Use Dapper with handwritten SQL.

### Alternatives

Entity Framework Core

### Why

SmsHubNext is a high-volume write-heavy system.

Performance and SQL control are first-class requirements.

The database uses partitioning, filtered indexes and columnstore indexes.

Handwritten SQL is considered an advantage rather than an implementation detail.

### Consequences

Pros

- Maximum SQL control
- Better performance
- Easier optimization

Cons

- More SQL to maintain

---

# ADR-003
## No Repository pattern

### Decision

Do not introduce repositories.

### Why

Repositories hide handwritten SQL.

The project will never swap SQL Server for another database.

Repositories provide little value while introducing additional abstraction.

Transactions can be handled directly inside handlers.

### Revisit

Only if multiple storage technologies are introduced.

---

# ADR-004
## ASP.NET Core Controllers

### Decision

Use MVC Controllers.

### Alternatives

Minimal APIs

### Why

Controllers provide clearer organization for a growing application.

The project is expected to contain many endpoints grouped by feature.

Controllers are familiar to most .NET developers.

Minimal APIs provide little benefit for this project's size.

---

# ADR-005
## Background processing

### Decision

Use built-in ASP.NET Core BackgroundService.

### Alternatives

Hangfire

TickerQ

Quartz.NET

### Why

The application primarily performs continuous background workers rather than scheduled jobs.

Examples:

- SMS dispatch
- Delivery report polling
- Cleanup
- Retention

These are long-running workers instead of scheduled jobs.

BackgroundService is part of ASP.NET Core, requires no additional infrastructure and keeps dependencies minimal.

### Important

Reliability does not come from the hosting mechanism.

Reliability comes from:

- SQL persistence
- Atomic claim
- Idempotency
- State transitions
- Crash recovery

Replacing BackgroundService with another scheduler should not require changes to business logic.

### Revisit

If the system eventually requires:

- Cron scheduling
- User-configurable schedules
- Dashboard
- Distributed execution
- Delayed jobs

then introducing a scheduling framework may become justified.

---

# ADR-006
## Result Pattern

### Decision

Expected failures return Result<T>.

Unexpected failures throw exceptions.

### Why

Business failures are not exceptional.

Using Result makes the execution flow explicit and testable.

---

# ADR-007
## No MediatR

### Decision

Handlers are plain classes.

### Why

The project does not benefit from an additional messaging abstraction.

Dependencies remain explicit.

Navigation is simpler.

---

# ADR-008
## No FluentValidation

### Decision

Validation is implemented using plain C#.

### Why

Validation rules are relatively simple.

Keeping validation beside the request improves readability.

---

# ADR-009
## No AutoMapper

### Decision

Mappings are written manually.

### Why

Mappings are few and straightforward.

Explicit mapping is easier to debug.

Compile-time safety is preferred.

---

# ADR-010
## Minimal abstractions

### Decision

Only introduce abstractions when multiple real implementations exist.

### Examples

Keep

- ISmsProvider
- TimeProvider

Avoid

- Generic repositories
- Generic services
- Base handlers
- Generic validators

---

# ADR-011
## SQL Server is the source of truth

The system assumes SQL Server is always authoritative.

Background workers are stateless.

Application restarts are expected.

Recovery always starts by reading database state.

No in-memory state is required for correctness.

---

# ADR-012
## No `api/` route prefix

### Decision

Controllers route on the resource directly, with no `api/` prefix.

Examples:

- `[Route("messages")]`
- `[Route("reference-data/message-types")]`

### Why

The service exposes only an API, so an `api/` segment on every route is redundant noise that adds nothing to readability or organization.

Routes stay short and read as the resource they represent.

### Consequences

Routes are grouped by resource, not by a blanket prefix.

### Revisit

Only if the same host ever needs to serve non-API surfaces (for example a server-rendered UI) alongside the API, where a prefix would disambiguate.

---

# ADR-013
## Explicit package versions only (no floating)

### Decision

Every NuGet dependency is pinned to an exact version in `Directory.Packages.props`.

Floating versions (`2.*`, `[2.0,)`, and similar) are not allowed.

### Why

Reproducible, deterministic builds: the same commit restores the same bytes on every machine and in CI.

Floating versions silently change dependencies between restores, which undermines the project's `Deterministic` build setting and makes failures hard to reproduce.

### How

Central Package Management with `CentralPackageFloatingVersionsEnabled` left off (the default), so a floating version fails the build (NU1011) instead of slipping through.

Upgrades are explicit edits to `Directory.Packages.props`.

### Revisit

Not expected.

---

# ADR-014
## Minimal composition root (Program.cs + Extensions/)

### Decision

`Program.cs` is a minimal composition root: create the builder, call extension methods, build and run — nothing else.

All wiring lives in `Extensions/`:

- `ServiceCollectionExtensions.AddApplicationServices` — DI (controllers, OpenAPI, `Db`, feature handlers, health checks).
- `ApplicationBuilderExtensions.ConfigurePipeline` — HTTP pipeline (Serilog request logging, OpenAPI + Scalar, root endpoint, controller and health-check mapping).
- `DatabaseExtensions.MigrateDatabase` — startup database migration.

Feature handlers are not listed by hand: `AddFeatureHandlers` scans the assembly and registers them by convention (ADR-017). Other services (options, workers, the provider seam, the auth resolver) are registered explicitly in the relevant extension method, not in `Program.cs`.

### Why

- **Separation of concerns.** Registration, pipeline configuration, and database bootstrap are distinct responsibilities and read better apart.
- **Maintainability at volume.** A single growing `Program.cs` becomes a merge-conflict magnet as features accrue; one obvious place per concern scales better.
- **Feature-first consistency.** Adding a feature touches its own folder and one registration line, rather than padding the host file.

### Consequences

Wiring is slightly more indirect (one hop into `Extensions/`), traded for a host file that stays readable.

### Revisit

Not expected. If the app ever needs environment-specific composition, add focused extension methods rather than branching inside `Program.cs`.

---

# ADR-015
## API-key authentication: implemented, not yet enforced

### Decision

API-key authentication is fully implemented (`Features/Authentication/`) but deliberately **left out of the request pipeline**. Every endpoint stays anonymous for now.

- `ApiKeyAuthenticator` (a plain service, registered in DI) is the whole of auth: it hashes the `X-Api-Key` header (SHA-256, `ApiKeyHasher`), seeks the active/non-revoked/non-expired `ApiKey` by hash, and enforces the optional per-key CIDR allow-list against the caller IP.
- `ApiKeyAuthenticationMiddleware` enforces it (401 on failure, stashes `ApiKeyIdentity` on `HttpContext.Items`) — but is **not** added to the pipeline.
- `GET /auth/whoami` exercises the resolver directly so a key can be tested without enforcing auth anywhere.

### Why

The caller asked to build auth ahead of activation so the APIs remain convenient to test (no key required on every call). Building the resolver, the enforcement middleware, and the identity accessor now means activation is a one-line change later, with the design already reviewed.

Keeping it a plain middleware + service (not an ASP.NET `AuthenticationHandler`/`[Authorize]` scheme) matches the house style (ARCHITECTURE.md §3: controllers + plain services, no framework ceremony).

### How to activate

1. Add `app.UseApiKeyAuthentication();` in `ApplicationBuilderExtensions.ConfigurePipeline`, just before `MapControllers`.
2. Replace the interim explicit `CustomerId`/`ApiKeyId` on `SendMessagesRequest` (and any other attributed call) with `HttpContext.GetApiKeyIdentity()`.
3. Decide which endpoints are public (e.g. health, reference data) vs. keyed.

### Revisit

When the project is ready to enforce tenancy at the edge (roadmap Phase 5). If per-endpoint policies or scopes become necessary, reconsider an ASP.NET authentication scheme then — not before.

---

# ADR-016
## Explicit types over `var`

### Decision

Local variables are declared with their **explicit concrete type**, never `var`.

```csharp
// no
var connection = await _db.OpenConnectionAsync(ct);
var messages = rows.AsList();

// yes
SqlConnection connection = await _db.OpenConnectionAsync(ct);
List<BatchMessage> messages = rows.AsList();
```

The single exception is an anonymous type (`new { … }`), which has no nameable type; such values are passed inline as arguments rather than bound to a `var` local wherever possible.

### Why

- **Readability at a glance.** The type is visible at the declaration, without inferring it from the right-hand side or a hover — which matters most in data-access code where the right-hand side is a Dapper/`Result<T>` generic call.
- **One house style.** A single rule removes per-author `var`/explicit drift and keeps diffs about behavior, not formatting.

This is a stylistic preference, not a correctness claim; both compile identically.

### How

`.editorconfig` sets `csharp_style_var_*` to prefer explicit types and raises `IDE0008` to `warning`. With `TreatWarningsAsErrors` (ADR-012/Directory.Build.props) and `EnforceCodeStyleInBuild`, a `var` fails the build.

### Revisit

Not expected. If a future type name becomes genuinely unhelpful (e.g. long generic tuples), prefer a named type over relaxing the rule.

---

# ADR-017
## Feature handlers auto-register via Scrutor assembly scanning

### Decision

Feature handlers are registered by **assembly scanning** (the [Scrutor](https://github.com/khellang/Scrutor) library), not one `services.AddScoped<…>()` line each. `AddFeatureHandlers` scans the application assembly and registers every concrete class named `*Handler` as **scoped**, **as itself** (controllers inject the concrete handler — there are no handler interfaces, per ADR-010):

```csharp
services.Scan(scan => scan
    .FromAssemblyOf<SendMessagesHandler>()
    .AddClasses(classes => classes.Where(type => type.Name.EndsWith("Handler")), publicOnly: true)
    .AsSelf()
    .WithScopedLifetime());
```

The `…Handler` name + `Features/**` location is the contract: adding a handler wires it automatically, with no edit to the composition root.

Services that are **not** use-case handlers stay explicitly registered in the relevant extension method: `ApiKeyAuthenticator` (a shared auth resolver, ADR-015), the options objects, the background workers and their pollers/dispatcher, and the `ISmsProvider` seam (selected at runtime, ADR — Magfa vs. loopback).

### Why

- **No registration drift.** The hand-maintained list grew with every feature and was easy to forget — a missing line is a runtime DI failure, not a compile error. A convention can't be forgotten.
- **Feature-first.** Adding a feature touches only its own folder; the host file stays untouched.
- **Matches the house style.** Handlers are already uniform — plain `*Handler` classes, scoped, no interface (ADR-010). The scan encodes exactly that uniformity; it is not a move toward a mediator/CQRS dispatch pipeline (still none — ARCHITECTURE.md §3).

### Consequences

- A new dependency (Scrutor) on the runtime project. It is a thin, widely-used wrapper over `IServiceCollection`; pinned exactly via central package management (ADR-013).
- The handler **naming convention is now load-bearing**: a use-case class that does not end in `Handler` is not registered, and a non-handler class that does would be picked up. Both are unlikely given the existing uniformity, and a handler needing a different lifetime can be registered explicitly (it will simply be registered twice — explicit wins for an exact match only if ordered after; prefer excluding it from the scan).
- Registration is no longer greppable as a static list; the convention is documented here and in `CLAUDE.md` §7 instead.

### Revisit

If handlers ever need per-handler lifetimes/decorators at scale, or the convention proves too blunt, revert to explicit registration (or use Scrutor's decoration support). Not expected.

---

# ADR-018
## Co-locate use-case request and response in one file

### Decision

For a command or query endpoint, put `{Operation}Request` and `{Operation}Response` (when both exist) in **one file** named after the operation — e.g. `CreateCustomer.cs`, `TopUp.cs`, `SendMessages.cs`. Type names keep the `Request`/`Response` suffix; only the file layout changes.

Request-only endpoints follow the same file naming (`AddIpRestriction.cs` holds `AddIpRestrictionRequest` only).

### Alternatives

- One public type per file (previous convention)
- Nested request/response types inside the handler file

### Why

- **Fewer files, same clarity.** Most response types are tiny records paired 1:1 with a request; splitting them added navigation cost without readability benefit.
- **Feature-first locality.** Opening `CreateCustomer.cs` shows the whole API contract for that operation in one place.
- **Matches ARCHITECTURE.md §4.** Simplicity and co-location are explicit project goals.

### Do not merge

- **Shared read models** reused across handlers (e.g. `TariffResponse` for list + admin)
- **Provider HTTP DTOs** under `Features/Providers/` (external API shapes, not app use-case contracts)
- **Domain/row types** (`Customer`, `ApiKeyIpRestriction`, …)
- **Nested item types** that belong to a larger request (`SendMessageItem` stays separate)

### Consequences

Pros

- Less folder clutter
- Contract for an operation is greppable by one filename

Cons

- Files with both request validation and response shape are slightly longer (still typically small)

### Revisit

Not expected. If a response type becomes shared across multiple operations, extract it to its own file rather than duplicating or over-merging unrelated contracts.

---

# Future Reconsideration

These decisions are intentionally conservative.

Every decision may be revisited only if measurable evidence demonstrates that the current solution no longer satisfies the project's requirements.

Complexity should always be introduced as a response to an actual problem — not in anticipation of one.