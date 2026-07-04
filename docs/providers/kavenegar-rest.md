# Kavenegar REST API reference notes

Source: <https://kavenegar.com/rest.html>

These notes keep only the surface needed by `ISmsProvider`. We intentionally do not implement
`/sms/send.json`: `SendBatchAsync` always uses `sendarray`, even for one message, so provider code has
one submission path.

## Transport and authentication

Base URL shape:

```text
https://api.kavenegar.com/v1/{API-KEY}/Scope/MethodName.OutputFormat
```

The API key is part of the URL path. We use JSON responses.

## SendBatchAsync

Endpoint:

```text
POST /v1/{API-KEY}/sms/sendarray.json
```

Fields:

- `receptor`: array of recipient numbers.
- `sender`: array of sender lines.
- `message`: array of message bodies.
- `localmessageids`: array of our `Message.Id` values.

The arrays must have equal length. The provider limit is 200 messages per request. `localmessageids`
prevents duplicate sends: if the same local id was already accepted, Kavenegar returns the existing
record instead of sending again.

## ResolveSubmittedMessageIdAsync

Endpoint:

```text
GET /v1/{API-KEY}/sms/statuslocalmessageid.json?localid={Message.Id}
```

This is the duplicate-send guard for lost responses. If Kavenegar returns a row with `status = 100`,
the local id has no known provider record, so the dispatcher may eventually re-send after the required
negative confirmations. Any other row with `messageid` means the original send was accepted; return
that provider message id.

Only the last 12 hours are available by local id, so the dispatcher's awaiting-confirmation window
must stay shorter than that.

`/sms/select.json` is not the first recovery call here because Kavenegar's Select method takes
`messageid`, not `localid`. It is useful after `StatusLocalMessageId` has recovered the provider
message id and a caller needs the full sent-message record (body, sender, receptor, cost, etc.).

## GetDeliveryReportsAsync

Endpoint:

```text
GET /v1/{API-KEY}/sms/status.json?messageid={id1,id2,...}
```

The provider limit is 500 ids per request. Status mapping:

- `10`: delivered.
- `6`, `11`, `13`, `14`: terminal undelivered.
- `100`: terminal expired/unknown id.
- `1`, `2`, `4`, `5`: in flight; keep polling.

## FetchInboundMessagesAsync

Endpoint:

```text
GET /v1/{API-KEY}/sms/receive.json?linenumber={line}&isread=0
```

The endpoint marks returned messages as read. One call returns up to 100 messages. Because the API is
line-scoped, the provider implementation fans out over configured inbound lines and merges results.

## Request-level result codes used by the implementation

- `200`: success.
- `409`: provider temporarily cannot respond; retry later.
- `418`: insufficient account credit; hold the batch.
- `411`, `412`, `413`, `414`, `419`: request/data/configuration faults; surface as provider errors
  unless the provider returns per-message rows that can be mapped individually.
