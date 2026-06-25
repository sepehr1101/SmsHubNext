# SmsHubNext — Database & Storage Architecture Design

> **Status:** Design proposal — *for architectural review*
> **Scope of this document:** Data model and storage architecture **only**. No application, transport (RabbitMQ), provider integration, or API code is described here. The goal is to **validate the data model before any implementation begins.**
> **Target engine:** **Microsoft SQL Server** (2019+). The requirements explicitly reference *lock escalation, page contention, clustered/nonclustered indexes,* and *hot partitions* — all SQL Server concepts — and the platform is built on .NET. The logical model remains portable; the physical tuning notes are SQL-Server-specific and are flagged as such.
> **Currency:** Iranian Rial (IRR). **Calendar:** Reporting periods are expressed in the **Jalali (Persian) calendar** (e.g. *Spring 1405*); storage is UTC with a Jalali date dimension.
> **Sending model:** Every message carries its **own distinct, fully-rendered text** (the caller supplies the final body per message). There is **no shared template, no merge-variable rendering, and no campaign/batch grouping**; recipients are **ad-hoc** (not a managed subscriber base).

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

SmsHubNext is a **high-volume, multi-tenant SMS dispatch and accounting platform**. Its primary documented use case is **utility/organizational notifications** — e.g. water-bill notices sent to recipients across Iranian geographic sections (province → city → zone) — billed and reported by provider, geography, message type, and time period (in the Jalali calendar). **Each message is individually composed by the caller** (e.g. *"Dear Mr. James, …"*, *"Dear Mrs. Johnson, …"*); the platform stores and dispatches the supplied text, it does not render from templates.

### 1.1 Expected usage patterns

| Pattern | Description | Storage impact |
|---|---|---|
| **Bursty bulk sends (distinct messages)** | Billing cycles push **hundreds of thousands to millions** of individually-composed messages in a short window. | Heavy concurrent inserts → write path must be the #1 optimization target. |
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
- **History of a single recipient** (by mobile number) and **history of a single bill**.
- **Monthly cost trends.**
- **Top provinces by spend.**

Reports are predominantly **aggregations over large ranges** plus a few **point-lookups** (recipient/bill history). These two access shapes are in tension and are handled differently (see §5, §8).

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
| **Customer (Tenant / Customer Context)** | The organization that owns the traffic and is billed (e.g. a regional water company). Top-level ownership and isolation boundary for billing and reporting. **Sends** messages. |
| **Provider** | An SMS carrier/aggregator (Magfa, …). Owns sender lines and tariffs; source of delivery reports. |
| **Sender Line** | A specific origin number (`3000…`, `1000…`, `4040…`) belonging to a provider. Affects routing, pricing, and operator reachability. |
| **Message Type** | Single classification axis for a message — both its *delivery class* (OTP / Transactional / Bulk-Notification) and its *business purpose* (Water Bill, Outage Alert, …). Drives priority, possibly tariff, and reporting splits. |
| **Message** | The central **fact**: one SMS send, with its **own distinct text reference**, recipient number, cost snapshot, and delivery status. The highest-volume table. |
| **Message Body** | The exact sent text for that one message. Physically separated from the Message fact (1:1) for storage and retention reasons. |
| **Recipient** | The end party receiving the SMS — represented simply as a **`MobileNumber` on the message** (ad-hoc; **not** a managed dimension). |
| **Tariff** | Time-bounded pricing for a (provider, encoding, message-type) combination. Versioned by effective date range. |
| **Delivery Report** | The provider's eventual status for a message. Normalized status is folded onto the Message; raw provider codes optionally retained for audit. |
| **Date (Jalali) Dimension** | Pre-computed mapping of calendar dates to Persian year/season/month for clean period reporting (*Spring 1405*). |
| **Geo Section** | A single self-referencing geographic dimension modeling the whole location hierarchy (Province → City → Zone → …) in one table via a parent link + `SectionType`. |

