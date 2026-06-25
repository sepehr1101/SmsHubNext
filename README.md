# SmsHubNext — Database & Storage Architecture Design

> **Status:** Design proposal — *for architectural review*
> **Scope of this document:** Data model and storage architecture **only**. No application, transport (RabbitMQ), provider integration, or API code is described here. The goal is to **validate the data model before any implementation begins.**
> **Target engine:** **Microsoft SQL Server** (2019+). The requirements explicitly reference *lock escalation, page contention, clustered/nonclustered indexes,* and *hot partitions* — all SQL Server concepts — and the platform is built on .NET. The logical model remains portable; the physical tuning notes are SQL-Server-specific and are flagged as such.
> **Currency:** Iranian Rial (IRR). **Calendar:** Reporting periods are expressed in the **Jalali (Persian) calendar** (e.g. *Spring 1405*); the precise instant is stored UTC (`SubmittedAtUtc`), and a single **`SubmitDateJalali`** column (`CHAR(10)`, e.g. `1405/01/03`) is the **partition key, clustered-index date key, and sargable period filter**. The fixed-width, zero-padded slash format sorts lexicographically = chronologically, so range/partition logic is exact. There is no date-dimension table and no pre-aggregate cube.
> **Delivery model:** `Message` is **insert-only**; delivery outcomes arrive asynchronously as **append-only `DeliveryReport` rows**, and a message's current delivery state is the **latest report** for it. Customers authenticate sends with **API keys**.

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

SmsHubNext is a **high-volume, multi-tenant SMS dispatch and accounting platform**. Its primary documented use case is **utility/organizational notifications** — e.g. water-bill notices sent to recipients across Iranian geographic sections (province → city → zone) — billed and reported by provider, geography, message type, and time period (in the Jalali calendar). **Each message is individually composed by the caller**; the platform stores and dispatches the supplied text. **Customers authenticate via API keys.**

### 1.1 Expected usage patterns

| Pattern | Description | Storage impact |
|---|---|---|
| **Bursty bulk sends (distinct messages)** | Billing cycles push **hundreds of thousands to millions** of individually-composed messages in a short window. | Heavy concurrent inserts → write path must be the #1 optimization target. |
| **Steady transactional/OTP traffic** | Lower-volume, latency-sensitive single messages (OTP, alerts). | Small but constant; must not be starved by bulk inserts. |
| **Asynchronous delivery reports (DLR)** | Each sent message later receives 1+ status reports from the provider, arriving minutes-to-hours later. | High-volume **append** traffic into `DeliveryReport` (no in-place fact updates). |
| **Heavy reporting/analytics** | Aggregate reports by geo / provider / type / Jalali period; **delivery success rate** via latest-report-per-message. | Read path served by **columnstores on cold partitions**, isolated from the OLTP hot path. |

### 1.2 Expected message volumes (working assumptions for sizing)

Baseline assumption: a national utility footprint, **~10 million messages/month** average with billing-cycle peaks of several million/day.

| Horizon | Cumulative messages | Order of magnitude |
|---|---|---|
| 1 month | ~10 million | 10⁷ |
| 1 year | ~120 million | 10⁸ |
| 5 years | ~600 million – 1 billion | 10⁸–10⁹ |

`DeliveryReport` is **≥ message volume** (a message may receive several reports). **Design must assume the billion-row regime** for both tables.

### 1.3 Reporting requirements

The schema must answer, efficiently and accurately:

- Cost by **geo section** (province / city / zone), by **provider**, by **message type** (e.g. water-bill notices), by **Jalali period** (e.g. *Spring 1405*).
- **Counts** and **delivery success rates** (latest delivery report per message) by the same dimensions.
- **History of a single recipient** (by mobile number) and **history of a single bill** (by bill id).
- **Monthly cost trends.**
- **Top provinces by spend.**

Reports are predominantly **aggregations over large ranges** plus a few **point-lookups** (recipient/bill history). These two access shapes are in tension and are handled differently (see §5, §8).

### 1.4 Historical data requirements

- **Cost and delivery facts must remain immutable and accurate forever** (or for the legally mandated retention period) — *even after tariffs change*. This forces **price snapshotting** (see §6).
- `Message` and `DeliveryReport` are **append-only**; cold partitions are columnstore-compressed and retain full history cheaply, dropped in lockstep by partition switching.
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
| **Customer (Tenant / Customer Context)** | The organization that owns the traffic and is billed (e.g. a regional water company). Isolation boundary. **Sends** messages. |
| **API Key** | The credential a customer presents to authenticate send requests; many per customer (rotation). Only a **hash** is stored. |
| **API Key IP Restriction** | Optional allow-list of source networks (CIDR) for a key. |
| **Provider** | An SMS carrier/aggregator (Magfa, …). Owns sender lines and tariffs; source of delivery reports. |
| **Sender Line** | A specific origin number (`3000…`, `1000…`, `4040…`) belonging to a provider. |
| **Message Type** | Single classification axis — *delivery class* (OTP / Transactional / Bulk) **and** *business purpose* (Water Bill, Outage Alert, …). |
| **Message** | The central **fact**: one SMS send. **Insert-only**; carries text reference, recipient, references, Jalali submit date, cost snapshot, and **send-lifecycle** status. |
| **Message Body** | The exact sent text (1:1 with Message), physically separated for storage/retention. |
| **Delivery Report** | An **append-only** status event for a message (Delivered/Undelivered/Expired/…). A message's current delivery state is its **latest** report. |
| **Recipient** | The receiver — a **`MobileNumber` on the message** (ad-hoc; not a managed dimension). |
| **Client / Business references** | Caller-supplied ids on the message: `ClientCorrelatedId` (idempotency key), `BillId`, `PayId`. |
| **Tariff** | Time-bounded pricing for a (provider, encoding, message-type) combination; versioned by effective date range. |
| **Geo Section** | A single self-referencing geographic dimension (Province → City → Zone → …) via a parent link + `SectionType`. |

