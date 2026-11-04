# AGENTS.md

Guidance for Codex when working in the **SmsHubNext** repository. Keep this file concise and high-signal; update it as decisions are made.

---

## 1. What this project is

**SmsHubNext** is a high-volume, multi-tenant **SMS dispatch and accounting platform** for Iranian SMS providers. Primary use case: utility/organizational notifications (e.g. water-bill SMS) sent to **ad-hoc recipients** across Iranian geographic sections (province → city → zone), billed and reported by provider, geography, message type, and **Jalali (Persian) calendar** period.

* **First provider:** Magfa (sender lines `3000…`, `1000…`, `4040…`, etc.). Designed so more providers are added later by inserting data, not changing schema.
* **Currency:** Iranian Rial (IRR).
* **Reporting calendar:** Jalali (e.g. "Spring 1405"); precise instant stored UTC (`SubmittedAtUtc`), and a single `SubmitDateJalali CHAR(10)` (`1405/01/03`) is the partition + period key (no date-dimension table).
* **Each message is distinct** (caller supplies the full text); **customers authenticate with API keys**.

## 2. CURRENT PHASE — scope guardrails ⚠️

The **database & storage architecture design** is **validated and locked** (the 6 review questions are resolved — see §8 / README §10). Schema changes from here should be **additive** (README §9) unless a locked decision is explicitly reopened.

**Phase 0 has started** (user moved us off the design-only guardrail on 2026-06-27). The solution is scaffolded (`SmsHubNext.slnx` + `src/SmsHubNext`). We now build the app **incrementally in small, reviewable steps** — each increment small enough to be criticized (spiral SDLC). Follow the roadmap (§6) and `ARCHITECTURE.md`.

**Do:**

* ✅ Build the app in small increments along the roadmap; keep each step reviewable
* ✅ Respect the locked architecture (`ARCHITECTURE.md`, `docs/adr/adr.md`) and data model (`README.md`)
* ✅ Keep schema changes additive (README §9)

**Still hold off on (until their roadmap phase):**

* ⏳ Provider integrations (Magfa HTTP client) — Phase 1+
* ⏳ Messaging/outbox reliability layer — Phase 3
* ⏳ Anything that contradicts a locked decision without an explicit ask

## 3. Tech decisions (locked unless changed here)

| Area               | Decision                                                                                                                                                                                                                                    | Notes                                                                                                 |
| ------------------ | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ----------------------------------------------------------------------------------------------------- |
| Language/Runtime   | **.NET 10 (LTS), C#**                                                                                                                                                                                                                       |                                                                                                       |
| Database           | **SQL Server (2019+)**                                                                                                                                                                                                                      | Chosen for partitioning, `OPTIMIZE_FOR_SEQUENTIAL_KEY`, lock-escalation control, columnstore          |
| Money type         | `DECIMAL(19,4)`, IRR                                                                                                                                                                                                                        |                                                                                                       |
| Text               | `NVARCHAR` for Persian; `VARCHAR` for phone numbers/codes (ASCII)                                                                                                                                                                           |                                                                                                       |
| Naming             | Every table's PK is `Id`; FKs are `<Table>Id` (`CustomerId`, `ProviderId`, `MessageId`, …)                                                                                                                                                  |                                                                                                       |
| Auth               | Customer API keys + administrator API keys exchanged for 15-minute JWTs; all keys stored as **SHA-256 hash** only (never plaintext); optional per-key CIDR allow-list                                                                      | Administrator keys belong to a hidden system customer so existing `ApiKeyId` audit FKs remain valid |
| App architecture   | **Monolith organized by feature** (DntSite-inspired coding style; **not** Clean/Onion/CQRS/Vertical-Slice/MediatR/FluentValidation), ASP.NET Core Controllers, Dapper (no repo/UoW), plain in-feature validation, Result pattern, no Aspire | See `ARCHITECTURE.md`; providers behind `ISmsProvider`; background impl-agnostic (Hangfire candidate) |
| Send model (later) | Async outbox + status polling (recommended)                                                                                                                                                                                                 | Not built yet                                                                                         |
| Windows installer  | **Inno Setup 6.7.3 + small PowerShell deployment scripts + setup command mode in the existing host**                                                                                                                                        | No WinForms/WPF or second backend; offline .NET 10 Hosting Bundle; SQL Server is supplied, never installed |