**Fact vs. Dimension split is the spine of this design:** small, stable **dimensions** (customer, provider, line, message-type, geo section, date, tariff) are referenced by surrogate keys; the enormous **Message fact** carries small dimension keys + the recipient number so reports group/filter without joining giant tables, and carries a frozen cost snapshot so history is immutable.

> **What was removed and why:** an earlier draft assumed *template-based campaigns* — a shared `MessageTemplate` + per-recipient merge variables + a `Campaign` grouping + a managed `Subscriber` base. Since **every message is distinct** and **recipients are ad-hoc**, those four abstractions added complexity without value and were dropped. The model can grow them back additively if requirements change (see §9).

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
| 6 | `Tariff` | Dimension (versioned) | <100 | <300 | ~1k |
| 7 | `TariffRate` | Dimension (versioned) | <500 | ~1.5k | ~5k |
| 8 | `DimDate` | Dimension (seed) | ~1.8k | ~1.8k | ~3.7k |
| 9 | **`Message`** | **Fact (hot)** | **~10M** | **~120M** | **~0.6–1B** |
| 10 | `MessageBody` | Fact satellite (text) | ~10M | ~120M | ~0.6–1B |
| 11 | `DeliveryReportLog` | Optional audit (raw DLR) | ~12M | ~150M | ~1B+ |
| 12 | `MessageDailyAggregate` | Pre-aggregated rollup | ~150k | ~1.8M | ~9M |

> Below, each table is summarized against the required facets. Full column-level detail is in **§4**.

### Dimension tables (1–8) — shared facets

- **Purpose:** Provide stable, deduplicated reference data referenced by the Message fact via surrogate keys.
- **Business justification:** Eliminate repetition of descriptive text (geo-section names, line numbers, type names) across ~10⁹ fact rows; enable consistent grouping/filtering in reports.
- **Read pattern:** Tiny lookups; frequently cached in the application; joined to aggregates (not to the raw fact) for labels.
- **Write pattern:** Rare inserts/updates (admin/onboarding).
- **Retention:** Effectively permanent. Versioned dimensions (`Tariff`, `TariffRate`) keep history via effective-date ranges; rows are never hard-deleted.
- **Storage:** Negligible relative to the fact. Their entire value is *avoiding* duplication in the fact.

### 9. `Message` — the central fact (hot path)

- **Purpose:** One row per SMS send. The system of record for what was sent, to which number, by which line/provider, at what cost, with what delivery outcome.
- **Business justification:** Every report, every cost calculation, and every history lookup ultimately resolves here.
- **Expected volume:** 10M (1mo) → 120M (1yr) → up to ~1B (5yr).
- **Read pattern:** (a) **range aggregations** for reporting — served primarily by `MessageDailyAggregate` and partition-eliminated columnstore scans; (b) **point lookups** by recipient number, by bill, by provider message id (for DLR matching).
- **Write pattern:** Massive **concurrent batch inserts** of distinct messages; high-volume **single-column status updates** when DLRs arrive. Designed to minimize both insert hot-spotting and update-time index churn (see §8).
- **Retention:** Long (billing/legal). Aged out by **partition switching** by month, not by row-level `DELETE`.
- **Storage:** Kept **deliberately narrow** (fixed-width keys + recipient number + cost snapshot + status; **no message text**) so more rows fit per page → faster scans, smaller indexes, cheaper inserts. Text lives in `MessageBody`.

### 10. `MessageBody` — text satellite (1:1 with Message)

- **Purpose:** Hold the exact distinct text of that one message, separate from the narrow fact.
- **Business justification:** Audit/legal proof of content without bloating the fact that powers reporting.
- **Volume:** 1:1 with `Message`.
- **Read pattern:** Rare — only when an operator inspects an individual message. Never scanned for aggregates.
- **Write pattern:** Inserted alongside the message (same batch). Immutable afterward.
- **Retention:** **Shorter** than the fact where policy allows — the body partition can be purged earlier to reclaim the bulk of storage while keeping cost/delivery facts.
- **Storage:** The largest per-row cost in the system, and — because each text is distinct — **non-deduplicable**. Isolated so it can be compressed and retired independently (see §7).

