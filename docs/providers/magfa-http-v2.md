# Magfa SMS — HTTP v2 API reference

> Distilled from the official Magfa wiki (`messaging.magfa.com/ui/?public/wiki/api/http_v2`,
> retrieved 2026-06-28). This is the working reference for the `magfa` provider integration
> (roadmap Phase 1). Where the original is ambiguous, the ambiguity is called out rather than
> guessed. Magfa is the **first** provider (sender lines `3000…`); the integration sits behind
> `Features/Providers/ISmsProvider` so later providers are added without touching dispatch.

---

## 1. Overview

* **REST/JSON over HTTPS.** Optimised, low-overhead successor to HTTP v1.
* **Charset: UTF-8 only** — request and response.
* Every accepted message gets a **unique provider message id** (`id`, a.k.a. `mid`) for status tracking.
* Concurrent calls are allowed (for send throughput).
* **gzip is optional** on both request (`Content-Encoding: gzip`) and response
  (`Accept-Encoding: gzip`). We do **not** need it for one-message-per-call dispatch.

### Limits / global rules

* **Max 100 recipients per request** (exceeding this returns an error code, see §8).
* Every response carries a top-level **`status`** field: `0` = request accepted with no
  request-level error; any non-zero value is a request-level error code (§8). A `0` top-level
  status does **not** mean every message succeeded — inspect the per-message `status` too.

---

## 2. Base URL & endpoints

> **Batch send is implemented:** the dispatcher hands the provider up to `Providers:Magfa:BatchSize`
> (≤100) queued messages per `POST /send` — one HTTP request per chunk — and applies each
> message's result individually (its own `ProviderMessageId`, refund, retry, hold). Batching changes
> the transport only, never the per-message state machine.

Base: `https://sms.magfa.com/api/http/sms/v2`

| Method | HTTP | Path | Purpose |
|--------|------|------|---------|
| `balance`  | `GET`  | `/balance`                      | Remaining account credit (IRR). |
| `send`     | `POST` | `/send`                         | Send 1..100 messages. |
| `mid`      | `GET`  | `/mid/{uid}`                    | Resolve our `uid` → provider `mid` (dedup / timeout recovery). |
| `statuses` | `GET`  | `/statuses/{mid1,mid2,...}`     | Delivery status (DLR) for up to 100 mids. |
| `messages` | `GET`  | `/messages/{count}`             | Pull up to 100 inbound (MO) messages. |

`send` is the only `POST`; everything else is `GET`.

---

## 3. Authentication

HTTP **Basic Auth**. The username is the account username **combined with the domain**, the
password is the **service password** (distinct from the panel login password — generated in the
panel under *Account Management → Password & Security → Service passwords*).

```
username field  =  USERNAME/DOMAIN
password field  =  PASSWORD
Header          =  Authorization: Basic base64("USERNAME/DOMAIN:PASSWORD")
```

Magfa also supports an optional **source-IP allow-list** in the panel; requests from
unlisted IPs are rejected with status `29`.

> Three credential parts to carry in config: **username**, **domain**, **password** — held
> **per account** under `Providers:Magfa:Accounts`, each listing the **sender lines** it owns, so
> different lines can authenticate against different Magfa accounts. Sending selects the account by
> the message's sender line; the `Authorization` header is set per request. Secrets stay out of
> source control: `appsettings.json` carries placeholders, real credentials go in the gitignored
> `appsettings.{Environment}.local.json` (see `appsettings.Development.local.json.example`).

---

## 4. `balance`

`GET /balance`

```json
{ "status": 0, "balance": 1000 }
```

| Field     | Type   | Notes |
|-----------|--------|-------|
| `status`  | int    | `0` = ok, else error code (§8). |
| `balance` | long   | Remaining credit (IRR). `null` on error. |

---

## 5. `send`

