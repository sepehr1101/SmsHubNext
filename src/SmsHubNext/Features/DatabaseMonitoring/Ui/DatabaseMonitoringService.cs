using SmsHubNext.Shared.Ui;

namespace SmsHubNext.Features.DatabaseMonitoring.Ui;

public sealed class DatabaseMonitoringService(UiOperations operations, DatabaseMonitoringHandler handler)
{
    public Task<UiResult<DatabaseMonitorReport>> Read(DatabaseMonitorSection section, CancellationToken token, int sort = 2) =>
        operations.Run((identity, ct) => handler.Read(section, ct, sort), token);
    public Task<UiResult<DatabaseMonitorReport>> InspectIndex(int objectId, int indexId, int partition, CancellationToken token) =>
        operations.Run((identity, ct) => handler.InspectIndex(objectId, indexId, partition, ct), token);
    public Task<UiResult<IReadOnlyList<DatabaseGrowthPoint>>> Growth(CancellationToken token) =>
        operations.Run((identity, ct) => handler.Growth(ct), token);
}