### 11. `DeliveryReportLog` — optional raw DLR audit

- **Purpose:** Append-only log of raw provider status callbacks/poll results.
- **Business justification:** Forensic/audit trail and reprocessing; the *normalized* current status already lives on `Message`.
- **Volume:** ≥ message volume (a message may receive multiple updates).
- **Read pattern:** Rare, point lookups by message; mostly write-only.
- **Write pattern:** Append-only inserts.
- **Retention:** **Short** (e.g. 30–90 days) — high volume, low long-term value.
- **Storage:** Justified only if audit/reprocessing is required; **off by default** (see §4.11 tradeoff). The normalized status on `Message` is the source of truth for all reporting.

### 12. `MessageDailyAggregate` — pre-rolled reporting cube

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

**Why:** This is the **single classification axis** for a message — it carries both the *delivery class* (OTP/Transactional/Bulk) and the *business purpose* (Water Bill, Outage Alert, …). Kept **global and `TINYINT`** for simplicity: the realistic value set is a few dozen. *Alternative considered:* a separate tenant-scoped `BusinessCategory` dimension + a second FK on the fact — rejected (an extra table, an extra fact key, and an extra report join for a distinction the platform does not currently need). *Alternative:* a `BIT IsOtp` flag — rejected (not extensible). **Future-proofing (additive):** if tenant-specific purposes proliferate, add a nullable `CustomerId` (NULL = global type) and/or widen to `SMALLINT` — both non-breaking.

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

**Why one self-referencing table instead of three (`Province`/`City`/`Zone`):** the location structure is a strict hierarchy, and modeling it as a single **adjacency-list** table (`ParentGeoSectionId`) collapses three tables into one while *preserving* the hierarchy — so province/city/zone rollups remain possible. Adding a deeper level later (e.g. a sub-zone) is a data insert, not a schema change. The denormalized **`Path`** makes "everything under Tehran province" a single sargable `Path LIKE '/<TehranId>/%'` filter, avoiding recursive walks at report time.

