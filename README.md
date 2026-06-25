# SmsHubNext — Database & Storage Architecture Design

> **Status:** Design proposal — *for architectural review*
> **Scope of this document:** Data model and storage architecture **only**. No application, transport (RabbitMQ), provider integration, or API code is described here. The goal is to **validate the data model before any implementation begins.**
> **Target engine:** **Microsoft SQL Server** (2019+). The requirements explicitly reference *lock escalation, page contention, clustered/nonclustered indexes,* and *hot partitions* — all SQL Server concepts — and the platform is built on .NET. The logical model remains portable; the physical tuning notes are SQL-Server-specific and are flagged as such.
> **Naming:** every table's own primary key is `Id`; a foreign key is named after the table it references (`CustomerId`, `ProviderId`, `MessageId`, …).
> **Currency:** Iranian Rial (IRR). **Calendar:** Reporting periods are Jalali (e.g. *Spring 1405*); the precise instant is UTC (`SubmittedAtUtc`), and **`SubmitDateJalali`** (`CHAR(10)`, e.g. `1405/01/03`) is the **partition key, clustered date key, and sargable period filter** (fixed-width zero-padded slashes sort chronologically). No date dimension, no pre-aggregate cube.
> **Delivery model:** `Message` carries a **denormalized current `DeliveryStatus`** (a read model updated in place only while a partition is hot); the full status history is the **append-only `DeliveryReport`** stream. Customers authenticate sends with **API keys**.

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

SmsHubNext is a **high-volume, multi-tenant SMS dispatch and accounting platform**. Primary use case: **utility/organizational notifications** — e.g. water-bill notices sent to recipients across Iranian geographic sections (province → city → zone) — billed and reported by provider, geography, message type, and Jalali period. **Each message is individually composed by the caller**; the platform stores and dispatches the supplied text. **Customers authenticate via API keys.**

### 1.1 Expected usage patterns

| Pattern | Description | Storage impact |
|---|---|---|
| **Bursty bulk sends (distinct messages)** | Billing cycles push **hundreds of thousands to millions** of individually-composed messages in a short window. | Heavy concurrent inserts → write path is the #1 optimization target. |
| **Steady transactional/OTP traffic** | Lower-volume, latency-sensitive single messages. | Small but constant; must not be starved by bulk inserts. |
| **Asynchronous delivery reports (DLR)** | Each sent message later receives 1+ status reports, arriving minutes-to-hours later, then reaching a **terminal** state. | Append into `DeliveryReport` + a narrow in-place update of `Message.DeliveryStatus` **on the hot partition only**. |
| **Heavy reporting/analytics** | Cost/count by geo/provider/type/period; **delivery success rate** is a direct `GROUP BY Message.DeliveryStatus`. | Served by **columnstores on cold partitions**, isolated from the OLTP hot path. |

### 1.2 Expected message volumes (working assumptions for sizing)

Baseline: a national utility footprint, **~10 million messages/month** average with billing-cycle peaks of several million/day.

| Horizon | Cumulative messages | Order of magnitude |
|---|---|---|
| 1 month | ~10 million | 10⁷ |
| 1 year | ~120 million | 10⁸ |
| 5 years | ~600 million – 1 billion | 10⁸–10⁹ |

`DeliveryReport` is **≥ message volume**. **Design must assume the billion-row regime** for both facts.

### 1.3 Reporting requirements

- Cost by **geo section**, **provider**, **message type**, **Jalali period** (e.g. *Spring 1405*).
- **Counts** and **delivery success rates** (a direct `GROUP BY` on the denormalized `Message.DeliveryStatus`).
- **History of a single recipient** (by mobile number) and **history of a single bill** (by bill id).
- **Monthly cost trends** and **top provinces by spend**.

Reports are predominantly **aggregations over large ranges** plus a few **point-lookups**. These two access shapes are handled differently (see §5, §8).

### 1.4 Historical data requirements

- **Cost and delivery facts must remain immutable and accurate forever** (or for the legally mandated retention period) — *even after tariffs change*. This forces **price snapshotting** (§6).
- `Message` and `DeliveryReport` are append-dominated; cold partitions are columnstore-compressed and dropped by partition switching.
- Raw **message bodies** carry the highest storage cost and lowest long-term value → **shorter retention** and **physical separation** (§7).

### 1.5 Cost calculation requirements

- Cost depends on **provider**, **encoding** (GSM-7 vs. UCS-2 for Persian text), **segment/part count**, and the **tariff in effect at submission time**.
- Persian (UCS-2) bills at **70 chars/segment** (67 concatenated) vs. GSM-7 **160/153** — part counting is central.
- The **computed cost is frozen onto the message at submission** so later tariff edits never change historical billing.

### 1.6 Future provider expansion requirements

- Today: **Magfa**. Tomorrow: more Iranian providers — addable by **inserting reference + tariff rows**, not schema changes. Provider-specific raw codes map to a **normalized status**.

---

## 2. Domain Analysis

