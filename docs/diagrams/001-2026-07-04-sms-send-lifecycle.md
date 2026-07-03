# 001 - SMS Send Lifecycle

Date: 2026-07-04

Purpose: show the current end-to-end SMS send flow from API acceptance through dispatch, provider outcomes, delivery-report polling, accounting, and batch timeline events.

Use this as the baseline diagram for later revisions. When the flow changes, add a new numbered diagram file instead of editing this one in place, unless the change is only a typo.

```mermaid
flowchart TD
    A["Client calls POST /messages"] --> B["Resolve API key from header"]
    B --> C{"API key valid?"}
    C -- "No" --> C1["401 Unauthorized"]
    C -- "Yes" --> D["Validate request fields"]

    D --> E{"Request valid?"}
    E -- "No" --> E1["400 Bad Request"]
    E -- "Yes" --> F["Validate sending reference data"]

    F --> G{"Reference data exists?"}
    G -- "No" --> G1["4xx client error"]
    G -- "Yes" --> H["Resolve sender line"]

    H --> I["Calculate encoding and SMS segments"]
    I --> J["Resolve active tariff/rate"]
    J --> K{"Rate found?"}
    K -- "No" --> K1["404 sending.no_rate"]
    K -- "Yes" --> L["Begin DB transaction"]

    L --> M["Debit customer prepaid balance"]
    M --> N{"Enough balance?"}
    N -- "No" --> N1["400 insufficient_balance"]
    N -- "Yes" --> O["Insert MessageBatch"]

    O --> P["Insert debit ledger"]
    P --> Q["Insert batch Accepted event"]
    Q --> R["Bulk insert Message rows"]
    R --> S["Bulk insert MessageBody rows"]
    S --> T["Commit transaction"]
    T --> U["Return 202 Accepted + BatchId"]

    U --> V["DispatchWorker claims Received batch"]
    V --> W["Send queued messages to provider"]
    W --> X{"Provider result"}

    X -- "Accepted" --> Y["Mark message Submitted"]
    Y --> Z["Create DeliveryReportPoll row"]

    X -- "Rejected" --> AA["Mark message Rejected"]
    AA --> AB["Refund message cost"]
    AB --> AC["Write batch event"]

    X -- "Provider low credit" --> AD["Hold batch"]
    AD --> AE["Write Held event"]

    X -- "Transient/lost response" --> AF["Requeue or AwaitingConfirmation"]
    AF --> AG["Write retry/confirmation event"]

    Z --> AH["Finalize dispatch batch status"]
    AC --> AH
    AE --> AI["Retry later"]
    AG --> AI
    AI --> V

    AH --> AJ["DeliveryReportPoller polls provider status"]
    AJ --> AK{"Terminal delivery status?"}
    AK -- "No" --> AL["Poll later"]
    AK -- "Yes" --> AM["Append DeliveryReport"]
    AM --> AN["Update Message.DeliveryStatus"]
```