**How reports avoid hierarchy walks on the billion-row fact:** the fact stores a **single** `GeoSectionId` (the most-specific section the caller tagged). Province/city rollups are done against the small `MessageDailyAggregate` joined to the small `GeoSection` tree via `Path` — never against the fact. *Alternatives considered:* (a) three separate geo tables with three fact keys — superseded by this consolidation; (b) a **flat, non-hierarchical** tag — rejected (reports #1/#3/#4/#9 need province/city/zone rollups); (c) SQL Server `HIERARCHYID` instead of a `Path` string — valid; `Path` chosen for transparency/portability, easy to swap later.

### 4.6 `Tariff` (versioned header)
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

### 4.7 `TariffRate` (per-segment detail)
| Column | Type | Notes |
|---|---|---|
| `TariffRateId` | `INT IDENTITY` | **PK**, `CIX` |
| `TariffId` | `INT` | **FK** → `Tariff` |
| `MinChars` | `SMALLINT` | character-range lower bound |
| `MaxChars` | `SMALLINT` | character-range upper bound (nullable = ∞) |
| `PricePerSegment` | `DECIMAL(19,4)` | |

**Why two tables:** the **header** carries the validity window and applicability; the **detail** carries the price banding by character range / segment. This separates "*which tariff applies*" (effective-date resolution) from "*how much*" (rate bands), and lets a tariff version own multiple bands without nullable sprawl. *Alternatives considered:* (a) a single wide tariff row with fixed columns per band — rejected (inflexible); (b) computing price in application config — rejected (prices must be **data**, auditable and snapshotted). **Crucially, tariff tables are never used at report time** — the resolved price is frozen onto the message (see §6).

### 4.8 `DimDate`
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

### 4.9 `Message` — the fact (most-scrutinized table)
| Column | Type | Notes |
|---|---|---|
| `MessageId` | `BIGINT IDENTITY` | **PK** (nonclustered — see §8) |
| `SubmitDateKey` | `INT` | **partition column**; part of `CIX`; **FK** → `DimDate` |
| `SubmittedAtUtc` | `DATETIME2(3)` | precise timestamp |
| `CustomerId` | `SMALLINT` | **FK** (tenant/sender) |
| `ProviderId` | `TINYINT` | **FK** |
| `SenderLineId` | `SMALLINT` | **FK** |
| `MessageTypeId` | `TINYINT` | **FK** (delivery class + business purpose) |
| `GeoSectionId` | `INT` | **FK** → `GeoSection`; nullable (caller-supplied geo tag) |
| `MobileNumber` | `VARCHAR(15)` | recipient, canonical `98…` (ad-hoc; replaces the former `Subscriber` FK) |
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
- **NCIX 1:** `(MobileNumber, SubmitDateKey)` — recipient history.
- **NCIX 2:** `(ProviderId, ProviderMessageId)` — DLR matching/update path.
- **NCIX 3 (filtered):** `(BillNumber) WHERE BillNumber IS NOT NULL` — bill history without indexing nulls.
- **Reporting is *not* served by NCIs here** — it is served by `MessageDailyAggregate` and an optional **nonclustered columnstore** on cold partitions (§8).

**Why this structure is preferred:**
- **Narrow + (mostly) fixed-width:** no message text on the fact; every reporting/cost attribute is a small key or number → maximal rows per page, smaller indexes, cheaper inserts, faster scans. (`MobileNumber` is the one short variable column — acceptable; see §7.)
- **Small dimension keys** (`GeoSectionId/ProviderId/MessageTypeId/CustomerId`): the §5 reports filter/group on these **without joining** the huge fact to dimensions. Joins (for labels / hierarchy rollup) happen only against the tiny aggregate output.
- **Frozen cost snapshot** (`Encoding/CharacterCount/SegmentCount/TariffId/UnitPrice/TotalCost`): historical accuracy is independent of later tariff edits.
- **Normalized `Status` + `ProviderMessageId`:** provider-agnostic reporting + an efficient DLR update path.

**Alternatives considered & rejected:**
- *Keep `Subscriber` dimension + `SubscriberId` on the fact:* dropped — recipients are ad-hoc, so there is no reuse to dedupe; the number lives on the fact directly. (Additive to re-introduce later — see §9.)
- *`CampaignId` / `MessageTemplateId` on the fact:* dropped — no campaign grouping, no templates (each message is distinct).
- *Cost computed at report time from tariff tables:* rejected — breaks historical accuracy and makes every cost report join versioned tariffs.
- *Storing message text inline on the fact:* rejected — bloats the fact, slashes rows-per-page, and couples retention of cheap facts to expensive text (see §4.10 / §7).
- *`UNIQUEIDENTIFIER` key:* rejected — random GUID clustered key causes fragmentation and worse page density; even as a nonclustered PK it is 16 bytes × 10⁹.

### 4.10 `MessageBody`
| Column | Type | Notes |
|---|---|---|
| `MessageId` | `BIGINT` | **PK = FK** → `Message` (1:1), `CIX`, partition-aligned on `SubmitDateKey` |
| `SubmitDateKey` | `INT` | partition column (aligned with `Message`) |
| `Body` | `NVARCHAR(MAX)` | the exact distinct text that was sent |

**Why a separate 1:1 table even though it's now just one column:** the text is variable-length and large, while the fact is fixed-width and hot. Keeping `NVARCHAR(MAX)` off the fact preserves high rows-per-page on the fact (faster scans/inserts, smaller indexes) and lets the body follow its **own shorter retention** and **`PAGE`/Unicode compression**. *Alternative:* inline `Body` on `Message` — rejected (couples the hot, scanned fact to cold, rarely-read text). *Alternative:* keep the old `TemplateId` + `MergeVariablesJson` shape — rejected (no templates; each body is distinct).

### 4.11 `DeliveryReportLog` (optional, off by default)
| Column | Type | Notes |
|---|---|---|
| `DeliveryReportLogId` | `BIGINT IDENTITY` | **PK**, `CIX` |
| `MessageId` | `BIGINT` | **FK**; `NCIX` |
| `RawStatusCode` | `INT` | provider-native code |
| `NormalizedStatus` | `TINYINT` | |
| `ReceivedAtUtc` | `DATETIME2(3)` | |

**Why optional:** the **current** normalized status already lives on `Message`, which satisfies all reporting. A full per-event log **at least doubles the row count of the largest table**. **Tradeoff/decision:** enable only if forensic audit or DLR reprocessing is a hard requirement; if enabled, give it **aggressive short retention** (30–90 days) and its own partitioning. *Default recommendation: keep disabled; rely on the snapshot status on `Message`.*

### 4.12 `MessageDailyAggregate`
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

**Indexing:** clustered on the composite key above (chosen so the most common range filter — `DateKey` — leads, enabling partition-eliminable, range-friendly scans). **The cube is keyed on `GeoSectionId` at a bounded reporting level (city by default).** The async rollup resolves each message's section up to its city ancestor before upserting, which **bounds cardinality**. Province/top-level rollups join the small aggregate to the `GeoSection` tree via `Path`; **zone-level** detail is served by the columnstore on the fact (§8). *Alternative:* key the cube at zone granularity — rejected by default (combinatorial explosion). *Alternative:* materialized/indexed view — rejected (indexed-view locking/maintenance on the hot insert path); a **physically maintained, post-commit** table is safer.

---

## 5. Reporting Validation

For each report: **required tables**, **join strategy**, **performance considerations**. The recurring theme: **aggregate reports read `MessageDailyAggregate` (tiny) and join only to small dimensions / the geo tree; they never scan the billion-row fact.** Point-lookup reports hit a targeted nonclustered index on the fact.

| # | Report | Required tables | Join strategy | Performance |
|---|---|---|---|---|
| 1 | **Cost for Tehran province, Spring 1405, water bills** | `MessageDailyAggregate` + `DimDate` + `GeoSection` + `MessageType` | Filter `DimDate` (`PersianYear=1405, PersianSeason=1`); resolve Tehran's `GeoSectionId`, restrict via `GeoSection.Path LIKE '/<Tehran>/%'`; filter `MessageTypeId=<Water Bill>`; `SUM(TotalCost)` | Aggregate + small-tree join; sub-second. No fact scan. |
| 2 | **Cost by provider** | `MessageDailyAggregate` + `Provider` | `GROUP BY ProviderId`, join `Provider` for names | Trivial; aggregate scan + tiny join. |
| 3 | **Count by city** | `MessageDailyAggregate` + `GeoSection` | `GROUP BY GeoSectionId` (cube is at city level), join `GeoSection` for names | Aggregate-only. |
| 4 | **Count by zone** | `Message` (columnstore) + `GeoSection` | `GROUP BY GeoSectionId` (zone) over partition-eliminated columnstore | Zone excluded from default cube → columnstore with date-partition elimination; seconds, not minutes. |
| 5 | **History of a recipient (mobile number)** | `Message` + `MessageBody` | Point lookup via `NCIX (MobileNumber, SubmitDateKey)`; optional body join | Index seek; milliseconds. Body fetched only if displayed. |
| 6 | **History of a bill** | `Message` (+`MessageBody`) | Seek `filtered NCIX (BillNumber)` | Index seek; few rows; fast. |
| 7 | **Delivery success rate by provider** | `MessageDailyAggregate` | `SUM(CASE Status=Delivered)/SUM(MessageCount) GROUP BY ProviderId` | `NormalizedStatus` is part of the aggregate grain → direct. |
| 8 | **Monthly cost trend** | `MessageDailyAggregate` + `DimDate` | `GROUP BY PersianYearMonth` | Aggregate-only; ideal for time series. |
| 9 | **Top provinces by spend** | `MessageDailyAggregate` + `GeoSection` | Roll each cube row up to its province ancestor via `GeoSection.Path`; `GROUP BY province ORDER BY SUM(TotalCost) DESC` | Aggregate + small-tree join; trivial. |

**Key validation outcome:** every *aggregate* report is answerable from a table that grows with **dimension combinations × days**, not with **messages** — so reporting performance stays flat as the fact grows from 10M to 1B. Every *point-lookup* report (recipient/bill history) is answerable by a single, deliberately chosen nonclustered index seek on the fact. (The former *campaign-summary* report is dropped along with the campaign concept.)

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

At 10⁸–10⁹ rows, storage strategy *is* the architecture. Principle: **duplicate small fixed-width keys freely (cheap, kills joins); isolate large/variable text (expensive).** Note that this refactor consciously **gave up two dedup wins** — template dedup (text is distinct) and recipient dedup (recipients are ad-hoc) — in exchange for a simpler, correct model. That makes the remaining levers (text separation, compression, retention) more important.

### 7.1 What should be duplicated (denormalized) — and why it's worth it
- **Dimension keys (`GeoSectionId`, `ProviderId`, `MessageTypeId`, `CustomerId`) on the fact.** Tiny fixed-width keys; duplicating them removes multi-join hops from every report. Trivial cost vs. read-time savings.
- **Cost snapshot on the fact.** Duplicating `UnitPrice/TotalCost` (per §6) buys immutability and join-free cost reporting.

### 7.2 What should **not** be duplicated
- **Descriptive names** (geo-section / provider / type names) → never on the fact; joined for labels only against tiny dimensions or aggregate output.
- **The recipient number IS now on the fact** (`MobileNumber`) — a deliberate reversal: with ad-hoc recipients there is no stable population to dedupe, so a `Subscriber` dimension would add a join and a table for no dedup benefit. Cost: ~15 bytes/row repeated. Accepted (see §7.4).

### 7.3 Should message text be normalized / stored separately? → **Separated, not normalizable**
Each message's text is **distinct**, so there is **nothing to normalize/deduplicate** (the old template approach is gone). Decisions:
- **Physically separate** text into `MessageBody` (1:1). Keeps the fact narrow and lets text follow its **own, shorter retention**.
- Apply **`PAGE` + Unicode compression** to `MessageBody` — now the primary storage lever, since dedup is unavailable.
- Bodies are the prime candidate for **earliest retention purge** (the fact's billing/delivery data outlives the text).

### 7.4 Should recipients be separated from messages? → **No (ad-hoc recipients)**
With a managed subscriber base, a `Subscriber` dimension wins (dedup phone/geo, subscriber history). Here recipients are **ad-hoc**, so we store `MobileNumber` **directly on the fact**. *Tradeoff:* the number repeats across a recipient's messages (~15 bytes × 10⁹) and there's no entity-level "subscriber" record — but we avoid a 25M-row dimension and a per-message join, and "recipient history" is still a single indexed seek on `MobileNumber`. If a managed base is needed later, a `Subscriber` table + nullable `SubscriberId` is an additive change (§9).

### 7.5 Pre-aggregation
`MessageDailyAggregate` keeps aggregate reports flat-cost regardless of fact growth (see §4.12) — the single highest-leverage storage/perf decision in the schema.

### 7.6 Tradeoff summary

| Decision | Saves | Costs | Verdict |
|---|---|---|---|
| Denormalize dimension keys onto fact | Join elimination at 10⁹ scale | ~5 bytes/row | **Adopt** |
| Single `GeoSection` tree vs. three geo tables | One fact key + fewer tables | Rollups join the small geo tree | **Adopt** |
| `MobileNumber` on fact (no `Subscriber`) | One fewer table + no join | ~15 bytes/row, no dedup | **Adopt** (ad-hoc recipients) |
| Text in `MessageBody` (separate) + compression | Narrow fact; independent retention | 1:1 table; compression CPU | **Adopt** |
| No templates / no campaigns | Three fewer tables; simpler writes | Lost template text dedup | **Adopt** (text is distinct) |
| Pre-aggregate (`MessageDailyAggregate`) | Flat reporting cost vs. fact growth | Rollup process + small table | **Adopt** |
| Full per-event `DeliveryReportLog` | Forensic audit | **Doubles largest table** | **Default off** |

---

## 8. Concurrency and Deadlock Prevention

> *This is the most safety-critical section.* Workload: bursty **concurrent batch inserts** of distinct messages + high-volume **status updates** (DLRs) + concurrent **reporting reads** — all on the same fact family.

### 8.1 Partitioning — the foundation
- **`Message` and `MessageBody` are range-partitioned by `SubmitDateKey` (monthly).**
- Benefits that directly serve the requirements:
  - **Lock escalation is contained to a partition,** not the whole table — set `ALTER TABLE … SET (LOCK_ESCALATION = AUTO)`. A reporting scan that escalates locks on an old month cannot block inserts into the current month.
  - **Reporting reads hit old partitions; inserts hit the current partition** → physical separation of read vs. write contention.
  - **Retention by `SWITCH`/drop** of whole partitions — no giant row-by-row `DELETE` storms.

### 8.2 Clustered index choice (the hot-page problem)
- A naïve **clustered `BIGINT IDENTITY`** funnels every concurrent insert to the **same trailing page** → `PAGELATCH_EX` "last-page insert" contention, the classic high-volume insert hotspot.
- **Decision:** clustered index = **`(SubmitDateKey, MessageId)`**, partition-aligned, **with `OPTIMIZE_FOR_SEQUENTIAL_KEY = ON`** (SQL Server 2019+) to throttle the last-page latch convoy.
  - `MessageId` remains the **nonclustered** PK (uniqueness + FK target) so it doesn't dictate physical insert order alone.
- **Rejected alternatives:**
  - *Clustered GUID to scatter inserts:* rejected — random keys cause page splits/fragmentation and poor page density.
  - *Hash-bucket prefix on the key:* rejected as default — harms range scans/partition elimination; revisit only if `OPTIMIZE_FOR_SEQUENTIAL_KEY` proves insufficient under load.

### 8.3 Nonclustered indexes — kept minimal *on purpose*
Every NCI is a second structure to maintain on **every insert** (and some on update), so the hot table carries only what point-lookups truly need:
| Index | Justification | Why not more |
|---|---|---|
| `CIX (SubmitDateKey, MessageId)` | Partition alignment + sequential locality | — |
| `NCIX (MobileNumber, SubmitDateKey)` | Recipient-history seeks | Required by report #5 |
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
| **Page contention** | Narrow fixed-width fact (more rows/page); minimal NCIs; `PAGE` compression on cold data |
| **Hot partitions** | Monthly partitioning isolates the write-hot current month; reads/analytics target cold partitions/columnstore |
| **Reader/writer deadlocks** | RCSI snapshot reads; async post-commit aggregation; consistent write ordering |

---

## 9. Future Evolution

The schema is built so that the following changes are **additive (insert rows / add a nullable column / add a partition / add a table + nullable FK)** — never a fact rewrite.

| Future need | How it's absorbed |
|---|---|
| **New SMS provider** | Insert into `Provider` (incl. its `BaseUrl`/`FallbackBaseUrl`), `SenderLine`, `Tariff`/`TariffRate`; add only the provider's **credentials** to the secret store. Fact stores `ProviderId` + normalized `Status` → no schema or report change. |
| **More than two provider network paths** | *(Deferred.)* Escalate `Provider`'s two URL columns to a child `ProviderEndpoint(ProviderId, NetworkType, BaseUrl, Priority, IsActive)` table (see §4.2). |
| **Re-introduce batching / campaigns** | Add a `Batch` (a.k.a. `Campaign`) table + a **nullable** `BatchId` on `Message` + maintained counters. Existing rows stay `NULL`; no rewrite. |
| **Re-introduce a managed subscriber base** | Add a `Subscriber` table + a **nullable** `SubscriberId` on `Message`; backfill optional. `MobileNumber` stays for ad-hoc sends. |
| **Re-introduce templates** | Add `MessageTemplate` + nullable `MessageTemplateId`/merge-variables on `MessageBody`; distinct-text sends keep using `Body`. |
| **Deeper geography (sub-zones, regions)** | Insert `GeoSection` rows at a new `SectionType` level; the self-referencing tree + `Path` absorb arbitrary depth. |
| **Tenant-specific message types** | Add a nullable `CustomerId` to `MessageType` and/or widen `MessageTypeId` to `SMALLINT` — additive. |
| **New reporting dimension** | Add the dimension table + a narrow FK on the fact; extend `MessageDailyAggregate` grain. Columnstore covers ad-hoc needs immediately. |
| **New delivery-report mechanism** (push webhooks, richer states) | Extend the normalized `Status` enum; `DeliveryReportLog` (if enabled) captures new raw codes. |
| **New Jalali/fiscal periods** | Add columns to `DimDate`; fact/aggregate unaffected. |
| **Scale beyond a single billion** | Move partitions monthly→weekly; archive/compress cold partitions via partition switching. |

---

## 10. Design Principles & Tradeoff Summary

Per the explicit mandate — **not** normalization purity or theoretical elegance, but operational reality:

1. **High-volume processing first.** Narrow fixed-width fact, partition-aligned clustered key, sequential-key optimization, minimal indexes, batched short-transaction inserts, async aggregation.
2. **Reporting simplicity.** Small dimension keys on the fact + a pre-aggregated cube ⇒ aggregate reports are flat-cost regardless of fact size; point-lookups are single index seeks.
3. **Long-term maintainability.** Additive evolution; versioned tariffs; provider-agnostic normalized status; one geo tree instead of three tables; **fewer tables overall** after removing templates/campaigns/subscribers.
4. **Low storage consumption.** No duplicated names; one geo key instead of three; text isolated in a compressible, short-retention satellite. (Template/recipient dedup was consciously traded away for correctness — §7.)
5. **Minimal deadlocks.** Partition-scoped locking, sub-escalation batch sizes, RCSI reads, async post-commit rollups, consistent write ordering.
6. **Simple operational support.** Retention via partition switching (no delete storms); self-contained billing rows (auditable without tariff tables); a small, easily-cached set of dimensions.

**Every major tradeoff was resolved in favor of write throughput, report simplicity, and storage economy — and is documented where it arises (§4 alternatives, §7 storage, §8 concurrency).**

---

### Open decisions for reviewers

1. **`DeliveryReportLog`** — confirm whether forensic/raw-DLR audit is a hard requirement (default: **off**, status snapshot on `Message` only).
2. **`MessageDailyAggregate` geo grain** — confirm the cube's default reporting level is **city** (zone rolled up at write time; zone-level detail via columnstore), vs. keying the cube at zone granularity.
3. **Body retention window** — confirm a shorter retention for `MessageBody` than for `Message` is legally acceptable (now the main storage lever, since text can't be deduped).
4. **`MessageType` scope** — confirm a single global, type-merged dimension (delivery class + business purpose) is sufficient, or whether tenant-specific purposes (nullable `CustomerId`) are needed now.
5. **Partition cadence** — monthly proposed; confirm vs. weekly given peak daily volumes.
