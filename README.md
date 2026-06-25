# SmsHubNext — Database & Storage Architecture Design

> **Status:** Design proposal — *for architectural review*
> **Scope of this document:** Data model and storage architecture **only**. No application, transport (RabbitMQ), provider integration, or API code is described here. The goal is to **validate the data model before any implementation begins.**
> **Target engine:** **Microsoft SQL Server** (2019+). The requirements explicitly reference *lock escalation, page contention, clustered/nonclustered indexes,* and *hot partitions* — all SQL Server concepts — and the platform is built on .NET. The logical model remains portable; the physical tuning notes are SQL-Server-specific and are flagged as such.
> **Currency:** Iranian Rial (IRR). **Calendar:** Reporting periods are expressed in the **Jalali (Persian) calendar** (e.g. *Spring 1405*); storage is UTC, and the Jalali period parts (`PersianYear`, `PersianMonth`) are **denormalized onto the `Message` fact** so period reporting is sargable — there is no date-dimension table and no pre-aggregate cube.
> **Sending model:** Every message carries its **own distinct, fully-rendered text** (the caller supplies the final body per message). There is **no shared template, no merge-variable rendering, and no campaign/batch grouping**; recipients are **ad-hoc** (not a managed subscriber base). Customers authenticate sends with **API keys**.

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
| **Asynchronous delivery reports (DLR)** | Each sent message later receives 1+ status updates from the provider, arriving minutes-to-hours later. | High-volume **update** traffic against already-written rows. |
| **Heavy reporting/analytics** | Finance and operations run aggregate reports by geo section / provider / message type / Jalali period. | Read path competes with writes → served by a **columnstore on cold partitions**, isolated from the OLTP hot path. |

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
- **History of a single recipient** (by mobile number) and **history of a single bill** (by bill id).
- **Monthly cost trends.**
- **Top provinces by spend.**

Reports are predominantly **aggregations over large ranges** plus a few **point-lookups** (recipient/bill history). These two access shapes are in tension and are handled differently (see §5, §8).

### 1.4 Historical data requirements

