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
/// FreeW's compatibility facade over the shared, renderer-neutral autosave lifecycle.
/// Native hosts retain scheduling and document access; this adapter supplies DOCX I/O policy.
/// </summary>
public sealed class FreeWAutosaveSession : IDisposable
{
    private static readonly AutosaveDocumentSessionOptions<TextDocument> SessionOptions = new(
        Interval: TimeSpan.FromSeconds(30),
        WriteSnapshot: (document, path) => DocxWriter.Write(document, path),
        ReadSnapshot: DocxReader.Read);

    private readonly AutosaveSnapshotStore _store;
    private readonly AutosaveDocumentSession<TextDocument> _session;

    public static TimeSpan DefaultInterval => SessionOptions.Interval;

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

        _store = store;
        _session = new AutosaveDocumentSession<TextDocument>(
            new AutosaveDocumentPorts<TextDocument>(
                ports.GetOriginalFilePath,
                ports.GetDisplayName,
                ports.GetIsDirty,
                ports.GetDirtyGeneration,
                ports.ExecuteWithDocument),
            SessionOptions,
            store,
            snapshotId);
    }

    public string SnapshotId => _session.SnapshotId;

    public static string CreateSnapshotId() =>
        AutosaveDocumentSession<TextDocument>.CreateSnapshotId();

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
        FreeWRecoveryRestoreExceptionPolicy exceptionPolicy =
            FreeWRecoveryRestoreExceptionPolicy.PreserveCandidate) =>
        _session.CompleteRecovery(
            plan,
            accepted,
            restoreSnapshot,
            MapExceptionPolicy(exceptionPolicy));

    public bool CompleteDocumentRecovery(
        AutosaveRecoveryPlan plan,
        bool accepted,
        Action<TextDocument, string?> applyRecoveredDocument,
        FreeWRecoveryRestoreExceptionPolicy exceptionPolicy =
            FreeWRecoveryRestoreExceptionPolicy.PreserveCandidate) =>
        _session.CompleteDocumentRecovery(
            plan,
            accepted,
            applyRecoveredDocument,
            MapExceptionPolicy(exceptionPolicy));

    public void Dispose() => _session.Dispose();

    private static AutosaveRecoveryRestoreExceptionPolicy MapExceptionPolicy(
        FreeWRecoveryRestoreExceptionPolicy exceptionPolicy) =>
        (AutosaveRecoveryRestoreExceptionPolicy)(int)exceptionPolicy;
}