**Fact vs. Dimension split is the spine of this design:** small dimensions referenced by surrogate keys; two large **append-only** tables — `Message` (the send fact) and `DeliveryReport` (the status-event stream) — both partitioned by `SubmitDateJalali` and analyzed via columnstores. Folding mutable delivery status onto the billion-row fact is deliberately **avoided** (it would fight the columnstore); current delivery state is derived as the latest report per message.

> **What was removed and why:** earlier drafts included `MessageTemplate` + merge variables, a `Campaign` grouping, a managed `Subscriber` base, a `DimDate` dimension, and a `MessageDailyAggregate` cube — all dropped (distinct text, ad-hoc recipients, Jalali-on-fact, columnstore analytics). The optional `DeliveryReportLog` audit has been **promoted to a first-class `DeliveryReport`** table that *is* the delivery-status source of truth. All removed pieces are re-introducible additively (see §9).

---

## 3. Database Schema Proposal

Tables are grouped by role. Volume estimates assume the §1.2 sizing.

### 3.0 Table inventory

| # | Table | Role | 1 mo | 1 yr | 5 yr |
|---|---|---|---|---|---|
| 1 | `Customer` | Dimension | <100 | <100 | <500 |
| 2 | `ApiKey` | Dimension (auth) | <500 | <1k | <3k |
| 3 | `ApiKeyIpRestriction` | Dimension (auth, optional) | <1k | <2k | <6k |
| 4 | `Provider` | Dimension | <10 | <20 | <50 |
| 5 | `SenderLine` | Dimension | <100 | <200 | <500 |
| 6 | `MessageType` | Dimension | ~10 | ~30 | ~80 |
| 7 | `GeoSection` | Dimension (self-referencing) | ~30k | ~50k | ~80k |
| 8 | `Tariff` | Dimension (versioned) | <100 | <300 | ~1k |
| 9 | `TariffRate` | Dimension (versioned) | <500 | ~1.5k | ~5k |
| 10 | **`Message`** | **Fact (hot, insert-only)** | **~10M** | **~120M** | **~0.6–1B** |
| 11 | `MessageBody` | Fact satellite (text) | ~10M | ~120M | ~0.6–1B |
| 12 | **`DeliveryReport`** | **Status-event stream (append-only)** | ~12M | ~150M | ~1B+ |

> Below, each table is summarized against the required facets. Full column-level detail is in **§4**.

### Dimension tables (1–9) — shared facets

- **Purpose:** Stable, deduplicated reference data referenced by the fact via surrogate keys.
- **Business justification:** Eliminate repetition across ~10⁹ fact rows; enable consistent grouping/filtering; authenticate senders (`ApiKey`).
- **Read pattern:** Tiny cached lookups. `ApiKey` is read on **every send** (by key hash) — a hot but trivially-indexed point lookup.
- **Write pattern:** Rare inserts/updates (admin/onboarding/key rotation).
- **Retention:** Permanent. Versioned dimensions keep history via effective ranges; revoked keys are kept for audit.
- **Storage:** Negligible relative to the facts.

### 10. `Message` — the central fact (hot, insert-only)

- **Purpose:** One row per SMS send — what was sent, to which number, by which line/provider, at what cost, with what **send-lifecycle** outcome, against which references.
- **Business justification:** Every report, cost calc, and history/reconciliation lookup resolves here.
- **Expected volume:** 10M → 120M → ~1B.
- **Read pattern:** (a) **range aggregations** via a **nonclustered columnstore on cold partitions**; (b) **point lookups** by recipient, bill id, client correlation id, provider message id; (c) joined to the latest `DeliveryReport` for delivery-state reports.
- **Write pattern:** Massive **concurrent batch inserts**; **insert-only** thereafter (delivery status is *not* updated here). The only near-insert mutation is an optional send-lifecycle transition on the current (rowstore) partition, never on the cold columnstore.
- **Retention:** Long (billing/legal); aged out by **partition switching** (with `DeliveryReport` and `MessageBody` in lockstep).
- **Storage:** Narrow; text in `MessageBody`, delivery events in `DeliveryReport`.

### 11. `MessageBody` — text satellite (1:1 with Message)

- **Purpose / justification:** Hold the exact distinct text, separate from the narrow fact (audit/legal proof without fact bloat).
- **Volume:** 1:1 with `Message`.
- **Read pattern:** Rare (operator inspects one message). **Write:** inserted with the message; immutable.
- **Retention:** **Shorter** than the fact where policy allows — purge bodies earliest to reclaim the bulk of storage.
- **Storage:** Largest per-row cost; **non-deduplicable** (distinct text). Isolated for compression + independent retirement (§7).

### 12. `DeliveryReport` — status-event stream (append-only)

