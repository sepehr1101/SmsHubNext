# CLAUDE.md

Guidance for Claude when working in the **SmsHubNext** repository. Keep this file concise and high-signal; update it as decisions are made.

---

## 1. What this project is

**SmsHubNext** is a high-volume, multi-tenant **SMS dispatch and accounting platform** for Iranian SMS providers. Primary use case: utility/organizational notifications (e.g. water-bill SMS) sent to subscribers across Iranian provinces/cities/zones, billed and reported by provider, geography, business category, and **Jalali (Persian) calendar** period.

- **First provider:** Magfa (sender lines `3000…`, `1000…`, `4040…`, etc.). Designed so more providers are added later by inserting data, not changing schema.
- **Currency:** Iranian Rial (IRR).
- **Reporting calendar:** Jalali (e.g. "Spring 1405"), stored UTC + a Jalali date dimension.

## 2. CURRENT PHASE — scope guardrails ⚠️

We are in the **database & storage architecture design** phase. The data model must be validated before any implementation.

**Do NOT, in this phase:**
- ❌ Implement the application or write ASP.NET Core code
- ❌ Write RabbitMQ / messaging implementation
- ❌ Write provider integrations (Magfa HTTP client, etc.)
- ❌ Write API controllers

**Do:**
- ✅ Work on the data model / storage architecture (`README.md` is the live design doc)
- ✅ Refine schema, indexing, partitioning, tariff/cost, reporting, concurrency analysis
- ✅ Ask before expanding scope beyond data modeling

> If a request implies writing app code, confirm the phase has formally moved on before generating it.

## 3. Tech decisions (locked unless changed here)

| Area | Decision | Notes |
|---|---|---|
| Language/Runtime | **.NET 8 (LTS), C#** | |
| Database | **SQL Server (2019+)** | Chosen for partitioning, `OPTIMIZE_FOR_SEQUENTIAL_KEY`, lock-escalation control, columnstore |
| Money type | `DECIMAL(19,4)`, IRR | |
| Text | `NVARCHAR` for Persian; `VARCHAR` for phone numbers/codes (ASCII) | |
| Architecture (later) | Clean Architecture; providers as pluggable adapters behind `ISmsProvider` | Not built yet |
| Send model (later) | Async outbox + status polling (recommended) | Not built yet |

## 4. Architecture decisions that must be respected

These are the load-bearing choices from `README.md`. Don't silently contradict them:

- **Fact + dimension model.** `Message` is the central high-volume fact (up to ~1B rows at 5yr). Small dimensions referenced by surrogate keys.
- **Deliberate denormalization for reporting.** The fact carries denormalized dimension keys (`ProvinceId/CityId/ZoneId/ProviderId/BusinessCategoryId/MessageTypeId`) so aggregate reports don't join the billion-row fact.
- **Cost snapshotting.** Tariffs are versioned by effective-date range, but the resolved price (`Encoding/CharacterCount/SegmentCount/TariffId/UnitPrice/TotalCost`) is **frozen onto each `Message`** at submission so history stays accurate after tariff changes. Reporting never re-resolves tariffs.
- **Narrow fact, separate text.** No free text on `Message`; the body lives in a 1:1 `MessageBody` table (template + merge vars for campaigns; full text for ad-hoc) with shorter retention.
- **Pre-aggregation.** `MessageDailyAggregate` powers aggregate reports at flat cost regardless of fact growth.
- **Concurrency:** monthly **partitioning** by `SubmitDateKey`; clustered key `(SubmitDateKey, MessageId)` with `OPTIMIZE_FOR_SEQUENTIAL_KEY = ON`; minimal nonclustered indexes; batch inserts < 5,000 rows/txn (under lock-escalation threshold); RCSI reads; async post-commit aggregation; retention via partition switching.
- **Optimize for:** write throughput, reporting simplicity, low storage, minimal deadlocks, maintainability — **not** normalization purity.

## 5. Domain glossary (quick reference)

Customer (tenant) · Provider · SenderLine · MessageType · BusinessCategory · Province/City/Zone · Subscriber (recipient) · Campaign · MessageTemplate · Message (fact) · MessageBody · Tariff/TariffRate · DeliveryReport (normalized status on Message) · DimDate (Jalali).

Full table-by-table detail lives in **`README.md`** — read it before changing the schema.

## 6. Roadmap (phases)

0. Foundation (solution scaffold) — *not started; blocked by current design phase*
1. Walking skeleton: send one SMS via Magfa
2. Persistence & delivery status
3. Reliability: async outbox + retries
4. Feature breadth: bulk, balance, inbox, part counting
5. Hardening & multi-tenancy
6. Second provider (proves the abstraction)
7. Finish line: tests, Docker, deploy

> We are **pre-Phase 0**, validating the data model.

## 7. Working conventions

- **Branch:** develop on the designated feature branch (currently `claude/modest-knuth-29zw1v`). Never push elsewhere without explicit permission.
- **Commits:** clear, descriptive messages. Commit author/email must be `Claude <noreply@anthropic.com>` so GitHub shows commits as verified.
- **PRs:** do **not** open a pull request unless explicitly asked.
- **Match surrounding code/docs style.** This repo's design docs are detailed and justify tradeoffs — keep that bar.
- **When a tradeoff exists, explain the reasoning** rather than just picking one.

## 8. Open review questions (from README §10)

1. `DeliveryReportLog` raw-DLR audit — keep off by default?
2. `MessageDailyAggregate` grain — exclude `Zone` (served via columnstore)?
3. `MessageBody` shorter retention than `Message` — legally OK?
4. Multi-tenancy grain = `Customer`; per-customer tariffs ever needed?
5. Partition cadence — monthly vs. weekly given peak daily volume?
