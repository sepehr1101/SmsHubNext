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

SmsHubNext is a **high-volume, multi-tenant SMS dispatch and accounting platform**. Its primary documented use case is **utility/organizational notifications** — e.g. water-bill notices sent to subscribers across Iranian provinces, cities, and service zones — billed and reported by provider, geography, business category, and time period (in the Jalali calendar).

### 1.1 Expected usage patterns

| Pattern | Description | Storage impact |
|---|---|---|
| **Bursty bulk campaigns** | Billing cycles trigger campaigns of **hundreds of thousands to millions** of messages in a short window. | Heavy concurrent inserts → write path must be the #1 optimization target. |
| **Steady transactional/OTP traffic** | Lower-volume, latency-sensitive single messages (OTP, alerts). | Small but constant; must not be starved by bulk inserts. |
| **Asynchronous delivery reports (DLR)** | Each sent message later receives 1+ status updates from the provider, arriving minutes-to-hours later. | High-volume **update** traffic against already-written rows. |
| **Heavy reporting/analytics** | Finance and operations run aggregate reports by province/city/zone/provider/category/period. | Read path competes with writes → must be isolated from OLTP hot path. |

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

- Cost by **province / city / zone**, by **provider**, by **business category** (e.g. water bills), by **Jalali period** (e.g. *Spring 1405*).
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
| **Message Type** | Classifies traffic (OTP / Transactional / Bulk-Notification). Drives priority, possibly tariff, and reporting splits. |
| **Business Category** | The *business reason* for the message (Water Bill, Electricity Bill, …). A core reporting dimension. |
| **Campaign** | A logical batch of messages produced by one dispatch operation. Unit of operational monitoring and summary reporting. |
| **Message Template** | The reusable body pattern for a campaign, with merge placeholders. Stored once; not duplicated per recipient. |
| **Subscriber (Recipient)** | The end party receiving the SMS (a utility subscriber). A **bounded** population reused across millions of messages → modeled as a dimension, not duplicated per message. |
| **Message** | The central **fact**: one SMS send. Carries denormalized reporting keys + frozen cost snapshot + current delivery status. The highest-volume table. |
| **Message Body** | The exact rendered/sent text (or template + merge variables). Physically separated from the Message fact for storage and retention reasons. |
| **Tariff** | Time-bounded pricing for a (provider, encoding, message-type) combination. Versioned by effective date range. |
| **Delivery Report** | The provider's eventual status for a message. Normalized status is folded onto the Message; raw provider codes optionally retained for audit. |
| **Date (Jalali) Dimension** | Pre-computed mapping of calendar dates to Persian year/season/month for clean period reporting (*Spring 1405*). |
| **Geography (Province / City / Zone)** | Hierarchical reporting dimensions. Province (31), City (hundreds–thousands), Zone (tens of thousands). |

**Fact vs. Dimension split is the spine of this design:** small, stable **dimensions** (provider, line, geography, subscriber, category, date, tariff) are referenced by surrogate keys; the enormous **Message fact** carries denormalized dimension keys so reports group/filter without joining giant tables, and carries a frozen cost snapshot so history is immutable.

---

## 3. Database Schema Proposal

Tables are grouped by role. Volume estimates assume the §1.2 sizing.

### 3.0 Table inventory

| # | Table | Role | 1 mo | 1 yr | 5 yr |
|---|---|---|---|---|---|
| 1 | `Customer` | Dimension | <100 | <100 | <500 |
| 2 | `Provider` | Dimension | <10 | <20 | <50 |
| 3 | `SenderLine` | Dimension | <100 | <200 | <500 |
| 4 | `MessageType` | Dimension (seed) | ~5 | ~5 | ~10 |
| 5 | `BusinessCategory` | Dimension | <50 | <100 | <200 |
| 6 | `Province` | Dimension (seed) | 31 | 31 | 31 |
| 7 | `City` | Dimension (seed) | ~1,200 | ~1,200 | ~1,200 |
| 8 | `Zone` | Dimension | ~30k | ~50k | ~80k |
| 9 | `Subscriber` | Large dimension | ~5M | ~15M | ~25M |
| 10 | `Campaign` | Dimension | ~3k | ~40k | ~200k |
| 11 | `MessageTemplate` | Dimension | ~1k | ~10k | ~50k |
| 12 | `Tariff` | Dimension (versioned) | <100 | <300 | ~1k |
| 13 | `TariffRate` | Dimension (versioned) | <500 | ~1.5k | ~5k |
| 14 | `DimDate` | Dimension (seed) | ~1.8k | ~1.8k | ~3.7k |
| 15 | **`Message`** | **Fact (hot)** | **~10M** | **~120M** | **~0.6–1B** |
| 16 | `MessageBody` | Fact satellite (text) | ~10M | ~120M | ~0.6–1B |
| 17 | `DeliveryReportLog` | Optional audit (raw DLR) | ~12M | ~150M | ~1B+ |
| 18 | `MessageDailyAggregate` | Pre-aggregated rollup | ~150k | ~1.8M | ~9M |

