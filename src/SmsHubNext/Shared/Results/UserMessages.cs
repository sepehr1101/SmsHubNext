namespace SmsHubNext.Shared.Results;

/// <summary>
/// User-facing API messages. Keep stable error codes near call sites, but centralize text here so
/// wording and future Persian localization do not require hunting through handlers.
/// </summary>
public static class UserMessages
{
    public static class Common
    {
        public const string RequestInvalid = "The request is invalid.";
        public const string UnexpectedError = "An unexpected error occurred.";
        public const string DatabaseUnavailable = "The database is temporarily unavailable.";
        public const string BadRequest = "The request could not be processed.";
        public const string Timeout = "The service timed out while processing the request.";
        public const string OperationCancelled = "The operation was cancelled before it completed.";
    }

    public static class Auth
    {
        public const string MissingKey = "An API key is required.";
        public const string InvalidKey = "The API key is not recognized.";
        public const string InactiveKey = "The API key is inactive or revoked.";
        public const string ExpiredKey = "The API key has expired.";
        public const string IpNotAllowed = "The caller IP is not allowed for this key.";
    }

    public static class ApiKeys
    {
        public const string UnknownCustomer = "The customer does not exist.";
        public const string CustomerRequired = "A valid customer id is required.";
        public const string NameRequired = "A key name is required.";
        public const string UnknownKey = "The API key does not exist.";
        public const string CidrRequired = "A CIDR range is required.";
        public const string InvalidId = "A valid API key or restriction id is required.";
        public const string NotFound = "The API key does not exist.";
        public const string RevokedCannotChange = "A revoked API key cannot be changed.";
        public const string AlreadyRevoked = "The API key has already been revoked.";
        public const string IpRestrictionNotFound = "The IP restriction does not exist.";
    }

    public static class Balances
    {
        public const string UnknownCustomer = "The customer does not exist.";
        public const string CustomerRequired = "A valid customer id is required.";
        public const string AmountPositive = "Top-up amount must be positive.";
        public const string ReferenceTooLong = "The top-up reference may contain at most 100 characters.";
        public const string DuplicateReference = "A top-up with this reference already exists.";
    }

    public static class Batches
    {
        public const string InvalidId = "A valid batch id is required.";
        public const string NotFound = "The batch does not exist.";
        public const string RetryNotAllowed =
            "Only dispatch-failed batches with queued or awaiting-confirmation messages can be retried.";
        public const string InsufficientBalanceForRetry =
            "The customer balance is not sufficient to retry this failed dispatch.";
    }

    public static class Sending
    {
        public const string ClientBatchPayloadMismatch =
            "A batch with this client batch id already exists, but its payload is different.";
        public const string UnknownCustomer = "The authenticated customer does not exist or is inactive.";
        public const string ApiKeyCustomerMismatch = "The API key does not belong to the authenticated customer.";
        public const string UnknownMessageType = "The message type does not exist.";
        public const string UnknownGeoSection = "One or more geo sections do not exist or are inactive.";
        public const string UnknownSenderLine = "The sender line does not exist.";
        public const string InactiveSenderLine = "The sender line is not active.";
        public const string SenderLineNotAllowed = "The sender line is not assigned to the authenticated customer.";
        public const string ProviderCredentialsRequired =
            "The sender line has no provider account credentials configured.";
        public const string ProviderAccountInactive = "The sender line's provider account is inactive.";
        public const string ProviderSecretRequired =
            "The sender line's provider account is missing its encrypted secret.";
        public const string NoActiveTariff =
            "No active tariff matches the sender line provider, message type, and SMS encoding.";
        public const string NoTariffRateBand =
            "An active tariff exists, but no tariff rate band covers the message character count.";
        public const string UnknownReference =
            "The customer, API key, message type, or geo section does not exist.";
        public const string DuplicateClientBatchId = "A batch with this client batch id already exists.";
        public const string InsufficientBalance = "The customer's prepaid balance is insufficient for this batch.";
        public const string SenderLineRequired = "A sender line is required.";
        public const string MessageTypeRequired = "A message type id is required.";
        public const string ClientBatchIdRequired =
            "A client batch id is required so retries cannot create duplicate SMS messages.";
        public const string ClientBatchIdTooLong = "Client batch ids may contain at most 100 characters.";
        public const string MessagesRequired = "At least one message is required.";
        public const string TooManyMessages = "At most 5,000 messages can be accepted in one request.";
        public static string TooManyMessagesLimit(int maxMessages) =>
            $"A request may contain at most {maxMessages} messages.";
        public const string RecipientRequired = "Each message must have a recipient.";
        public static string MissingRecipient(int index) => $"Message at index {index} is missing a recipient.";
        public static string InvalidRecipient(int index) => $"Message at index {index} has an invalid Iranian mobile number.";
        public const string TextRequired = "Each message must have text.";
        public static string MissingText(int index) => $"Message at index {index} is missing text.";
        public static string TextTooLong(int index, int maxLength) =>
            $"Message at index {index} may contain at most {maxLength} characters.";
        public static string ClientCorrelatedIdTooLongAt(int index, int maxLength) =>
            $"Message at index {index} has a client correlated id longer than {maxLength} characters.";
        public static string BillIdTooLong(int index, int maxLength) =>
            $"Message at index {index} has a bill id longer than {maxLength} characters.";
        public static string PayIdTooLong(int index, int maxLength) =>
            $"Message at index {index} has a pay id longer than {maxLength} characters.";
    }