`POST /send` — JSON (or form). Parameters are **parallel arrays**, indexed by recipient.
We send **up to `BatchSize` (≤100) messages per call**; the response `messages` array has one
entry per recipient, which we correlate back to each message by the `uids`/`userId` echo (falling
back to position only when Magfa doesn't echo uids).

### Request (JSON)

| Param        | Type       | Required | Notes |
|--------------|------------|----------|-------|
| `senders`    | string[]   | yes | Originating line(s). One value broadcasts to all recipients; or one-per-recipient. |
| `recipients` | string[]   | yes | Destination MSISDN(s). |
| `messages`   | string[]   | yes | Body/bodies (UTF-8). |
| `encodings`  | int[]      | no  | Per message: `0` auto-detect (default), `2` Farsi/UCS2, `5` 8-bit, `6` binary. |
| `uids`       | long[]     | no  | Our correlation id per message; later resolvable via `/mid/{uid}`. Enables **idempotent** resend after a transport timeout. |
| `udhs`       | string[]   | no  | User-Data-Header (concatenation/binary); not needed for plain text. |

Array-length rules: any optional array supplied must match the `recipients` length, else a
`10x` error (§8).

```json
{
  "senders":    ["30008710"],
  "recipients": ["09120000000"],
  "messages":   ["پیام فارسی"],
  "uids":       [123456]
}
```

### Sender number formats

`3000xxxxxx` · `983000xxxxxx` · `+983000xxxxxx`

### Recipient number formats

`09xxxxxxxxx` · `989xxxxxxxxx` · `+989xxxxxxxxx` · `9xxxxxxxxx`

### Response

```json
{
  "status": 0,
  "messages": [
    { "status": 13, "userId": "1223", "recipient": "98912xxxxxxx" },
    { "status": 0, "id": 111111111, "userId": 1224, "parts": 3,
      "tariff": 160.00, "alphabet": "UCS2", "recipient": "989xxxxxxxxx" }
  ]
}
```

Top-level `status` is request-level. Each element of `messages` is the **per-recipient** outcome:

| Field       | Type   | Notes |
|-------------|--------|-------|
| `status`    | int    | `0` = accepted; non-zero = this recipient failed (§8). |
| `id`        | long   | Provider message id (`mid`) — the **DLR-matching key**. Present only when `status = 0`. |
| `userId`    | long   | Echo of our `uid` if supplied. |
| `parts`     | int    | Segment count Magfa billed. |
| `tariff`    | float  | Unit tariff Magfa applied. |
| `alphabet`  | string | `DEFAULT` (Latin/GSM-7) or `UCS2` (Farsi). |
| `recipient` | string | Echo of the recipient. |

**Text & segmentation (informational — our own `SmsPartCalculator` is authoritative for billing):**
Persian (UCS2) = 70 chars single / 67 per part; Latin (GSM-7) = 160 / 153; binary = 140 bytes.
A single non-ASCII char forces the whole message to UCS2. Max **265** parts (else error `30`).

---

## 6. `mid` — resolve uid → mid

`GET /mid/{uid}`

```json
{ "status": 0, "mid": 1111111111 }
```

| Field    | Type | Notes |
|----------|------|-------|
| `status` | int  | `0` = ok, else error (§8). |
| `mid`    | long | Provider message id, or `-1` if not found. |

**Purpose: idempotent resend.** If a `send` call times out (we never saw the response), call
`/mid/{uid}` with the same `uid` we sent. A real `mid` back ⇒ Magfa already accepted it; do
**not** resend. Requires we pass `uids` on every `send`.

---

## 7. `statuses` — delivery reports (DLR)

`GET /statuses/{mid1,mid2,...}` — up to 100 comma-separated mids.

```json
{
  "status": 0,
  "dlrs": [
    { "mid": 1234567, "status": 8,  "date": "2020-01-01 00:00:00" },
    { "mid": 7654321, "status": -1, "date": "2020-01-01 10:10:00" }
  ]
}
```

`date` format: `yyyy-mm-dd hh:mm:ss`.

### DLR status values (per mid — distinct from the request error codes in §8)

| Value | Meaning |
|------:|---------|
| `-1`  | Id not found (wrong id, or >24h since send). |
| `0`   | No status received yet. |
| `1`   | **Delivered to handset.** |
| `2`   | Not delivered to handset. |
| `8`   | Delivered to operator (SMSC). |
| `16`  | Not delivered to operator. |

Mapping to our `DeliveryReportStatus` / `Message.DeliveryStatus` is decided in the integration
(terminal: `1` delivered, `2`/`16` failed; in-flight: `0`, `8`).

---

## 8. Request / per-message error codes (top-level & per-recipient `status`)

| Code | Meaning | Class (our handling) |
|-----:|---------|----------------------|
| `0`  | Success | — |
| `1`  | Recipient number invalid | reject (refund) |
| `2`  | Sender number invalid | reject (config) |
| `3`  | `encoding` param invalid | reject (config) |
| `4`  | `mclass` param invalid | reject (config) |
| `6`  | `udh` param invalid | reject (config) |
| `8`  | Sent outside permitted advertising window (07–22) | reject / retry-later |
| `13` | Message content (UDH+text) empty | reject |
| `14` | **Insufficient IRR credit** | **hold batch** (provider credit) |
| `15` | Server busy / transient internal error during send | **transient → retry** |
| `16` | Account inactive (contact sales) | fail (config) |
| `17` | Account expired (contact sales) | fail (config) |
| `18` | Username or password invalid | fail (auth) |
| `19` | Request invalid (username/password/domain combo) | fail (auth) |
| `20` | Sender number not owned by account | reject (config) |
| `22` | Service not enabled for account | fail (config) |
| `23` | No capacity to process new request now | **transient → retry** |
| `24` | Message id invalid (wrong, or >1 day old) | n/a (statuses/mid) |
| `25` | Method name invalid | fail (bug) |
| `27` | Recipient on operator blacklist | reject |
| `28` | Recipient blocked by prefix at Magfa | reject |
| `29` | Source IP not allowed | fail (config) |
| `30` | Message parts exceed standard limit (265) | reject |
| `31` | Invalid JSON format | fail (bug) |
| `33` | Subscriber blocked receiving from this line (opt-out "لغو 11") | reject |
| `34` | No confirmed info for this number | reject |
| `35` | Invalid character in message text | reject |
| `101`| `messageBodies` array length ≠ recipients | fail (bug) |
| `102`| `messageClass` array length ≠ recipients | fail (bug) |
| `103`| `senderNumbers` array length ≠ recipients | fail (bug) |
| `104`| `udhs` array length ≠ recipients | fail (bug) |
| `105`| `priorities` array length ≠ recipients | fail (bug) |
| `106`| recipients array empty | fail (bug) |
| `107`| recipients array exceeds allowed length | fail (bug) |
| `108`| senders array empty | fail (bug) |
| `109`| `encoding` array length ≠ recipients | fail (bug) |
| `110`| `checkingMessageIds` array length ≠ recipients | fail (bug) |

**How these map to `ProviderDispatchResult` (single-message dispatch):**

* **Accepted** — top-level `0` **and** per-message `0` (carry `id` as the provider message id).
* **InsufficientCredit** — `14` (top-level or per-message) ⇒ dispatcher **holds** the batch.
* **Rejected** — per-message permanent refusals: `1, 8, 13, 20, 27, 28, 30, 33, 34, 35`
  (message never sent ⇒ refund).
* **Transient** (failed `Result`, batch re-queued & retried) — `15`, `23`, HTTP/transport
  errors, timeouts.
* **Config/auth/bug** (`16, 17, 18, 19, 22, 25, 29, 31, 3, 4, 6, 2, 10x`) — not a normal
  per-message outcome. Treated as a non-retryable provider error and logged loudly; the exact
  disposition (fail the batch vs. reject the message) is finalised in the integration code.

---

## 9. `messages` — inbound (MO) — *not in Phase 1 scope*

`GET /messages/{count}` (max 100). Destructive pull — returned messages are dequeued.

```json
{
  "status": 0,
  "messages": [
    { "body": "…", "senderNumber": "98912xxxxxxx",
      "recipientNumber": "983000xxxx", "date": "2020-…" }
  ]
}
```

Documented here for completeness; inbound handling is a later roadmap phase (there is no
`Subscriber`/inbox table yet — README "Removed" list).

---

## 10. What we use in Phase 1

Send-only walking skeleton. The dispatcher already submits **one message per call**, so:

* **`POST /send`** in chunks of `BatchSize` (≤100): parallel `senders`/`recipients`/`messages`
  arrays (+ `uids` = `Message.Id` for correlation/idempotency), then map each `messages[]` entry to
  its `ProviderDispatchResult`.
* **`GET /balance`** — handy for an ops/health surface (optional this phase).
* **`GET /mid/{uid}`** — Phase 3 (timeout-safe resend). When a `/send` response is lost the message
  is parked `AwaitingConfirmation`; the next dispatch cycle looks up `/mid/{Message.Id}` — a real mid
  means Magfa already accepted it (confirm `Submitted`, no re-send), `-1` means re-send is safe.
* **`GET /statuses`** — Phase 2 (delivery-status polling worker), maps to `DeliveryReport`.
* **`GET /messages`** — later phase (inbound).
