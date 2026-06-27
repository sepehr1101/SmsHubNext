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

New feature handlers are registered in `AddFeatureHandlers`, not in `Program.cs`.

### Why

- **Separation of concerns.** Registration, pipeline configuration, and database bootstrap are distinct responsibilities and read better apart.
- **Maintainability at volume.** A single growing `Program.cs` becomes a merge-conflict magnet as features accrue; one obvious place per concern scales better.
- **Feature-first consistency.** Adding a feature touches its own folder and one registration line, rather than padding the host file.

### Consequences

Wiring is slightly more indirect (one hop into `Extensions/`), traded for a host file that stays readable.

### Revisit

Not expected. If the app ever needs environment-specific composition, add focused extension methods rather than branching inside `Program.cs`.

---

# Future Reconsideration

These decisions are intentionally conservative.

Every decision may be revisited only if measurable evidence demonstrates that the current solution no longer satisfies the project's requirements.

Complexity should always be introduced as a response to an actual problem — not in anticipation of one.