| Concept | Responsibility |
|---|---|
| **Customer (Tenant)** | The organization that owns the traffic and is billed. Isolation boundary. **Sends** messages. |
| **API Key** | Credential a customer presents to authenticate sends; many per customer (rotation). Only a **hash** is stored. |
| **API Key IP Restriction** | Optional allow-list of source networks (CIDR) for a key. |
| **Provider** | An SMS carrier/aggregator (Magfa, …). Owns sender lines and tariffs; source of delivery reports. |
| **Sender Line** | A specific origin number (`3000…`, `1000…`, `4040…`). |
| **Message Type** | Single classification axis — *delivery class* (OTP/Transactional/Bulk) **and** *business purpose* (Water Bill, …). |
| **Message** | The central **fact**: one SMS send. Carries text reference, recipient, references, Jalali date, cost snapshot, **send-lifecycle status**, and a **denormalized current `DeliveryStatus`** (read model). |
| **Message Body** | The exact sent text (1:1 with Message), separated for storage/retention. |
| **Delivery Report** | An **append-only** status event for a message; full history. The current state is projected onto `Message.DeliveryStatus`. |
| **Recipient** | The receiver — a **`MobileNumber` on the message** (ad-hoc; not a managed dimension). |
| **Client / Business references** | Caller-supplied ids on the message: `ClientCorrelatedId` (idempotency), `BillId`, `PayId`. |
| **Tariff** | Time-bounded pricing for a (provider, encoding, message-type) combination; versioned by effective range. |
| **Geo Section** | A single self-referencing geographic dimension (Province → City → Zone → …) via a parent link + `SectionType`. |

**Fact vs. Dimension split is the spine of this design:** small dimensions by surrogate keys; two large partitioned facts — `Message` (send fact + current delivery projection) and `DeliveryReport` (status-event history) — analyzed via columnstores. The current delivery state is **denormalized onto `Message`** so success-rate reporting is a join-free `GROUP BY`; the append-only `DeliveryReport` preserves full history.

> **What was removed and why (cumulative):** `MessageTemplate` + merge variables, `Campaign`, `Subscriber`, `DimDate`, and `MessageDailyAggregate` — all dropped (distinct text, ad-hoc recipients, Jalali-on-fact, columnstore analytics). All re-introducible additively (§9).

---

## 3. Database Schema Proposal

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
| 10 | **`Message`** | **Fact (hot) + delivery read model** | **~10M** | **~120M** | **~0.6–1B** |
| 11 | `MessageBody` | Fact satellite (text) | ~10M | ~120M | ~0.6–1B |
| 12 | **`DeliveryReport`** | **Status-event history (append-only)** | ~12M | ~150M | ~1B+ |

> Each table is summarized against the required facets below. Full column detail in **§4**.

### Dimension tables (1–9) — shared facets

- **Purpose:** Stable, deduplicated reference data referenced by the facts via surrogate keys.
- **Read/write:** Tiny cached lookups; rare admin writes. `ApiKey` is read on **every send** (by key hash) — a trivially-indexed point lookup.
- **Retention:** Permanent; versioned dimensions keep history via effective ranges; revoked keys kept for audit.
- **Storage:** Negligible relative to the facts.

### 10. `Message` — the central fact + delivery read model

- **Purpose:** One row per SMS send — what was sent, to which number, by which line/provider, at what cost, with what **send-lifecycle** outcome **and current `DeliveryStatus`**.
- **Read pattern:** (a) range aggregations (incl. **success rate** as a direct `GROUP BY DeliveryStatus`) via a **columnstore on cold partitions**; (b) point lookups by recipient, bill id, client correlation id, provider message id.
- **Write pattern:** Massive **concurrent batch inserts**; thereafter only a **narrow in-place update of `DeliveryStatus`/`DeliveredAtUtc`** as DLRs arrive — and only while the partition is **hot (rowstore)**. By the time a partition is columnstore-compressed, delivery is terminal, so the columnstore is effectively immutable.
- **Retention:** Long (billing/legal); partition-switched in lockstep with `DeliveryReport`.
- **Storage:** Narrow; text in `MessageBody`, status history in `DeliveryReport`.

### 11. `MessageBody` — text satellite (1:1 with Message)

- **Purpose / justification:** Hold the exact distinct text separate from the narrow fact (audit/legal proof without bloat).
- **Read pattern:** Rare (operator inspects one message). **Write:** inserted with the message; immutable.
- **Retention:** **Shorter** than the fact and now **independent** — partitioned by its own `Id` (see §4.11), purged earliest.
- **Storage:** Largest per-row cost; non-deduplicable (distinct text). Isolated for compression + independent retirement.

### 12. `DeliveryReport` — status-event history (append-only)