- **Cost and delivery facts must remain immutable and accurate forever** (or for the legally mandated retention period) — *even after tariffs change*. This forces **price snapshotting** (see §6).
- Cold fact partitions, compressed under a **columnstore**, retain full history cheaply and answer historical analytics directly.
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
| **Customer (Tenant / Customer Context)** | The organization that owns the traffic and is billed (e.g. a regional water company). Top-level ownership and isolation boundary. **Sends** messages. |
| **API Key** | The credential a customer presents to authenticate send requests. A customer may hold several (rotation, per-system); only a **hash** is stored. |
| **API Key IP Restriction** | Optional allow-list of source networks (CIDR) for a key — relevant to internet-vs-intranet access paths. |
| **Provider** | An SMS carrier/aggregator (Magfa, …). Owns sender lines and tariffs; source of delivery reports. |
| **Sender Line** | A specific origin number (`3000…`, `1000…`, `4040…`) belonging to a provider. Affects routing, pricing, and operator reachability. |
| **Message Type** | Single classification axis — both *delivery class* (OTP / Transactional / Bulk) and *business purpose* (Water Bill, Outage Alert, …). |
| **Message** | The central **fact**: one SMS send, with its own distinct text reference, recipient number, client/business references, Jalali period parts, cost snapshot, and delivery status. The highest-volume table. |
| **Message Body** | The exact sent text for that one message. Physically separated from the fact (1:1) for storage/retention. |
| **Recipient** | The end party receiving the SMS — a **`MobileNumber` on the message** (ad-hoc; not a managed dimension). |
| **Client / Business references** | Caller-supplied identifiers on the message: `ClientCorrelatedId` (caller's id / idempotency key), `BillId`, `PayId`. |
| **Tariff** | Time-bounded pricing for a (provider, encoding, message-type) combination. Versioned by effective date range. |
| **Delivery Report** | The provider's eventual status. Normalized status folded onto the Message; raw codes optionally retained for audit. |
| **Geo Section** | A single self-referencing geographic dimension (Province → City → Zone → …) via a parent link + `SectionType`. |

**Fact vs. Dimension split is the spine of this design:** small, stable **dimensions** (customer, api-key, provider, line, message-type, geo section, tariff) are referenced by surrogate keys; the enormous **Message fact** carries small dimension keys + recipient + caller references + Jalali parts so reports group/filter without joining giant tables, and carries a frozen cost snapshot so history is immutable. **Aggregate analytics run on a columnstore over cold fact partitions** rather than a pre-built cube.

> **What was removed and why:** earlier drafts included `MessageTemplate` + merge variables, a `Campaign` grouping, a managed `Subscriber` base, a `DimDate` dimension, and a `MessageDailyAggregate` cube. Distinct text + ad-hoc recipients made the first three pointless; `DimDate` and the cube were dropped in favor of **Jalali parts on the fact** + a **columnstore** for analytics. All are re-introducible additively (see §9).

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
| 10 | **`Message`** | **Fact (hot)** | **~10M** | **~120M** | **~0.6–1B** |
| 11 | `MessageBody` | Fact satellite (text) | ~10M | ~120M | ~0.6–1B |
| 12 | `DeliveryReportLog` | Optional audit (raw DLR) | ~12M | ~150M | ~1B+ |

> Below, each table is summarized against the required facets. Full column-level detail is in **§4**.

### Dimension tables (1–9) — shared facets

- **Purpose:** Provide stable, deduplicated reference data referenced by the Message fact via surrogate keys.
- **Business justification:** Eliminate repetition of descriptive text across ~10⁹ fact rows; enable consistent grouping/filtering; authenticate and authorize senders (`ApiKey`).
- **Read pattern:** Tiny lookups; frequently cached in the application. `ApiKey` is read on **every send** (by key hash) — a hot but trivially-indexed point lookup.
- **Write pattern:** Rare inserts/updates (admin/onboarding/key rotation).
- **Retention:** Effectively permanent. Versioned dimensions (`Tariff`, `TariffRate`) keep history via effective-date ranges; revoked keys are kept (audit), not deleted.
- **Storage:** Negligible relative to the fact.

### 10. `Message` — the central fact (hot path)

- **Purpose:** One row per SMS send. The system of record for what was sent, to which number, by which line/provider, at what cost, with what delivery outcome, and against which client/business references.
- **Business justification:** Every report, cost calculation, and history/reconciliation lookup resolves here.
- **Expected volume:** 10M (1mo) → 120M (1yr) → up to ~1B (5yr).
- **Read pattern:** (a) **range aggregations** for reporting — served by a **nonclustered columnstore on cold partitions** (batch mode + partition/segment elimination); (b) **point lookups** by recipient number, bill id, client correlation id, provider message id.
- **Write pattern:** Massive **concurrent batch inserts** of distinct messages into the rowstore; high-volume **single-column status updates** when DLRs arrive. Designed to minimize insert hot-spotting and update-time index churn (see §8).
- **Retention:** Long (billing/legal). Aged out by **partition switching** by month, not by row-level `DELETE`.
- **Storage:** Kept **narrow** (fixed-width keys + recipient + references + Jalali parts + cost snapshot + status; **no message text**) so the rowstore packs rows per page and the columnstore compresses hard. Text lives in `MessageBody`.

### 11. `MessageBody` — text satellite (1:1 with Message)

- **Purpose:** Hold the exact distinct text of that one message, separate from the narrow fact.
- **Business justification:** Audit/legal proof of content without bloating the fact.
- **Volume:** 1:1 with `Message`.
- **Read pattern:** Rare — only when an operator inspects an individual message.
- **Write pattern:** Inserted alongside the message (same batch). Immutable afterward.
- **Retention:** **Shorter** than the fact where policy allows — purge the body partition earlier to reclaim the bulk of storage.
- **Storage:** Largest per-row cost and **non-deduplicable** (each text distinct). Isolated for compression + independent retirement (see §7).

### 12. `DeliveryReportLog` — optional raw DLR audit

- **Purpose:** Append-only log of raw provider status callbacks/poll results.
- **Business justification:** Forensic/audit trail and reprocessing; the *normalized* current status already lives on `Message`.
- **Volume:** ≥ message volume.
- **Read pattern:** Rare, point lookups by message; mostly write-only.
- **Write pattern:** Append-only inserts.
- **Retention:** **Short** (e.g. 30–90 days).
- **Storage:** Justified only if audit/reprocessing is required; **off by default** (see §4.12). The normalized status on `Message` is the source of truth for reporting.

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

**Why a separate table (not a column on `Customer`):** a customer holds **many** keys over time — rotation, one per integrating system/environment — each with its own lifecycle (active/expired/revoked). That is a clean one-to-many, impossible to model as a single column.

**Why store only a hash:** if the database is ever exposed, **plaintext keys must not leak**. We store `SHA-256(key)` and authenticate by hashing the presented key and seeking `KeyHash`. The `KeyPrefix` (e.g. first chars of the key) is non-secret and lets operators identify a key in logs/UI without revealing it. *Alternatives:* (a) store the key in plaintext/encrypted — rejected (plaintext is a breach liability; encryption is unnecessary since we never need to recover the original); (b) JWT/OAuth client-credentials — heavier; API keys are the simplest correct fit for server-to-server SMS submission and OAuth can be layered later (§9). Per-minute throttle limits live in the application/cache layer, not as columns here.

### 4.3 `ApiKeyIpRestriction` (optional)
| Column | Type | Notes |
|---|---|---|
| `ApiKeyIpRestrictionId` | `INT IDENTITY` | **PK**, `CIX` |
| `ApiKeyId` | `INT` | **FK** → `ApiKey`; `NCIX` |
| `Cidr` | `VARCHAR(43)` | allowed source range (IPv4/IPv6 CIDR) |
| `Description` | `NVARCHAR(100)` | nullable |

**Why this exists (and is optional):** governmental/enterprise customers reach the platform over **specific networks** — the public internet and/or an intranet gateway (the same dual-path reality that shaped `Provider.BaseUrl`/`FallbackBaseUrl`). Binding a key to known source CIDRs is a strong, cheap defense-in-depth control. It's a child table (a key may allow several ranges) and is **omitted entirely** for keys that need no restriction. *Alternative:* a single delimited column on `ApiKey` — rejected (not queryable/extensible; multiple ranges are natural rows).

### 4.4 `Provider`
| Column | Type | Notes |
|---|---|---|
| `ProviderId` | `TINYINT IDENTITY` | **PK**, `CIX` |
| `Name` | `NVARCHAR(100)` | e.g. "Magfa" |
| `Code` | `VARCHAR(50)` | `NCIX` unique |
| `BaseUrl` | `VARCHAR(300)` | primary API endpoint (e.g. public-internet gateway) |
| `FallbackBaseUrl` | `VARCHAR(300)` | secondary endpoint over a different network path (e.g. intranet/private gateway); nullable |
| `IsActive` | `BIT` | |

**Why:** Provider count is tiny → `TINYINT`. New providers = new rows, never schema change. *Alternative considered:* provider as a string enum on the fact — rejected (4–6 bytes × 10⁹ and no referential integrity).

**Endpoints live here (provider *info*, not reporting); credentials do not.** `Provider` is a small (<50-row) **entity/info** table that is *also* referenced by the fact — but the "keep it lean for reporting" rule applies to the billion-row fact (which only ever stores `ProviderId`), **not** to this table. In Iranian governmental/enterprise deployments the **same provider is reached over more than one network path simultaneously** (public internet **and** an intranet/private gateway) with automatic failover — runtime data the app reads, **not** per-environment `appsettings`. We model it as `BaseUrl` (primary) + `FallbackBaseUrl` (secondary). **Credentials** (`username/domain/password`) are deliberately **excluded** — URLs aren't secret, credentials are, so they stay in the secret store keyed by `Provider.Code`. *Deferred alternative:* a child `ProviderEndpoint` table for **three or more** paths with explicit failover order (§9).

### 4.5 `SenderLine`
| Column | Type | Notes |
|---|---|---|
| `SenderLineId` | `SMALLINT IDENTITY` | **PK**, `CIX` |
| `ProviderId` | `TINYINT` | **FK** → `Provider` |
| `LineNumber` | `VARCHAR(20)` | `3000…`, `1000…`, `4040…`; `NCIX` |
| `IsSharedLine` | `BIT` | shared (public) line vs. dedicated (private) number |
| `IsActive` | `BIT` | |

**Why:** Lines have distinct pricing/reachability and belong to a provider. Surrogate `SMALLINT` keeps the fact FK small. *Alternative:* storing the raw line string on each message — rejected (repetition + no metadata). The shared-vs-dedicated distinction is genuinely **binary**, so it is a `BIT` (`IsSharedLine`) rather than a `TINYINT` type code — simpler and self-documenting. (Number-class nuances like `3000`/`1000`/`021` are derivable from `LineNumber`; if a true third line *class* emerges, promote to a `TINYINT`/lookup.)

### 4.6 `MessageType`
| Column | Type | Notes |
|---|---|---|
| `MessageTypeId` | `TINYINT` | **PK** (seeded), `CIX` |
| `Name` | `NVARCHAR(80)` | "OTP", "Transactional", "Bulk", "Water Bill", "Outage Alert", … |
| `Code` | `VARCHAR(50)` | `NCIX` |

**Why:** The **single classification axis** for a message — both *delivery class* (OTP/Transactional/Bulk) and *business purpose* (Water Bill, …). Kept **global and `TINYINT`** for simplicity. *Alternative considered:* a separate tenant-scoped `BusinessCategory` dimension + a second FK — rejected (extra table/key/join for a distinction not currently needed). *Alternative:* a `BIT IsOtp` flag — rejected (not extensible). **Future-proofing (additive):** add a nullable `CustomerId` and/or widen to `SMALLINT`.

### 4.7 `GeoSection` (self-referencing geographic hierarchy)
| Column | Type | Notes |
|---|---|---|
| `GeoSectionId` | `INT IDENTITY` | **PK**, `CIX` |
| `ParentGeoSectionId` | `INT` | **FK** → `GeoSection` (self); `NULL` at the top level (province) |
| `SectionType` | `TINYINT` | 1 = Province, 2 = City, 3 = Zone (extensible to more levels) |
| `Name` | `NVARCHAR(100)` | |
| `Code` | `VARCHAR(20)` | `NCIX` |
| `Path` | `VARCHAR(900)` | materialized ancestor path, e.g. `/12/450/8123/`; `NCIX` for fast subtree filters |
| `IsActive` | `BIT` | |

**Why one self-referencing table instead of three (`Province`/`City`/`Zone`):** a strict hierarchy modeled as a single **adjacency-list** table collapses three tables into one while preserving rollups. Deeper levels are data inserts, not schema changes. The denormalized **`Path`** makes "everything under Tehran province" a single sargable `Path LIKE '/<TehranId>/%'` filter. The fact stores a **single** `GeoSectionId` (the most-specific section the caller tagged); reports roll it up by joining the small `GeoSection` tree. *Alternatives:* three geo tables (superseded); a flat non-hierarchical tag (rejected — reports need rollups); SQL Server `HIERARCHYID` (valid; `Path` chosen for transparency/portability).

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
| `MaxChars` | `SMALLINT` | character-range upper bound (nullable = ∞) |
| `PricePerSegment` | `DECIMAL(19,4)` | |

**Why two tables:** the **header** carries the validity window and applicability; the **detail** carries price banding by character range / segment. Separates "*which tariff applies*" from "*how much*", and lets a version own multiple bands without nullable sprawl. *Alternatives:* one wide tariff row (inflexible); price in app config (prices must be auditable data). **Tariff tables are never used at report time** — the resolved price is frozen onto the message (see §6).

### 4.10 `Message` — the fact (most-scrutinized table)
| Column | Type | Notes |
|---|---|---|
| `MessageId` | `BIGINT IDENTITY` | **PK** (nonclustered — see §8) |
| `SubmitDateKey` | `INT` | **partition column** (`yyyymmdd`, Gregorian); part of `CIX`. No FK. |
| `SubmittedAtUtc` | `DATETIME2(3)` | precise timestamp |
| `PersianYear` | `SMALLINT` | Jalali year (e.g. 1405) — denormalized for period reporting |
| `PersianMonth` | `TINYINT` | Jalali month 1–12 (season = month-range; Spring = 1–3) |
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
| `Status` | `TINYINT` | normalized: Queued/Submitted/Sent/Delivered/Failed/Expired/Unknown |
| `ProviderMessageId` | `VARCHAR(50)` | provider's id, for DLR matching |
| `StatusUpdatedAtUtc` | `DATETIME2(3)` | last DLR application |

**Primary/clustered/indexes (justified in §8):**
- **PK:** `MessageId` — **nonclustered**, unique.
- **CIX (clustered):** `(SubmitDateKey, MessageId)` — aligns with monthly **partitioning**.
- **NCIX 1:** `(MobileNumber, SubmitDateKey)` — recipient history.
- **NCIX 2:** `(ProviderId, ProviderMessageId)` — DLR matching/update path.
- **NCIX 3 (filtered):** `(CustomerId, ClientCorrelatedId) WHERE ClientCorrelatedId IS NOT NULL` — idempotency + client lookups.
- **NCIX 4 (filtered):** `(BillId) WHERE BillId IS NOT NULL` — bill history.
- **Nonclustered columnstore on cold partitions** — **the reporting engine** (replaces the former aggregate cube). `PayId` is not indexed by default.

**Why this structure is preferred:**
- **Narrow + (mostly) fixed-width:** no text on the fact → packs the rowstore and compresses the columnstore. `MobileNumber`/references are short.
- **Small dimension keys + denormalized Jalali parts** (`PersianYear/PersianMonth`): period and dimension reports filter/group **without** a date dimension or a join to giant tables.
- **Caller references** (`ClientCorrelatedId/BillId/PayId`): idempotent submission + reconciliation without a lookup table; nullable.
- **Frozen cost snapshot:** historical accuracy independent of later tariff edits.
- **Normalized `Status` + `ProviderMessageId`:** provider-agnostic reporting + efficient DLR updates.

**Alternatives considered & rejected:**
- *`MessageDailyAggregate` cube:* removed — aggregate reports now run on the columnstore. (Re-introducible if dashboards need constant-time reads — §9.) *Tradeoff:* report latency now scales with scanned range, not total size.
- *`DimDate` FK:* dropped — Jalali parts are columns here; `SubmitDateKey` is just a partition key.
- *`Subscriber`/`CampaignId`/`MessageTemplateId`:* dropped — ad-hoc recipients, no batching, no templates.
- *Separate `Bill`/`Payment` tables:* not needed — `BillId`/`PayId` are external references, not owned entities.
- *Cost computed at report time:* rejected — breaks historical accuracy.
- *Message text inline / `UNIQUEIDENTIFIER` key:* rejected — fact bloat / fragmentation.

### 4.11 `MessageBody`
| Column | Type | Notes |
|---|---|---|
| `MessageId` | `BIGINT` | **PK = FK** → `Message` (1:1), `CIX`, partition-aligned on `SubmitDateKey` |
| `SubmitDateKey` | `INT` | partition column (aligned with `Message`) |
| `Body` | `NVARCHAR(MAX)` | the exact distinct text that was sent |

**Why a separate 1:1 table:** the text is variable-length and large, the fact is fixed-width and hot. Keeping `NVARCHAR(MAX)` off the fact preserves rows-per-page and columnstore compression on the fact, and lets the body follow its **own shorter retention** + **`PAGE`/Unicode compression**. *Alternatives:* inline `Body` on `Message` (couples hot fact to cold text); old `TemplateId`+`MergeVariables` shape (no templates now).

### 4.12 `DeliveryReportLog` (optional, off by default)
| Column | Type | Notes |
|---|---|---|
| `DeliveryReportLogId` | `BIGINT IDENTITY` | **PK**, `CIX` |
| `MessageId` | `BIGINT` | **FK**; `NCIX` |
| `RawStatusCode` | `INT` | provider-native code |
| `NormalizedStatus` | `TINYINT` | |
| `ReceivedAtUtc` | `DATETIME2(3)` | |

**Why optional:** the **current** normalized status already lives on `Message`. A full per-event log **at least doubles the row count of the largest table**. **Decision:** enable only for forensic audit / DLR reprocessing; if enabled, give it **short retention** (30–90 days) + its own partitioning. *Default: disabled.*

---

## 5. Reporting Validation

For each report: **required tables**, **strategy**, **performance**. Aggregate reports now run on the **nonclustered columnstore over cold `Message` partitions** (the current month is rowstore); point-lookups hit a targeted nonclustered index.

| # | Report | Required objects | Strategy | Performance |
|---|---|---|---|---|
| 1 | **Cost for Tehran province, Spring 1405, water bills** | `Message` (columnstore) + `GeoSection` + `MessageType` | Filter `PersianYear=1405 AND PersianMonth BETWEEN 1 AND 3`; restrict geo via join to `GeoSection.Path LIKE '/<Tehran>/%'`; filter `MessageTypeId=<Water Bill>`; `SUM(TotalCost)` | Columnstore batch scan with **segment elimination** on `PersianYear/Month` + partition elimination; small geo join. Sub-second–seconds. |
| 2 | **Cost by provider** | `Message` (columnstore) + `Provider` | `GROUP BY ProviderId`, join names | Columnstore aggregate; fast. |
| 3 | **Count by city** | `Message` (columnstore) + `GeoSection` | Join fact→`GeoSection`, roll zone→city ancestor via `Path`/parent, `GROUP BY` city | Columnstore scan + small hash join; seconds. |
| 4 | **Count by zone** | `Message` (columnstore) + `GeoSection` | `GROUP BY GeoSectionId` (leaf=zone) | Columnstore aggregate; fast. |
| 5 | **History of a recipient (mobile number)** | `Message` + `MessageBody` | Point lookup `NCIX (MobileNumber, SubmitDateKey)`; optional body join | Index seek; ms. |
| 6 | **History of a bill** | `Message` (+`MessageBody`) | Seek `filtered NCIX (BillId)` | Index seek; ms. |
| 7 | **Delivery success rate by provider** | `Message` (columnstore) | `GROUP BY ProviderId, Status` → ratio | Columnstore aggregate; fast. |
| 8 | **Monthly cost trend** | `Message` (columnstore) | `GROUP BY PersianYear, PersianMonth` | Columnstore aggregate on denormalized Jalali parts; fast. |
| 9 | **Top provinces by spend** | `Message` (columnstore) + `GeoSection` | Join + roll up to province via `Path`; `GROUP BY` province `ORDER BY SUM(TotalCost) DESC` | Columnstore scan + small join. |

**Key validation outcome:** aggregate reports are served by **batch-mode columnstore scans** with **partition + segment elimination** on `SubmitDateKey`/`PersianYear`/`PersianMonth` — so cost scales with the **scanned range**, not the full table. Jalali period filtering needs no date dimension (parts are columns). Point-lookups (recipient/bill history, client correlation) are single nonclustered index seeks. **Tradeoff vs. the removed cube:** no constant-time aggregate; if sub-second dashboards over the *entire* billion-row history are later required, reintroduce a rollup (§9).

---

## 6. Tariff and Pricing Design

### 6.1 Structure

```
Provider ──< Tariff (versioned by EffectiveFrom/EffectiveTo, per Encoding/MessageType) ──< TariffRate (per character-range band → PricePerSegment)
```

- **Multiple providers:** `Tariff.ProviderId`.
- **Historical tariffs + effective ranges:** `EffectiveFromUtc` / `EffectiveToUtc` (open-ended when `NULL`). New pricing = **new tariff version**, never an UPDATE.
- **Character-count ranges + multipart:** `TariffRate.MinChars/MaxChars` + `PricePerSegment`; parts derive from encoding (GSM-7 160/153, UCS-2 70/67).
- **Future changes:** insert a new `Tariff` row, close the prior `EffectiveToUtc`. No fact/schema change.

### 6.2 How historical pricing stays accurate after tariffs change

**Snapshotting, not recomputation.** At submission the engine resolves the applicable tariff (provider + type + encoding + `SubmittedAtUtc` within range) and **copies the price onto the message**. Reporting reads the frozen cost and **never re-resolves tariffs** — so editing/adding a tariff tomorrow cannot alter a historical cost.

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

At 10⁸–10⁹ rows, storage strategy *is* the architecture. Principle: **duplicate small fixed-width keys freely (cheap, kills joins); isolate large/variable text.** This design consciously **gave up two dedup wins** — template dedup (distinct text) and recipient dedup (ad-hoc) — and replaced a pre-aggregate cube with a **columnstore**.

### 7.1 What should be duplicated (denormalized) — and why
- **Dimension keys (`GeoSectionId`, `ProviderId`, `MessageTypeId`, `CustomerId`)** and **Jalali parts (`PersianYear`, `PersianMonth`)** on the fact — tiny, low-cardinality, and they **compress superbly in the columnstore** (dictionary encoding) while removing joins/conversions from every report.
- **Cost snapshot** (`UnitPrice/TotalCost`) — immutability + join-free cost reporting.
- **Caller references** (`ClientCorrelatedId/BillId/PayId`) — idempotency + reconciliation inline; nullable.

### 7.2 What should **not** be duplicated
- **Descriptive names** (geo-section / provider / type names) → only in tiny dimensions, joined for labels.
- **The recipient number IS on the fact** (`MobileNumber`) — deliberate, since ad-hoc recipients offer nothing to dedupe. ~15 bytes/row, accepted (§7.4).

### 7.3 Should message text be normalized / stored separately? → **Separated, not normalizable**
Distinct text → nothing to dedupe. Separate into `MessageBody` (1:1); apply **`PAGE` + Unicode compression**; bodies are the prime **earliest-purge** candidate.

### 7.4 Should recipients be separated from messages? → **No (ad-hoc)**
Store `MobileNumber` on the fact. *Tradeoff:* repeats (~15 bytes × 10⁹), no entity record — but avoids a 25M-row dimension and a per-message join; "recipient history" stays a single indexed seek. Re-introducible additively (§9).

### 7.5 Analytics storage: columnstore instead of a pre-aggregate
A **nonclustered columnstore index on cold partitions** compresses the fact ~10× and answers aggregate reports via batch-mode scans with partition/segment elimination. This replaces the removed `MessageDailyAggregate` and its async rollup pipeline — **fewer moving parts**, at the cost of report latency that scales with the scanned range rather than being constant.

### 7.6 Tradeoff summary

| Decision | Saves | Costs | Verdict |
|---|---|---|---|
| Denormalize dimension keys + Jalali parts onto fact | Joins/conversions eliminated; columnstore-friendly | ~9 bytes/row (compressible) | **Adopt** |
| Single `GeoSection` tree vs. three geo tables | One fact key + fewer tables | Rollups join the small tree | **Adopt** |
| `MobileNumber` on fact (no `Subscriber`) | One fewer table + no join | ~15 bytes/row, no dedup | **Adopt** (ad-hoc) |
| Caller references inline | Idempotency + reconciliation, no join | A few nullable columns | **Adopt** |
| Text in `MessageBody` + compression | Narrow fact; independent retention | 1:1 table; compression CPU | **Adopt** |
| Columnstore for analytics (no cube) | Fewer tables/pipelines | Latency scales with scanned range | **Adopt** |
| Hashed `ApiKey` (no plaintext) | Breach safety | Hash compute per auth | **Adopt** |
| Full per-event `DeliveryReportLog` | Forensic audit | **Doubles largest table** | **Default off** |

---

## 8. Concurrency and Deadlock Prevention

> *Most safety-critical section.* Workload: bursty **concurrent batch inserts** of distinct messages + high-volume **status updates** (DLRs) + concurrent **columnstore reporting reads** — all on the same fact family.

### 8.1 Partitioning — the foundation
- **`Message` and `MessageBody` are range-partitioned by `SubmitDateKey` (monthly).**
- **Lock escalation is contained to a partition** (`ALTER TABLE … SET (LOCK_ESCALATION = AUTO)`); a reporting scan on an old month can't block inserts into the current month.
- **The current (hot) partition is rowstore-only; closed partitions carry the columnstore** for analytics — so inserts and analytics are physically separated.
- **Retention by `SWITCH`/drop** of whole partitions — no `DELETE` storms.

### 8.2 Clustered index choice (the hot-page problem)
- A naïve **clustered `BIGINT IDENTITY`** funnels every insert to the **same trailing page** → `PAGELATCH_EX` "last-page insert" contention.
- **Decision:** clustered index = **`(SubmitDateKey, MessageId)`**, partition-aligned, **`OPTIMIZE_FOR_SEQUENTIAL_KEY = ON`** (SQL Server 2019+).
  - `MessageId` remains the **nonclustered** PK.
- **Rejected:** clustered GUID (fragmentation/page density); hash-bucket prefix as default (harms range scans/partition elimination — revisit only under proven contention).

### 8.3 Nonclustered indexes — kept minimal *on purpose*
| Index | Justification | Why not more |
|---|---|---|
| `CIX (SubmitDateKey, MessageId)` | Partition alignment + sequential locality | — |
| `NCIX (MobileNumber, SubmitDateKey)` | Recipient-history seeks | Report #5 |
| `NCIX (ProviderId, ProviderMessageId)` | **DLR update path** — seek to apply status | Updates would otherwise scan |
| `Filtered NCIX (CustomerId, ClientCorrelatedId) WHERE NOT NULL` | Idempotency + client lookups | Filtered → null/transactional traffic unindexed |
| `Filtered NCIX (BillId) WHERE NOT NULL` | Bill-history seeks | Nulls excluded |
| **Nonclustered columnstore on cold partitions** | **All aggregate reporting** | Off the hot partition to protect inserts |

**`PayId` is intentionally not indexed**; there are **no per-dimension reporting NCIs** (the columnstore covers analytics), so the insert path stays lean.

### 8.4 Insert pattern (write strategy)
- **Set-based batch inserts** (TVP / `SqlBulkCopy`), **1,000–5,000 rows per transaction** — under the ~5,000-lock escalation threshold.
- **Short transactions** → minimal lock duration, fewer deadlocks.
- **Consistent access order** across write paths (Message → MessageBody).
- The columnstore on **closed** partitions is built/rebuilt out of band, so it never contends with current-month inserts. (No async-aggregate writer to deadlock with — the cube is gone.)

### 8.5 Update pattern (DLR application)
- Locate rows by `NCIX (ProviderId, ProviderMessageId)` (seek) and write only `Status` + `StatusUpdatedAtUtc` — a narrow update on a column in **no index**, so no row movement / index churn.

### 8.6 Read pattern (reporting isolation)
- Aggregate reads target the **columnstore on cold partitions**; current-month reads hit the rowstore.
- **Enable RCSI** so reporting `SELECT`s use row-versioning and never block writers — directly attacking reader/writer deadlocks.

### 8.7 Summary of how each risk is mitigated
| Risk | Mitigation |
|---|---|
| **Last-page insert contention** | `OPTIMIZE_FOR_SEQUENTIAL_KEY`; batched short transactions |
| **Lock escalation** | Partitioning + `LOCK_ESCALATION = AUTO`; batches < 5,000 rows; partition-switch retention |
| **Page contention** | Narrow fixed-width fact; minimal NCIs; `PAGE`/columnstore compression on cold data |
| **Hot partitions** | Monthly partitioning; rowstore-hot vs. columnstore-cold separation |
| **Reader/writer deadlocks** | RCSI snapshot reads; out-of-band columnstore builds; consistent write ordering |

---

## 9. Future Evolution

All additive — insert rows / add a nullable column / add a partition / add a table + nullable FK — never a fact rewrite.

| Future need | How it's absorbed |
|---|---|
| **New SMS provider** | Insert into `Provider` (incl. URLs), `SenderLine`, `Tariff`/`TariffRate`; credentials to the secret store. No schema/report change. |
| **More than two provider network paths** | *(Deferred.)* Escalate to a child `ProviderEndpoint(ProviderId, NetworkType, BaseUrl, Priority, IsActive)` (see §4.4). |
| **API key scopes / permissions** | Add an `ApiKeyScope(ApiKeyId, Scope)` table; today every key implies "send". |
| **Per-message key attribution** | Add a nullable `ApiKeyId` on `Message` for forensic "which key sent this" — additive. |
| **OAuth / JWT auth** | Layer alongside `ApiKey`; the auth table model is unaffected. |
| **Constant-time dashboards over full history** | Re-introduce a `MessageDailyAggregate` rollup (async, post-commit) — additive; the columnstore keeps serving until then. |
| **Re-introduce batching / subscribers / templates / date-dimension** | Each returns as a new table + a nullable FK (`BatchId`/`SubscriberId`/`MessageTemplateId`/`DimDate`); existing rows stay `NULL`. |
| **Payment-id lookups** | Add a `Filtered NCIX (PayId)` — additive. |
| **Deeper geography** | Insert `GeoSection` rows at a new `SectionType`; tree + `Path` absorb depth. |
| **Tenant-specific message types** | Nullable `CustomerId` on `MessageType` and/or widen to `SMALLINT`. |
| **New delivery-report mechanism** | Extend the normalized `Status` enum; `DeliveryReportLog` (if on) captures new raw codes. |
| **Scale beyond a billion** | Monthly→weekly partitions; archive/compress cold partitions via partition switching. |

---

## 10. Design Principles & Tradeoff Summary

Per the explicit mandate — operational reality over normalization purity:

1. **High-volume processing first.** Narrow fixed-width fact, partition-aligned clustered key, sequential-key optimization, minimal NCIs, batched short-transaction inserts.
2. **Reporting simplicity.** Small dimension keys + denormalized Jalali parts on the fact + a **columnstore** ⇒ aggregate reports are batch-mode scans with partition/segment elimination; point-lookups are single seeks.
3. **Long-term maintainability.** Additive evolution; versioned tariffs; provider-agnostic normalized status; one geo tree; **fewer tables** after removing templates/campaigns/subscribers/date-dimension/cube.
4. **Low storage consumption.** No duplicated names; one geo key; text isolated + compressed; columnstore ~10× compression for analytics. (Dedup wins consciously traded for correctness/simplicity — §7.)
5. **Minimal deadlocks.** Partition-scoped locking, sub-escalation batches, RCSI reads, out-of-band columnstore builds, consistent write ordering.
6. **Simple operational support.** Retention via partition switching; self-contained billing rows; hashed API keys; a small, cacheable set of dimensions.
7. **Security.** API keys stored as hashes only; optional per-key CIDR allow-listing aligned with the internet/intranet access model.

**Every major tradeoff was resolved in favor of write throughput, report simplicity, storage economy, and security — documented where it arises (§4 alternatives, §7 storage, §8 concurrency).**

---

### Open decisions for reviewers

1. **`DeliveryReportLog`** — confirm whether forensic/raw-DLR audit is required (default: **off**).
2. **Reporting latency** — confirm columnstore-on-fact is acceptable, or whether constant-time dashboards justify re-introducing a `MessageDailyAggregate` rollup now (§9).
3. **Body retention window** — confirm shorter retention for `MessageBody` than `Message` is legally acceptable.
4. **`MessageType` scope** — single global type-merged dimension sufficient, or tenant-specific purposes needed now?
5. **API key model** — confirm hashed `ApiKey` + optional `ApiKeyIpRestriction` is sufficient, or whether scopes/per-message attribution are needed now.
6. **Partition cadence** — monthly proposed; confirm vs. weekly given peak daily volumes.