## 4. Architecture decisions that must be respected

These are the load-bearing choices from `README.md`. Don't silently contradict them:

* **Fact + dimension model.** `Message` is the central high-volume fact (up to ~1B rows at 5yr). Small dimensions referenced by surrogate keys.
* **Deliberate denormalization for reporting.** The fact carries denormalized dimension keys (`GeoSectionId/ProviderId/MessageTypeId/CustomerId`), the Jalali date (`SubmitDateJalali`), and the current `DeliveryStatus` so aggregate reports don't join the billion-row fact.
* **Single self-referencing `GeoSection`** (Province → City → Zone via `ParentGeoSectionId` + `SectionType` + materialized `Path`) replaces separate Province/City/Zone tables.
* **`MessageType` is the single classification axis** — delivery class **and** business purpose merged (no separate `BusinessCategory`).
* **Cost snapshotting.** Tariffs are versioned by effective-date range, but the resolved price (`Encoding/CharacterCount/SegmentCount/TariffId/UnitPrice/TotalCost`) is **frozen onto each `Message`** at submission so history stays accurate after tariff changes. Reporting never re-resolves tariffs.
* **Narrow fact, separate text.** No text on `Message`; the distinct body lives in a 1:1 `MessageBody` (keyed by `Id`, partitioned by `Id`, shorter independent retention).
* **Delivery model (CQRS-ish).** `Message` carries a **denormalized current `DeliveryStatus`** (read model, updated in place **only on the hot rowstore partition** — terminal by the time a partition is columnstore-compressed) ⇒ success rate is a **join-free `GROUP BY`**. Full status history is the **append-only `DeliveryReport`** stream.
* **Analytics via columnstore**, not a pre-aggregate cube (the former `MessageDailyAggregate` was removed). Nonclustered columnstore on cold partitions.
* **Concurrency:** **Jalali-monthly partitioning** by `SubmitDateJalali`; clustered key `(SubmitDateJalali, Id)` with `OPTIMIZE_FOR_SEQUENTIAL_KEY = ON`; minimal nonclustered indexes (`DeliveryStatus` deliberately un-indexed); batch inserts < 5,000 rows/txn; append-only DLR history + narrow hot-partition status updates; RCSI reads; retention via partition switching.
* **Removed (re-introducible additively):** `Campaign`, `MessageTemplate`, `Subscriber`, `DimDate`, `MessageDailyAggregate`, `BusinessCategory`. Don't reintroduce without explicit ask.
* **Reference-data deletion is logical.** `Customer`, `Provider`, `SenderLine`, `MessageType`, and `GeoSection` retain deleted rows with `DeletedAtUtc` + `DeletedByApiKeyId`; live reads and new-send/auth paths exclude them, while historical reports keep resolving their labels. Deleted business codes are not reused; a `GeoSection` with non-deleted children cannot be deleted.
* **Operational configuration has explicit lifecycle rules.** `Tariff`, `ProviderAccount`, and `ApiKeyIpRestriction` are soft-deleted with actor/time attribution. Tariff price axes/rates are immutable: price changes create a new non-overlapping version; only `EffectiveToUtc`/`IsActive` are editable. API keys are never deleted: edit label/expiry/active state before permanent revocation, and retain `RevokedAtUtc` + `RevokedByApiKeyId` for attribution. `ApiKey.AccessLevel` separates customer send keys from administrator keys; administrator keys are exchanged for short-lived management JWTs.
* **Optimize for:** write throughput, reporting simplicity, low storage, minimal deadlocks, maintainability — **not** normalization purity.

## 5. Domain glossary (quick reference)

17 tables: Customer · ApiKey · ApiKeyIpRestriction · Provider · SenderLine · MessageType · GeoSection (self-referencing) · Tariff · TariffRate · Message (fact + current `DeliveryStatus` + `MessageBatchId`) · MessageBody · DeliveryReport (append-only status history) · MessageBatch (one per API call: accounting/attribution/idempotency + dispatch `Status`/holds + 3 lifecycle timestamps) · CustomerBalance (signed balance, 1/customer) · BalanceTransaction (append-only money ledger) · MessageBatchEvent (operational event store, append-only, ~90-day retention) · ProviderAccount (provider auth settings + encrypted secret).

