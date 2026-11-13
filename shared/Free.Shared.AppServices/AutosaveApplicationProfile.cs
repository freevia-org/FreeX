namespace Free.Shared.AppServices;

/// <summary>One recovery offer's worth of state: the candidate plus the display name it resolved to.</summary>
public sealed record AutosaveRecoveryPlan(
    AutosaveRecoveryCandidate Candidate,
    string DisplayName) : IAutosaveRecoveryPlan;

/// <summary>
/// A single prompt shown for one recovery plan. The product wording comes from
/// <see cref="AutosaveRecoveryPromptText"/>; the prompt mode is chosen by the host.
/// </summary>
public sealed record AutosaveRecoveryOffer(
    AutosaveRecoveryPlan Recovery,
    int RemainingCount,
    AutosaveRecoveryPromptMode PromptMode,
    AutosaveRecoveryPromptText PromptText)
{
    public string Prompt => AutosaveRecoveryPromptFormatter.Format(
        Recovery.DisplayName,
        RemainingCount,
        PromptMode,
        PromptText);
}

/// <summary>
/// The product-specific, document-type-neutral half of an application's autosave contract: the
/// product name and unsaved-item noun used in prompts, the fallback display name for unnamed
/// snapshots, and the non-localised defaults for the recovery dialog chrome. One instance per
/// application drives planning, offer creation, text resolution, and the recovery sequence.
/// </summary>
public sealed class AutosaveRecoveryProfile
{
    private readonly AutosaveRecoveryTextResolver _textResolver;

    public AutosaveRecoveryProfile(
        AutosaveRecoveryPromptText promptText,
        string unnamedItemFallback,
        AutosaveRecoveryTextDefaults textDefaults)
    {
        ArgumentNullException.ThrowIfNull(promptText);
        ArgumentException.ThrowIfNullOrWhiteSpace(promptText.ProductName);
        ArgumentException.ThrowIfNullOrWhiteSpace(promptText.UnsavedItemPlural);
        ArgumentException.ThrowIfNullOrWhiteSpace(unnamedItemFallback);
        ArgumentNullException.ThrowIfNull(textDefaults);

        PromptText = promptText;
        UnnamedItemFallback = unnamedItemFallback;
        _textResolver = new AutosaveRecoveryTextResolver(textDefaults);
    }

    public AutosaveRecoveryPromptText PromptText { get; }

    /// <summary>Display name used when a snapshot sidecar carries none, e.g. "a document".</summary>
    public string UnnamedItemFallback { get; }

    public IReadOnlyList<string> RequiredResourceKeys => _textResolver.RequiredResourceKeys;

    public AutosaveRecoveryTextValues ResolveText(Func<string, string?>? getText = null) =>
        _textResolver.Resolve(getText);

    public AutosaveRecoveryPlan? PlanLatest(AutosaveSnapshotStore store) =>
        AutosaveRecoveryPlannerCore.PlanLatest(store, UnnamedItemFallback, CreatePlan);

    public AutosaveRecoveryPlan? PlanLatest(IEnumerable<AutosaveRecoveryCandidate> candidates) =>
        AutosaveRecoveryPlannerCore.PlanLatest(candidates, UnnamedItemFallback, CreatePlan);

    public IReadOnlyList<AutosaveRecoveryPlan> PlanAll(AutosaveSnapshotStore store) =>
        AutosaveRecoveryPlannerCore.PlanAll(store, UnnamedItemFallback, CreatePlan);

    public AutosaveRecoveryDisposition Complete(
        AutosaveRecoveryPlan plan,
        bool accepted,
        bool recovered) =>
        AutosaveRecoveryPlannerCore.Complete(plan, accepted, recovered);

    public AutosaveRecoveryOffer CreateOffer(
        AutosaveRecoveryPlan recovery,
        int remainingCount,
        AutosaveRecoveryPromptMode promptMode) =>
        new(recovery, remainingCount, promptMode, PromptText);

    /// <summary>Runs the shared recovery sequencer with this profile's offer type.</summary>
    public ValueTask<AutosaveRecoveryWorkflowResult> RunAsync(
        IReadOnlyList<AutosaveRecoveryPlan> recoveries,
        AutosaveRecoveryPromptMode promptMode,
        Func<AutosaveRecoveryOffer, ValueTask<bool>> promptAsync,
        Func<AutosaveRecoveryPlan, bool, ValueTask<bool>> completeRecoveryAsync) =>
        AutosaveRecoveryWorkflow.RunAsync(
            recoveries,
            promptMode,
            (recovery, remainingCount) => CreateOffer(recovery, remainingCount, promptMode),
            promptAsync,
            completeRecoveryAsync);

