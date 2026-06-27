# CLAUDE.md

Guidance for Claude when working in the **SmsHubNext** repository. Keep this file concise and high-signal; update it as decisions are made.

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
| Auth               | Per-customer **API keys**, stored as **SHA-256 hash** only (never plaintext); optional per-key CIDR allow-list                                                                                                                              |                                                                                                       |
| App architecture   | **Monolith organized by feature** (DntSite-inspired coding style; **not** Clean/Onion/CQRS/Vertical-Slice/MediatR/FluentValidation), ASP.NET Core Controllers, Dapper (no repo/UoW), plain in-feature validation, Result pattern, no Aspire | See `ARCHITECTURE.md`; providers behind `ISmsProvider`; background impl-agnostic (Hangfire candidate) |
| Send model (later) | Async outbox + status polling (recommended)                                                                                                                                                                                                 | Not built yet                                                                                         |

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
* **Optimize for:** write throughput, reporting simplicity, low storage, minimal deadlocks, maintainability — **not** normalization purity.

## 5. Domain glossary (quick reference)

17 tables: Customer · ApiKey · ApiKeyIpRestriction · Provider · SenderLine · MessageType · GeoSection (self-referencing) · Tariff · TariffRate · Message (fact + current `DeliveryStatus` + `MessageBatchId`) · MessageBody · DeliveryReport (append-only status history) · MessageBatch (one per API call: accounting/attribution/idempotency + dispatch `Status`/holds + 3 lifecycle timestamps) · CustomerBalance (prepaid, 1/customer) · BalanceTransaction (append-only money ledger) · MessageBatchEvent (operational event store, append-only, ~90-day retention) · ProviderCredential (encrypted provider secrets).

Recipient = `MobileNumber` on the message (ad-hoc, no Subscriber table). Caller references on the message: `ClientCorrelatedId` (idempotency), `BillId`, `PayId` (all nullable). **Billing is prepaid:** atomic overspend-safe debit at batch accept; provider low-credit ⇒ batch `Held`, messages stay `Queued`.

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

* **Branch:** develop on the designated feature branch (currently `claude/continue-previous-work-c6qqvn`). Never push elsewhere without explicit permission.
* **Commits:** clear, descriptive messages. Commit author/email must be `Claude <noreply@anthropic.com>` so GitHub shows commits as verified.
* **PRs:** do **not** open a pull request unless explicitly asked.
* **Dependencies:** pin **exact** NuGet versions in `Directory.Packages.props` (Central Package Management). **No floating versions** (`2.*`, ranges) — see ADR-013.
* **Composition root:** keep `Program.cs` **minimal** — builder creation, calling extension methods, `Build()`/`Run()` only. All service registration and HTTP-pipeline wiring go through `Extensions/` (`ServiceCollectionExtensions.AddApplicationServices`, `ApplicationBuilderExtensions.ConfigurePipeline`, `DatabaseExtensions.MigrateDatabase`). New feature handlers are registered in `AddFeatureHandlers`, not in `Program.cs`.
* **Match surrounding code/docs style.** This repo's design docs are detailed and justify tradeoffs — keep that bar.
* **When a tradeoff exists, explain the reasoning** rather than just picking one.

## 8. Resolved review decisions (locked)

All resolved in favor of the design as presented (README §10):

1. Delivery model — **denormalized `Message.DeliveryStatus` + append-only `DeliveryReport`**.
2. `DeliveryReport` retention — **lockstep with `Message`** (full history).
3. `MessageBody` retention — **shorter, `Id`-partitioned** purge is acceptable.
4. `MessageType` — **single global `TINYINT`** (tenant-scoping additive later).
5. API keys — **hashed `ApiKey` + optional `ApiKeyIpRestriction`** sufficient (scopes/attribution additive later).
6. Partition cadence — **Jalali-monthly** (weekly is an additive fallback).
7. Request accounting — **`MessageBatch`** added (one per API call) + `MessageBatchId` FK on `Message`; fact keys stay denormalized (not moved up).
8. Billing — **prepaid**: `CustomerBalance` + `BalanceTransaction` ledger; atomic debit; immediate-debit + refund only on provider submission-reject.
9. Batch ops visibility — authoritative `MessageBatch.Status` + 3 timestamps (`ReceivedAtUtc`/`DispatchStartedAtUtc`/`FinishedAtUtc` + rolling `StatusChangedAtUtc`); **no per-status timestamps**. Granular timeline in `MessageBatchEvent` (operational event store, append-only, ~90-day retention — **not** business audit).