- **Purpose:** One row per delivery-status report received for a message. **The source of truth for delivery outcomes.**
- **Business justification:** Delivery is asynchronous and may update multiple times; storing each event append-only keeps the `Message` fact immutable (columnstore-friendly) and preserves the full status history.
- **Volume:** ≥ message volume (~1B+ at 5yr).
- **Read pattern:** (a) **latest report per message** (window over a partition range) joined to `Message` for **success-rate** reports — accelerated by a columnstore on cold partitions; (b) point lookup of a message's report history.
- **Write pattern:** **Append-only inserts** as DLRs arrive; never updated.
- **Retention:** Aligned with `Message` (partition-switched together) so delivery history stays joinable; shorten independently only if success-rate history isn't needed long-term.
- **Storage:** A second large table — the accepted cost of an immutable, event-sourced delivery model (see §7 for the denormalized-current-status alternative).

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

**Why:** Tenancy is a first-class isolation and reporting boundary. A `SMALLINT` key (hundreds of customers) is the cheapest FK to repeat ~10⁹ times. *Alternative:* customer name on the fact — rejected (text duplication at 10⁹ scale).

### 4.2 `ApiKey`
| Column | Type | Notes |
|---|---|---|
| `ApiKeyId` | `INT IDENTITY` | **PK**, `CIX` |
| `CustomerId` | `SMALLINT` | **FK** → `Customer` |
| `Name` | `NVARCHAR(100)` | human label, e.g. "Production billing system" |
| `KeyPrefix` | `VARCHAR(12)` | **non-secret** public prefix shown in dashboards/logs; `NCIX` |
| `KeyHash` | `BINARY(32)` | **SHA-256 of the secret key**; `NCIX` **unique**. Plaintext is **never** stored |
| `IsActive` | `BIT` | |
| `ExpiresAtUtc` | `DATETIME2(3)` | nullable — optional expiry |
| `RevokedAtUtc` | `DATETIME2(3)` | nullable — set on revocation (row kept for audit) |
| `LastUsedAtUtc` | `DATETIME2(3)` | nullable — updated coarsely/async, not on the per-request hot path |
| `CreatedAtUtc` | `DATETIME2(3)` | |

**Indexes:** `NCIX UNIQUE (KeyHash)` — the **auth lookup** on every send; `NCIX (CustomerId)` — list a tenant's keys.

**Why a separate table (not a column on `Customer`):** a customer holds **many** keys (rotation, per-system/environment), each with its own lifecycle. **Why store only a hash:** if the DB leaks, **plaintext keys must not**. Authenticate by hashing the presented key and seeking `KeyHash`; `KeyPrefix` identifies a key in logs without exposing it. *Alternatives:* plaintext/encrypted key (rejected — breach liability / unnecessary); JWT/OAuth (heavier; layer later, §9). Per-minute throttling lives in the app/cache layer.

### 4.3 `ApiKeyIpRestriction` (optional)
| Column | Type | Notes |
|---|---|---|
| `ApiKeyIpRestrictionId` | `INT IDENTITY` | **PK**, `CIX` |
| `ApiKeyId` | `INT` | **FK** → `ApiKey`; `NCIX` |
| `Cidr` | `VARCHAR(43)` | allowed source range (IPv4/IPv6 CIDR) |
| `Description` | `NVARCHAR(100)` | nullable |

**Why (optional):** governmental/enterprise customers reach the platform over **specific networks** (public internet and/or intranet gateway — the same dual-path reality behind `Provider.BaseUrl`/`FallbackBaseUrl`). Binding a key to known CIDRs is cheap defense-in-depth. Child table (multiple ranges per key); **omitted** for unrestricted keys. *Alternative:* a delimited column on `ApiKey` — rejected (not queryable/extensible).

### 4.4 `Provider`
| Column | Type | Notes |
|---|---|---|
| `ProviderId` | `TINYINT IDENTITY` | **PK**, `CIX` |
| `Name` | `NVARCHAR(100)` | e.g. "Magfa" |
| `Code` | `VARCHAR(50)` | `NCIX` unique |
| `BaseUrl` | `VARCHAR(300)` | primary API endpoint (e.g. public-internet gateway) |
| `FallbackBaseUrl` | `VARCHAR(300)` | secondary endpoint over a different network path (e.g. intranet/private gateway); nullable |
| `IsActive` | `BIT` | |

**Why:** Provider count is tiny → `TINYINT`. New providers = new rows, never schema change. **Endpoints live here (provider info, not reporting); credentials do not.** The same provider is reached over more than one network path simultaneously (internet **and** intranet) with failover — runtime data, not per-environment `appsettings`. We model `BaseUrl` + `FallbackBaseUrl`; credentials (`username/domain/password`) stay in the secret store keyed by `Provider.Code`. *Deferred alternative:* a child `ProviderEndpoint` for **three or more** paths (§9). *Alternative:* provider as a string on the fact — rejected (bytes × 10⁹, no FK).

### 4.5 `SenderLine`
| Column | Type | Notes |
|---|---|---|
| `SenderLineId` | `SMALLINT IDENTITY` | **PK**, `CIX` |
| `ProviderId` | `TINYINT` | **FK** → `Provider` |
| `LineNumber` | `VARCHAR(20)` | `3000…`, `1000…`, `4040…`; `NCIX` |
| `IsSharedLine` | `BIT` | shared (public) line vs. dedicated (private) number |
| `IsActive` | `BIT` | |

**Why:** Lines have distinct pricing/reachability and belong to a provider. Surrogate `SMALLINT` keeps the fact FK small. The shared-vs-dedicated distinction is genuinely **binary** → a `BIT` (`IsSharedLine`), not a type code. *Alternative:* raw line string per message — rejected (repetition + no metadata).