Recipient = `MobileNumber` on the message (ad-hoc, no Subscriber table). Caller references on the message: `ClientCorrelatedId` (idempotency), `BillId`, `PayId` (all nullable). **Billing allows debt (user decision 2026-10-14):** atomic debit at batch accept even with zero/negative balance; provider low-credit ⇒ batch `Held`, messages stay `Queued`.

Full table-by-table detail lives in **`README.md`** — read it before changing the schema.

## 6. Roadmap (phases)

0. Foundation (solution scaffold) — *in progress: `.slnx` + `src/SmsHubNext` host created*
1. Walking skeleton: send one SMS via Magfa
2. Persistence & delivery status
3. Reliability: async outbox + retries
4. Feature breadth: bulk, balance, inbox, part counting
5. Hardening & multi-tenancy
6. Second provider (proves the abstraction)
7. Finish line: tests, Docker, deploy

> We are in **Phase 0** (foundation/scaffold), building forward in small increments.

## 7. Working conventions

* **Branch:** develop on the designated feature branch (currently `Codex/continue-previous-work-c6qqvn`). Never push elsewhere without explicit permission.
* **Commits:** clear, descriptive messages. Commit author/email must be `Codex <noreply@anthropic.com>` so GitHub shows commits as verified.
* **PRs:** do **not** open a pull request unless explicitly asked.
* **One application project and one host.** Blazor UI, controllers, database access and workers live in `src/SmsHubNext`. Feature UI lives in each feature's `Ui/` folder and calls existing handlers directly through the authenticated UI boundary. Management pages use `/admin/**`; existing API routes remain unchanged. Do not reintroduce a separate UI host or loopback HTTP dependency. See ADR-019.
* **4040 is independent of Magfa.** ESB routing is selected explicitly by `SenderLine.UseEsb` for provider code `4040`. OAuth client credentials live under `Esb`; keep real values in gitignored local configuration. Share the token cache until `expires_in` elapses, serialize concurrent logins, and retry uncertain submissions only with the same UID within the user-confirmed 24-hour deduplication window (2026-10-19). All providers are capped at three dispatch retries per stored message after the initial attempt; persisted counters must survive batch retries/restarts. See `docs/providers/4040-esb.md` for the supported contract and current limits.
* **Dependencies:** pin **exact** NuGet versions in `Directory.Packages.props` (Central Package Management). **No floating versions** (`2.*`, ranges) — see ADR-013.
* **Composition root:** keep `Program.cs` **minimal** — builder creation, calling extension methods, `Build()`/`Run()` only. All service registration and HTTP-pipeline wiring go through `Extensions/` (`ServiceCollectionExtensions.AddApplicationServices`, `ApplicationBuilderExtensions.ConfigurePipeline`, `DatabaseExtensions.MigrateDatabase`).
* **Feature handlers auto-register by convention (ADR-017).** `AddFeatureHandlers` scans the assembly with **Scrutor** and registers every concrete `*Handler` as scoped, `AsSelf`. A new handler just needs to be a `public` class named `…Handler` under `Features/**` — **do not** add a manual `services.AddScoped<…Handler>()` line. Services that are not use-case handlers (e.g. `ApiKeyAuthenticator`, options, workers, the provider seam) are still registered explicitly. If you ever need a `*Handler` that must *not* be a per-request scoped service, register it explicitly and exclude it from the scan.
* **No `var` — declare the explicit concrete type.** Write `Result validation = …`, `SqlConnection connection = …`, `List<BatchMessage> messages = …`, never `var`. The only exception is anonymous types, which have no nameable type (avoid binding those to a local where possible). Enforced by `.editorconfig` (IDE0008 = warning → error via `TreatWarningsAsErrors`). See ADR-016.
* **Match surrounding code/docs style.** This repo's design docs are detailed and justify tradeoffs — keep that bar.
* **When a tradeoff exists, explain the reasoning** rather than just picking one.
* **Keep handlers focused on the use case; split large methods.** A handler's public entry point should read as the business flow (e.g. validate → resolve → price → persist), with each step a small, clearly-named private method. Decompose rather than letting one method do validation, pricing, persistence, and SQL mapping at once. Prefer small private helpers over new classes/interfaces/services — this is a readability tool, not an excuse for extra layers.
* **Don't leak SQL Server details into application logic.** Magic error numbers (`547`, `2601/2627`) go through the named checks in `Shared/Database/SqlServerErrors` (`ex.IsConstraintConflict()`, `ex.IsUniqueViolation()`), not inline in handler `catch` filters. Destination table names live next to the row shapes they belong to (a feature SQL/mapper class), not as string literals in the handler.
* **Bulk-copy / `DataTable` mapping lives in a feature-local mapper, not the handler.** Infrastructure row-building (e.g. `SendMessagesRowMapper`) is a SQL concern; keep it out of the use-case handler so the handler stays about the flow.
* **Never use `DateTime.UtcNow`/`DateTime.Now` directly — inject `TimeProvider`.** Take `TimeProvider` via the constructor (registered as `TimeProvider.System`) and read `_clock.GetUtcNow().UtcDateTime`, so time is controllable in tests. Tests construct with `TimeProvider.System` (or a fake). SQL-side defaults (`SYSUTCDATETIME()`) are fine where the DB owns the timestamp.
* **Reduce duplication with small private helpers; favour simplicity over abstraction.** When you see repeated mapping/persistence/error-handling, extract a small helper or a shared static (like `SqlServerErrors`). Prefer a straightforward implementation over an overly abstract one — don't add interfaces or layers that aren't pulling their weight.