- **Purpose:** One row per delivery-status report — the **full history** of how a message's status evolved.
- **Business justification:** Forensics/audit and re-derivation; current state is already on `Message.DeliveryStatus`.
- **Volume:** ≥ message volume.
- **Read pattern:** Point lookup of a message's report history; bulk re-derivation if ever needed. **Not** on the success-rate hot path.
- **Write pattern:** **Append-only inserts** as DLRs arrive; never updated.
- **Retention:** Lockstep with `Message` (or shorter if full history isn't needed — see §10 Q2).
- **Storage:** A second large table; columnstore on cold partitions.

---

## 4. Table-by-Table Deep Analysis

> Notation: `PK` primary key, `FK` foreign key, `CIX` clustered index, `NCIX` nonclustered index. SQL Server types. Money = `DECIMAL(19,4)` (IRR). Phone numbers ASCII → `VARCHAR`; Persian text → `NVARCHAR`. **PK is always `Id`; FKs are `<Table>Id`.**

### 4.1 `Customer`
| Column | Type | Notes |
|---|---|---|
| `Id` | `SMALLINT IDENTITY` | **PK**, `CIX` |
| `Name` | `NVARCHAR(200)` | |
| `Code` | `VARCHAR(50)` | external/business code; `NCIX` unique |
| `IsActive` | `BIT` | |
| `CreatedAtUtc` | `DATETIME2(3)` | |

**Why:** Tenancy is the isolation/reporting boundary. A `SMALLINT` key is the cheapest FK to repeat ~10⁹ times. *Alternative:* customer name on the fact — rejected (text duplication at scale).

### 4.2 `ApiKey`
| Column | Type | Notes |
|---|---|---|
| `Id` | `INT IDENTITY` | **PK**, `CIX` |
| `CustomerId` | `SMALLINT` | **FK** → `Customer` |
| `Name` | `NVARCHAR(100)` | human label, e.g. "Production billing system" |
| `KeyPrefix` | `VARCHAR(12)` | **non-secret** public prefix shown in dashboards/logs; `NCIX` |
| `KeyHash` | `BINARY(32)` | **SHA-256 of the secret key**; `NCIX` **unique**. Plaintext is **never** stored |
| `IsActive` | `BIT` | |
| `ExpiresAtUtc` | `DATETIME2(3)` | nullable — optional expiry |
| `RevokedAtUtc` | `DATETIME2(3)` | nullable — set on revocation (row kept for audit) |
| `LastUsedAtUtc` | `DATETIME2(3)` | nullable — updated coarsely/async |
| `CreatedAtUtc` | `DATETIME2(3)` | |

**Indexes:** `NCIX UNIQUE (KeyHash)` — auth lookup on every send; `NCIX (CustomerId)` — list a tenant's keys.

**Why a separate table:** a customer holds **many** keys (rotation, per-system), each with its own lifecycle. **Why hash-only:** if the DB leaks, plaintext keys must not; authenticate by hashing the presented key and seeking `KeyHash`; `KeyPrefix` identifies a key without exposing it. *Alternatives:* plaintext/encrypted (breach liability / unnecessary); JWT/OAuth (heavier; layer later, §9). Throttling lives in the app/cache layer.

### 4.3 `ApiKeyIpRestriction` (optional)
| Column | Type | Notes |
|---|---|---|
| `Id` | `INT IDENTITY` | **PK**, `CIX` |
| `ApiKeyId` | `INT` | **FK** → `ApiKey`; `NCIX` |
| `Cidr` | `VARCHAR(43)` | allowed source range (IPv4/IPv6 CIDR) |
| `Description` | `NVARCHAR(100)` | nullable |

**Why (optional):** customers reach the platform over **specific networks** (internet and/or intranet gateway — the same dual-path reality behind `Provider.BaseUrl`/`FallbackBaseUrl`). Binding a key to known CIDRs is cheap defense-in-depth. Child table (multiple ranges per key); omitted for unrestricted keys. *Alternative:* a delimited column — rejected (not queryable).

### 4.4 `Provider`
| Column | Type | Notes |
|---|---|---|
| `Id` | `TINYINT IDENTITY` | **PK**, `CIX` |
| `Name` | `NVARCHAR(100)` | e.g. "Magfa" |
| `Code` | `VARCHAR(50)` | `NCIX` unique |
| `BaseUrl` | `VARCHAR(300)` | primary API endpoint (e.g. public-internet gateway) |
| `FallbackBaseUrl` | `VARCHAR(300)` | secondary endpoint over a different network path (e.g. intranet); nullable |
| `IsActive` | `BIT` | |

**Why:** Tiny → `TINYINT`. **Endpoints live here (provider info), credentials do not** — the same provider is reached over internet **and** intranet with failover (runtime data, not `appsettings`); credentials stay in the secret store keyed by `Code`. *Deferred:* a child `ProviderEndpoint` for ≥3 paths (§9). *Alternative:* provider string on the fact — rejected (bytes × 10⁹, no FK).

### 4.5 `SenderLine`
| Column | Type | Notes |
|---|---|---|
| `Id` | `SMALLINT IDENTITY` | **PK**, `CIX` |
| `ProviderId` | `TINYINT` | **FK** → `Provider` |
| `LineNumber` | `VARCHAR(20)` | `3000…`, `1000…`, `4040…`; `NCIX` |
| `IsSharedLine` | `BIT` | shared (public) vs. dedicated (private) |
| `IsActive` | `BIT` | |

**Why:** Lines have distinct pricing/reachability and belong to a provider. Surrogate `SMALLINT` keeps the fact FK small. Shared-vs-dedicated is **binary** → a `BIT`. *Alternative:* raw line string per message — rejected.

### 4.6 `MessageType`
| Column | Type | Notes |
|---|---|---|
| `Id` | `TINYINT` | **PK** (seeded), `CIX` |
| `Name` | `NVARCHAR(80)` | "OTP", "Transactional", "Bulk", "Water Bill", … |
| `Code` | `VARCHAR(50)` | `NCIX` |

**Why:** The **single classification axis** — delivery class + business purpose (former `BusinessCategory`, merged). Global + `TINYINT`. *Future (additive):* nullable `CustomerId` and/or widen to `SMALLINT`. *Alternatives:* separate `BusinessCategory` (extra table/key/join); `BIT IsOtp` (not extensible) — rejected.

### 4.7 `GeoSection` (self-referencing geographic hierarchy)
| Column | Type | Notes |
|---|---|---|
| `Id` | `INT IDENTITY` | **PK**, `CIX` |
| `ParentGeoSectionId` | `INT` | **FK** → `GeoSection` (self); `NULL` at province level |
| `SectionType` | `TINYINT` | 1 = Province, 2 = City, 3 = Zone (extensible) |
| `Name` | `NVARCHAR(100)` | |
| `Code` | `VARCHAR(20)` | `NCIX` |
| `Path` | `VARCHAR(900)` | materialized ancestor path, e.g. `/12/450/8123/`; `NCIX` for subtree filters |
| `IsActive` | `BIT` | |

**Why one self-referencing table instead of three:** a strict hierarchy as an **adjacency list** collapses three tables into one while preserving rollups; deeper levels are inserts. The denormalized **`Path`** makes "everything under Tehran province" a sargable `Path LIKE '/<TehranId>/%'`. The fact stores one `GeoSectionId`; reports roll up via the small tree. *Alternatives:* three tables (superseded); flat tag (no rollups); `HIERARCHYID` (valid; `Path` for portability).

### 4.8 `Tariff` (versioned header)
| Column | Type | Notes |
|---|---|---|
| `Id` | `INT IDENTITY` | **PK**, `CIX` |
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
| `Id` | `INT IDENTITY` | **PK**, `CIX` |
| `TariffId` | `INT` | **FK** → `Tariff` |
| `MinChars` | `SMALLINT` | character-range lower bound |
| `MaxChars` | `SMALLINT` | upper bound (nullable = ∞) |
| `PricePerSegment` | `DECIMAL(19,4)` | |

**Why two tables:** header = validity + applicability; detail = price banding by character range. *Alternatives:* one wide row (inflexible); price in config (must be auditable data). **Never used at report time** — price is frozen onto the message (§6).

### 4.10 `Message` — the fact + delivery read model (most-scrutinized table)
| Column | Type | Notes |
|---|---|---|
| `Id` | `BIGINT IDENTITY` | **PK** (nonclustered — see §8) |
| `SubmitDateJalali` | `CHAR(10)` | **partition column** — `yyyy/mm/dd` (e.g. `1405/01/03`); part of `CIX`; sargable period key |
| `SubmittedAtUtc` | `DATETIME2(3)` | precise UTC instant |
| `CustomerId` | `SMALLINT` | **FK** (tenant/sender) |
| `ProviderId` | `TINYINT` | **FK** |
| `SenderLineId` | `SMALLINT` | **FK** |
| `MessageTypeId` | `TINYINT` | **FK** (delivery class + business purpose) |
| `GeoSectionId` | `INT` | **FK** → `GeoSection`; nullable (caller-supplied) |
| `MobileNumber` | `VARCHAR(15)` | recipient, canonical `98…` (ad-hoc) |
| `ClientCorrelatedId` | `VARCHAR(100)` | caller's id / **idempotency** key; nullable; maps to provider `uid` |
| `BillId` | `VARCHAR(31)` | external **bill** reference; nullable |
| `PayId` | `VARCHAR(31)` | external **payment** reference; nullable |
| `Encoding` | `TINYINT` | GSM7 / UCS2 (snapshot) |
| `CharacterCount` | `SMALLINT` | snapshot |
| `SegmentCount` | `TINYINT` | parts (snapshot) |
| `TariffId` | `INT` | **FK** — which tariff priced this (audit) |
| `UnitPrice` | `DECIMAL(19,4)` | per-segment price **at submission** (snapshot) |
| `TotalCost` | `DECIMAL(19,4)` | `UnitPrice × SegmentCount` (snapshot) |
| `Status` | `TINYINT` | **send-lifecycle**: Queued / Submitted / Sent / Rejected / Unknown |
| `DeliveryStatus` | `TINYINT` | **current delivery state** (read model): Pending / Delivered / Undelivered / Expired / Unknown |
| `DeliveredAtUtc` | `DATETIME2(3)` | nullable — set when `DeliveryStatus` becomes Delivered |
| `ProviderMessageId` | `VARCHAR(50)` | provider's id — the key used to **match incoming DLRs** |

**Primary/clustered/indexes (justified in §8):**
- **PK:** `Id` — **nonclustered**, unique.
- **CIX:** `(SubmitDateJalali, Id)` — aligns with **Jalali-monthly partitioning**.
- **NCIX 1:** `(MobileNumber, SubmitDateJalali)` — recipient history.
- **NCIX 2:** `(ProviderId, ProviderMessageId)` — resolve an incoming DLR to its `Id`.
- **NCIX 3 (filtered):** `(CustomerId, ClientCorrelatedId) WHERE ClientCorrelatedId IS NOT NULL` — idempotency + client lookups.
- **NCIX 4 (filtered):** `(BillId) WHERE BillId IS NOT NULL` — bill history.
- **Nonclustered columnstore on cold partitions** — analytics, incl. the join-free success rate. `PayId` not indexed by default.

**The performant success-rate solution (this revision):** `DeliveryStatus` is **denormalized onto the fact** and upserted by DLR ingestion, so success rate is a **direct columnstore `GROUP BY ProviderId, DeliveryStatus`** — no window, no join. The classic objection (in-place updates fighting the columnstore) does **not** apply here: delivery reaches a **terminal** state within minutes–hours, while the columnstore is only built on **cold** partitions — so updates land on the hot rowstore partition and the columnstore never absorbs them. `DeliveryStatus` is also **in no index**, so its update never moves a row or churns an index.

**Why else this structure is preferred:**
- **Narrow:** no text on the fact; text in `MessageBody`, status history in `DeliveryReport`.
- **Small dimension keys + denormalized Jalali date** (`SubmitDateJalali`): period/dimension reports filter/group without a date dimension or a giant join.
- **Frozen cost snapshot:** historical accuracy independent of later tariff edits.
- **Caller references** (`ClientCorrelatedId/BillId/PayId`): idempotency + reconciliation inline; nullable.

**Alternatives considered & rejected:**
- *Compute current status by a window over `DeliveryReport` + join (previous revision):* rejected as the *reporting* path — too costly; replaced by the denormalized `DeliveryStatus`. `DeliveryReport` is retained for full history.
- *`INT yyyymmdd` for `SubmitDateJalali`:* the chosen `CHAR(10)` is readable and still sorts chronologically; ~6 bytes/row more (in CIX + NCIs) — accepted.
- *`DimDate` / `MessageDailyAggregate` / `Subscriber` / `Campaign` / `MessageTemplate`:* removed (§2); additive to restore (§9).
- *`UNIQUEIDENTIFIER` key:* rejected — fragmentation; 16 bytes × 10⁹.

### 4.11 `MessageBody`
| Column | Type | Notes |
|---|---|---|
| `Id` | `BIGINT` | **PK = FK** → `Message.Id` (1:1, shared key), `CIX` |
| `Body` | `NVARCHAR(MAX)` | the exact distinct text that was sent |

**Why separate, and why no `SubmitDateJalali`:** the text is variable-length and large, the fact is fixed-width and hot — keeping `NVARCHAR(MAX)` off the fact preserves rows-per-page + columnstore compression. The body is reached only by **`Id` point lookup**, so it does **not** need the message's Jalali partition scheme; removing `SubmitDateJalali` eliminates a redundant column and **decouples body retention from message retention**. The body table is **partitioned by `Id`** (a monotonic identity ⇒ time-ordered), so it still supports **partition-switch retention** — purged earlier than the fact on its own schedule. *Alternatives:* inline `Body` on `Message` (couples hot fact to cold text); keep `SubmitDateJalali` (redundant — derivable via `Id`).

### 4.12 `DeliveryReport` — status-event history (append-only)
| Column | Type | Notes |
|---|---|---|
| `Id` | `BIGINT IDENTITY` | **PK** (nonclustered) |
| `SubmitDateJalali` | `CHAR(10)` | **partition column** — copied from the message; aligns each report to its message's Jalali-month partition |
| `MessageId` | `BIGINT` | **FK** → `Message` |
| `NormalizedStatus` | `TINYINT` | Delivered / Undelivered / Expired / Rejected / Unknown |
| `RawStatusCode` | `INT` | provider-native code |
| `ReceivedAtUtc` | `DATETIME2(3)` | when this report arrived |

**Indexes:** **CIX** `(SubmitDateJalali, MessageId, ReceivedAtUtc DESC)` — partition-aligned; clusters a message's reports together for history reads. Nonclustered **columnstore on cold partitions** for any bulk re-derivation.

**Why keep it (given `Message.DeliveryStatus` exists):** it is the **full audit history** — every raw report, multiple per message, with provider codes and timestamps — used for forensics, disputes, and re-deriving the projection if logic changes. *Why partition by the message's `SubmitDateJalali`* (not the report's receive date): co-locates a message and its reports for lockstep retention. *Could be downgraded* to optional/short-retention if full history isn't required (§10 Q2).

