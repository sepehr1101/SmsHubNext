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

If `SMSHUBNEXT_SQL_PASSWORD` is changed for Docker Compose, update the local connection string in `src/SmsHubNext/appsettings.Development.local.json` to the same value.

## API Documentation

`OpenApi:Enabled` controls both the OpenAPI JSON endpoint at `/openapi/v1.json` and the Scalar UI at `/scalar/v1`. It defaults to `true` so backend and React development can use the deployed IIS instance directly. Set it to `false` in `appsettings.Production.json` and restart the application if documentation should be hidden for a deployment. The landing-page documentation button follows the same setting.

## Cross-Origin Resource Sharing (CORS)

The `Cors` section controls which browser applications may call the API from a different origin. An origin is the exact combination of scheme, host, and port. For example, `https://panel.example.com` and `http://panel.example.com` are different origins, as are ports `443` and `8443`.

The committed default permits the local Vite development server at `http://localhost:5173`. For a deployed React application, override the section in `appsettings.Production.json`:

```json
{
  "Cors": {
    "Enabled": true,
    "AllowedOrigins": [
      "https://panel.example.com",
      "https://support.example.com"
    ],
    "AllowedMethods": [ "GET", "POST", "PUT", "DELETE" ],
    "AllowedHeaders": [ "Accept", "Authorization", "Content-Type", "X-Api-Key" ],
    "AllowCredentials": false,
    "PreflightMaxAgeSeconds": 600
  }
}
```

| Setting | Meaning |
|---|---|
| `Enabled` | Enables or disables the configured CORS policy. Same-origin requests continue to work when disabled. |
| `AllowedOrigins` | Exact HTTP/HTTPS origins allowed to call the API. Do not include a path, query, fragment, or wildcard. A trailing slash is accepted but unnecessary. |
| `AllowedMethods` | HTTP methods permitted in cross-origin requests. Keep this list limited to methods used by the frontend. |
| `AllowedHeaders` | Request headers accepted by preflight. `Authorization` is required for Bearer tokens and `X-Api-Key` for customer send requests. |
| `AllowCredentials` | Allows browser credentials such as cookies. Keep this `false` for the current header-based authentication model. |
| `PreflightMaxAgeSeconds` | How long the browser may cache a successful preflight response, from `0` to `86400` seconds. |

Operational rules:

1. Use explicit trusted origins. The application deliberately rejects `*` to avoid accidentally exposing credential-bearing APIs to every website.
2. Include the scheme and non-default port when applicable, for example `https://panel.example.com:8443`.
3. Do not write paths such as `https://panel.example.com/app`; CORS matches origins, not pages.
4. Restart the application pool/site after changing these startup settings.
5. If startup fails after a change, inspect the application log. Invalid origins and invalid preflight cache values fail fast with a `Cors:` configuration message.
6. Test both the preflight request and the real API call from the deployed frontend domain. A successful request from Postman does not prove that browser CORS is configured correctly.

The equivalent environment-variable form for the first allowed origin is `Cors__AllowedOrigins__0=https://panel.example.com`.

## Dispatch Settings

The `Dispatch` section controls the background worker that submits queued messages to the SMS provider.

### Awaiting-Confirmation Safety

These settings protect against duplicate SMS sends when the application cannot prove whether the provider accepted a submit request.

Before a dispatch chunk is sent to the provider, its messages are moved from `Queued` to `AwaitingConfirmation`. If the HTTP response is lost, times out, or the process crashes around the provider call, the worker reconciles by provider uid/mid instead of resending immediately.

| Setting | Default | Operator guidance |
|---|---:|---|
| `MinAwaitingConfirmationAge` | `00:02:00` | Minimum time a message must stay in `AwaitingConfirmation` before a provider "no record" lookup can be trusted. Increase this if the provider sometimes needs more time to expose uid/mid lookups. Decrease it only after proving the provider lookup is immediately consistent. |
| `AwaitingConfirmationRetryDelay` | `00:02:00` | Delay before the next confirmation lookup when the lookup fails, times out, or does not yet provide enough evidence to resend. During this delay the message is not sent again. Increase it to reduce provider pressure during incidents. |
| `AwaitingConfirmationMaxAge` | `11:00:00` | Maximum age at which provider lookup evidence is accepted. It must remain below Kavenegar's 12-hour local-id lookup retention. Older unknown outcomes are held for manual review and are never resent automatically. |
| `RequiredNegativeConfirmations` | `2` | Number of delayed "no record" lookups required before a message may return to `Queued` for resend. Increase it to reduce duplicate-send risk. Lower values recover faster but are riskier if provider lookup can be stale. |

Operational rule: while a message is in `AwaitingConfirmation`, do not send it again. A positive provider lookup moves it to `Submitted`. Enough delayed negative lookups permit an automatic resend only for providers that guarantee idempotency for the supplied local message id (currently Kavenegar). Magfa does not document that guarantee, so an unresolved Magfa outcome is held with `ManualReviewRequired` instead of being resent automatically.

### Other Dispatch Values

| Setting | Meaning |
|---|---|
| `PollInterval` | Idle delay when no batch is ready to dispatch. |
| `HoldRetryDelay` | Delay before a provider-credit `Held` batch can be checked again. |
| `DispatchLeaseTimeout` | Renewable ownership lease for a `Dispatching` batch. It must be at least one minute. A replacement worker may reclaim an expired lease, while the old worker's token prevents it from sending later chunks or changing the reclaimed batch. |
| `MaxDispatchAttempts` | Maximum ordinary transient dispatch attempts before terminal dispatch failure. Awaiting-confirmation reconciliation is intentionally more conservative and should not be treated as ordinary resend retry. |
| `RetryBackoffSeconds` | Backoff schedule for ordinary transient resend retries. |

## Delivery Report Polling

The `DeliveryReportPolling` section controls how submitted messages are checked for final delivery state. These settings affect delivery statistics, not initial submit idempotency.

## Inbound Polling

The `InboundPolling` section controls provider inbox polling. Enable it only when inbound message handling is operationally required and the provider inbox behavior is understood.

## Provider Settings

The `Providers` section controls provider integration options such as base URL, timeout, batch size, and account mapping. In production, verify sender-line to account mapping before enabling dispatch.

`LoopbackSmsProvider` is available only in the `Development` and `Testing` environments. In every production-like environment the application fails at startup unless at least one real provider (`Magfa` or `Kavenegar`) is enabled. This is an intentional deployment safety check: a missing provider configuration must never create fake successful deliveries.

Before production startup:

1. Set the host environment explicitly to `Production`.
2. Enable at least one real provider and supply its settings through production configuration/secrets.
3. Create and activate the provider account, then link each production sender line to the correct account.
4. Submit a uniquely identified low-risk test batch and confirm both the provider message id and subsequent delivery report.
5. Treat `ClientBatchId` as mandatory. Generate one stable value per logical API request and reuse the same value, with the identical payload, for every client retry. A changed payload with the same key is rejected.