> Below, each table is summarized against the required facets. Full column-level detail is in **§4**.

### Dimension tables (1–14) — shared facets

- **Purpose:** Provide stable, deduplicated reference data referenced by the Message fact via surrogate keys.
- **Business justification:** Eliminate repetition of descriptive text (province names, line numbers, templates) across ~10⁹ fact rows; enable consistent grouping/filtering in reports.
- **Read pattern:** Tiny lookups; frequently cached in the application; joined to aggregates (not to the raw fact) for labels.
- **Write pattern:** Rare inserts/updates (admin/onboarding). `Subscriber` is the exception — moderate insert/upsert volume as new subscribers appear, but **bounded** by the real population.
- **Retention:** Effectively permanent. Versioned dimensions (`Tariff`, `TariffRate`) keep history via effective-date ranges; rows are never hard-deleted.
- **Storage:** Negligible relative to the fact. Their entire value is *avoiding* duplication in the fact.

### 15. `Message` — the central fact (hot path)

- **Purpose:** One row per SMS send. The system of record for what was sent, to whom, by which line/provider, at what cost, with what delivery outcome.
- **Business justification:** Every report, every cost calculation, and every history lookup ultimately resolves here.
- **Expected volume:** 10M (1mo) → 120M (1yr) → up to ~1B (5yr).
- **Read pattern:** (a) **range aggregations** for reporting — served primarily by `MessageDailyAggregate` and partition-eliminated columnstore scans, *not* by hammering the rowstore; (b) **point lookups** by subscriber, by bill, by provider message id (for DLR matching).
- **Write pattern:** Massive **concurrent batch inserts** during campaigns; high-volume **single-column status updates** when DLRs arrive. Designed to minimize both insert hot-spotting and update-time index churn (see §8).
- **Retention:** Long (billing/legal). Aged out by **partition switching** by month, not by row-level `DELETE`.
- **Storage:** Kept **deliberately narrow** (fixed-width keys + cost snapshot + status; **no free text**) so more rows fit per page → faster scans, smaller indexes, cheaper inserts. Text lives in `MessageBody`.

### 16. `MessageBody` — text satellite (1:1 with Message)

- **Purpose:** Hold the exact sent text (or `TemplateId` + merge variables) separately from the narrow fact.
- **Business justification:** Audit/legal proof of content without bloating the fact that powers reporting.
- **Volume:** 1:1 with `Message`.
- **Read pattern:** Rare — only when an operator inspects an individual message. Never scanned for aggregates.
- **Write pattern:** Inserted alongside the message (same batch). Immutable afterward.
- **Retention:** **Shorter** than the fact where policy allows — the body partition can be purged earlier to reclaim the bulk of storage while keeping cost/delivery facts.
- **Storage:** The largest per-row cost in the system → isolated so it can be compressed and retired independently (see §7).

### 17. `DeliveryReportLog` — optional raw DLR audit

- **Purpose:** Append-only log of raw provider status callbacks/poll results.
- **Business justification:** Forensic/audit trail and reprocessing; the *normalized* current status already lives on `Message`.
- **Volume:** ≥ message volume (a message may receive multiple updates).
- **Read pattern:** Rare, point lookups by message; mostly write-only.
- **Write pattern:** Append-only inserts.
- **Retention:** **Short** (e.g. 30–90 days) — high volume, low long-term value.
- **Storage:** Justified only if audit/reprocessing is required; **off by default** (see §4.17 tradeoff). The normalized status on `Message` is the source of truth for all reporting.