### 4.6 `MessageType`
| Column | Type | Notes |
|---|---|---|
| `MessageTypeId` | `TINYINT` | **PK** (seeded), `CIX` |
| `Name` | `NVARCHAR(80)` | "OTP", "Transactional", "Bulk", "Water Bill", "Outage Alert", … |
| `Code` | `VARCHAR(50)` | `NCIX` |

**Why:** The **single classification axis** — *delivery class* + *business purpose* (the former `BusinessCategory`, merged in). Global + `TINYINT` for simplicity. *Future (additive):* nullable `CustomerId` and/or widen to `SMALLINT`. *Alternatives:* separate `BusinessCategory` dimension (extra table/key/join, not needed); `BIT IsOtp` (not extensible) — rejected.

### 4.7 `GeoSection` (self-referencing geographic hierarchy)
| Column | Type | Notes |
|---|---|---|
| `GeoSectionId` | `INT IDENTITY` | **PK**, `CIX` |
| `ParentGeoSectionId` | `INT` | **FK** → `GeoSection` (self); `NULL` at the top level (province) |
| `SectionType` | `TINYINT` | 1 = Province, 2 = City, 3 = Zone (extensible) |
| `Name` | `NVARCHAR(100)` | |
| `Code` | `VARCHAR(20)` | `NCIX` |
| `Path` | `VARCHAR(900)` | materialized ancestor path, e.g. `/12/450/8123/`; `NCIX` for subtree filters |
| `IsActive` | `BIT` | |

**Why one self-referencing table instead of three:** a strict hierarchy as a single **adjacency-list** table collapses three tables into one while preserving rollups; deeper levels are inserts, not schema changes. The denormalized **`Path`** makes "everything under Tehran province" a sargable `Path LIKE '/<TehranId>/%'`. The fact stores one `GeoSectionId` (most-specific section); reports roll it up via the small tree. *Alternatives:* three geo tables (superseded); flat tag (no rollups); `HIERARCHYID` (valid; `Path` chosen for portability).

### 4.8 `Tariff` (versioned header)
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

### 4.9 `TariffRate` (per-segment detail)
| Column | Type | Notes |
|---|---|---|
| `TariffRateId` | `INT IDENTITY` | **PK**, `CIX` |
| `TariffId` | `INT` | **FK** → `Tariff` |
| `MinChars` | `SMALLINT` | character-range lower bound |
| `MaxChars` | `SMALLINT` | upper bound (nullable = ∞) |
| `PricePerSegment` | `DECIMAL(19,4)` | |

**Why two tables:** header = validity window + applicability; detail = price banding by character range. Separates "*which tariff applies*" from "*how much*". *Alternatives:* one wide row (inflexible); price in config (must be auditable data). **Never used at report time** — the resolved price is frozen onto the message (§6).

### 4.10 `Message` — the fact (most-scrutinized table)
| Column | Type | Notes |
|---|---|---|
| `MessageId` | `BIGINT IDENTITY` | **PK** (nonclustered — see §8) |
| `SubmitDateJalali` | `CHAR(10)` | **partition column** — Jalali date `yyyy/mm/dd` (e.g. `1405/01/03`); part of `CIX`; sargable period key. No FK. |
| `SubmittedAtUtc` | `DATETIME2(3)` | precise UTC instant |
| `CustomerId` | `SMALLINT` | **FK** (tenant/sender) |
| `ProviderId` | `TINYINT` | **FK** |
| `SenderLineId` | `SMALLINT` | **FK** |
| `MessageTypeId` | `TINYINT` | **FK** (delivery class + business purpose) |
| `GeoSectionId` | `INT` | **FK** → `GeoSection`; nullable (caller-supplied geo tag) |
| `MobileNumber` | `VARCHAR(15)` | recipient, canonical `98…` (ad-hoc) |
| `ClientCorrelatedId` | `VARCHAR(100)` | caller's own message id / **idempotency** key; nullable; maps to provider `uid` |
| `BillId` | `VARCHAR(31)` | external **bill** reference; nullable |
| `PayId` | `VARCHAR(31)` | external **payment** reference; nullable |
| `Encoding` | `TINYINT` | GSM7 / UCS2 (snapshot) |
| `CharacterCount` | `SMALLINT` | snapshot |
| `SegmentCount` | `TINYINT` | parts (snapshot) |
| `TariffId` | `INT` | **FK** — which tariff priced this (audit) |
| `UnitPrice` | `DECIMAL(19,4)` | price per segment **at submission** (snapshot) |
| `TotalCost` | `DECIMAL(19,4)` | `UnitPrice × SegmentCount` (snapshot) |
| `Status` | `TINYINT` | **send-lifecycle** status: Queued / Submitted / Sent / Rejected / Unknown (delivery outcomes live in `DeliveryReport`) |
| `ProviderMessageId` | `VARCHAR(50)` | provider's id — the key used to **match incoming DLRs** to this message |

**Primary/clustered/indexes (justified in §8):**
- **PK:** `MessageId` — **nonclustered**, unique.
- **CIX (clustered):** `(SubmitDateJalali, MessageId)` — aligns with **Jalali-monthly partitioning**.
- **NCIX 1:** `(MobileNumber, SubmitDateJalali)` — recipient history.
- **NCIX 2:** `(ProviderId, ProviderMessageId)` — resolve an incoming DLR to its `MessageId`.
- **NCIX 3 (filtered):** `(CustomerId, ClientCorrelatedId) WHERE ClientCorrelatedId IS NOT NULL` — idempotency + client lookups.
- **NCIX 4 (filtered):** `(BillId) WHERE BillId IS NOT NULL` — bill history.
- **Nonclustered columnstore on cold partitions** — the analytics engine. `PayId` is not indexed by default.

