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
/// Owns FreeP's renderer-neutral autosave and recovery lifecycle. Native hosts schedule ticks,
/// marshal editor access, render prompts, and manage window lifetime.
///
/// <para>
/// Mirrors FreeW's <c>FreeWAutosaveSession</c> one-for-one on top of the same shared
/// <see cref="AutosaveSnapshotStore"/>/<see cref="AutosaveSnapshotCoordinator"/> engine; only the
/// serialization format differs (a .pptx package written by <see cref="PptxPackageWriter"/> instead
/// of a .docx). Before this existed FreeP had no autosave at all: a crash lost every edit back to
/// the last manual save.
/// </para>
/// </summary>
public sealed class FreePAutosaveSession : IDisposable
{
    /// <summary>
    /// Periodic autosave cadence. Longer than FreeW's 30 s because serializing a presentation
    /// writes a whole OPC package including embedded images and media, which is materially more
    /// expensive than a .docx of comparable authoring effort; the emergency snapshot
    /// (<see cref="TryEmergencySnapshot"/>) is what bounds the actual worst-case loss on a crash,
    /// not this interval.
    /// </summary>
    public static TimeSpan DefaultInterval { get; } = TimeSpan.FromSeconds(60);

    private readonly AutosaveSnapshotStore _store;
    private readonly AutosaveSnapshotCoordinator _coordinator;
    private readonly SnapshotSource _source;

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
        ArgumentNullException.ThrowIfNull(ports.GetOriginalFilePath);
        ArgumentNullException.ThrowIfNull(ports.GetDisplayName);
        ArgumentNullException.ThrowIfNull(ports.GetIsDirty);
        ArgumentNullException.ThrowIfNull(ports.GetDirtyGeneration);
        ArgumentNullException.ThrowIfNull(ports.ExecuteWithPresentation);
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

    /// <summary>
    /// Best-effort emergency snapshot for crash handlers. Bypasses the periodic-tick generation
    /// gate (so it still captures the latest dirty state even when nothing has changed since the
    /// last periodic tick) but still requires the presentation to be dirty. Must never throw --
    /// delegates to <see cref="AutosaveSnapshotCoordinator.TryEmergencySnapshot"/>, which is
    /// never-throw by design.
    /// </summary>
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
            FreePRecoveryRestoreExceptionPolicy.PreserveCandidate)
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
        catch when (exceptionPolicy == FreePRecoveryRestoreExceptionPolicy.QuarantineCandidate)
        {
            recovered = false;
        }

        AutosaveRecoveryPlanner.Complete(plan, accepted: true, recovered: recovered);
        return recovered;
    }

    /// <summary>
    /// Recovery variant for hosts that want the deserialized <see cref="Presentation"/> handed to
    /// them rather than a snapshot path -- mirrors FreeW's <c>CompleteDocumentRecovery</c>.
    /// </summary>
    public bool CompletePresentationRecovery(
        AutosaveRecoveryPlan plan,
        bool accepted,
        Action<Presentation, string?> applyRecoveredPresentation,
        FreePRecoveryRestoreExceptionPolicy exceptionPolicy =
            FreePRecoveryRestoreExceptionPolicy.PreserveCandidate)
    {
        ArgumentNullException.ThrowIfNull(applyRecoveredPresentation);

        return CompleteRecovery(
            plan,
            accepted,
            (snapshotPath, originalPath) =>
            {
                var presentation = PptxPackageReader.Read(snapshotPath);
                applyRecoveredPresentation(presentation, originalPath);
                return true;
            },
            exceptionPolicy);
    }

    public void Dispose() => _coordinator.Dispose();

    private sealed class SnapshotSource : IAutosaveSnapshotSource
    {
        private readonly FreePAutosavePorts _ports;

        public SnapshotSource(FreePAutosavePorts ports)
        {
            _ports = ports;
        }

        public string? OriginalFilePath => _ports.GetOriginalFilePath();
        public string DisplayName => _ports.GetDisplayName();
        public bool IsDirty => _ports.GetIsDirty();
        public int DirtyGeneration => _ports.GetDirtyGeneration();

        // The store names snapshots ".fxl" for every sister app; PptxPackageWriter's path overload
        // falls back to the presentation's own PackageKind for unknown extensions, so a .pptm/.potx
        // original round-trips through recovery as its own family rather than being downgraded.
        public void WriteSnapshot(string snapshotPath) =>
            _ports.ExecuteWithPresentation(presentation =>
                PptxPackageWriter.Write(presentation, snapshotPath));
    }
}
