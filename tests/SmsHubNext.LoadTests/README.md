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
- every message is `AwaitingConfirmation`, with no provider message id or delivery-poll row;
- every batch is `Held / ManualReviewRequired` after its first provider call;
- the original prepaid debits remain and no automatic refund is created, because an authentication
  response currently travels through the conservative unknown-outcome lane;
- no automatic resend or confirmation lookup occurs during this bounded probe.

The JSON timing and invariant report is written to
`tests/SmsHubNext.LoadTests/results/live-invalid-credentials-<provider>.json`.