    private static AutosaveRecoveryPlan CreatePlan(
        AutosaveRecoveryCandidate candidate,
        string displayName) =>
        new(candidate, displayName);
}

/// <summary>
/// An application's complete autosave profile for one document type: the recovery chrome plus the
/// cadence and the snapshot serializer pair. Applications declare one static instance.
/// </summary>
public sealed record AutosaveApplicationProfile<TDocument>(
    AutosaveRecoveryProfile Recovery,
    TimeSpan Interval,
    Action<TDocument, string> WriteSnapshot,
    Func<string, TDocument> ReadSnapshot)
{
    public AutosaveDocumentSessionOptions<TDocument> SessionOptions =>
        new(Interval, WriteSnapshot, ReadSnapshot);
}

/// <summary>
/// Generic application autosave facade over <see cref="AutosaveDocumentSession{TDocument}"/>: it binds
/// the shared lifecycle to one <see cref="AutosaveApplicationProfile{TDocument}"/> and adds the
/// profile-driven recovery planning. Native hosts retain scheduling and document access.
/// </summary>
public class AutosaveApplicationSession<TDocument> : IDisposable
{
    private readonly AutosaveApplicationProfile<TDocument> _profile;
    private readonly AutosaveSnapshotStore _store;
    private readonly AutosaveDocumentSession<TDocument> _session;

    public AutosaveApplicationSession(
        AutosaveApplicationProfile<TDocument> profile,
        AutosaveDocumentPorts<TDocument> ports)
        : this(
            profile,
            ports,
            AutosaveSnapshotStore.CreateDefault(PlatformApplicationDataPathProvider.LocalInstance),
            CreateSnapshotId())
    {
    }

    public AutosaveApplicationSession(
        AutosaveApplicationProfile<TDocument> profile,
        AutosaveDocumentPorts<TDocument> ports,
        AutosaveSnapshotStore store)
        : this(profile, ports, store, CreateSnapshotId())
    {
    }

    public AutosaveApplicationSession(
        AutosaveApplicationProfile<TDocument> profile,
        AutosaveDocumentPorts<TDocument> ports,
        AutosaveSnapshotStore store,
        string snapshotId)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(ports);

        _profile = profile;
        _store = store;
        _session = new AutosaveDocumentSession<TDocument>(
            ports,
            profile.SessionOptions,
            store,
            snapshotId);
    }

    public string SnapshotId => _session.SnapshotId;

    public static string CreateSnapshotId() =>
        AutosaveDocumentSession<TDocument>.CreateSnapshotId();

    public void Snapshot() => _session.Snapshot();

    public void TryEmergencySnapshot() => _session.TryEmergencySnapshot();

    public void CompleteCleanExit() => _session.CompleteCleanExit();

    public AutosaveRecoveryPlan? PlanLatestRecovery() =>
        _profile.Recovery.PlanLatest(_store);

    public IReadOnlyList<AutosaveRecoveryPlan> PlanRecoveries() =>
        _profile.Recovery.PlanAll(_store);

    public AutosaveRecoveryDisposition CompleteRecoveryResult(
        AutosaveRecoveryPlan plan,
        bool accepted,
        bool recovered) =>
        _profile.Recovery.Complete(plan, accepted, recovered);

    public bool CompleteRecovery(
        AutosaveRecoveryPlan plan,
        bool accepted,
        Func<string, string?, bool> restoreSnapshot,
        AutosaveRecoveryRestoreExceptionPolicy exceptionPolicy =
            AutosaveRecoveryRestoreExceptionPolicy.PreserveCandidate) =>
        _session.CompleteRecovery(plan, accepted, restoreSnapshot, exceptionPolicy);

    public bool CompleteDocumentRecovery(
        AutosaveRecoveryPlan plan,
        bool accepted,
        Action<TDocument, string?> applyRecoveredDocument,
        AutosaveRecoveryRestoreExceptionPolicy exceptionPolicy =
            AutosaveRecoveryRestoreExceptionPolicy.PreserveCandidate) =>
        _session.CompleteDocumentRecovery(plan, accepted, applyRecoveredDocument, exceptionPolicy);

    public void Dispose() => _session.Dispose();
}
