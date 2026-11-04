using SmsHubNext.Shared.Results;

namespace SmsHubNext.Features.Providers.Esb;

internal static class EsbFailure
{
    public static Error Create(string code, string message, FailureStage stage, FailureOrigin origin = FailureOrigin.EsbGateway,
        int? httpStatus = null, int? upstreamStatus = null, int? businessCode = null) =>
        Error.Provider(code, message) with
        {
            Diagnostic = new(origin, stage, code,
                stage == FailureStage.Token || stage == FailureStage.Validation ? FailureImpact.NotSubmitted
                    : stage == FailureStage.Submission ? FailureImpact.SubmissionUnknown : FailureImpact.DeliveryQueryUnavailable,
                stage == FailureStage.Validation ? FailureAction.FixConfiguration : FailureAction.RetryAutomatically,
                httpStatus, upstreamStatus, businessCode),
        };
}
