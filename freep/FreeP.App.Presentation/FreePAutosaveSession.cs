using Free.Shared.AppServices;
using FreeP.Core.IO;
using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public sealed record FreePAutosavePorts(
    Func<string?> GetOriginalFilePath,
    Func<string> GetDisplayName,
    Func<bool> GetIsDirty,
    Func<int> GetDirtyGeneration,
    Action<Action<Presentation>> ExecuteWithPresentation);

public enum FreePRecoveryRestoreExceptionPolicy
{
    PreserveCandidate,
    QuarantineCandidate
}

/// <summary>
/// FreeP's compatibility facade over the shared, renderer-neutral autosave lifecycle.
/// Native hosts retain scheduling and presentation access; this adapter supplies PPTX I/O policy.
/// </summary>
public sealed class FreePAutosaveSession : IDisposable
{
    private static readonly AutosaveDocumentSessionOptions<Presentation> SessionOptions = new(
        Interval: TimeSpan.FromSeconds(60),
        WriteSnapshot: (presentation, path) => PptxPackageWriter.Write(presentation, path),
        ReadSnapshot: PptxPackageReader.Read);

    private readonly AutosaveSnapshotStore _store;
    private readonly AutosaveDocumentSession<Presentation> _session;

    public static TimeSpan DefaultInterval => SessionOptions.Interval;

    public FreePAutosaveSession(FreePAutosavePorts ports)
        : this(
            ports,
            AutosaveSnapshotStore.CreateDefault(PlatformApplicationDataPathProvider.LocalInstance),
            CreateSnapshotId())
    {
    }

    public FreePAutosaveSession(FreePAutosavePorts ports, AutosaveSnapshotStore store)
        : this(ports, store, CreateSnapshotId())
    {
    }

    public FreePAutosaveSession(
        FreePAutosavePorts ports,
        AutosaveSnapshotStore store,
        string snapshotId)
    {
        ArgumentNullException.ThrowIfNull(ports);

        _store = store;
        _session = new AutosaveDocumentSession<Presentation>(
            new AutosaveDocumentPorts<Presentation>(
                ports.GetOriginalFilePath,
                ports.GetDisplayName,
                ports.GetIsDirty,
                ports.GetDirtyGeneration,
                ports.ExecuteWithPresentation),
            SessionOptions,
            store,
            snapshotId);
    }

    public string SnapshotId => _session.SnapshotId;

    public static string CreateSnapshotId() =>
        AutosaveDocumentSession<Presentation>.CreateSnapshotId();

    public void Snapshot() => _session.Snapshot();

    public void TryEmergencySnapshot() => _session.TryEmergencySnapshot();

    public void CompleteCleanExit() => _session.CompleteCleanExit();

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
        FreePRecoveryRestoreExceptionPolicy exceptionPolicy =
            FreePRecoveryRestoreExceptionPolicy.PreserveCandidate) =>
        _session.CompleteRecovery(
            plan,
            accepted,
            restoreSnapshot,
            MapExceptionPolicy(exceptionPolicy));

    public bool CompletePresentationRecovery(
        AutosaveRecoveryPlan plan,
        bool accepted,
        Action<Presentation, string?> applyRecoveredPresentation,
        FreePRecoveryRestoreExceptionPolicy exceptionPolicy =
            FreePRecoveryRestoreExceptionPolicy.PreserveCandidate) =>
        _session.CompleteDocumentRecovery(
            plan,
            accepted,
            applyRecoveredPresentation,
            MapExceptionPolicy(exceptionPolicy));

    public void Dispose() => _session.Dispose();

    private static AutosaveRecoveryRestoreExceptionPolicy MapExceptionPolicy(
        FreePRecoveryRestoreExceptionPolicy exceptionPolicy) =>
        (AutosaveRecoveryRestoreExceptionPolicy)(int)exceptionPolicy;
}
