using SmsHubNext.Shared.Results;

namespace SmsHubNext.Features.ReferenceData;

public sealed class CreateSenderLineRequest
{
    /// <summary>The provider this line belongs to (README §4.5).</summary>
    public byte ProviderId { get; init; }

    /// <summary>The line number, e.g. <c>30001234</c> (ASCII).</summary>
    public string LineNumber { get; init; } = string.Empty;

    /// <summary>Whether the line is shared across customers (shared <c>3000…</c>/<c>4040…</c>) or dedicated.</summary>
    public bool IsSharedLine { get; init; }

    /// <summary>New lines are active by default; set false to register a line that cannot yet send.</summary>
    public bool IsActive { get; init; } = true;

    public Result Validate()
    {
        if (ProviderId == 0)
            return Error.Validation("sender_lines.provider_required", "A provider id is required.");

        if (string.IsNullOrWhiteSpace(LineNumber))
            return Error.Validation("sender_lines.line_number_required", "A line number is required.");

        return Result.Success();
    }
}

public sealed record CreateSenderLineResponse(short Id);
