using Free.Shared.AppServices;
using FreeW.Core.IO;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Shell;

public sealed record FreeWAutosavePorts(
    Func<string?> GetOriginalFilePath,
    Func<string> GetDisplayName,
    Func<bool> GetIsDirty,
    Func<int> GetDirtyGeneration,
    Action<Action<TextDocument>> ExecuteWithDocument);

public enum FreeWRecoveryRestoreExceptionPolicy
{
    PreserveCandidate,
    QuarantineCandidate
}

/// <summary>
/// Owns FreeW's renderer-neutral autosave and recovery lifecycle. Native hosts schedule ticks,
/// marshal editor access, render prompts, and manage window lifetime.
/// </summary>
public sealed class FreeWAutosaveSession : IDisposable
{
    public static TimeSpan DefaultInterval { get; } = TimeSpan.FromSeconds(30);

    private readonly AutosaveSnapshotStore _store;
    private readonly AutosaveSnapshotCoordinator _coordinator;
    private readonly SnapshotSource _source;

    public FreeWAutosaveSession(FreeWAutosavePorts ports)
        : this(
            ports,
            AutosaveSnapshotStore.CreateDefault(PlatformApplicationDataPathProvider.LocalInstance),
            CreateSnapshotId())
    {
    }

    public FreeWAutosaveSession(FreeWAutosavePorts ports, AutosaveSnapshotStore store)
        : this(ports, store, CreateSnapshotId())
    {
    }

    public FreeWAutosaveSession(
        FreeWAutosavePorts ports,
        AutosaveSnapshotStore store,
        string snapshotId)
    {
        ArgumentNullException.ThrowIfNull(ports);
        ArgumentNullException.ThrowIfNull(ports.GetOriginalFilePath);
        ArgumentNullException.ThrowIfNull(ports.GetDisplayName);
        ArgumentNullException.ThrowIfNull(ports.GetIsDirty);
        ArgumentNullException.ThrowIfNull(ports.GetDirtyGeneration);
        ArgumentNullException.ThrowIfNull(ports.ExecuteWithDocument);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentException.ThrowIfNullOrWhiteSpace(snapshotId);

        _store = store;
        _source = new SnapshotSource(ports);
        _coordinator = new AutosaveSnapshotCoordinator(store, snapshotId);
    }

    public string SnapshotId => _coordinator.SnapshotId;

    public static string CreateSnapshotId()
    {
        var launchTag = AutosaveSnapshotStore.LaunchId.ToString("N")[..8];
        var windowTag = Guid.NewGuid().ToString("N")[..8];
        return FormattableString.Invariant(
            $"recovery-{Environment.ProcessId}-{launchTag}-{windowTag}");
    }

    public void Snapshot() => _coordinator.Snapshot(_source);

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

    public AutosaveRecoveryPlan? PlanLatestRecovery() =>
        AutosaveRecoveryPlanner.PlanLatest(_store);

    public IReadOnlyList<AutosaveRecoveryPlan> PlanRecoveries() =>
        AutosaveRecoveryPlanner.PlanAll(_store);

    public AutosaveRecoveryDisposition CompleteRecoveryResult(
        AutosaveRecoveryPlan plan,
        bool accepted,
        bool recovered) =>
        AutosaveRecoveryPlanner.Complete(plan, accepted, recovered);

    public bool CompleteRecovery(
        AutosaveRecoveryPlan plan,
        bool accepted,
        Func<string, string?, bool> restoreSnapshot,
        FreeWRecoveryRestoreExceptionPolicy exceptionPolicy =
            FreeWRecoveryRestoreExceptionPolicy.PreserveCandidate)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(restoreSnapshot);

        if (!accepted)
        {
            AutosaveRecoveryPlanner.Complete(plan, accepted: false, recovered: false);
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
        catch when (exceptionPolicy == FreeWRecoveryRestoreExceptionPolicy.QuarantineCandidate)
        {
            recovered = false;
        }

        AutosaveRecoveryPlanner.Complete(plan, accepted: true, recovered: recovered);
        return recovered;
    }

    public bool CompleteDocumentRecovery(
        AutosaveRecoveryPlan plan,
        bool accepted,
        Action<TextDocument, string?> applyRecoveredDocument,
        FreeWRecoveryRestoreExceptionPolicy exceptionPolicy =
            FreeWRecoveryRestoreExceptionPolicy.PreserveCandidate)
    {
        ArgumentNullException.ThrowIfNull(applyRecoveredDocument);

        return CompleteRecovery(
            plan,
            accepted,
            (snapshotPath, originalPath) =>
            {
                var document = DocxReader.Read(snapshotPath);
                applyRecoveredDocument(document, originalPath);
                return true;
            },
            exceptionPolicy);
    }

    public void Dispose() => _coordinator.Dispose();

    private sealed class SnapshotSource : IAutosaveSnapshotSource
    {
        private readonly FreeWAutosavePorts _ports;

        public SnapshotSource(FreeWAutosavePorts ports)
        {
            _ports = ports;
        }

        public string? OriginalFilePath => _ports.GetOriginalFilePath();
        public string DisplayName => _ports.GetDisplayName();
        public bool IsDirty => _ports.GetIsDirty();
        public int DirtyGeneration => _ports.GetDirtyGeneration();

        public void WriteSnapshot(string snapshotPath) =>
            _ports.ExecuteWithDocument(document => DocxWriter.Write(document, snapshotPath));
    }
}