---

## 5. Reporting Validation

Aggregate reports run on the **columnstore over cold `Message` partitions**; point-lookups hit a targeted nonclustered index; **delivery success rate reads the denormalized `Message.DeliveryStatus`** (no join, no window).

| # | Report | Required objects | Strategy | Performance |
|---|---|---|---|---|
| 1 | **Cost for Tehran province, Spring 1405, water bills** | `Message` (columnstore) + `GeoSection` + `MessageType` | Filter `SubmitDateJalali BETWEEN '1405/01/01' AND '1405/03/31'`; geo via `GeoSection.Path LIKE '/<Tehran>/%'`; `MessageTypeId=<Water Bill>`; `SUM(TotalCost)` | Columnstore scan with **segment + partition elimination**; small geo join. Sub-second–seconds. |
| 2 | **Cost by provider** | `Message` (columnstore) + `Provider` | `GROUP BY ProviderId`, join names | Columnstore aggregate; fast. |
| 3 | **Count by city** | `Message` (columnstore) + `GeoSection` | Join → roll zone→city via `Path`, `GROUP BY` city | Columnstore scan + small join; seconds. |
| 4 | **Count by zone** | `Message` (columnstore) + `GeoSection` | `GROUP BY GeoSectionId` (leaf=zone) | Columnstore aggregate; fast. |
| 5 | **History of a recipient (mobile number)** | `Message` + `MessageBody` (+`DeliveryReport`) | Point lookup `NCIX (MobileNumber, SubmitDateJalali)`; optional body + report-history joins | Index seek; ms. |
| 6 | **History of a bill** | `Message` (+`MessageBody`) | Seek `filtered NCIX (BillId)` | Index seek; ms. |
| 7 | **Delivery success rate by provider** | `Message` (columnstore) **only** | `GROUP BY ProviderId, DeliveryStatus` on the denormalized read model | **Join-free, window-free** columnstore aggregate; fast. |
| 8 | **Monthly cost trend** | `Message` (columnstore) | `GROUP BY LEFT(SubmitDateJalali, 7)` (Jalali `yyyy/mm`) | Columnstore aggregate; fast. |
| 9 | **Top provinces by spend** | `Message` (columnstore) + `GeoSection` | Join + roll up to province via `Path`; `GROUP BY` province `ORDER BY SUM(TotalCost) DESC` | Columnstore scan + small join. |

