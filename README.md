# SmsHubNext — Database & Storage Architecture Design

> **Status:** Design proposal — *for architectural review*
> **Scope of this document:** Data model and storage architecture **only**. No application, transport (RabbitMQ), provider integration, or API code is described here. The goal is to **validate the data model before any implementation begins.**
> **Target engine:** **Microsoft SQL Server** (2019+). The requirements explicitly reference *lock escalation, page contention, clustered/nonclustered indexes,* and *hot partitions* — all SQL Server concepts — and the platform is built on .NET. The logical model remains portable; the physical tuning notes are SQL-Server-specific and are flagged as such.
> **Currency:** Iranian Rial (IRR). **Calendar:** Reporting periods are expressed in the **Jalali (Persian) calendar** (e.g. *Spring 1405*); storage is UTC with a Jalali date dimension.

---

## Table of Contents

1. [Business Requirements Analysis](#1-business-requirements-analysis)
2. [Domain Analysis](#2-domain-analysis)
3. [Database Schema Proposal](#3-database-schema-proposal)
4. [Table-by-Table Deep Analysis](#4-table-by-table-deep-analysis)
5. [Reporting Validation](#5-reporting-validation)
6. [Tariff and Pricing Design](#6-tariff-and-pricing-design)
7. [Storage Optimization Analysis](#7-storage-optimization-analysis)
8. [Concurrency and Deadlock Prevention](#8-concurrency-and-deadlock-prevention)
9. [Future Evolution](#9-future-evolution)
10. [Design Principles & Tradeoff Summary](#10-design-principles--tradeoff-summary)

---

## 1. Business Requirements Analysis

SmsHubNext is a **high-volume, multi-tenant SMS dispatch and accounting platform**. Its primary documented use case is **utility/organizational notifications** — e.g. water-bill notices sent to subscribers across Iranian geographic sections (province → city → zone) — billed and reported by provider, geography, message type, and time period (in the Jalali calendar).

### 1.1 Expected usage patterns

| Pattern | Description | Storage impact |
|---|---|---|
| **Bursty bulk campaigns** | Billing cycles trigger campaigns of **hundreds of thousands to millions** of messages in a short window. | Heavy concurrent inserts → write path must be the #1 optimization target. |
| **Steady transactional/OTP traffic** | Lower-volume, latency-sensitive single messages (OTP, alerts). | Small but constant; must not be starved by bulk inserts. |
| **Asynchronous delivery reports (DLR)** | Each sent message later receives 1+ status updates from the provider, arriving minutes-to-hours later. | High-volume **update** traffic against already-written rows. |
| **Heavy reporting/analytics** | Finance and operations run aggregate reports by geo section / provider / message type / period. | Read path competes with writes → must be isolated from OLTP hot path. |

### 1.2 Expected message volumes (working assumptions for sizing)

Baseline assumption: a national utility footprint, **~10 million messages/month** average with billing-cycle peaks of several million/day.

| Horizon | Cumulative messages | Order of magnitude |
|---|---|---|
| 1 month | ~10 million | 10⁷ |
| 1 year | ~120 million | 10⁸ |
| 5 years | ~600 million – 1 billion | 10⁸–10⁹ |

**Design must assume the billion-row regime.** Every per-message byte and every per-message index is multiplied by ~10⁹.

### 1.3 Reporting requirements

The schema must answer, efficiently and accurately:

- Cost by **geo section** (province / city / zone), by **provider**, by **message type** (e.g. water-bill notices), by **Jalali period** (e.g. *Spring 1405*).
- **Counts** and **delivery success rates** by the same dimensions.
- **History of a single subscriber** and **history of a single bill**.
- **Campaign summaries** and **monthly cost trends**.
- **Top provinces by spend.**

Reports are predominantly **aggregations over large ranges** plus a few **point-lookups** (subscriber/bill history). These two access shapes are in tension and are handled differently (see §5, §8).

### 1.4 Historical data requirements

- **Cost and delivery facts must remain immutable and accurate forever** (or for the legally mandated retention period) — *even after tariffs change*. This forces **price snapshotting** (see §6).
- Aggregated reporting data is effectively **permanent** (cheap, summarized).
- Raw **message bodies** carry the highest storage cost and the lowest long-term value → candidate for **shorter retention** and **physical separation** (see §7).

### 1.5 Cost calculation requirements

- Cost depends on **provider**, **encoding** (GSM-7 vs. UCS-2 for Persian text), **segment/part count**, and the **tariff in effect at submission time**.
- Persian (UCS-2) messages bill at **70 chars/segment** (67 for concatenated parts) vs. GSM-7 **160/153** — part counting is central to cost.
- The **computed cost is frozen onto the message at submission** so later tariff edits never retroactively change historical billing.

### 1.6 Future provider expansion requirements

- Today: **Magfa** (sender lines `3000…`, `1000…`, `4040…`, etc.). Tomorrow: additional Iranian providers.
- A new provider must be addable by **inserting reference + tariff rows**, not by altering the fact schema. Provider-specific raw status codes are mapped to a **normalized status** so reporting is provider-agnostic.

---

## 2. Domain Analysis

| Concept | Responsibility |
|---|---|
| **Customer (Tenant / Customer Context)** | The organization that owns the traffic (e.g. a regional water company). Top-level ownership and isolation boundary for billing and reporting. |
| **Provider** | An SMS carrier/aggregator (Magfa, …). Owns sender lines and tariffs; source of delivery reports. |
| **Sender Line** | A specific origin number (`3000…`, `1000…`, `4040…`) belonging to a provider. Affects routing, pricing, and operator reachability. |
| **Message Type** | Single classification axis for a message — both its *delivery class* (OTP / Transactional / Bulk-Notification) and its *business purpose* (Water Bill, Outage Alert, …). Drives priority, possibly tariff, and reporting splits. (Business purpose was a separate `BusinessCategory` dimension in an earlier draft; merged here for simplicity.) |
| **Campaign** | A logical batch of messages produced by one dispatch operation. Unit of operational monitoring and summary reporting. |
| **Message Template** | The reusable body pattern for a campaign, with merge placeholders. Stored once; not duplicated per recipient. |
| **Subscriber (Recipient)** | The end party receiving the SMS (a utility subscriber). A **bounded** population reused across millions of messages → modeled as a dimension, not duplicated per message. |
| **Message** | The central **fact**: one SMS send. Carries denormalized reporting keys + frozen cost snapshot + current delivery status. The highest-volume table. |
| **Message Body** | The exact rendered/sent text (or template + merge variables). Physically separated from the Message fact for storage and retention reasons. |
| **Tariff** | Time-bounded pricing for a (provider, encoding, message-type) combination. Versioned by effective date range. |
| **Delivery Report** | The provider's eventual status for a message. Normalized status is folded onto the Message; raw provider codes optionally retained for audit. |
| **Date (Jalali) Dimension** | Pre-computed mapping of calendar dates to Persian year/season/month for clean period reporting (*Spring 1405*). |
| **Geo Section** | A single self-referencing geographic dimension that models the whole location hierarchy (Province → City → Zone → …) in one table via a parent link + `SectionType`. Replaces the former separate `Province`/`City`/`Zone` tables. |

**Fact vs. Dimension split is the spine of this design:** small, stable **dimensions** (provider, line, geo section, subscriber, message-type, date, tariff) are referenced by surrogate keys; the enormous **Message fact** carries denormalized dimension keys so reports group/filter without joining giant tables, and carries a frozen cost snapshot so history is immutable.

---

## 3. Database Schema Proposal

Tables are grouped by role. Volume estimates assume the §1.2 sizing.

### 3.0 Table inventory

| # | Table | Role | 1 mo | 1 yr | 5 yr |
|---|---|---|---|---|---|
| 1 | `Customer` | Dimension | <100 | <100 | <500 |
| 2 | `Provider` | Dimension | <10 | <20 | <50 |
| 3 | `SenderLine` | Dimension | <100 | <200 | <500 |
| 4 | `MessageType` | Dimension | ~10 | ~30 | ~80 |
| 5 | `GeoSection` | Dimension (self-referencing) | ~30k | ~50k | ~80k |
| 6 | `Subscriber` | Large dimension | ~5M | ~15M | ~25M |
| 7 | `Campaign` | Dimension | ~3k | ~40k | ~200k |
| 8 | `MessageTemplate` | Dimension | ~1k | ~10k | ~50k |
| 9 | `Tariff` | Dimension (versioned) | <100 | <300 | ~1k |
| 10 | `TariffRate` | Dimension (versioned) | <500 | ~1.5k | ~5k |
| 11 | `DimDate` | Dimension (seed) | ~1.8k | ~1.8k | ~3.7k |
| 12 | **`Message`** | **Fact (hot)** | **~10M** | **~120M** | **~0.6–1B** |
| 13 | `MessageBody` | Fact satellite (text) | ~10M | ~120M | ~0.6–1B |
| 14 | `DeliveryReportLog` | Optional audit (raw DLR) | ~12M | ~150M | ~1B+ |
| 15 | `MessageDailyAggregate` | Pre-aggregated rollup | ~150k | ~1.8M | ~9M |

> Below, each table is summarized against the required facets. Full column-level detail is in **§4**.

### Dimension tables (1–11) — shared facets

- **Purpose:** Provide stable, deduplicated reference data referenced by the Message fact via surrogate keys.
- **Business justification:** Eliminate repetition of descriptive text (geo-section names, line numbers, templates) across ~10⁹ fact rows; enable consistent grouping/filtering in reports.
- **Read pattern:** Tiny lookups; frequently cached in the application; joined to aggregates (not to the raw fact) for labels.
- **Write pattern:** Rare inserts/updates (admin/onboarding). `Subscriber` is the exception — moderate insert/upsert volume as new subscribers appear, but **bounded** by the real population.
- **Retention:** Effectively permanent. Versioned dimensions (`Tariff`, `TariffRate`) keep history via effective-date ranges; rows are never hard-deleted.
- **Storage:** Negligible relative to the fact. Their entire value is *avoiding* duplication in the fact.

### 12. `Message` — the central fact (hot path)

- **Purpose:** One row per SMS send. The system of record for what was sent, to whom, by which line/provider, at what cost, with what delivery outcome.
- **Business justification:** Every report, every cost calculation, and every history lookup ultimately resolves here.
- **Expected volume:** 10M (1mo) → 120M (1yr) → up to ~1B (5yr).
- **Read pattern:** (a) **range aggregations** for reporting — served primarily by `MessageDailyAggregate` and partition-eliminated columnstore scans, *not* by hammering the rowstore; (b) **point lookups** by subscriber, by bill, by provider message id (for DLR matching).
- **Write pattern:** Massive **concurrent batch inserts** during campaigns; high-volume **single-column status updates** when DLRs arrive. Designed to minimize both insert hot-spotting and update-time index churn (see §8).
- **Retention:** Long (billing/legal). Aged out by **partition switching** by month, not by row-level `DELETE`.
- **Storage:** Kept **deliberately narrow** (fixed-width keys + cost snapshot + status; **no free text**) so more rows fit per page → faster scans, smaller indexes, cheaper inserts. Text lives in `MessageBody`.

### 13. `MessageBody` — text satellite (1:1 with Message)

- **Purpose:** Hold the exact sent text (or `TemplateId` + merge variables) separately from the narrow fact.
- **Business justification:** Audit/legal proof of content without bloating the fact that powers reporting.
- **Volume:** 1:1 with `Message`.
- **Read pattern:** Rare — only when an operator inspects an individual message. Never scanned for aggregates.
- **Write pattern:** Inserted alongside the message (same batch). Immutable afterward.
- **Retention:** **Shorter** than the fact where policy allows — the body partition can be purged earlier to reclaim the bulk of storage while keeping cost/delivery facts.
- **Storage:** The largest per-row cost in the system → isolated so it can be compressed and retired independently (see §7).

### 14. `DeliveryReportLog` — optional raw DLR audit

- **Purpose:** Append-only log of raw provider status callbacks/poll results.
- **Business justification:** Forensic/audit trail and reprocessing; the *normalized* current status already lives on `Message`.
- **Volume:** ≥ message volume (a message may receive multiple updates).
- **Read pattern:** Rare, point lookups by message; mostly write-only.
- **Write pattern:** Append-only inserts.
- **Retention:** **Short** (e.g. 30–90 days) — high volume, low long-term value.
- **Storage:** Justified only if audit/reprocessing is required; **off by default** (see §4.14 tradeoff). The normalized status on `Message` is the source of truth for all reporting.

### 15. `MessageDailyAggregate` — pre-rolled reporting cube

- **Purpose:** Pre-summarized counts and costs at a daily grain across the core reporting dimensions.
- **Business justification:** Turns "sum 200 million rows" into "sum a few thousand rows." This is what the dashboards and finance reports actually read.
- **Volume:** Bounded by *dimension combinations × days*, not by message count (~1.8M/yr).
- **Read pattern:** The primary surface for §5 reports — small, indexed, fast.
- **Write pattern:** Incrementally upserted by a background rollup process (post-commit), **never** in the message insert transaction (avoids contention).
- **Retention:** Permanent (cheap).
- **Storage:** Tiny vs. the fact; the highest leverage object in the schema.

---

## 4. Table-by-Table Deep Analysis

> Notation: `PK` primary key, `FK` foreign key, `CIX` clustered index, `NCIX` nonclustered index. SQL Server types. Money = `DECIMAL(19,4)` (IRR). Phone numbers are ASCII → `VARCHAR`; Persian descriptive text → `NVARCHAR`.

### 4.1 `Customer`
| Column | Type | Notes |
|---|---|---|
| `CustomerId` | `SMALLINT IDENTITY` | **PK**, `CIX` |
| `Name` | `NVARCHAR(200)` | |
| `Code` | `VARCHAR(50)` | external/business code; `NCIX` unique |
| `IsActive` | `BIT` | |
| `CreatedAtUtc` | `DATETIME2(3)` | |

**Why separated / preferred / alternatives:** Tenancy is a first-class isolation and reporting boundary. A `SMALLINT` key (hundreds of customers max) is the cheapest possible FK to repeat ~10⁹ times on the fact. *Alternative:* embedding customer name on the fact — rejected (text duplication at 10⁹ scale).

### 4.2 `Provider`
| Column | Type | Notes |
|---|---|---|
| `ProviderId` | `TINYINT IDENTITY` | **PK**, `CIX` |
| `Name` | `NVARCHAR(100)` | e.g. "Magfa" |
| `Code` | `VARCHAR(50)` | `NCIX` unique |
| `BaseUrl` | `VARCHAR(300)` | primary API endpoint (e.g. public-internet gateway) |
| `FallbackBaseUrl` | `VARCHAR(300)` | secondary endpoint over a different network path (e.g. intranet/private gateway); nullable |
| `IsActive` | `BIT` | |

**Why:** Provider count is tiny → `TINYINT`. New providers = new rows, never schema change. *Alternative considered:* provider as a string enum on the fact — rejected (4–6 bytes × 10⁹ and no referential integrity).

**Endpoints live here (provider *info*, not reporting); credentials do not.** `Provider` is a small (<50-row) **entity/info** table that is *also* referenced by the fact — but the "keep it lean for reporting" rule applies to the billion-row fact (which only ever stores `ProviderId` and never widens), **not** to this table. Storing connection endpoints here is therefore free and correct. The driving requirement: in Iranian governmental/enterprise deployments the **same provider is reached over more than one network path simultaneously** — e.g. the public internet **and** an intranet/private gateway — with automatic failover between them. That is runtime data the application reads and fails over on, **not** per-environment (`appsettings`) configuration. We model it as **two columns** — `BaseUrl` (primary) + `FallbackBaseUrl` (secondary path) — which covers the realistic *primary + one fallback* case with zero extra machinery. **Credentials** (`username/domain/password`) are deliberately **excluded** from the table: URLs are not secret, but credentials are, so they stay in the secret store (user-secrets/Key Vault), keyed by `Provider.Code`. *Alternative considered (and deferred):* a child `ProviderEndpoint` table supporting **three or more** network paths with an explicit failover order — rejected for now as over-engineering (YAGNI); see §9 for the escalation path if a provider ever needs more than two paths.

### 4.3 `SenderLine`
| Column | Type | Notes |
|---|---|---|
| `SenderLineId` | `SMALLINT IDENTITY` | **PK**, `CIX` |
| `ProviderId` | `TINYINT` | **FK** → `Provider` |
| `LineNumber` | `VARCHAR(20)` | `3000…`, `1000…`, `4040…`; `NCIX` |
| `IsSharedLine` | `BIT` | shared (public) line vs. dedicated (private) number |
| `IsActive` | `BIT` | |

**Why:** Lines have distinct pricing/reachability and belong to a provider. Surrogate `SMALLINT` keeps the fact FK small. *Alternative:* storing the raw line string on each message — rejected (repetition + no metadata). The shared-vs-dedicated distinction is genuinely **binary**, so it is modeled as a `BIT` (`IsSharedLine`) rather than a `TINYINT` type code — simpler and self-documenting. (Number-class nuances like `3000`/`1000`/`021` are derivable from `LineNumber` and don't warrant a type column; if a true third line *class* ever emerges, promote `IsSharedLine` back to a `TINYINT`/lookup.)

### 4.4 `MessageType`
| Column | Type | Notes |
|---|---|---|
| `MessageTypeId` | `TINYINT` | **PK** (seeded), `CIX` |
| `Name` | `NVARCHAR(80)` | "OTP", "Transactional", "Bulk", "Water Bill", "Outage Alert", … |
| `Code` | `VARCHAR(50)` | `NCIX` |

**Why:** This is now the **single classification axis** for a message — it carries both the *delivery class* (OTP/Transactional/Bulk) and the *business purpose* (Water Bill, Outage Alert, …) after merging the former `BusinessCategory` dimension. Kept **global and `TINYINT`** for simplicity: the realistic value set is a few dozen. *Alternative considered:* keep `BusinessCategory` as a separate tenant-scoped dimension + a second FK on the fact — rejected for now (an extra table, an extra ~2-byte fact key, and an extra report join for a distinction the platform does not currently need). *Alternative:* a `BIT IsOtp` flag — rejected (not extensible). **Future-proofing (additive):** if tenant-specific purposes proliferate, add a nullable `CustomerId` (NULL = global type) and/or widen to `SMALLINT` — both are non-breaking changes.

### 4.5 `GeoSection` (self-referencing geographic hierarchy)
| Column | Type | Notes |
|---|---|---|
| `GeoSectionId` | `INT IDENTITY` | **PK**, `CIX` |
| `ParentGeoSectionId` | `INT` | **FK** → `GeoSection` (self); `NULL` at the top level (province) |
| `SectionType` | `TINYINT` | 1 = Province, 2 = City, 3 = Zone (extensible to more levels) |
| `Name` | `NVARCHAR(100)` | |
| `Code` | `VARCHAR(20)` | `NCIX` |
| `Path` | `VARCHAR(900)` | materialized ancestor path, e.g. `/12/450/8123/`; `NCIX` for fast subtree filters |
| `IsActive` | `BIT` | |

**Why one self-referencing table instead of three (`Province`/`City`/`Zone`):** the location structure is a strict hierarchy, and modeling it as a single **adjacency-list** table (`ParentGeoSectionId`) collapses three tables into one while *preserving* the hierarchy — so province/city/zone rollups remain possible. Adding a deeper level later (e.g. a sub-zone) is a data insert, not a schema change. The denormalized **`Path`** (materialized path of ancestor ids) makes "everything under Tehran province" a single sargable `Path LIKE '/<TehranId>/%'` filter, avoiding recursive walks at report time.

**How reports still avoid hierarchy walks on the billion-row fact:** the fact stores a **single** `GeoSectionId` (the most-specific section, typically the zone) instead of three separate keys. Province/city rollups are **not** done against the fact — they are done against the small `MessageDailyAggregate` joined to the small `GeoSection` tree via `Path`. So the billion-row fact is never hierarchy-walked; the aggregate (a few million rows) is. This preserves the original "no multi-join over 10⁹ rows" guarantee with one fact column instead of three (a small storage win: 4 bytes vs. 7).

*Alternatives considered:* (a) keep three separate `Province`/`City`/`Zone` tables with three denormalized fact keys — workable but more tables/keys than needed; superseded by this consolidation at the user's request. (b) a **flat, non-hierarchical** "Section" tag (one level, no parent) — rejected because reports #1/#3/#4/#10 require province/city/zone rollups, which a flat tag cannot express. (c) SQL Server's native **`HIERARCHYID`** type instead of a `Path` string — a valid alternative with built-in `IsDescendantOf`/ordering support; `Path` is chosen here for transparency and portability, but `HIERARCHYID` is an easy swap if subtree queries become hot.

### 4.6 `Subscriber`
| Column | Type | Notes |
|---|---|---|
| `SubscriberId` | `BIGINT IDENTITY` | **PK**, `CIX` |
| `CustomerId` | `SMALLINT` | **FK** |
| `MobileNumber` | `VARCHAR(15)` | canonical `98…`; **`NCIX` unique** per customer |
| `GeoSectionId` | `INT` | **FK** → `GeoSection` (subscriber's home section, typically zone) |
| `CreatedAtUtc` | `DATETIME2(3)` | |

**Why separated:** The same subscriber receives many messages; storing the phone number + home section **once** per subscriber rather than once per message saves enormous space and enables *"history of a subscriber"* via a single FK. **Note the deliberate redundancy:** `GeoSectionId` also lives on the Message fact (denormalized) — this is intentional (see §7) so reports don't join the 25M-row subscriber table. *Alternative:* no subscriber table, phone number repeated on every message — rejected (15 bytes × 10⁹ + no subscriber-level history + no upsert dedupe).

### 4.7 `Campaign`
| Column | Type | Notes |
|---|---|---|
| `CampaignId` | `BIGINT IDENTITY` | **PK**, `CIX` |
| `CustomerId` | `SMALLINT` | **FK** |
| `MessageTypeId` | `TINYINT` | **FK** — the campaign's classification/purpose |
| `MessageTemplateId` | `BIGINT` | **FK** (nullable) |
| `Name` | `NVARCHAR(200)` | |
| `Status` | `TINYINT` | Draft/Running/Completed/Failed |
| `CreatedAtUtc` | `DATETIME2(3)` | |
| `TotalCount` / `SentCount` / `FailedCount` | `INT` | maintained counters |

**Why:** Operational grouping + the unit of the *campaign summary* report. A campaign sends one kind of message, so it carries a default `MessageTypeId` (which replaced the former `BusinessCategoryId`). Counters are maintained by the rollup process, not recomputed by scanning the fact. *Alternative:* derive campaigns implicitly from message timestamps — rejected (no durable identity, no template link, no clean summary).

### 4.8 `MessageTemplate`
| Column | Type | Notes |
|---|---|---|
| `MessageTemplateId` | `BIGINT IDENTITY` | **PK**, `CIX` |
| `CustomerId` | `SMALLINT` | **FK** |
| `Body` | `NVARCHAR(1000)` | template with `{placeholders}` |
| `Encoding` | `TINYINT` | GSM7 / UCS2 (intrinsic to template language) |
| `CreatedAtUtc` | `DATETIME2(3)` | |

**Why separated:** A campaign of 1M messages shares **one** template body. Storing the body once and referencing it (with per-message merge variables in `MessageBody`) instead of repeating ~200 chars × 1M is a decisive storage win. *Alternative:* render and store full text on every message — rejected for campaigns (see §7); still allowed for ad-hoc/transactional messages.

### 4.9 `Tariff` (versioned header)
| Column | Type | Notes |
|---|---|---|
| `TariffId` | `INT IDENTITY` | **PK**, `CIX` |
| `ProviderId` | `TINYINT` | **FK** |
| `MessageTypeId` | `TINYINT` | **FK** (nullable = applies to all) |
| `Encoding` | `TINYINT` | GSM7 / UCS2 |
| `EffectiveFromUtc` | `DATETIME2(3)` | inclusive |
| `EffectiveToUtc` | `DATETIME2(3)` | nullable = open-ended |
| `Currency` | `CHAR(3)` | `IRR` |
| `IsActive` | `BIT` | |
| | | `NCIX (ProviderId, MessageTypeId, Encoding, EffectiveFromUtc)` |

### 4.10 `TariffRate` (per-segment detail)
| Column | Type | Notes |
|---|---|---|
| `TariffRateId` | `INT IDENTITY` | **PK**, `CIX` |
| `TariffId` | `INT` | **FK** → `Tariff` |
| `MinChars` | `SMALLINT` | character-range lower bound |
| `MaxChars` | `SMALLINT` | character-range upper bound (nullable = ∞) |
| `PricePerSegment` | `DECIMAL(19,4)` | |

**Why two tables:** the **header** carries the validity window and applicability; the **detail** carries the price banding by character range / segment. This separates "*which tariff applies*" (effective-date resolution) from "*how much*" (rate bands), and lets a tariff version own multiple bands without nullable sprawl. *Alternatives considered:* (a) a single wide tariff row with fixed columns per band — rejected (inflexible, not future-proof for new bands); (b) computing price in application config — rejected (prices must be **data**, auditable and snapshotted). **Crucially, tariff tables are never used at report time** — the resolved price is frozen onto the message (see §6).

### 4.11 `DimDate`
| Column | Type | Notes |
|---|---|---|
| `DateKey` | `INT` | **PK**, `CIX` — `yyyymmdd` (Gregorian) |
| `GregorianDate` | `DATE` | |
| `PersianYear` | `SMALLINT` | e.g. 1405 |
| `PersianMonth` | `TINYINT` | 1–12 |
| `PersianDay` | `TINYINT` | |
| `PersianSeason` | `TINYINT` | 1=Spring(Far–Ord–Kho) … 4=Winter |
| `PersianYearMonth` | `INT` | `yyyymm` for trend grouping |

**Why:** *"Spring 1405"* is not expressible as a contiguous Gregorian range without conversion. A pre-computed Jalali dimension turns it into `WHERE PersianYear=1405 AND PersianSeason=1`. The fact stores an `INT DateKey` (4 bytes, cheaper than `DATE`/`DATETIME2` for grouping and partition alignment). *Alternative:* convert Jalali at query time with functions — rejected (non-sargable, kills index usage on a billion rows).

### 4.12 `Message` — the fact (most-scrutinized table)
| Column | Type | Notes |
|---|---|---|
| `MessageId` | `BIGINT IDENTITY` | **PK** (nonclustered — see §8) |
| `SubmitDateKey` | `INT` | **partition column**; part of `CIX`; **FK** → `DimDate` |
| `SubmittedAtUtc` | `DATETIME2(3)` | precise timestamp |
| `CustomerId` | `SMALLINT` | **FK** |
| `CampaignId` | `BIGINT` | **FK** (nullable for transactional) |
| `SubscriberId` | `BIGINT` | **FK** |
| `ProviderId` | `TINYINT` | **FK** (denormalized) |
| `SenderLineId` | `SMALLINT` | **FK** |
| `MessageTypeId` | `TINYINT` | **FK** (delivery class + business purpose) |
| `GeoSectionId` | `INT` | **FK** → `GeoSection` (denormalized from subscriber; most-specific section, typically zone) |
| `BillNumber` | `VARCHAR(40)` | nullable; business reference for *bill history* |
| `Encoding` | `TINYINT` | GSM7 / UCS2 (snapshot) |
| `CharacterCount` | `SMALLINT` | snapshot |
| `SegmentCount` | `TINYINT` | parts (snapshot) |
| `TariffId` | `INT` | **FK** — which tariff priced this (audit) |
| `UnitPrice` | `DECIMAL(19,4)` | price per segment **at submission** (snapshot) |
| `TotalCost` | `DECIMAL(19,4)` | `UnitPrice × SegmentCount` (snapshot) |
| `Status` | `TINYINT` | normalized: Queued/Submitted/Sent/Delivered/Failed/Expired/Unknown |
| `ProviderMessageId` | `VARCHAR(50)` | provider's id, for DLR matching |
| `StatusUpdatedAtUtc` | `DATETIME2(3)` | last DLR application |

**Primary/clustered/indexes (justified in detail in §8):**
- **PK:** `MessageId` (BIGINT identity) — **nonclustered**, unique.
- **CIX (clustered):** `(SubmitDateKey, MessageId)` — aligns with monthly **partitioning** by `SubmitDateKey`.
- **NCIX 1:** `(SubscriberId, SubmitDateKey)` — subscriber history.
- **NCIX 2:** `(ProviderId, ProviderMessageId)` — DLR matching/update path.
- **NCIX 3 (filtered):** `(BillNumber) WHERE BillNumber IS NOT NULL` — bill history without indexing nulls.
- **Reporting is *not* served by NCIs here** — it is served by `MessageDailyAggregate` and an optional **nonclustered columnstore** on cold partitions (§8).

**Why this structure is preferred:**
- **Narrow + fixed-width:** no free text; every reporting/cost attribute is a small key or number → maximal rows per page, smaller indexes, cheaper inserts, faster scans.
- **Denormalized dimension keys** (`GeoSectionId/ProviderId/MessageTypeId/CustomerId`): the §5 reports filter/group on these **without joining** the huge fact to dimensions. Joins (for labels / hierarchy rollup) happen only against the tiny aggregate output.
- **Frozen cost snapshot** (`Encoding/CharacterCount/SegmentCount/TariffId/UnitPrice/TotalCost`): historical accuracy is independent of later tariff edits.
- **Normalized `Status` + `ProviderMessageId`:** provider-agnostic reporting + an efficient DLR update path.

**Alternatives considered & rejected:**
- *Fully normalized fact (only `SubscriberId`, derive geo/provider/type by join):* rejected — multi-join aggregation over 10⁹ rows is the exact anti-pattern the requirements forbid.
- *Three geo keys (`ProvinceId/CityId/ZoneId`) on the fact:* superseded — a single `GeoSectionId` + the `GeoSection` tree gives the same rollups with one column.
- *Cost computed at report time from tariff tables:* rejected — breaks historical accuracy and makes every cost report join versioned tariffs.
- *Storing message text inline:* rejected — bloats the fact, slashes rows-per-page, and couples retention of cheap facts to expensive text (see §4.13 / §7).
- *`UNIQUEIDENTIFIER` key:* rejected — random GUID clustered key causes fragmentation and worse page density; even as a nonclustered PK it is 16 bytes × 10⁹.

### 4.13 `MessageBody`
| Column | Type | Notes |
|---|---|---|
| `MessageId` | `BIGINT` | **PK = FK** → `Message` (1:1), `CIX`, partition-aligned on `SubmitDateKey` |
| `SubmitDateKey` | `INT` | partition column (aligned with `Message`) |
| `MessageTemplateId` | `BIGINT` | **FK** (nullable) — for campaign messages |
| `RenderedText` | `NVARCHAR(MAX)` | full text for ad-hoc/transactional; NULL for template-driven |
| `MergeVariablesJson` | `NVARCHAR(MAX)` | per-recipient substitutions when template-driven |

**Why separated from `Message`:** detailed in §7. Short version: keeps the fact narrow and lets text be compressed and **purged on its own (shorter) retention schedule**. *Alternative:* one wide table — rejected (couples hot fact to cold text). *Alternative:* always store `RenderedText` — rejected for campaigns (template + variables is far smaller).

### 4.14 `DeliveryReportLog` (optional, off by default)
| Column | Type | Notes |
|---|---|---|
| `DeliveryReportLogId` | `BIGINT IDENTITY` | **PK**, `CIX` |
| `MessageId` | `BIGINT` | **FK**; `NCIX` |
| `RawStatusCode` | `INT` | provider-native code |
| `NormalizedStatus` | `TINYINT` | |
| `ReceivedAtUtc` | `DATETIME2(3)` | |

**Why optional:** the **current** normalized status already lives on `Message`, which satisfies all reporting. A full per-event log **at least doubles the row count of the largest table**. **Tradeoff/decision:** enable only if forensic audit or DLR reprocessing is a hard requirement; if enabled, give it **aggressive short retention** (30–90 days) and its own partitioning. *Default recommendation: keep disabled; rely on the snapshot status on `Message`.*

### 4.15 `MessageDailyAggregate`
| Column | Type | Notes |
|---|---|---|
| `DateKey` | `INT` | part of **PK**, **FK** → `DimDate` |
| `CustomerId` | `SMALLINT` | part of PK |
| `GeoSectionId` | `INT` | part of PK — at the configured reporting level (city by default) |
| `ProviderId` | `TINYINT` | part of PK |
| `MessageTypeId` | `TINYINT` | part of PK (delivery class + business purpose) |
| `NormalizedStatus` | `TINYINT` | part of PK (for success-rate reports) |
| `MessageCount` | `BIGINT` | measure |
| `SegmentCount` | `BIGINT` | measure |
| `TotalCost` | `DECIMAL(19,4)` | measure |

**Indexing:** clustered on the composite key above (chosen so the most common range filter — `DateKey` — leads, enabling partition-eliminable, range-friendly scans). **The cube is keyed on `GeoSectionId` at a bounded reporting level (city by default).** The async rollup resolves each message's zone up to its city ancestor (a cheap `GeoSection` lookup) before upserting, which **bounds cardinality** the same way the earlier design excluded `Zone`. Province/top-level rollups are obtained by joining the small aggregate to the `GeoSection` tree via `Path`; **zone-level** detail is served by the columnstore on the fact (§8). *Alternative:* key the cube at zone granularity — rejected by default (combinatorial explosion: zones × days × provider × type × status). *Alternative:* materialized/indexed view — considered, but a **physically maintained table** updated post-commit avoids indexed-view locking/maintenance overhead on the hot insert path.

---

## 5. Reporting Validation

For each report: **required tables**, **join strategy**, **performance considerations**. The recurring theme: **aggregate reports read `MessageDailyAggregate` (tiny) and join only to small dimensions/the geo tree for labels & rollups; they never scan the billion-row fact.** Point-lookup reports hit a targeted nonclustered index on the fact.

| # | Report | Required tables | Join strategy | Performance |
|---|---|---|---|---|
| 1 | **Cost for Tehran province, Spring 1405, water bills** | `MessageDailyAggregate` + `DimDate` + `GeoSection` + `MessageType` | Filter `DimDate` (`PersianYear=1405, PersianSeason=1`) → join `DateKey`; resolve Tehran's `GeoSectionId`, restrict aggregate rows under it via `GeoSection.Path LIKE '/<Tehran>/%'`; filter `MessageTypeId=<Water Bill>`; `SUM(TotalCost)` | Aggregate + small-tree join; sub-second. No fact scan. |
| 2 | **Cost by provider** | `MessageDailyAggregate` + `Provider` | `GROUP BY ProviderId`, join `Provider` for names | Trivial; aggregate scan + tiny join. |
| 3 | **Count by city** | `MessageDailyAggregate` + `GeoSection` | `GROUP BY GeoSectionId` (cube is at city level), join `GeoSection` for names | Aggregate-only. |
| 4 | **Count by zone** | `Message` (columnstore) + `GeoSection` | `GROUP BY GeoSectionId` (zone) over partition-eliminated columnstore | Zone excluded from default cube → columnstore with date-partition elimination; seconds, not minutes. |
| 5 | **History of a subscriber** | `Message` + `MessageBody` + `Subscriber` | Point lookup via `NCIX (SubscriberId, SubmitDateKey)`; optional body join | Index seek; milliseconds. Body fetched only if displayed. |
| 6 | **History of a bill** | `Message` (+`MessageBody`) | Seek `filtered NCIX (BillNumber)` | Index seek; few rows; fast. |
| 7 | **Delivery success rate by provider** | `MessageDailyAggregate` | `SUM(CASE Status=Delivered)/SUM(MessageCount) GROUP BY ProviderId` | `NormalizedStatus` is part of the aggregate grain → direct. |
| 8 | **Campaign summary** | `Campaign` (+ `Message` if drill-down) | Read maintained counters on `Campaign`; drill-down via `NCIX` on `CampaignId` if needed | Counters = O(1). Drill-down = partitioned index seek. |
| 9 | **Monthly cost trend** | `MessageDailyAggregate` + `DimDate` | `GROUP BY PersianYearMonth` | Aggregate-only; ideal for time series. |
| 10 | **Top provinces by spend** | `MessageDailyAggregate` + `GeoSection` | Roll each cube row up to its province ancestor via `GeoSection.Path`; `GROUP BY province ORDER BY SUM(TotalCost) DESC` | Aggregate + small-tree join; trivial. |

**Key validation outcome:** every *aggregate* report is answerable from a table that grows with **dimension combinations × days**, not with **messages** — so reporting performance stays flat as the fact grows from 10M to 1B. Province/city rollups join only the small aggregate to the small `GeoSection` tree (never the fact). Every *point-lookup* report is answerable by a single, deliberately chosen nonclustered index seek on the fact. A reporting query for a campaign that fires DLR updates does **not** contend with the fact's hot insert region because reads target the aggregate / cold partitions (see §8).

---

## 6. Tariff and Pricing Design

### 6.1 Structure

```
Provider ──< Tariff (versioned by EffectiveFrom/EffectiveTo, per Encoding/MessageType) ──< TariffRate (per character-range band → PricePerSegment)
```

- **Multiple providers:** `Tariff.ProviderId`.
- **Historical tariffs + effective ranges:** `EffectiveFromUtc` / `EffectiveToUtc` (open-ended when `NULL`). New pricing = **new tariff version**, never an UPDATE of an existing one.
- **Character-count ranges + multipart:** `TariffRate.MinChars/MaxChars` + `PricePerSegment`; total parts derive from encoding (GSM-7 160/153, UCS-2 70/67).
- **Future changes:** insert a new `Tariff` row with a new effective window; close the prior version's `EffectiveToUtc`. No fact or schema change.

### 6.2 How historical pricing stays accurate after tariffs change

**Snapshotting, not recomputation.** At submission, the engine resolves the applicable tariff (provider + type + encoding + `SubmittedAtUtc` within effective range) and **copies the resulting price onto the message**. Reporting reads the message's frozen cost and **never re-resolves tariffs**. Therefore editing or adding a tariff tomorrow cannot alter a single historical cost.

### 6.3 Exact values persisted on `Message` at submission time

| Persisted column | Why it must be frozen |
|---|---|
| `Encoding` | Determines segmentation rules; recomputation later could drift. |
| `CharacterCount` | Source measure for segmentation/cost. |
| `SegmentCount` | The billed unit count. |
| `TariffId` | **Audit trail** — exactly which tariff version priced this message. |
| `UnitPrice` | Resolved `PricePerSegment` at submission. |
| `TotalCost` | `UnitPrice × SegmentCount` — the authoritative billed amount. |

This makes each `Message` row a **self-contained billing record**: even if `Tariff`/`TariffRate` were dropped entirely, every historical cost would remain reproducible and auditable.

---

## 7. Storage Optimization Analysis

At 10⁸–10⁹ rows, storage strategy *is* the architecture. Principle: **duplicate small fixed-width keys freely (cheap, kills joins); never duplicate large/variable text (expensive).**

### 7.1 What should be duplicated (denormalized) — and why it's worth it
- **Geo-section key (`GeoSectionId`, a 4-byte INT) and `ProviderId` on the fact.** These are tiny keys. Duplicating them across the fact removes multi-join hops from every report. The cost (a few bytes × rows) is trivial next to the read-time savings — and the single `GeoSectionId` is *cheaper* than the former three geo keys.
- **Cost snapshot on the fact.** Duplicating `UnitPrice/TotalCost` (already implied by §6) buys immutability and join-free cost reporting.

### 7.2 What should **not** be duplicated
- **Phone numbers / subscriber attributes** → live once in `Subscriber`; fact stores `SubscriberId`. (15-byte string × 10⁹ avoided.)
- **Descriptive names** (geo-section / provider / message-type names) → never on the fact; joined for labels only against tiny dimensions or aggregate output.
- **Template bodies** → stored once in `MessageTemplate`.

### 7.3 Should message text be normalized? → **Yes, separated and conditionally normalized**
`RenderedText` is the single largest per-row cost. Decisions:
- **Physically separate** text into `MessageBody` (1:1). Keeps the fact narrow (more rows/page, smaller/faster everything) and lets text follow its **own, shorter retention**.
- **Campaign messages:** store `MessageTemplateId` + `MergeVariablesJson` (tens of bytes) instead of the full rendered ~70–200 chars. The body is reconstructable on demand.
- **Ad-hoc / transactional messages:** store `RenderedText` directly (no shared template to reference).
- Apply **`PAGE` compression** (and Unicode compression) to `MessageBody`.

### 7.4 Should campaign templates be stored separately? → **Yes**
One template body per campaign vs. repeating it across up to millions of rows is among the largest single storage wins; it also enables template reuse and analytics. (See `MessageTemplate`, §4.8.)

### 7.5 Should recipients be separated from messages? → **Yes**
`Subscriber` is a **bounded** population reused across the unbounded message stream. Separation (a) deduplicates phone/geo, (b) enables subscriber-level history via one FK, (c) supports upsert/dedup of inbound recipient lists. The deliberate **counter-duplication** of `GeoSectionId` onto the fact (so reports avoid joining the 25M-row subscriber table) is the consciously accepted tradeoff.

### 7.6 Tradeoff summary

| Decision | Saves | Costs | Verdict |
|---|---|---|---|
| Denormalize geo-section + provider keys onto fact | Join elimination at 10⁹ scale | ~5 bytes/row | **Adopt** |
| Single `GeoSection` tree vs. three geo tables | One fact key + fewer tables | Rollups join the small geo tree | **Adopt** |
| Subscriber as dimension | Phone/geo dedupe; subscriber history | One FK join for some lookups | **Adopt** |
| Text in `MessageBody` (separate) | Narrow fact; independent retention/compression | 1 extra 1:1 table | **Adopt** |
| Template + variables for campaigns | Largest text saving | On-demand render to view body | **Adopt** |
| Pre-aggregate (`MessageDailyAggregate`) | Flat reporting cost vs. fact growth | Rollup process + small table | **Adopt** |
| Full per-event `DeliveryReportLog` | Forensic audit | **Doubles largest table** | **Default off** |

---

## 8. Concurrency and Deadlock Prevention

> *This is the most safety-critical section.* Workload: bursty **concurrent batch inserts** (hundreds of thousands per campaign, many workers) + high-volume **status updates** (DLRs) + concurrent **reporting reads** — all on the same fact family.

### 8.1 Partitioning — the foundation
- **`Message` and `MessageBody` are range-partitioned by `SubmitDateKey` (monthly).**
- Benefits that directly serve the requirements:
  - **Lock escalation is contained to a partition,** not the whole table — set `ALTER TABLE … SET (LOCK_ESCALATION = AUTO)`. A reporting scan that escalates locks on an old month cannot block inserts into the current month.
  - **Reporting reads hit old partitions; inserts hit the current partition** → physical separation of read vs. write contention.
  - **Retention by `SWITCH`/drop** of whole partitions — no giant row-by-row `DELETE` storms (which themselves cause lock escalation and blocking).

### 8.2 Clustered index choice (the hot-page problem)
- A naïve **clustered `BIGINT IDENTITY`** funnels every concurrent insert to the **same trailing page** → `PAGELATCH_EX` "last-page insert" contention, the classic high-volume insert hotspot.
- **Decision:** clustered index = **`(SubmitDateKey, MessageId)`**, partition-aligned, **with `OPTIMIZE_FOR_SEQUENTIAL_KEY = ON`** (SQL Server 2019+) to throttle the last-page latch convoy.
  - `MessageId` remains the **nonclustered** PK (uniqueness + FK target) so it doesn't dictate physical insert order alone.
- **Rejected alternatives:**
  - *Clustered GUID to scatter inserts:* rejected — random keys cause page splits/fragmentation and poor page density; the cure is worse than the disease.
  - *Hash-bucket prefix on the key:* rejected as default — adds complexity and harms range scans/partition elimination; revisit only if `OPTIMIZE_FOR_SEQUENTIAL_KEY` proves insufficient under load testing.

### 8.3 Nonclustered indexes — kept minimal *on purpose*
Every NCI is a second structure to maintain on **every insert** (and some on update), so the hot table carries only what point-lookups truly need:
| Index | Justification | Why not more |
|---|---|---|
| `CIX (SubmitDateKey, MessageId)` | Partition alignment + sequential locality | — |
| `NCIX (SubscriberId, SubmitDateKey)` | Subscriber-history seeks | Required by report #5 |
| `NCIX (ProviderId, ProviderMessageId)` | **DLR update path** — locate the row to apply a status without scanning | Updates would otherwise scan/escalate |
| `Filtered NCIX (BillNumber) WHERE NOT NULL` | Bill-history seeks; nulls excluded to keep it small | Avoids indexing the many null bills |
| *(reporting)* **Nonclustered columnstore on cold partitions** | Ad-hoc/zone-level analytics at scan speed | Kept off the hot current partition to protect insert throughput |

**No per-dimension reporting NCIs on the fact** — reporting is offloaded to `MessageDailyAggregate` and the columnstore, so the insert path stays lean.

### 8.4 Insert pattern (write strategy)
- **Set-based batch inserts** via table-valued parameters / `SqlBulkCopy`, **1,000–5,000 rows per transaction** — deliberately **under the ~5,000-lock escalation threshold** so per-statement locks don't escalate to a table/partition lock.
- **Short transactions**, committed promptly → minimal lock duration, fewer blocking chains, fewer deadlocks.
- **No `MessageDailyAggregate` update inside the insert transaction.** Aggregation is a **post-commit, asynchronous** rollup (batch upserts keyed by the aggregate's clustered key). This removes the classic deadlock cycle where insert workers and an aggregate updater grab the same resources in opposite order.
- **Consistent access order** across all write paths (Message → MessageBody) to prevent ordering-based deadlocks.

### 8.5 Update pattern (DLR application)
- Updates locate rows by `NCIX (ProviderId, ProviderMessageId)` (seek, not scan) and write only `Status` + `StatusUpdatedAtUtc` — a **narrow update on a non-indexed-by-status column**, so it doesn't move the row or churn reporting indexes.
- Status is **not** part of any fact index (it's part of the *aggregate's* key instead) → status churn never triggers index key updates / page moves on the fact.

### 8.6 Read pattern (reporting isolation)
- Aggregate reports read the small aggregate table (or `READ COMMITTED SNAPSHOT` / RCSI to avoid reader-writer blocking).
- **Recommendation: enable RCSI** so reporting `SELECT`s use row-versioning and never take shared locks that block the insert/update workers — directly attacking reader/writer deadlocks and blocking.

### 8.7 Summary of how each risk is mitigated
| Risk | Mitigation |
|---|---|
| **Last-page insert contention (hot page)** | `OPTIMIZE_FOR_SEQUENTIAL_KEY`; batched short transactions; (fallback) hash prefix |
| **Lock escalation** | Partitioning + `LOCK_ESCALATION = AUTO`; batches < 5,000 rows; partition-switch retention |
| **Page contention** | Narrow fixed-width fact (more rows/page handled cleanly); minimal NCIs; `PAGE` compression on cold data |
| **Hot partitions** | Monthly partitioning isolates the write-hot current month; reads/analytics target cold partitions/columnstore |
| **Reader/writer deadlocks** | RCSI snapshot reads; async post-commit aggregation; consistent write ordering |

---

## 9. Future Evolution

The schema is built so that the following changes are **additive (insert rows / add a nullable column / add a partition)** — never a fact rewrite.

| Future need | How it's absorbed |
|---|---|
| **New SMS provider** | Insert into `Provider` (incl. its `BaseUrl`/`FallbackBaseUrl`), `SenderLine`, `Tariff`/`TariffRate`; add only the provider's **credentials** to the secret store. Fact stores `ProviderId` + normalized `Status` → no schema or report change. Provider-specific codes map into the existing normalized status. |
| **More than two provider network paths** | *(Deferred — only if required.)* `Provider` carries `BaseUrl` + `FallbackBaseUrl` (primary + one fallback). If a provider must be reached over **three or more** network paths (e.g. internet + multiple intranets) with explicit failover ordering, escalate to a child `ProviderEndpoint(ProviderId, NetworkType, BaseUrl, Priority, IsActive)` table rather than adding more URL columns (see §4.2). |
| **Deeper geography (sub-zones, regions above province)** | Insert `GeoSection` rows at a new `SectionType` level; the self-referencing tree and `Path` absorb arbitrary depth with no schema change. |
| **Tenant-specific message types / business purposes** | Add a nullable `CustomerId` to `MessageType` (NULL = global) and/or widen `MessageTypeId` to `SMALLINT` — additive; no fact rewrite. |
| **New business metadata** | Add nullable columns to the **fact** (cheap fixed-width) or to `MessageBody` (variable). Existing rows default to NULL; no backfill required. New *categorical* metadata → a new dimension table + a narrow FK on the fact. |
| **New reporting dimension** | Add the dimension table + a narrow FK key on the fact; extend `MessageDailyAggregate` grain (or add a second purpose-built aggregate). Columnstore on the fact covers ad-hoc needs immediately. |
| **New delivery-report mechanism** (push webhooks, richer states) | The normalized `Status` enum extends with new values; `DeliveryReportLog` (if enabled) captures new raw codes. No change to reporting that reads normalized status. |
| **New Jalali reporting periods / fiscal calendars** | Add columns to `DimDate` (e.g. fiscal week); fact/aggregate unaffected. |
| **Scale beyond a single billion** | Partition granularity can move monthly→weekly; cold partitions can be archived/columnstore-compressed or moved to cheaper storage via partition switching. |

---

## 10. Design Principles & Tradeoff Summary

Per the explicit mandate — **not** normalization purity or theoretical elegance, but operational reality:

1. **High-volume processing first.** Narrow fixed-width fact, partition-aligned clustered key, sequential-key optimization, minimal indexes, batched short-transaction inserts, async aggregation.
2. **Reporting simplicity.** Denormalized dimension keys on the fact + a pre-aggregated cube ⇒ aggregate reports are flat-cost regardless of fact size; point-lookups are single index seeks.
3. **Long-term maintainability.** Additive evolution; versioned tariffs; provider-agnostic normalized status; clean fact/dimension separation; one geo tree instead of three tables.
4. **Low storage consumption.** No duplicated text/phones/names; one geo key instead of three; templates and bodies separated; campaign messages store template+variables; compression on cold data; optional high-volume log off by default.
5. **Minimal deadlocks.** Partition-scoped locking, sub-escalation batch sizes, RCSI reads, async post-commit rollups, consistent write ordering.
6. **Simple operational support.** Retention via partition switching (no delete storms); self-contained billing rows (auditable without tariff tables); small dimensions easy to cache and reason about.

**Every major tradeoff was resolved in favor of write throughput, report simplicity, and storage economy — duplicating cheap keys, never duplicating expensive text — and is documented in the section where it arises (§4 alternatives, §7 storage, §8 concurrency).**

---

### Open decisions for reviewers

1. **`DeliveryReportLog`** — confirm whether forensic/raw-DLR audit is a hard requirement (default: **off**, status snapshot on `Message` only).
2. **`MessageDailyAggregate` geo grain** — confirm the cube's default reporting level is **city** (zone rolled up at write time; zone-level detail via columnstore), vs. keying the cube at zone granularity.
3. **Body retention window** — confirm a shorter retention for `MessageBody` than for `Message` is legally acceptable.
4. **`MessageType` scope** — confirm a single global, type-merged dimension (delivery class + business purpose) is sufficient, or whether tenant-specific purposes (nullable `CustomerId`) are needed now.
5. **Partition cadence** — monthly proposed; confirm vs. weekly given peak daily volumes.
