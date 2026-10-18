using SmsHubNext.Shared.Ui;

namespace SmsHubNext.Features.Administration.Ui;

public sealed class ApplicationErrorsService(UiOperations operations, ListApplicationErrorsHandler handler)
{
    public Task<UiResult<ApplicationErrorResults>> List(DateTime fromUtc, DateTime toUtc, CancellationToken token) =>
        operations.Run((identity, cancellationToken) => handler.Handle(fromUtc, toUtc, cancellationToken), token);
}