**Key validation outcome:** all aggregates — **including delivery success rate** — are now batch-mode columnstore scans with partition/segment elimination; success rate in particular is a plain `GROUP BY` on the denormalized `DeliveryStatus`, with **no window and no join to `DeliveryReport`**. Point-lookups are single index seeks. `DeliveryReport` is consulted only for a specific message's full history.

---

## 6. Tariff and Pricing Design

### 6.1 Structure

```
Provider ──< Tariff (versioned by EffectiveFrom/EffectiveTo, per Encoding/MessageType) ──< TariffRate (per character-range band → PricePerSegment)
```

- **Multiple providers:** `Tariff.ProviderId`.
- **Historical tariffs + ranges:** `EffectiveFromUtc`/`EffectiveToUtc` (open-ended when `NULL`). New pricing = **new version**, never an UPDATE.
- **Character-count ranges + multipart:** `TariffRate.MinChars/MaxChars` + `PricePerSegment`; parts derive from encoding.
- **Future changes:** insert a new `Tariff`, close the prior `EffectiveToUtc`. No fact/schema change.

### 6.2 How historical pricing stays accurate after tariffs change

**Snapshotting, not recomputation.** At submission the engine resolves the tariff (provider + type + encoding + `SubmittedAtUtc` within range) and **copies the price onto the message**. Reporting reads the frozen cost and **never re-resolves tariffs**.