    public static class Setup
    {
        public const string FactoryResetConfirmationRequired =
            "Factory reset requires the exact confirmation value 'RESET'.";
        public const string FactoryResetMessagesExist =
            "Factory reset is permanently unavailable because at least one SMS exists.";
    }

    public static class Reports
    {
        public const string FromJalaliInvalid = "fromJalali must be in yyyy/MM/dd format.";
        public const string ToJalaliInvalid = "toJalali must be in yyyy/MM/dd format.";
        public const string InvalidRange = "fromJalali must be before or equal to toJalali.";
        public const string CustomerInvalid = "customerId must be positive when supplied.";
        public const string ProviderInvalid = "providerId must be positive when supplied.";
        public const string MessageTypeInvalid = "messageTypeId must be positive when supplied.";
        public const string GeoSectionInvalid = "geoSectionId must be positive when supplied.";
        public const string CustomerMismatch = "The requested customer does not match the authenticated API key.";
    }

    public static class DispatchOperations
    {
        public const string FromJalaliInvalid = "fromJalali must be in yyyy/MM/dd format.";
        public const string ToJalaliInvalid = "toJalali must be in yyyy/MM/dd format.";
        public const string InvalidRange = "fromJalali must be before or equal to toJalali.";
        public const string CustomerInvalid = "customerId must be positive when supplied.";
        public const string ProviderInvalid = "providerId must be positive when supplied.";
        public const string PageInvalid = "page must be positive.";
        public const string TakeInvalid = "take must be between 1 and 200.";
    }

    public static class Tariffs
    {
        public const string NoRate = "No active tariff rate matches the request.";
        public const string ProviderRequired = "A provider id is required.";
        public const string TextRequired = "Message text is required.";
        public const string EncodingInvalid = "The SMS encoding is invalid.";
        public const string EffectiveFromRequired = "An effective-from UTC instant is required.";
        public const string EffectiveRangeInvalid = "The effective-to instant must be after effective-from.";
        public const string RatesRequired = "At least one tariff rate is required.";
        public const string RateRangesInvalid =
            "Tariff rate ranges must be contiguous from character 1 and end with an unbounded range.";
        public const string RatePriceInvalid = "Every tariff rate price must be positive.";
        public const string UnknownProvider = "The provider does not exist.";
        public const string UnknownMessageType = "The message type does not exist.";
        public const string EffectiveRangeOverlap =
            "An active tariff already covers this provider, message type, encoding, and time range.";
        public const string InvalidId = "A valid tariff id is required.";
        public const string NotFound = "The tariff does not exist.";
    }

    public static class DeliveryReports
    {
        public const string UnknownMessage = "The message does not exist.";
        public const string MessageRequired = "A valid message id is required.";
        public const string InvalidStatus = "The normalized status is not recognized.";
    }

