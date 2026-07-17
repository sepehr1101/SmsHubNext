using System.Collections.Concurrent;

namespace SmsHubNext.Shared.Health;

/// <summary>
/// In-memory heartbeat shared by the SQL-backed workers. It adds no database writes and resets
/// naturally with the process; persisted queue state remains the source of truth for recovery.
/// </summary>
public sealed class BackgroundWorkerHealthMonitor
{
    private readonly ConcurrentDictionary<string, WorkerState> _states = new(StringComparer.Ordinal);
    private readonly TimeProvider _clock;

    public BackgroundWorkerHealthMonitor(TimeProvider clock)
    {
        _clock = clock;
        CreatedAtUtc = _clock.GetUtcNow().UtcDateTime;
    }

    public DateTime CreatedAtUtc { get; }

    public void ReportStarted(string name) => GetState(name).ReportStarted(Now());

    public void ReportSucceeded(string name) => GetState(name).ReportSucceeded(Now());

    public void ReportFailed(string name) => GetState(name).ReportFailed(Now());

    public void ReportStopped(string name) => GetState(name).ReportStopped(Now());

    public IReadOnlyList<BackgroundWorkerHealthSnapshot> GetSnapshots()
    {
        List<BackgroundWorkerHealthSnapshot> snapshots = [];
        foreach (KeyValuePair<string, WorkerState> entry in _states.OrderBy(entry => entry.Key, StringComparer.Ordinal))
            snapshots.Add(entry.Value.Snapshot(entry.Key));

        return snapshots;
    }

    private WorkerState GetState(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return _states.GetOrAdd(name, static _ => new WorkerState());
    }

    private DateTime Now() => _clock.GetUtcNow().UtcDateTime;

    private sealed class WorkerState
    {
        private readonly object _gate = new();
        private bool _isRunning;
        private DateTime? _startedAtUtc;
        private DateTime? _lastSucceededAtUtc;
        private DateTime? _lastFailedAtUtc;
        private int _consecutiveFailures;

        public void ReportStarted(DateTime timestampUtc)
        {
            lock (_gate)
            {
                _isRunning = true;
                _startedAtUtc = timestampUtc;
                _consecutiveFailures = 0;
            }
        }

        public void ReportSucceeded(DateTime timestampUtc)
        {
            lock (_gate)
            {
                _isRunning = true;
                _lastSucceededAtUtc = timestampUtc;
                _consecutiveFailures = 0;
            }
        }

        public void ReportFailed(DateTime timestampUtc)
        {
            lock (_gate)
            {
                _lastFailedAtUtc = timestampUtc;
                _consecutiveFailures++;
            }
        }

        public void ReportStopped(DateTime timestampUtc)
        {
            lock (_gate)
            {
                _isRunning = false;
                _lastFailedAtUtc ??= timestampUtc;
            }
        }

        public BackgroundWorkerHealthSnapshot Snapshot(string name)
        {
            lock (_gate)
            {
                return new BackgroundWorkerHealthSnapshot(
                    name,
                    _isRunning,
                    _startedAtUtc,
                    _lastSucceededAtUtc,
                    _lastFailedAtUtc,
                    _consecutiveFailures);
            }
        }
    }
}

public sealed record BackgroundWorkerHealthSnapshot(
    string Name,
    bool IsRunning,
    DateTime? StartedAtUtc,
    DateTime? LastSucceededAtUtc,
    DateTime? LastFailedAtUtc,
    int ConsecutiveFailures);