### 6.3 Exact values persisted on `Message` at submission

| Persisted column | Why frozen |
|---|---|
| `Encoding` | Segmentation rules; recomputation could drift. |
| `CharacterCount` | Source measure for segmentation/cost. |
| `SegmentCount` | The billed unit count. |
| `TariffId` | **Audit** — which tariff version priced this. |
| `UnitPrice` | Resolved `PricePerSegment` at submission. |
| `TotalCost` | `UnitPrice × SegmentCount` — authoritative billed amount. |

Each `Message` row is a **self-contained billing record** — reproducible even if `Tariff`/`TariffRate` were dropped.

---

## 7. Storage Optimization Analysis

Principle: **duplicate small fixed-width keys freely (kills joins); isolate large/variable text; keep facts append-dominated so columnstores stay efficient.**

### 7.1 What should be duplicated (denormalized)
- **Dimension keys** (`GeoSectionId`, `ProviderId`, `MessageTypeId`, `CustomerId`), the **Jalali date** (`SubmitDateJalali`), and the **current `DeliveryStatus`** on the fact — tiny/low-cardinality, **columnstore-friendly**, and they remove joins/windows/conversions from every report (delivery success rate especially).
- **Cost snapshot** (`UnitPrice/TotalCost`) — immutability + join-free cost reporting.
- **Caller references** (`ClientCorrelatedId/BillId/PayId`) — idempotency + reconciliation inline; nullable.
- **`SubmitDateJalali` on `DeliveryReport`** — partition-wise alignment with `Message`.

### 7.2 What should **not** be duplicated
- **Descriptive names** (geo/provider/type) → only in tiny dimensions, joined for labels.
- **The recipient number IS on the fact** (`MobileNumber`) — ad-hoc recipients offer nothing to dedupe (~15 bytes/row, accepted).

### 7.3 Message text → **separated, not normalizable**
Distinct text → nothing to dedupe. Separate into `MessageBody` (1:1, keyed by `Id`); **`PAGE` + Unicode compression**; **independent, shorter retention** (partitioned by `Id`).