    public static class ReferenceData
    {
        public const string MessageTypeExists = "A message type with this id or code already exists.";
        public const string MessageTypeIdRequired = "A non-zero message type id is required.";
        public const string NameRequired = "A name is required.";
        public const string CodeRequired = "A code is required.";
        public const string SenderLineUnknownReference = "The provider or customer does not exist.";
        public const string SenderLineUnknownProvider = "The provider does not exist.";
        public const string SenderLineUnknownCustomer = "The customer does not exist.";
        public const string ProviderRequired = "A provider id is required.";
        public const string InvalidSenderLine = "A sender line id must be positive.";
        public const string SenderLineNotFound = "The sender line does not exist.";
        public const string LineNumberRequired = "A line number is required.";
        public const string InvalidCustomer = "A customer id must be positive when provided.";
        public const string InvalidProviderAccount = "A provider account id must be positive when provided.";
        public const string SharedLineHasOwner = "A shared sender line cannot be assigned to one customer.";
        public const string SenderLineUnknownProviderAccount = "The provider account does not exist.";
        public const string ProviderAccountProviderMismatch =
            "The provider account must belong to the same provider as the sender line.";
        public const string InactiveProviderAccount = "The provider account is inactive.";
        public const string GeoSectionTypeInvalid = "A valid section type is required.";
        public const string CustomerCodeExists = "A customer with this code already exists.";
        public const string GeoUnknownParent = "The parent section does not exist.";
        public const string CustomerNameRequired = "A customer name is required.";
        public const string CustomerCodeRequired = "A customer code is required.";
        public const string ProviderCodeExists = "A provider with this code already exists.";
        public const string ProviderNameRequired = "A provider name is required.";
        public const string ProviderCodeRequired = "A provider code is required.";
        public const string BaseUrlRequired = "A base URL is required.";
        public const string CustomerNotFound = "The customer does not exist.";
        public const string InvalidProvider = "A provider id is required.";
        public const string ProviderNotFound = "The provider does not exist.";
        public const string InvalidMessageType = "A message type id is required.";
        public const string MessageTypeNotFound = "The message type does not exist.";
        public const string InvalidGeoSection = "A geographic section id must be positive.";
        public const string GeoSectionNotFound = "The geographic section does not exist.";
        public const string GeoSectionHasActiveChildren =
            "A geographic section with non-deleted children cannot be deleted.";
    }

    public static class Providers
    {
        public static string NotRegistered(string providerCode) =>
            $"No SMS provider implementation is registered for provider '{providerCode}'.";

        public static string NoMagfaAccountForSenderLine(string senderLine) =>
            $"No Magfa account is configured for sender line '{senderLine}'.";

        public static string NoKavenegarAccountForSenderLine(string senderLine) =>
            $"No Kavenegar account is configured for sender line '{senderLine}'.";

        public static string MagfaRequestStatus(int status) => $"Magfa request-level status {status}.";
        public const string MagfaTransport = "HTTP transport error while calling Magfa.";
        public const string MagfaTimeout = "Magfa request timed out.";
        public static string MagfaHttpStatus(int statusCode) => $"Magfa returned HTTP {statusCode}.";
        public static string MagfaBadJson(string message) => $"Could not parse Magfa response: {message}";
        public const string MagfaEmptyBody = "Magfa returned an empty response body.";
        public const string MagfaMissingResult = "Magfa returned no result for this message.";
        public const string MagfaMissingId = "Magfa accepted the message but returned no id.";
        public static string MagfaMessageStatus(int status) => $"Magfa message status {status}.";
        public static string MagfaRejectedStatus(int status) => $"Magfa status {status}.";
        public static string MagfaRejectedConfigurationStatus(int status) =>
            $"Magfa status {status} (configuration).";

        public const string KavenegarTransport = "HTTP transport error while calling Kavenegar.";
        public const string KavenegarTimeout = "Kavenegar request timed out.";
        public static string KavenegarHttpStatus(int statusCode) => $"Kavenegar returned HTTP {statusCode}.";
        public static string KavenegarBadJson(string message) => $"Could not parse Kavenegar response: {message}";
        public const string KavenegarEmptyBody = "Kavenegar returned an empty response body.";
        public const string KavenegarMissingResult = "Kavenegar returned no result for this message.";
        public const string KavenegarMissingId = "Kavenegar accepted the message but returned no id.";
        public static string KavenegarRejectedStatus(int status) => $"Kavenegar status {status}.";
        public static string KavenegarMessageStatus(long messageId, int status) =>
            $"Kavenegar message {messageId} returned unhandled status {status}.";
        public static string KavenegarRequestStatus(string method, int status, string message) =>
            $"Kavenegar {method} returned status {status}: {message}";

        public const string ProviderThrew = "The SMS provider failed unexpectedly.";
    }

    public static class ProviderAccounts
    {
        public const string ProviderCodeRequired = "A provider code is required.";
        public const string DisplayNameRequired = "A display name is required.";
        public const string AuthTypeRequired = "An authentication type is required.";
        public const string SecretRequired = "A secret is required.";
        public const string InvalidId = "A valid provider account id is required.";
        public const string NotFound = "The provider account does not exist.";
        public const string UnknownProvider = "The provider does not exist.";
        public const string UnsupportedProvider = "This provider is not supported for account validation.";
        public const string InvalidAuthType = "The authentication type is not valid for this provider.";
        public const string MagfaUsernameRequired = "Magfa account settings must include a username.";
        public const string MagfaDomainRequired = "Magfa account settings must include a domain.";
    }
}
