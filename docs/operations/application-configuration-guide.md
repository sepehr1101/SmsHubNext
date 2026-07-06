# Application Configuration Guide

This guide is for system operators who maintain SmsHubNext runtime configuration.

The actual configuration file is `src/SmsHubNext/appsettings.json` plus any environment-specific override such as `appsettings.Production.json` or a local, gitignored override. This document is only a guide; do not paste secrets into it.

## How To Manage Settings

1. Change runtime values in the environment-specific configuration file, not in source-controlled defaults, when preparing a production deployment.
2. Keep secrets out of source control. Provider credentials should be supplied through the approved production secret/configuration mechanism.
3. Restart the application after changing settings that are read at startup.
4. Record operational changes in the deployment/change log, especially values that affect retry, duplicate-prevention, provider credentials, or polling behavior.
5. Prefer conservative dispatch settings in production. Sending a message late is usually safer than sending it twice.

## Local Development Database

For local development, use `compose.dev.yml` from the repository root to run SQL Server on `localhost,14333`.
The application reads the gitignored `src/SmsHubNext/appsettings.Development.local.json` after the committed settings, so local connection strings and provider credentials can be changed without touching source-controlled defaults.

The committed `src/SmsHubNext/appsettings.Development.local.json.example` matches the default compose password. If `SMSHUBNEXT_SQL_PASSWORD` is changed for Docker Compose, update the local connection string to the same value.

## Dispatch Settings

The `Dispatch` section controls the background worker that submits queued messages to the SMS provider.

### Awaiting-Confirmation Safety

These settings protect against duplicate SMS sends when the application cannot prove whether the provider accepted a submit request.

Before a dispatch chunk is sent to the provider, its messages are moved from `Queued` to `AwaitingConfirmation`. If the HTTP response is lost, times out, or the process crashes around the provider call, the worker reconciles by provider uid/mid instead of resending immediately.

| Setting | Default | Operator guidance |
|---|---:|---|
| `MinAwaitingConfirmationAge` | `00:02:00` | Minimum time a message must stay in `AwaitingConfirmation` before a provider "no record" lookup can be trusted. Increase this if the provider sometimes needs more time to expose uid/mid lookups. Decrease it only after proving the provider lookup is immediately consistent. |
| `AwaitingConfirmationRetryDelay` | `00:02:00` | Delay before the next confirmation lookup when the lookup fails, times out, or does not yet provide enough evidence to resend. During this delay the message is not sent again. Increase it to reduce provider pressure during incidents. |
| `RequiredNegativeConfirmations` | `2` | Number of delayed "no record" lookups required before a message may return to `Queued` for resend. Increase it to reduce duplicate-send risk. Lower values recover faster but are riskier if provider lookup can be stale. |

Operational rule: while a message is in `AwaitingConfirmation`, do not send it again. A positive provider lookup moves it to `Submitted`; only enough delayed negative lookups move it back to `Queued`.

### Other Dispatch Values

| Setting | Meaning |
|---|---|
| `PollInterval` | Idle delay when no batch is ready to dispatch. |
| `HoldRetryDelay` | Delay before a provider-credit `Held` batch can be checked again. |
| `DispatchLeaseTimeout` | How long a `Dispatching` batch can stay stale before another worker may reclaim it. |
| `MaxDispatchAttempts` | Maximum ordinary transient dispatch attempts before terminal dispatch failure. Awaiting-confirmation reconciliation is intentionally more conservative and should not be treated as ordinary resend retry. |
| `RetryBackoffSeconds` | Backoff schedule for ordinary transient resend retries. |

## Delivery Report Polling

The `DeliveryReportPolling` section controls how submitted messages are checked for final delivery state. These settings affect delivery statistics, not initial submit idempotency.

## Inbound Polling

The `InboundPolling` section controls provider inbox polling. Enable it only when inbound message handling is operationally required and the provider inbox behavior is understood.

## Provider Settings

The `Providers` section controls provider integration options such as base URL, timeout, batch size, and account mapping. In production, verify sender-line to account mapping before enabling dispatch.