### UI table conventions (apply to every new or revised management table)

* Provide exactly one search input per table; do not duplicate page-level and grid-level search fields.
* Keep toolbar actions together as icon-only controls: Excel export, filters, column visibility, and add (when needed). Show add as a plus icon beside the other actions, not as a separate text button.
* Use an Excel icon in green for Excel export. Use appropriate filter and column icons for the other actions.
* Give every toolbar icon a Persian hover tooltip and an accessible Persian label; also expose the tooltip on keyboard focus.
* All filter buttons, options, operators, and actions must use Persian text, including any built-in grid filter UI.
* Do not add redundant explanatory copy above tables, such as “۲ مورد فعال و قابل مدیریت”.
* Keep input labels and table text comfortably readable, with sufficient contrast against the panel background.
* Use neutral blue-gray alternate table rows (#f3f6fa) and hover backgrounds (#e9f0f8); do not use pink or purple row striping.
* Use the bundled Vazirmatn font throughout management UI and overlays. Format display dates as Jalali using DNTPersianUtils.Core and display numbers with Persian digits (including pagination). Keep raw values typed for sorting/calculation, preserve UTC timestamp semantics, and normalize Persian/Arabic digits for search and date-query inputs; never modify API secrets or machine identifiers.

### UI completion and verification criteria — standing user preference (2026-11-01)

* **Completion means an observable user outcome.** Changed files, successful builds and test counts alone do not establish that the requested behavior works. Translate requests such as «قشنگش کن» and «مطمئن شو درست کار می‌کند» into concrete acceptance criteria yourself, using repository conventions; do not shift routine specification work back to the user.
* **Visual review:** inspect the actual rendered UI for consistency with neighboring pages, readable text/numbers, contrast, spacing, desktop/mobile layout and keyboard access. Check relevant empty, loading, error and success states. Capture before/after screenshots where practical; label mock-data previews explicitly.
* **Functional review:** exercise the user's path from the actual control through the authenticated UI boundary to the visible result in an isolated test environment. Verify enabled/disabled controls, request execution, feedback and retry behavior. Calling a handler/component method directly or finding a button's label in HTML is not sufficient evidence that clicking it works.
* **Regression tests must catch the reported failure.** For behavior fixes, ensure the regression check would fail on the broken version. Include duplicate-click/retry/idempotency checks when an action creates messages or charges. Do not use test volume as a substitute for relevant coverage; cosmetic-only changes need visual verification rather than tests that merely mirror CSS.
* **Review after the final edit.** Recheck affected user flows and rendered states after the last change, including whether the fix introduced a new problem. Earlier evidence does not automatically verify later changes.
* **Keep verification isolated.** Use synthetic data and a test provider for SMS/accounting workflows; testing must not send real messages, create real charges or alter production data without explicit authorization.
* **Report the verification scope honestly.** Distinguish build/unit checks, rendered/mock previews, interactive UI checks, integration tests, real-provider checks and deployed-server verification. State what was actually checked and any remaining limitation; never imply that a mock preview proves real-service behavior or that unpublished code is already deployed.

## 8. Resolved review decisions (locked)

All resolved in favor of the design as presented (README §10):

1. Delivery model — **denormalized `Message.DeliveryStatus` + append-only `DeliveryReport`**.
2. `DeliveryReport` retention — **lockstep with `Message`** (full history).
3. `MessageBody` retention — **shorter, `Id`-partitioned** purge is acceptable.
4. `MessageType` — **single global `TINYINT`** (tenant-scoping additive later).
5. API keys — **hashed `ApiKey` + optional `ApiKeyIpRestriction`** sufficient (scopes/attribution additive later).
6. Partition cadence — **Jalali-monthly** (weekly is an additive fallback).
7. Request accounting — **`MessageBatch`** added (one per API call) + `MessageBatchId` FK on `Message`; fact keys stay denormalized (not moved up).
8. Billing — **signed balance; debt allowed (2026-10-14)**: `CustomerBalance` + `BalanceTransaction` ledger; atomic debit; immediate-debit + refund only on provider submission-reject.
9. Batch ops visibility — authoritative `MessageBatch.Status` + 3 timestamps (`ReceivedAtUtc`/`DispatchStartedAtUtc`/`FinishedAtUtc` + rolling `StatusChangedAtUtc`); **no per-status timestamps**. Granular timeline in `MessageBatchEvent` (operational event store, append-only, ~90-day retention — **not** business audit).

## Live dispatch monitoring (2026-10-18)

`/admin/dispatch-live` reads existing send/delivery states; `DispatchAcceptance` (migration 0034) records the guarded acceptance transition atomically for a ten-second UTC chart. This is operational evidence, not a second dispatch queue. Never infer handset delivery from `Submitted`, or backfill missing provider acceptance times from request receipt. See `docs/operations/live-dispatch-fa.md`.

## Single-message resend (2026-10-18)

`POST /messages/{messageId}/resend` accepts a customer API key and a required request UUID. Delivery-report UI uses the same resend handler through the administrator UI boundary. Resends create a new single-message batch using current validation/pricing/billing; source history is immutable. Only rejected submissions or accepted messages with definite delivery outcomes are eligible; queued/uncertain outcomes and missing text are blocked. Billing customer comes from the stored source, while administrator actions retain the actual administrator ApiKeyId. The `resend:{sourceId}:{requestId:N}` client batch/correlation key preserves linkage and idempotency. See `docs/operations/resend-message-api-fa.md`.

## Guided management workflows (2026-11-01)

Daily resend at `/admin/send` uses a durable once-per-Jalali-day manifest (migration 0039), explicit cost confirmation, stored per-source request UUIDs and the existing atomic acceptance path. Include only proven pre-submission rejection or stored definite non-delivery; exclude delivered/unknown/queued/expired outcomes, prior resend sources and ambiguous same-day recipients. Revalidate before acceptance and dispatch; never label queue acceptance as provider submission or handset delivery. Preserve the manifest beyond operational-event retention. See `docs/operations/daily-resend-fa.md`.

Sending requires a separate cost-review/confirmation step; editing invalidates the estimate and confirmation. Keep technical identifiers in advanced settings. `/admin/dispatch-operations` is the primary send/delivery follow-up entry, with per-message history and eligible resend inline; legacy diagnostic routes stay available. Search/filter server-paged tables before pagination, and keep their summaries consistent with the same filters. Dashboard balances require explicit customer selection. See `docs/operations/management-workflows-fa.md`.

## Standard publish — standing user preference (2026-11-01)

When the user asks for a publish/package, run `scripts/publish-production.ps1` immediately without re-asking packaging preferences. The fixed contract is **Release, win-x64, self-contained .NET + ASP.NET Core, Windows executable, existing private Production settings from `artifacts/host-settings/appsettings.Production.json`, IIS Production, timestamped folder + ZIP**. Unit tests run by default; integration tests are opt-in. The script verifies the package. Do not substitute the generic defaults of `publish.ps1`, omit Production settings, or switch to framework-dependent output unless explicitly requested. If the private settings file is missing, report that concrete blocker rather than making a different package. Keep prior packages. Return the verified ZIP link and test result. See `scripts/README.md` and `docs/operations/deployment-runbook-fa.md`.
