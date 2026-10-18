# SmsHubNext Load and Reliability Tests

This suite is intentionally isolated from production. It starts a disposable SQL Server database,
runs SmsHubNext in `Testing` with `LoopbackSmsProvider`, seeds a test-only customer/API key,
and sends only synthetic recipients. It must never be pointed at a real provider configuration.

## Quick smoke and idempotency test

Prerequisites: Docker Desktop with Linux containers and PowerShell 7+.

The runner publishes SmsHubNext on the host first, then builds a runtime-only test image. This keeps
the load test independent from NuGet connectivity inside Docker and uses the repository's pinned SDK.

```powershell
./tests/SmsHubNext.LoadTests/run-smoke.ps1
```

The default run performs:

- 20 seconds at two API batches/second, ten messages per batch;
- 50 concurrent retries of one identical `ClientBatchId` and payload;
- k6 latency/error/dropped-iteration thresholds;
- direct SQL verification of batch uniqueness, exactly one debit per batch, batch/message cost
  agreement, non-negative balance, ledger agreement, and eventual queue drain.

The result summary is written to `tests/SmsHubNext.LoadTests/results/smoke-summary.json`.
Use `-KeepEnvironment` only for diagnostics; the normal run removes its containers and volume.

## Capacity and soak profiles

Capacity tests are not part of the normal test command. The defaults execute an 11-minute step
stress test with batches of 100 messages:

```powershell
./tests/SmsHubNext.LoadTests/run-capacity.ps1
```

For a soak run:

```powershell
$env:SMSHUBNEXT_LOAD_SOAK_RATE = '5'
$env:SMSHUBNEXT_LOAD_SOAK_DURATION = '4h'
./tests/SmsHubNext.LoadTests/run-capacity.ps1 -Mode soak
```

The runner creates and seeds a fresh database, runs k6, verifies the SQL invariants, and removes the
environment. Its result is written to `tests/SmsHubNext.LoadTests/results/capacity-summary.json`.

For meaningful server-capacity numbers, run k6 from a separate load-generator host against an
isolated staging deployment. Set `SMSHUBNEXT_LOAD_BASE_URL`, `SMSHUBNEXT_LOAD_API_KEY`,
`SMSHUBNEXT_LOAD_SENDER_LINE`, and `SMSHUBNEXT_LOAD_MESSAGE_TYPE_ID` to staging-only test data.
Never use production credentials or a deployment connected to live SMS providers. The SQL invariant
verifier is intended for the disposable local database; use equivalent read-only checks for staging.

## Network chaos coverage

`DispatchNetworkChaosTests` belongs to the regular integration-test project. It uses SQL Server and
Toxiproxy containers to drop the database connection after a provider accepts a message and verifies
that lease recovery reconciles the provider id without sending the message twice.

## Real invalid-credential probe (explicit opt-in)

This probe contacts either Magfa's or Kavenegar's real HTTPS endpoint with a freshly generated,
deliberately invalid credential. It persists and accounts for 10,000 synthetic messages in a fresh
disposable SQL Server, submits them in the provider's native maximum batch size, measures persistence
and dispatch time, and verifies the resulting database state.

It is excluded from every normal test run. Run exactly one provider explicitly:

```powershell
./tests/SmsHubNext.LoadTests/run-live-invalid-credentials.ps1 `
    -Provider kavenegar `
    -IUnderstandThisContactsLiveProvider
```

Use `-Provider magfa` for Magfa. The run makes 50 real Kavenegar requests (200 messages each) or
100 real Magfa requests (100 messages each). The credentials and sender lines are intentionally
invalid, so the expected result is no accepted SMS and no provider charge. Do not point the test at
custom endpoints or replace its generated credentials with real ones.

The expected safety behavior is:

- all 10,000 `Message` and `MessageBody` rows are present;
- every message returns to `Queued`, with no provider message id or delivery-poll row;
- every batch is `DispatchFailed / InvalidProviderCredentials` after its first provider call;
- every prepaid debit has one matching batch-level refund because authentication failure proves the
  provider accepted no message;
- no automatic resend or confirmation lookup occurs during this bounded probe.

The JSON timing and invariant report is written to
`tests/SmsHubNext.LoadTests/results/live-invalid-credentials-<provider>.json`.

## sms.ir Sandbox end-to-end load

This is a separate, explicit opt-in profile. It contacts `https://api.sms.ir` with a Sandbox-type
API key, but routes every message through the SmsHubNext HTTP API, SQL accounting, dispatcher, and
the real sms.ir adapter. The disposable database and app containers are removed after the run.

Set the three required values without placing the provider key in a command line or repository file:

```powershell
$env:SMSHUBNEXT_SMSIR_SANDBOX_API_KEY = '<SANDBOX_API_KEY>'
$env:SMSHUBNEXT_SMSIR_SANDBOX_SENDER_LINE = '<SANDBOX_SENDER_LINE>'
$env:SMSHUBNEXT_SMSIR_SANDBOX_RECIPIENT = '09120000000'
```

Start with a bounded rehearsal:

```powershell
./tests/SmsHubNext.LoadTests/run-smsir-sandbox.ps1 `
    -MessageCount 1000 `
    -RequestIntervalMs 1000 `
    -IUnderstandThisContactsSmsIrSandbox
```

After the rehearsal succeeds, run the full profile:

```powershell
./tests/SmsHubNext.LoadTests/run-smsir-sandbox.ps1 `
    -MessageCount 100000 `
    -BatchSize 100 `
    -RequestIntervalMs 1000 `
    -IUnderstandThisContactsSmsIrSandbox
```

The full default produces 1,000 application batches and 1,000 sms.ir requests, with at least one
second between provider request starts. Before load, a one-message probe must reach
`DispatchCompleted`; authentication, configuration, and terminal dispatch failures stop the run.
The API key is sent only to the local provider-account endpoint and is encrypted by the running app;
the generated report contains no key and only the last four recipient digits.

sms.ir Sandbox does not store send reports. Consequently `DeliveryReportPolling` is disabled only in
this profile, while every submitted message must still have one durable `DeliveryReportPoll` row.
The verifier also requires exact message/body/batch counts, unique client correlation ids, provider
message ids, one debit per batch, no refunds, ledger/balance agreement, and a fully drained dispatch
queue. Results are written to:

```text
tests/SmsHubNext.LoadTests/results/smsir-sandbox-100000.json
```