### 7.4 Delivery status → **denormalized current state + append-only history**
The current `DeliveryStatus` lives on `Message` (fast reads); the full event history lives in append-only `DeliveryReport`. The fact's in-place status update is **narrow, un-indexed, and hot-partition-only**, so it never touches the cold columnstore. *Cost:* `DeliveryReport` is a second large table — downgrade to short-retention/optional if full history isn't needed (§10 Q2).

### 7.5 Analytics storage: columnstores instead of a pre-aggregate
**Nonclustered columnstores on cold partitions** compress ~10× and answer aggregates via batch-mode scans with elimination — replacing the removed `MessageDailyAggregate`. *Cost:* latency scales with the scanned range rather than being constant.

### 7.6 Tradeoff summary

| Decision | Saves | Costs | Verdict |
|---|---|---|---|
| Denormalize dimension keys + Jalali date + `DeliveryStatus` onto fact | Joins/windows/conversions eliminated; columnstore-friendly | a few bytes/row (compressible) | **Adopt** |
| `SubmitDateJalali` as `CHAR(10)` (readable) | Human-readable dates | ~6 bytes/row vs `INT` | **Adopt** (per requirement) |
| Current `DeliveryStatus` on fact (hot-partition updates) | **Join-free, window-free success rate** | Narrow in-place update while hot | **Adopt** |
| Append-only `DeliveryReport` (history) | Full audit; re-derivation | A second ~1B-row table | **Adopt** (downgrade optional) |
| `MessageBody` keyed by `Id`, no `SubmitDateJalali` | One redundant column gone; independent body retention | Body not Jalali-partition-aligned (point-accessed only) | **Adopt** |
| Single `GeoSection` tree; `MobileNumber` on fact; columnstores; hashed `ApiKey` | (as previously) | (as previously) | **Adopt** |

---

## 8. Concurrency and Deadlock Prevention

> Workload: bursty **concurrent batch inserts**, **append-only DLR inserts**, **narrow hot-partition `DeliveryStatus` updates**, and concurrent **columnstore reads**.

### 8.1 Partitioning — the foundation
- **`Message` and `DeliveryReport` are range-partitioned by `SubmitDateJalali` (one partition per Jalali month); `MessageBody` is range-partitioned by `Id`** (its own, shorter-retention schedule).
- **Lock escalation contained to a partition** (`LOCK_ESCALATION = AUTO`).
- **Current (hot) partitions are rowstore; closed partitions carry columnstores** — inserts/updates and analytics are physically separated, and the hot-partition `DeliveryStatus` updates never reach a columnstore.
- **Retention by `SWITCH`/drop**; `Message`+`DeliveryReport` in lockstep, `MessageBody` independently.

### 8.2 Clustered index choice (the hot-page problem)
- A naïve **clustered `BIGINT IDENTITY`** funnels inserts to the same trailing page → `PAGELATCH_EX` contention. `SubmitDateJalali` is monotonic with time, so the same applies and is mitigated identically.
- **Decision:** `Message` CIX = **`(SubmitDateJalali, Id)`**; `DeliveryReport` CIX = **`(SubmitDateJalali, MessageId, ReceivedAtUtc DESC)`**; `MessageBody` CIX = **`(Id)`**; all with **`OPTIMIZE_FOR_SEQUENTIAL_KEY = ON`**.
  - `Id` remains the **nonclustered** PK on `Message`.
- **Rejected:** clustered GUID (fragmentation); hash-bucket prefix as default (harms range scans/partition elimination).

### 8.3 Nonclustered indexes — minimal *on purpose*
| Index | Justification | Why not more |
|---|---|---|
| `Message CIX (SubmitDateJalali, Id)` | Partition alignment + sequential locality | — |
| `Message NCIX (MobileNumber, SubmitDateJalali)` | Recipient-history seeks | Report #5 |
| `Message NCIX (ProviderId, ProviderMessageId)` | Resolve incoming DLR → message; locate the row to update `DeliveryStatus` | DLR handling would otherwise scan |
| `Message Filtered NCIX (CustomerId, ClientCorrelatedId) WHERE NOT NULL` | Idempotency + client lookups | Nulls/transactional unindexed |
| `Message Filtered NCIX (BillId) WHERE NOT NULL` | Bill-history seeks | Nulls excluded |
| `DeliveryReport CIX (SubmitDateJalali, MessageId, ReceivedAtUtc DESC)` | Message report-history reads | — |
| **Nonclustered columnstore on cold partitions** (both facts) | All aggregate reporting | Off hot partitions to protect writes |

**`DeliveryStatus` is deliberately in no index** so its in-place update never moves a row or churns an index. `PayId` is not indexed.

### 8.4 Insert pattern (write strategy)
- **Set-based batch inserts** (TVP / `SqlBulkCopy`), **1,000–5,000 rows/transaction** — under the ~5,000-lock escalation threshold; **short transactions**.
- Columnstores on **closed** partitions are built out of band; current-month inserts/updates never contend with them.
- **Consistent access order** (Message → MessageBody → DeliveryReport).