### 18. `MessageDailyAggregate` — pre-rolled reporting cube

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
| `IsActive` | `BIT` | |

**Why:** Provider count is tiny → `TINYINT`. New providers = new rows, never schema change. *Alternative considered:* provider as a string enum on the fact — rejected (4–6 bytes × 10⁹ and no referential integrity).

### 4.3 `SenderLine`
| Column | Type | Notes |
|---|---|---|
| `SenderLineId` | `SMALLINT IDENTITY` | **PK**, `CIX` |
| `ProviderId` | `TINYINT` | **FK** → `Provider` |
| `LineNumber` | `VARCHAR(20)` | `3000…`, `1000…`, `4040…`; `NCIX` |
| `LineType` | `TINYINT` | shared/dedicated |
| `IsActive` | `BIT` | |

**Why:** Lines have distinct pricing/reachability and belong to a provider. Surrogate `SMALLINT` keeps the fact FK small. *Alternative:* storing the raw line string on each message — rejected (repetition + no metadata).

### 4.4 `MessageType`
| Column | Type | Notes |
|---|---|---|
| `MessageTypeId` | `TINYINT` | **PK** (seeded), `CIX` |
| `Name` | `NVARCHAR(50)` | OTP / Transactional / Bulk |

**Why:** Drives priority, tariff selection, and report splits. Seeded enum-like dimension. *Alternative:* a `BIT IsOtp` flag — rejected (not extensible to >2 types).

### 4.5 `BusinessCategory`
| Column | Type | Notes |
|---|---|---|
| `BusinessCategoryId` | `SMALLINT IDENTITY` | **PK**, `CIX` |
| `CustomerId` | `SMALLINT` | **FK** (category may be tenant-scoped) |
| `Name` | `NVARCHAR(100)` | "Water Bill", … |
| `Code` | `VARCHAR(50)` | `NCIX` |

**Why:** "Water bills" is a primary reporting axis; modeling it as a dimension (not a free-text tag on the fact) makes the *"cost for water bills"* report a single indexed key filter. *Alternative:* free-text category on the message — rejected (inconsistent values, no clean grouping, text bloat).

### 4.6 `Province` / 4.7 `City` / 4.8 `Zone`
| `Province` | Type | | `City` | Type | | `Zone` | Type |
|---|---|---|---|---|---|---|---|
| `ProvinceId` **PK** | `TINYINT` | | `CityId` **PK** | `SMALLINT` | | `ZoneId` **PK** | `INT` |
| `Name` | `NVARCHAR(60)` | | `ProvinceId` **FK** | `TINYINT` | | `CityId` **FK** | `SMALLINT` |
| `Code` | `VARCHAR(10)` | | `Name` | `NVARCHAR(80)` | | `Name` | `NVARCHAR(100)` |
| | | | `Code` | `VARCHAR(15)` | | `Code` | `VARCHAR(20)` |

**Why a 3-level hierarchy of separate tables:** Province (31), City (~1.2k), Zone (~50k) each have independent reporting demand. Right-sized keys (`TINYINT`/`SMALLINT`/`INT`) minimize the fact footprint. The **fact denormalizes all three FK keys** (not just zone) so province/city reports never need to walk the hierarchy at 10⁹ scale. *Alternative considered:* single flattened "Location" table — rejected (loses clean hierarchy + forces wider/ambiguous keys). *Alternative:* store only `ZoneId` on the fact and derive province/city via joins — rejected (a 3-table join against a billion-row fact for the most common reports).