**Why this structure is preferred:**
- **Insert-only + narrow:** no message text, no in-place delivery-status churn → the rowstore packs rows and the columnstore on cold partitions never has to absorb updates.
- **Small dimension keys + denormalized Jalali date** (`SubmitDateJalali`): period and dimension reports filter/group **without** a date dimension or a join to giant tables.
- **Caller references** (`ClientCorrelatedId/BillId/PayId`): idempotency + reconciliation inline; nullable.
- **Frozen cost snapshot:** historical accuracy independent of later tariff edits.
- **`Status` (send-side) + `ProviderMessageId`:** the send outcome and the key to attach asynchronous delivery reports.

**Alternatives considered & rejected:**
- *Fold the latest delivery status onto `Message` via UPDATE (old `StatusUpdatedAtUtc` model):* rejected — in-place updates fight the columnstore and churn indexes on the billion-row fact. Delivery state now lives in `DeliveryReport`; `StatusUpdatedAtUtc` removed.
- *`INT yyyymmdd` for `SubmitDateJalali`:* the chosen `CHAR(10)` (`1405/01/03`) is human-readable and still sorts chronologically; it costs ~6 bytes/row more (replicated into the clustered key + NCIs) — accepted for readability.
- *`DimDate` / `MessageDailyAggregate` / `Subscriber` / `Campaign` / `MessageTemplate`:* all removed (see §2); re-introducible additively (§9).
- *`UNIQUEIDENTIFIER` key:* rejected — fragmentation; 16 bytes × 10⁹.

### 4.11 `MessageBody`
| Column | Type | Notes |
|---|---|---|
| `MessageId` | `BIGINT` | **PK = FK** → `Message` (1:1), `CIX`, partition-aligned on `SubmitDateJalali` |
| `SubmitDateJalali` | `CHAR(10)` | partition column (aligned with `Message`) |
| `Body` | `NVARCHAR(MAX)` | the exact distinct text that was sent |

**Why separate:** variable-length large text off the fixed-width hot fact preserves rows-per-page + columnstore compression, and lets the body follow its **own shorter retention** + **`PAGE`/Unicode compression**. *Alternatives:* inline `Body` (couples hot fact to cold text); old `TemplateId`+`MergeVariables` (no templates now).

### 4.12 `DeliveryReport` — status-event stream (append-only)
| Column | Type | Notes |
|---|---|---|
| `DeliveryReportId` | `BIGINT IDENTITY` | **PK** (nonclustered) |
| `SubmitDateJalali` | `CHAR(10)` | **partition column** — copied from the message; aligns each report to its message's Jalali-month partition |
| `MessageId` | `BIGINT` | **FK** → `Message` |
| `NormalizedStatus` | `TINYINT` | Delivered / Undelivered / Expired / Rejected / Unknown |
| `RawStatusCode` | `INT` | provider-native code |
| `ReceivedAtUtc` | `DATETIME2(3)` | when this report arrived — the **ordering key for "latest"** |

**Indexes:**
- **CIX (clustered):** `(SubmitDateJalali, MessageId, ReceivedAtUtc DESC)` — partition-aligned; clusters a message's reports together so **latest-per-message** (`ROW_NUMBER() OVER (PARTITION BY MessageId ORDER BY ReceivedAtUtc DESC)`) is an ordered, index-supported read.
- **Nonclustered columnstore on cold partitions** — accelerates the windowed success-rate aggregates over a period.