### 8.5 Delivery-report handling (append + narrow hot update)
- An arriving DLR is matched via `Message NCIX (ProviderId, ProviderMessageId)` (seek), then: (1) **inserted** into `DeliveryReport` (history), and (2) used to **update `Message.DeliveryStatus`/`DeliveredAtUtc`** — a single-column, **un-indexed**, hot-partition (rowstore) update. No row movement, no index churn, no columnstore impact. Status only ever advances toward a terminal value, so updates are bounded and idempotent.

### 8.6 Read pattern (reporting isolation)
- Aggregate/success-rate reads target the **columnstores on cold partitions**; current-month reads hit the rowstore.
- **Enable RCSI** so reporting `SELECT`s use row-versioning and never block writers.

### 8.7 Summary of how each risk is mitigated
| Risk | Mitigation |
|---|---|
| **Last-page insert contention** | `OPTIMIZE_FOR_SEQUENTIAL_KEY`; batched short transactions |
| **Lock escalation** | Partitioning + `LOCK_ESCALATION = AUTO`; batches < 5,000 rows; partition-switch retention |
| **Page contention** | Narrow fixed-width facts; minimal NCIs; un-indexed `DeliveryStatus`; `PAGE`/columnstore compression on cold data |
| **Hot partitions** | Jalali-monthly partitioning; rowstore-hot vs. columnstore-cold separation |
| **Reader/writer deadlocks** | Hot-partition-only narrow updates; RCSI reads; out-of-band columnstore builds; consistent write ordering |

---

## 9. Future Evolution

All additive — insert rows / add a nullable column / add a partition / add a table + nullable FK — never a fact rewrite.

| Future need | How it's absorbed |
|---|---|
| **New SMS provider** | Insert into `Provider` (incl. URLs), `SenderLine`, `Tariff`/`TariffRate`; credentials to the secret store. |
| **≥3 provider network paths** | *(Deferred.)* Child `ProviderEndpoint(ProviderId, NetworkType, BaseUrl, Priority, IsActive)` (§4.4). |
| **API key scopes / per-message attribution / OAuth** | Add `ApiKeyScope`; add a nullable `ApiKeyId` on `Message`; layer OAuth beside `ApiKey`. |
| **Constant-time cost dashboards over full history** | Re-introduce a `MessageDailyAggregate` rollup — additive; columnstore serves until then. |
| **Re-introduce batching / subscribers / templates / date-dimension** | New table + a nullable FK; existing rows stay `NULL`. |
| **Payment-id lookups** | Add a `Filtered NCIX (PayId)` — additive. |
| **Deeper geography** | Insert `GeoSection` rows at a new `SectionType`; tree + `Path` absorb depth. |
| **Tenant-specific message types** | Nullable `CustomerId` on `MessageType` and/or widen to `SMALLINT`. |
| **Richer delivery states** | Extend the `DeliveryStatus`/`NormalizedStatus` enums; no schema change. |
| **Scale beyond a billion** | Jalali-monthly → finer partitions; archive/compress cold partitions via switching. |

---

## 10. Design Principles & Tradeoff Summary

1. **High-volume processing first.** Narrow facts, partition-aligned clustered keys, sequential-key optimization, minimal NCIs, batched short-transaction inserts, append-only DLR history with narrow hot-partition status updates.
2. **Reporting simplicity.** Small dimension keys + denormalized Jalali date + **denormalized `DeliveryStatus`** + columnstores ⇒ every aggregate (incl. success rate) is a join-free scan; point-lookups are single seeks.
3. **Long-term maintainability.** Additive evolution; versioned tariffs; provider-agnostic normalized status; one geo tree; immutable event history for delivery; uniform `Id` PK naming.
4. **Low storage consumption.** No duplicated names; one geo key; text isolated + compressed; columnstore ~10× for analytics; `MessageBody` shorn of a redundant column.
5. **Minimal deadlocks.** Hot-partition-only narrow updates, partition-scoped locking, sub-escalation batches, RCSI reads, out-of-band columnstore builds.
6. **Simple operational support.** Lockstep partition-switch retention (`Message`+`DeliveryReport`), independent body retention; self-contained billing rows; hashed API keys; a small cacheable dimension set.
7. **Security.** API keys stored as hashes only; optional per-key CIDR allow-listing aligned with the internet/intranet access model.

**Every major tradeoff was resolved for write throughput, report simplicity, storage economy, and security — documented where it arises (§4 alternatives, §7 storage, §8 concurrency).**

---

### Open decisions for reviewers

1. **Delivery model** — confirm the **denormalized `Message.DeliveryStatus` (fast reads) + append-only `DeliveryReport` (history)** split.
2. **`DeliveryReport` retention** — keep full history in lockstep with `Message`, or shorten/make optional now that current state is on `Message`?
3. **Body retention window** — confirm shorter, `Id`-partitioned retention for `MessageBody` is legally acceptable.
4. **`MessageType` scope** — single global type-merged dimension sufficient, or tenant-specific purposes now?
5. **API key model** — hashed `ApiKey` + optional `ApiKeyIpRestriction` sufficient, or scopes/per-message attribution now?
6. **Partition cadence** — Jalali-monthly proposed; confirm vs. weekly given peak daily volumes.