### 4.9 `Subscriber`
| Column | Type | Notes |
|---|---|---|
| `SubscriberId` | `BIGINT IDENTITY` | **PK**, `CIX` |
| `CustomerId` | `SMALLINT` | **FK** |
| `MobileNumber` | `VARCHAR(15)` | canonical `98…`; **`NCIX` unique** per customer |
| `ProvinceId` | `TINYINT` | **FK** (subscriber's home geo) |
| `CityId` | `SMALLINT` | **FK** |
| `ZoneId` | `INT` | **FK** |
| `CreatedAtUtc` | `DATETIME2(3)` | |

**Why separated:** The same subscriber receives many messages; storing the phone number + geography **once** per subscriber rather than once per message saves enormous space and enables *"history of a subscriber"* via a single FK. **Note the deliberate redundancy:** geography also lives on the Message fact (denormalized) — this is intentional (see §7) so reports don't join the 25M-row subscriber table. *Alternative:* no subscriber table, phone number repeated on every message — rejected (15 bytes × 10⁹ + no subscriber-level history + no upsert dedupe).

### 4.10 `Campaign`
| Column | Type | Notes |
|---|---|---|
| `CampaignId` | `BIGINT IDENTITY` | **PK**, `CIX` |
| `CustomerId` | `SMALLINT` | **FK** |
| `BusinessCategoryId` | `SMALLINT` | **FK** |
| `MessageTemplateId` | `BIGINT` | **FK** (nullable) |
| `Name` | `NVARCHAR(200)` | |
| `Status` | `TINYINT` | Draft/Running/Completed/Failed |
| `CreatedAtUtc` | `DATETIME2(3)` | |
| `TotalCount` / `SentCount` / `FailedCount` | `INT` | maintained counters |

**Why:** Operational grouping + the unit of the *campaign summary* report. Counters are maintained by the rollup process, not recomputed by scanning the fact. *Alternative:* derive campaigns implicitly from message timestamps — rejected (no durable identity, no template link, no clean summary).

### 4.11 `MessageTemplate`
| Column | Type | Notes |
|---|---|---|
| `MessageTemplateId` | `BIGINT IDENTITY` | **PK**, `CIX` |
| `CustomerId` | `SMALLINT` | **FK** |
| `Body` | `NVARCHAR(1000)` | template with `{placeholders}` |
| `Encoding` | `TINYINT` | GSM7 / UCS2 (intrinsic to template language) |
| `CreatedAtUtc` | `DATETIME2(3)` | |

**Why separated:** A campaign of 1M messages shares **one** template body. Storing the body once and referencing it (with per-message merge variables in `MessageBody`) instead of repeating ~200 chars × 1M is a decisive storage win. *Alternative:* render and store full text on every message — rejected for campaigns (see §7); still allowed for ad-hoc/transactional messages.

### 4.12 `Tariff` (versioned header)
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

### 4.13 `TariffRate` (per-segment detail)
| Column | Type | Notes |
|---|---|---|
| `TariffRateId` | `INT IDENTITY` | **PK**, `CIX` |
| `TariffId` | `INT` | **FK** → `Tariff` |
| `MinChars` | `SMALLINT` | character-range lower bound |
| `MaxChars` | `SMALLINT` | character-range upper bound (nullable = ∞) |
| `PricePerSegment` | `DECIMAL(19,4)` | |

**Why two tables:** the **header** carries the validity window and applicability; the **detail** carries the price banding by character range / segment. This separates "*which tariff applies*" (effective-date resolution) from "*how much*" (rate bands), and lets a tariff version own multiple bands without nullable sprawl. *Alternatives considered:* (a) a single wide tariff row with fixed columns per band — rejected (inflexible, not future-proof for new bands); (b) computing price in application config — rejected (prices must be **data**, auditable and snapshotted). **Crucially, tariff tables are never used at report time** — the resolved price is frozen onto the message (see §6).

### 4.14 `DimDate`
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

### 4.15 `Message` — the fact (most-scrutinized table)
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
| `MessageTypeId` | `TINYINT` | **FK** |
| `BusinessCategoryId` | `SMALLINT` | **FK** |
| `ProvinceId` | `TINYINT` | **FK** (denormalized from subscriber) |
| `CityId` | `SMALLINT` | **FK** (denormalized) |
| `ZoneId` | `INT` | **FK** (denormalized) |
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
- **Denormalized dimension keys** (`ProvinceId/CityId/ZoneId/ProviderId/BusinessCategoryId/MessageTypeId`): the §5 reports filter/group on these **without joining** the huge fact to dimensions. Joins (for labels) happen only against the tiny aggregate output.
- **Frozen cost snapshot** (`Encoding/CharacterCount/SegmentCount/TariffId/UnitPrice/TotalCost`): historical accuracy is independent of later tariff edits.
- **Normalized `Status` + `ProviderMessageId`:** provider-agnostic reporting + an efficient DLR update path.

**Alternatives considered & rejected:**
- *Fully normalized fact (only `ZoneId` + `SubscriberId`, derive everything by join):* rejected — multi-join aggregation over 10⁹ rows is the exact anti-pattern the requirements forbid.
- *Cost computed at report time from tariff tables:* rejected — breaks historical accuracy and makes every cost report join versioned tariffs.
- *Storing message text inline:* rejected — bloats the fact, slashes rows-per-page, and couples retention of cheap facts to expensive text (see §16/§7).
- *`UNIQUEIDENTIFIER` key:* rejected — random GUID clustered key causes fragmentation and worse page density; even as a nonclustered PK it is 16 bytes × 10⁹.

### 4.16 `MessageBody`
| Column | Type | Notes |
|---|---|---|
| `MessageId` | `BIGINT` | **PK = FK** → `Message` (1:1), `CIX`, partition-aligned on `SubmitDateKey` |
| `SubmitDateKey` | `INT` | partition column (aligned with `Message`) |
| `MessageTemplateId` | `BIGINT` | **FK** (nullable) — for campaign messages |
| `RenderedText` | `NVARCHAR(MAX)` | full text for ad-hoc/transactional; NULL for template-driven |
| `MergeVariablesJson` | `NVARCHAR(MAX)` | per-recipient substitutions when template-driven |

**Why separated from `Message`:** detailed in §7. Short version: keeps the fact narrow and lets text be compressed and **purged on its own (shorter) retention schedule**. *Alternative:* one wide table — rejected (couples hot fact to cold text). *Alternative:* always store `RenderedText` — rejected for campaigns (template + variables is far smaller).

### 4.17 `DeliveryReportLog` (optional, off by default)
| Column | Type | Notes |
|---|---|---|
| `DeliveryReportLogId` | `BIGINT IDENTITY` | **PK**, `CIX` |
| `MessageId` | `BIGINT` | **FK**; `NCIX` |
| `RawStatusCode` | `INT` | provider-native code |
| `NormalizedStatus` | `TINYINT` | |
| `ReceivedAtUtc` | `DATETIME2(3)` | |

**Why optional:** the **current** normalized status already lives on `Message`, which satisfies all reporting. A full per-event log **at least doubles the row count of the largest table**. **Tradeoff/decision:** enable only if forensic audit or DLR reprocessing is a hard requirement; if enabled, give it **aggressive short retention** (30–90 days) and its own partitioning. *Default recommendation: keep disabled; rely on the snapshot status on `Message`.*

### 4.18 `MessageDailyAggregate`
| Column | Type | Notes |
|---|---|---|
| `DateKey` | `INT` | part of **PK**, **FK** → `DimDate` |
| `CustomerId` | `SMALLINT` | part of PK |
| `ProvinceId` | `TINYINT` | part of PK |
| `CityId` | `SMALLINT` | part of PK |
| `ProviderId` | `TINYINT` | part of PK |
| `BusinessCategoryId` | `SMALLINT` | part of PK |
| `MessageTypeId` | `TINYINT` | part of PK |
| `NormalizedStatus` | `TINYINT` | part of PK (for success-rate reports) |
| `MessageCount` | `BIGINT` | measure |
| `SegmentCount` | `BIGINT` | measure |
| `TotalCost` | `DECIMAL(19,4)` | measure |

**Indexing:** clustered on the composite key above (chosen so the most common range filter — `DateKey` — leads, enabling partition-eliminable, range-friendly scans). **Zone is intentionally excluded** from the default grain to bound cardinality; zone-level analytics use the columnstore on the fact instead. *Alternative:* include every dimension (incl. zone) — rejected (combinatorial explosion erodes the aggregate's value). *Alternative:* materialized/indexed view — considered, but a **physically maintained table** updated post-commit avoids indexed-view locking/maintenance overhead on the hot insert path.

---

## 5. Reporting Validation

For each report: **required tables**, **join strategy**, **performance considerations**. The recurring theme: **aggregate reports read `MessageDailyAggregate` (tiny) and join only to small dimensions for labels; they never scan the billion-row fact.** Point-lookup reports hit a targeted nonclustered index on the fact.

| # | Report | Required tables | Join strategy | Performance |
|---|---|---|---|---|
| 1 | **Cost for Tehran province, Spring 1405, water bills** | `MessageDailyAggregate` + `DimDate` + `Province` + `BusinessCategory` | Filter `DimDate` (`PersianYear=1405, PersianSeason=1`) → join `DateKey`; filter `ProvinceId=<Tehran>`, `BusinessCategoryId=<Water>`; `SUM(TotalCost)` | Aggregate-only; hundreds–thousands of rows; sub-second. No fact scan. |
| 2 | **Cost by provider** | `MessageDailyAggregate` + `Provider` | `GROUP BY ProviderId`, join `Provider` for names | Trivial; aggregate scan + tiny join. |
| 3 | **Count by city** | `MessageDailyAggregate` + `City` | `GROUP BY CityId` | Aggregate-only. |
| 4 | **Count by zone** | `Message` (columnstore) **or** a dedicated zone aggregate | `GROUP BY ZoneId` over partition-eliminated columnstore | Zone excluded from default aggregate → use columnstore with date-partition elimination; seconds, not minutes. |
| 5 | **History of a subscriber** | `Message` + `MessageBody` + `Subscriber` | Point lookup via `NCIX (SubscriberId, SubmitDateKey)`; optional body join | Index seek; milliseconds. Body fetched only if displayed. |
| 6 | **History of a bill** | `Message` (+`MessageBody`) | Seek `filtered NCIX (BillNumber)` | Index seek; few rows; fast. |
| 7 | **Delivery success rate by provider** | `MessageDailyAggregate` | `SUM(CASE Status=Delivered)/SUM(MessageCount) GROUP BY ProviderId` | `NormalizedStatus` is part of the aggregate grain → direct. |
| 8 | **Campaign summary** | `Campaign` (+ `Message` if drill-down) | Read maintained counters on `Campaign`; drill-down via `NCIX` on `CampaignId` if needed | Counters = O(1). Drill-down = partitioned index seek. |
| 9 | **Monthly cost trend** | `MessageDailyAggregate` + `DimDate` | `GROUP BY PersianYearMonth` | Aggregate-only; ideal for time series. |
| 10 | **Top provinces by spend** | `MessageDailyAggregate` + `Province` | `GROUP BY ProvinceId ORDER BY SUM(TotalCost) DESC` | Aggregate-only; trivial. |

**Key validation outcome:** every *aggregate* report is answerable from a table that grows with **dimension combinations × days**, not with **messages** — so reporting performance stays flat as the fact grows from 10M to 1B. Every *point-lookup* report is answerable by a single, deliberately chosen nonclustered index seek on the fact. A reporting query for a campaign that fires DLR updates does **not** contend with the fact's hot insert region because reads target the aggregate / cold partitions (see §8).

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
- **Geographic keys (`ProvinceId/CityId/ZoneId`) and `ProviderId` on the fact.** These are 1–4 byte keys. Duplicating them across the fact removes multi-join hops from every report. The cost (a few bytes × rows) is trivial next to the read-time savings.
- **Cost snapshot on the fact.** Duplicating `UnitPrice/TotalCost` (already implied by §6) buys immutability and join-free cost reporting.

### 7.2 What should **not** be duplicated
- **Phone numbers / subscriber attributes** → live once in `Subscriber`; fact stores `SubscriberId`. (15-byte string × 10⁹ avoided.)
- **Descriptive names** (province/city/provider/category names) → never on the fact; joined for labels only against tiny dimensions or aggregate output.
- **Template bodies** → stored once in `MessageTemplate`.

### 7.3 Should message text be normalized? → **Yes, separated and conditionally normalized**
`RenderedText` is the single largest per-row cost. Decisions:
- **Physically separate** text into `MessageBody` (1:1). Keeps the fact narrow (more rows/page, smaller/faster everything) and lets text follow its **own, shorter retention**.
- **Campaign messages:** store `MessageTemplateId` + `MergeVariablesJson` (tens of bytes) instead of the full rendered ~70–200 chars. The body is reconstructable on demand.
- **Ad-hoc / transactional messages:** store `RenderedText` directly (no shared template to reference).
- Apply **`PAGE` compression** (and Unicode compression) to `MessageBody`.

### 7.4 Should campaign templates be stored separately? → **Yes**
One template body per campaign vs. repeating it across up to millions of rows is among the largest single storage wins; it also enables template reuse and analytics. (See `MessageTemplate`, §4.11.)

### 7.5 Should recipients be separated from messages? → **Yes**
`Subscriber` is a **bounded** population reused across the unbounded message stream. Separation (a) deduplicates phone/geography, (b) enables subscriber-level history via one FK, (c) supports upsert/dedup of inbound recipient lists. The deliberate **counter-duplication** of geography keys onto the fact (so reports avoid joining the 25M-row subscriber table) is the consciously accepted tradeoff.

### 7.6 Tradeoff summary

| Decision | Saves | Costs | Verdict |
|---|---|---|---|
| Denormalize geo/provider keys onto fact | Join elimination at 10⁹ scale | ~8–10 bytes/row | **Adopt** |
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
| **New SMS provider** | Insert into `Provider`, `SenderLine`, `Tariff`/`TariffRate`. Fact stores `ProviderId` + normalized `Status` → no schema or report change. Provider-specific codes map into the existing normalized status. |
| **New business metadata** | Add nullable columns to the **fact** (cheap fixed-width) or to `MessageBody` (variable). Existing rows default to NULL; no backfill required. New *categorical* metadata → a new dimension table + a `SMALLINT` FK on the fact. |
| **New reporting dimension** | Add the dimension table + a narrow FK key on the fact; extend `MessageDailyAggregate` grain (or add a second purpose-built aggregate). Columnstore on the fact covers ad-hoc needs immediately. |
| **New delivery-report mechanism** (push webhooks, richer states) | The normalized `Status` enum extends with new values; `DeliveryReportLog` (if enabled) captures new raw codes. No change to reporting that reads normalized status. |
| **New Jalali reporting periods / fiscal calendars** | Add columns to `DimDate` (e.g. fiscal week); fact/aggregate unaffected. |
| **Scale beyond a single billion** | Partition granularity can move monthly→weekly; cold partitions can be archived/columnstore-compressed or moved to cheaper storage via partition switching. |

---

## 10. Design Principles & Tradeoff Summary

Per the explicit mandate — **not** normalization purity or theoretical elegance, but operational reality:

1. **High-volume processing first.** Narrow fixed-width fact, partition-aligned clustered key, sequential-key optimization, minimal indexes, batched short-transaction inserts, async aggregation.
2. **Reporting simplicity.** Denormalized dimension keys on the fact + a pre-aggregated cube ⇒ aggregate reports are flat-cost regardless of fact size; point-lookups are single index seeks.
3. **Long-term maintainability.** Additive evolution; versioned tariffs; provider-agnostic normalized status; clean fact/dimension separation.
4. **Low storage consumption.** No duplicated text/phones/names; templates and bodies separated; campaign messages store template+variables; compression on cold data; optional high-volume log off by default.
5. **Minimal deadlocks.** Partition-scoped locking, sub-escalation batch sizes, RCSI reads, async post-commit rollups, consistent write ordering.
6. **Simple operational support.** Retention via partition switching (no delete storms); self-contained billing rows (auditable without tariff tables); small dimensions easy to cache and reason about.

**Every major tradeoff was resolved in favor of write throughput, report simplicity, and storage economy — duplicating cheap keys, never duplicating expensive text — and is documented in the section where it arises (§4 alternatives, §7 storage, §8 concurrency).**

---

### Open decisions for reviewers

1. **`DeliveryReportLog`** — confirm whether forensic/raw-DLR audit is a hard requirement (default: **off**, status snapshot on `Message` only).
2. **`MessageDailyAggregate` grain** — confirm exclusion of `Zone` from the default cube (zone served via columnstore). Add a second zone-grain aggregate if zone reports are frequent.
3. **Body retention window** — confirm a shorter retention for `MessageBody` than for `Message` is legally acceptable.
4. **Multi-tenancy depth** — confirm `Customer` is the isolation grain and whether per-customer tariffs are ever needed (currently tariffs are per provider).
5. **Partition cadence** — monthly proposed; confirm vs. weekly given peak daily volumes.