**Why a separate append-only table (the core of this revision):** delivery is asynchronous and may report multiple times; appending events keeps `Message` immutable (columnstore-friendly) and preserves full status history. **Current state = the latest report per message**, computed with a window function over the relevant partition range and joined to `Message` for dimensions (see report #7). *Why partition by the message's `SubmitDateJalali`* (not the report's receive date): it co-locates a message and its reports in the same partition → **partition-wise joins** and lockstep retention. *Alternatives:* (a) update status in place on `Message` — rejected (columnstore/update churn); (b) keep only the latest report per message via in-place update / `IsLatest` flag — rejected (updates again); (c) a denormalized **`MessageDeliveryStatus`** (1 row/message, upserted) for constant-time success rates — deferred as an additive option (§9) if the windowed approach proves too slow.

---

## 5. Reporting Validation

For each report: **required objects**, **strategy**, **performance**. Aggregate reports run on the **columnstores over cold partitions**; point-lookups hit a targeted nonclustered index; delivery-success uses the **latest report per message**.

| # | Report | Required objects | Strategy | Performance |
|---|---|---|---|---|
| 1 | **Cost for Tehran province, Spring 1405, water bills** | `Message` (columnstore) + `GeoSection` + `MessageType` | Filter `SubmitDateJalali BETWEEN '1405/01/01' AND '1405/03/31'`; restrict geo via `GeoSection.Path LIKE '/<Tehran>/%'`; filter `MessageTypeId=<Water Bill>`; `SUM(TotalCost)` | Columnstore batch scan with **segment + partition elimination** on `SubmitDateJalali`; small geo join. Sub-second–seconds. |
| 2 | **Cost by provider** | `Message` (columnstore) + `Provider` | `GROUP BY ProviderId`, join names | Columnstore aggregate; fast. |
| 3 | **Count by city** | `Message` (columnstore) + `GeoSection` | Join fact→`GeoSection`, roll zone→city via `Path`/parent, `GROUP BY` city | Columnstore scan + small hash join; seconds. |
| 4 | **Count by zone** | `Message` (columnstore) + `GeoSection` | `GROUP BY GeoSectionId` (leaf=zone) | Columnstore aggregate; fast. |
| 5 | **History of a recipient (mobile number)** | `Message` + `MessageBody` (+`DeliveryReport`) | Point lookup `NCIX (MobileNumber, SubmitDateJalali)`; optional body + report-history joins | Index seek; ms. |
| 6 | **History of a bill** | `Message` (+`MessageBody`) | Seek `filtered NCIX (BillId)` | Index seek; ms. |
| 7 | **Delivery success rate by provider** | `DeliveryReport` + `Message` | Latest report per message over the period (`ROW_NUMBER() PARTITION BY MessageId ORDER BY ReceivedAtUtc DESC` = 1) → join `Message` → `GROUP BY ProviderId, NormalizedStatus` | Window over the period's `DeliveryReport` partitions (elimination bounds it) + partition-aligned join; columnstore-accelerated. Heavier than a column read — the accepted cost of the immutable-event model. |
| 8 | **Monthly cost trend** | `Message` (columnstore) | `GROUP BY LEFT(SubmitDateJalali, 7)` (Jalali `yyyy/mm`) | Columnstore aggregate on the denormalized Jalali date; fast. |
| 9 | **Top provinces by spend** | `Message` (columnstore) + `GeoSection` | Join + roll up to province via `Path`; `GROUP BY` province `ORDER BY SUM(TotalCost) DESC` | Columnstore scan + small join. |

**Key validation outcome:** cost/count aggregates are batch-mode columnstore scans with partition/segment elimination on `SubmitDateJalali` — cost scales with the **scanned range**, not the full table. **Delivery success rate** is the one report that needs the latest-report-per-message window + join; it is bounded by partition elimination and accelerated by the `DeliveryReport` columnstore. Point-lookups are single index seeks. *(If constant-time success rates over huge ranges are required, maintain a denormalized `MessageDeliveryStatus` — §9.)*

---

## 6. Tariff and Pricing Design

### 6.1 Structure

```
Provider ──< Tariff (versioned by EffectiveFrom/EffectiveTo, per Encoding/MessageType) ──< TariffRate (per character-range band → PricePerSegment)
```

- **Multiple providers:** `Tariff.ProviderId`.
- **Historical tariffs + effective ranges:** `EffectiveFromUtc` / `EffectiveToUtc` (open-ended when `NULL`). New pricing = **new version**, never an UPDATE.
- **Character-count ranges + multipart:** `TariffRate.MinChars/MaxChars` + `PricePerSegment`; parts derive from encoding (GSM-7 160/153, UCS-2 70/67).
- **Future changes:** insert a new `Tariff`, close the prior `EffectiveToUtc`. No fact/schema change.

### 6.2 How historical pricing stays accurate after tariffs change

**Snapshotting, not recomputation.** At submission the engine resolves the applicable tariff (provider + type + encoding + `SubmittedAtUtc` within range) and **copies the price onto the message**. Reporting reads the frozen cost and **never re-resolves tariffs** — so editing/adding a tariff cannot alter a historical cost.

### 6.3 Exact values persisted on `Message` at submission time

| Persisted column | Why it must be frozen |
|---|---|
| `Encoding` | Determines segmentation; recomputation could drift. |
| `CharacterCount` | Source measure for segmentation/cost. |
| `SegmentCount` | The billed unit count. |
| `TariffId` | **Audit trail** — which tariff version priced this. |
| `UnitPrice` | Resolved `PricePerSegment` at submission. |
| `TotalCost` | `UnitPrice × SegmentCount` — the authoritative billed amount. |

Each `Message` row is a **self-contained billing record**: even if `Tariff`/`TariffRate` were dropped, every historical cost remains reproducible.

---

## 7. Storage Optimization Analysis

At 10⁸–10⁹ rows, storage strategy *is* the architecture. Principle: **duplicate small fixed-width keys freely (cheap, kills joins); isolate large/variable text; keep facts append-only so columnstores stay efficient.**

### 7.1 What should be duplicated (denormalized) — and why
- **Dimension keys** (`GeoSectionId`, `ProviderId`, `MessageTypeId`, `CustomerId`) and the **Jalali date** (`SubmitDateJalali`) on the fact — tiny/low-cardinality, **compress superbly in the columnstore**, and remove joins/conversions from every report.
- **Cost snapshot** (`UnitPrice/TotalCost`) — immutability + join-free cost reporting.
- **Caller references** (`ClientCorrelatedId/BillId/PayId`) — idempotency + reconciliation inline; nullable.
- **`SubmitDateJalali` copied onto `DeliveryReport`** — enables partition-wise joins and lockstep retention with `Message`.

### 7.2 What should **not** be duplicated
- **Descriptive names** (geo-section / provider / type) → only in tiny dimensions, joined for labels.
- **The recipient number IS on the fact** (`MobileNumber`) — deliberate; ad-hoc recipients offer nothing to dedupe (~15 bytes/row, accepted).

### 7.3 Message text → **separated, not normalizable**
Distinct text → nothing to dedupe. Separate into `MessageBody` (1:1); apply **`PAGE` + Unicode compression**; bodies are the prime **earliest-purge** candidate.

### 7.4 Delivery status → **append-only events, not in-place updates**
Storing each delivery report as an immutable row (rather than overwriting a status column on the fact) keeps `Message` columnstore-friendly and preserves history. *Cost:* `DeliveryReport` is a second ~billion-row table retained alongside `Message`. *Mitigations:* columnstore compression on cold partitions; lockstep partition retention; and — if success-rate latency demands it — a denormalized `MessageDeliveryStatus` (1 row/message) so the report becomes a simple `GROUP BY` (§9).

### 7.5 Analytics storage: columnstores instead of a pre-aggregate
**Nonclustered columnstore indexes on cold partitions** of `Message` (and `DeliveryReport`) compress ~10× and answer aggregates via batch-mode scans with partition/segment elimination — replacing the removed `MessageDailyAggregate` and its rollup pipeline. *Cost:* report latency scales with the scanned range rather than being constant.

### 7.6 Tradeoff summary

| Decision | Saves | Costs | Verdict |
|---|---|---|---|
| Denormalize dimension keys + Jalali date onto fact | Joins/conversions eliminated; columnstore-friendly | a few bytes/row (compressible) | **Adopt** |
| `SubmitDateJalali` as `CHAR(10)` (readable) | Human-readable dates everywhere | ~6 bytes/row vs `INT` (in CIX + NCIs) | **Adopt** (per requirement) |
| Single `GeoSection` tree vs. three geo tables | One fact key + fewer tables | Rollups join the small tree | **Adopt** |
| `MobileNumber` on fact (no `Subscriber`) | One fewer table + no join | ~15 bytes/row, no dedup | **Adopt** (ad-hoc) |
| Append-only `DeliveryReport` (no in-place status) | Immutable fact; columnstore-friendly; full history | A second ~1B-row table; windowed success-rate | **Adopt** |
| Text in `MessageBody` + compression | Narrow fact; independent retention | 1:1 table; compression CPU | **Adopt** |
| Columnstores for analytics (no cube) | Fewer tables/pipelines | Latency scales with scanned range | **Adopt** |
| Hashed `ApiKey` (no plaintext) | Breach safety | Hash compute per auth | **Adopt** |

---

## 8. Concurrency and Deadlock Prevention

> *Most safety-critical section.* Workload: bursty **concurrent batch inserts** of distinct messages, **append-only DLR inserts**, and concurrent **columnstore reporting reads** — across `Message`, `MessageBody`, `DeliveryReport`.

### 8.1 Partitioning — the foundation
- **`Message`, `MessageBody`, and `DeliveryReport` are range-partitioned by `SubmitDateJalali` (one partition per Jalali month).**
- **Lock escalation is contained to a partition** (`ALTER TABLE … SET (LOCK_ESCALATION = AUTO)`).
- **Current (hot) partitions are rowstore; closed partitions carry columnstores** → inserts and analytics are physically separated.
- **Retention by `SWITCH`/drop** of whole partitions, all three tables in lockstep — no `DELETE` storms.

### 8.2 Clustered index choice (the hot-page problem)
- A naïve **clustered `BIGINT IDENTITY`** funnels every insert to the **same trailing page** → `PAGELATCH_EX` contention. (`SubmitDateJalali` increases monotonically with real time, so the same sequential-key hotspot applies and is mitigated identically.)
- **Decision:** `Message` clustered = **`(SubmitDateJalali, MessageId)`**; `DeliveryReport` clustered = **`(SubmitDateJalali, MessageId, ReceivedAtUtc DESC)`**; both partition-aligned with **`OPTIMIZE_FOR_SEQUENTIAL_KEY = ON`**.
  - `MessageId` remains the **nonclustered** PK on `Message`.
- **Rejected:** clustered GUID (fragmentation); hash-bucket prefix as default (harms range scans/partition elimination).

### 8.3 Nonclustered indexes — minimal *on purpose*
| Index | Justification | Why not more |
|---|---|---|
| `Message CIX (SubmitDateJalali, MessageId)` | Partition alignment + sequential locality | — |
| `Message NCIX (MobileNumber, SubmitDateJalali)` | Recipient-history seeks | Report #5 |
| `Message NCIX (ProviderId, ProviderMessageId)` | Resolve incoming DLR → `MessageId` | DLR ingestion would otherwise scan |
| `Message Filtered NCIX (CustomerId, ClientCorrelatedId) WHERE NOT NULL` | Idempotency + client lookups | Nulls/transactional unindexed |
| `Message Filtered NCIX (BillId) WHERE NOT NULL` | Bill-history seeks | Nulls excluded |
| `DeliveryReport CIX (SubmitDateJalali, MessageId, ReceivedAtUtc DESC)` | Latest-per-message window/seek | — |
| **Nonclustered columnstore on cold partitions** (both facts) | All aggregate reporting | Off hot partitions to protect inserts |

**`PayId` is intentionally not indexed**; no per-dimension reporting NCIs (columnstore covers analytics).

### 8.4 Insert pattern (write strategy)
- **Set-based batch inserts** (TVP / `SqlBulkCopy`), **1,000–5,000 rows per transaction** — under the ~5,000-lock escalation threshold; **short transactions**.
- **Both facts are append-only** → no in-place fact updates to deadlock with; the columnstores on **closed** partitions are built out of band and never contend with current-month inserts.
- **Consistent access order** (Message → MessageBody) for the co-inserted body.

### 8.5 Delivery-report ingestion (append-only — *replaces the old in-place update path*)
- An arriving DLR is matched to its message via `Message NCIX (ProviderId, ProviderMessageId)` (seek), then **inserted** into `DeliveryReport` — **no `UPDATE` of the fact**. This eliminates the previous status-update churn and any insert-vs-update deadlock window entirely.
- Reports for the current Jalali month land in the hot `DeliveryReport` partition (sequential-key mitigation applies); older months are columnstore.

### 8.6 Read pattern (reporting isolation)
- Aggregate/success-rate reads target the **columnstores on cold partitions**; current-month reads hit the rowstore.
- **Enable RCSI** so reporting `SELECT`s use row-versioning and never block writers.

### 8.7 Summary of how each risk is mitigated
| Risk | Mitigation |
|---|---|
| **Last-page insert contention** | `OPTIMIZE_FOR_SEQUENTIAL_KEY`; batched short transactions |
| **Lock escalation** | Partitioning + `LOCK_ESCALATION = AUTO`; batches < 5,000 rows; partition-switch retention |
| **Page contention** | Narrow fixed-width facts; minimal NCIs; `PAGE`/columnstore compression on cold data |
| **Hot partitions** | Jalali-monthly partitioning; rowstore-hot vs. columnstore-cold separation |
| **Reader/writer deadlocks** | Append-only facts (no in-place updates); RCSI reads; out-of-band columnstore builds; consistent write ordering |

---

## 9. Future Evolution

All additive — insert rows / add a nullable column / add a partition / add a table + nullable FK — never a fact rewrite.

| Future need | How it's absorbed |
|---|---|
| **New SMS provider** | Insert into `Provider` (incl. URLs), `SenderLine`, `Tariff`/`TariffRate`; credentials to the secret store. No schema/report change. |
| **More than two provider network paths** | *(Deferred.)* Child `ProviderEndpoint(ProviderId, NetworkType, BaseUrl, Priority, IsActive)` (see §4.4). |
| **Constant-time delivery success rates** | Add a denormalized `MessageDeliveryStatus(MessageId, NormalizedStatus, …)` (1 row/message, upserted from `DeliveryReport`) so report #7 becomes a simple `GROUP BY` — additive; the windowed approach serves until then. |
| **API key scopes / per-message attribution / OAuth** | Add `ApiKeyScope`; add a nullable `ApiKeyId` on `Message`; layer OAuth beside `ApiKey`. |
| **Constant-time cost dashboards over full history** | Re-introduce a `MessageDailyAggregate` rollup (async, post-commit) — additive; columnstore serves until then. |
| **Re-introduce batching / subscribers / templates / date-dimension** | New table + a nullable FK (`BatchId`/`SubscriberId`/`MessageTemplateId`/`DimDate`); existing rows stay `NULL`. |
| **Payment-id lookups** | Add a `Filtered NCIX (PayId)` — additive. |
| **Deeper geography** | Insert `GeoSection` rows at a new `SectionType`; tree + `Path` absorb depth. |
| **Tenant-specific message types** | Nullable `CustomerId` on `MessageType` and/or widen to `SMALLINT`. |
| **Richer delivery states** | Extend the `NormalizedStatus` enum on `DeliveryReport`; no schema change. |
| **Scale beyond a billion** | Jalali-monthly → finer partitions; archive/compress cold partitions via partition switching. |

---

## 10. Design Principles & Tradeoff Summary

Per the explicit mandate — operational reality over normalization purity:

1. **High-volume processing first.** Narrow **insert-only** facts, partition-aligned clustered keys, sequential-key optimization, minimal NCIs, batched short-transaction inserts, append-only DLR ingestion.
2. **Reporting simplicity.** Small dimension keys + denormalized Jalali date + columnstores ⇒ aggregates are partition/segment-eliminated scans; point-lookups are single seeks; delivery success = latest-report-per-message.
3. **Long-term maintainability.** Additive evolution; versioned tariffs; provider-agnostic normalized status; one geo tree; immutable event stream for delivery.
4. **Low storage consumption.** No duplicated names; one geo key; text isolated + compressed; columnstore ~10× for analytics. (Dedup wins and the readable `CHAR(10)` date consciously traded — §7.)
5. **Minimal deadlocks.** Append-only facts (no in-place updates), partition-scoped locking, sub-escalation batches, RCSI reads, out-of-band columnstore builds.
6. **Simple operational support.** Lockstep partition-switch retention across `Message`/`MessageBody`/`DeliveryReport`; self-contained billing rows; hashed API keys; a small, cacheable set of dimensions.
7. **Security.** API keys stored as hashes only; optional per-key CIDR allow-listing aligned with the internet/intranet access model.

**Every major tradeoff was resolved in favor of write throughput, report simplicity, storage economy, and security — documented where it arises (§4 alternatives, §7 storage, §8 concurrency).**

---

### Open decisions for reviewers

1. **Delivery model** — confirm the **append-only `DeliveryReport` + latest-per-message** approach, or whether a denormalized `MessageDeliveryStatus` (constant-time success rates) is wanted **now** rather than deferred.
2. **`DeliveryReport` retention** — confirm it should be retained in lockstep with `Message` (full delivery history), vs. a shorter window.
3. **Body retention window** — confirm shorter retention for `MessageBody` than `Message` is legally acceptable.
4. **`MessageType` scope** — single global type-merged dimension sufficient, or tenant-specific purposes now?
5. **API key model** — hashed `ApiKey` + optional `ApiKeyIpRestriction` sufficient, or scopes/per-message attribution now?
6. **Partition cadence** — Jalali-monthly proposed; confirm vs. weekly given peak daily volumes.
