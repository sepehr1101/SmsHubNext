# sms.ir REST API reference notes

Source: <https://sms.ir/rest-api/>

These notes record the parts of the official API consumed by `ISmsProvider`. The stable
`Provider.Code` used by SmsHubNext is `smsir`. Authentication is an API key in the `X-API-KEY`
request header. The encrypted key is stored in `ProviderAccount.SecretEncrypted` with
`AuthType = ApiKey`; account settings may be `{}`.

## SendBatchAsync

Endpoint:

```text
POST /v1/send/likeToLike
```

This endpoint is used instead of `/v1/send/bulk` because SmsHubNext accepts a distinct body for every
message. `lineNumber` is one numeric sender line, while `messageTexts` and `mobiles` are parallel
arrays. Their lengths must match and the official maximum is 100.

The successful envelope has `status = 1`. `data.messageIds` is positionally aligned with the request:

- positive id: accepted; persist it as `Message.ProviderMessageId`;
- zero: recipient is on the provider blacklist, so reject and refund that message;
- null: invalid mobile or invalid/too-long message, so reject and refund that message.

sms.ir exposes no caller-supplied local id on this endpoint and documents no idempotent-resend
guarantee. Therefore `SupportsIdempotentResend` is false. If the HTTP response is lost, the message
must remain held for manual review rather than being sent a second time.

## ResolveSubmittedMessageIdAsync

sms.ir can retrieve a sent message only by its provider `messageId`; it has no lookup by the local
`Message.Id` that was known before submission. The adapter therefore returns null without making an
HTTP request. This is not permission to resend: the dispatcher observes `SupportsIdempotentResend =
false` and holds an unresolved outcome.

## GetDeliveryReportsAsync

Endpoint:

```text
GET /v1/send/{messageId}
```

The API accepts one provider message id per request. Delivery mapping:

- `1`: delivered;
- `2`, `4`, `6`, `7`: terminal undelivered;
- `3`, `5`, or null: still in flight; keep polling.

If multiple sms.ir accounts are configured, the adapter tries them until the message is found because
the persisted provider message id does not encode its account.

## FetchInboundMessagesAsync

Endpoint:

```text
GET /v1/receive/latest?count={count}
```

The endpoint returns at most 100 newest unread inbound messages and marks each returned message as
read, so the pull is destructive. The adapter caps `count` to 100 and never returns more than the
requested amount across configured accounts. Provider Unix timestamps are kept verbatim as strings,
matching the existing provider-neutral inbound contract.

## Request outcomes

The official response envelope has `status`, `message`, and `data`. HTTP 400 denotes logical errors,
so the adapter parses a valid envelope even for non-2xx responses.

- `1`: success.
- `102`: insufficient provider credit; hold the batch.
- `10`, `11`, `12`, `13`, `14`: authentication/account rejection; definitely not submitted.
- `0`, `20`: provider system error or rate limit; explicitly not submitted and safe to retry.
- `101`, `103`-`110`, `112`-`119`, `123`: validation/configuration rejection; reject and refund.
- an undocumented status remains an unknown provider outcome and enters confirmation/manual-review
  handling instead of being guessed.

HTTP 401/403 without a usable envelope is definitely not submitted, HTTP 429 is retryable, and a bare
HTTP 400 is rejected. Transport errors, timeouts, HTTP 5xx, malformed successful responses, and
missing positional results stay unknown outcomes.

## Sandbox load profile

The opt-in runner `tests/SmsHubNext.LoadTests/run-smsir-sandbox.ps1` exercises the real Sandbox
contract through SmsHubNext. It creates a disposable SQL Server, stores the Sandbox key through the
normal encrypted provider-account API, performs a one-message contract probe, then submits an exact
bounded workload (100,000 messages by default) in batches no larger than 100. The adapter's
`MinimumRequestInterval` and the producer interval are both configured conservatively.

Because sms.ir documents that Sandbox responses are simulated and reports are not stored, the runner
disables the delivery-report worker. It verifies submission, provider ids, accounting, ledger
agreement, queue drain, and DLR-queue insertion; native delivery status remains covered by WireMock
contract tests rather than by the Sandbox load run.
