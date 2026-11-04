using System.Text.Json;

namespace SmsHubNext.Shared.Results;

// Persisted JSON values: append new members without renumbering existing ones.
public enum FailureOrigin { Application = 0, Network = 1, EsbGateway = 2, SmsProvider = 3 }
public enum FailureStage { Validation = 0, Token = 1, Submission = 2, DeliveryQuery = 3 }
public enum FailureImpact { NotSubmitted = 0, SubmissionUnknown = 1, DeliveryQueryUnavailable = 2 }
public enum FailureAction { RetryAutomatically = 0, FixConfiguration = 1, Review = 2 }

/// <summary>Allow-listed diagnostic fields only: never response bodies, headers, tokens or exception text.</summary>
public sealed record OperationFailure(
    FailureOrigin Origin, FailureStage Stage, string Code, FailureImpact Impact, FailureAction Action,
    int? HttpStatus = null, int? UpstreamStatus = null, int? BusinessCode = null)
{
    public string ToJson() => JsonSerializer.Serialize(this);

    public static OperationFailure? FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            OperationFailure? failure = JsonSerializer.Deserialize<OperationFailure>(json);
            return failure is not null && !string.IsNullOrWhiteSpace(failure.Code) &&
                Enum.IsDefined(failure.Origin) && Enum.IsDefined(failure.Stage) &&
                Enum.IsDefined(failure.Impact) && Enum.IsDefined(failure.Action) ? failure : null;
        }
        catch (JsonException) { return null; }
    }
}
