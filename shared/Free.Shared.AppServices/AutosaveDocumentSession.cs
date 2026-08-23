namespace Free.Shared.AppServices;

public sealed record AutosaveDocumentPorts<TDocument>(
    Func<string?> GetOriginalFilePath,
    Func<string> GetDisplayName,
    Func<bool> GetIsDirty,
    Func<int> GetDirtyGeneration,
    Action<Action<TDocument>> ExecuteWithDocument);

public sealed record AutosaveDocumentSessionOptions<TDocument>(
    TimeSpan Interval,
    Action<TDocument, string> WriteSnapshot,
    Func<string, TDocument> ReadSnapshot);

public enum AutosaveRecoveryRestoreExceptionPolicy
{
    PreserveCandidate,
    QuarantineCandidate
}

/// <summary>
/// Owns the renderer- and document-neutral autosave lifecycle. Apps supply document access,
/// serialization, deserialization, and cadence while native hosts own scheduling and prompts.
/// </summary>
public sealed class AutosaveDocumentSession<TDocument> : IDisposable
{
    private readonly AutosaveSnapshotCoordinator _coordinator;
    private readonly AutosaveDocumentSessionOptions<TDocument> _options;
    private readonly SnapshotSource _source;

    public AutosaveDocumentSession(
        AutosaveDocumentPorts<TDocument> ports,
        AutosaveDocumentSessionOptions<TDocument> options,
        AutosaveSnapshotStore store,
        string snapshotId)
    {
        ArgumentNullException.ThrowIfNull(ports);
        ArgumentNullException.ThrowIfNull(ports.GetOriginalFilePath);
        ArgumentNullException.ThrowIfNull(ports.GetDisplayName);
        ArgumentNullException.ThrowIfNull(ports.GetIsDirty);
        ArgumentNullException.ThrowIfNull(ports.GetDirtyGeneration);
        ArgumentNullException.ThrowIfNull(ports.ExecuteWithDocument);
        ArgumentNullException.ThrowIfNull(options);
        if (options.Interval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(options), "Autosave interval must be positive.");
        ArgumentNullException.ThrowIfNull(options.WriteSnapshot);
        ArgumentNullException.ThrowIfNull(options.ReadSnapshot);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentException.ThrowIfNullOrWhiteSpace(snapshotId);

        _options = options;
        _source = new SnapshotSource(ports, options.WriteSnapshot);
        _coordinator = new AutosaveSnapshotCoordinator(store, snapshotId);
    }

    public TimeSpan Interval => _options.Interval;

    public string SnapshotId => _coordinator.SnapshotId;

    public static string CreateSnapshotId()
    {
        var launchTag = AutosaveSnapshotStore.LaunchId.ToString("N")[..8];
        var windowTag = Guid.NewGuid().ToString("N")[..8];
        return FormattableString.Invariant(
            $"recovery-{Environment.ProcessId}-{launchTag}-{windowTag}");
    }

    public void Snapshot() => _coordinator.Snapshot(_source);

    public void TryEmergencySnapshot() => _coordinator.TryEmergencySnapshot(_source);

    public void CompleteCleanExit()
    {
        try
        {
            _coordinator.DeleteSnapshot();
        }
        catch
        {
            // Autosave cleanup must not block a normal close.
        }
        finally
        {
            _coordinator.Dispose();
        }
    }

    public bool CompleteRecovery(
        IAutosaveRecoveryPlan plan,
        bool accepted,
        Func<string, string?, bool> restoreSnapshot,
        AutosaveRecoveryRestoreExceptionPolicy exceptionPolicy =
            AutosaveRecoveryRestoreExceptionPolicy.PreserveCandidate)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(restoreSnapshot);

        if (!accepted)
        {
            AutosaveRecoveryPlannerCore.Complete(plan, accepted: false, recovered: false);
            return false;
        }

        var candidate = plan.Candidate;
        bool recovered;
        try
        {
            recovered = restoreSnapshot(
                candidate.SnapshotPath,
                candidate.Sidecar.OriginalFilePath);
        }
        catch when (exceptionPolicy == AutosaveRecoveryRestoreExceptionPolicy.QuarantineCandidate)
        {
            recovered = false;
        }

        AutosaveRecoveryPlannerCore.Complete(plan, accepted: true, recovered);
        return recovered;
    }

    public bool CompleteDocumentRecovery(
        IAutosaveRecoveryPlan plan,
        bool accepted,
        Action<TDocument, string?> applyRecoveredDocument,
        AutosaveRecoveryRestoreExceptionPolicy exceptionPolicy =
            AutosaveRecoveryRestoreExceptionPolicy.PreserveCandidate)
    {
        ArgumentNullException.ThrowIfNull(applyRecoveredDocument);

        return CompleteRecovery(
            plan,
            accepted,
            (snapshotPath, originalPath) =>
            {
                var document = _options.ReadSnapshot(snapshotPath);
                applyRecoveredDocument(document, originalPath);
                return true;
            },
            exceptionPolicy);
    }

    public void Dispose() => _coordinator.Dispose();

    private sealed class SnapshotSource : IAutosaveSnapshotSource
    {
        private readonly AutosaveDocumentPorts<TDocument> _ports;
        private readonly Action<TDocument, string> _writeSnapshot;

        public SnapshotSource(
            AutosaveDocumentPorts<TDocument> ports,
            Action<TDocument, string> writeSnapshot)
        {
            _ports = ports;
            _writeSnapshot = writeSnapshot;
        }

        public string? OriginalFilePath => _ports.GetOriginalFilePath();
        public string DisplayName => _ports.GetDisplayName();
        public bool IsDirty => _ports.GetIsDirty();
        public int DirtyGeneration => _ports.GetDirtyGeneration();

        public void WriteSnapshot(string snapshotPath) =>
            _ports.ExecuteWithDocument(document => _writeSnapshot(document, snapshotPath));
    }
}
