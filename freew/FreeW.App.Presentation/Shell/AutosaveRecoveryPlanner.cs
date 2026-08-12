using Free.Shared.AppServices;

namespace FreeW.App.Presentation.Shell;

public enum AutosaveRecoveryDisposition
{
    Keep,
    Delete,
    Quarantine
}

public sealed record AutosaveRecoveryPlan(
    AutosaveRecoveryCandidate Candidate,
    string DisplayName);

public sealed record AutosaveRecoveryText(
    string Title,
    string RecoverButton,
    string SkipButton,
    string NoDocumentsMessage,
    string FailureMessageFormat);

public static class AutosaveRecoveryTextCatalog
{
    private static readonly ResourceTextDescriptor[] Texts =
    [
        new("Autosave_Recovery_Title", "FreeW - Recover"),
        new("Autosave_Recovery_Recover_Button", "Recover"),
        new("Autosave_Recovery_Skip_Button", "Skip"),
        new("Autosave_Recovery_None_Message", "No unsaved documents were found."),
        new("Autosave_Recovery_Failure_Message_Format", "Could not recover the document.\n\n{0}"),
    ];

    public static IReadOnlyList<string> RequiredResourceKeys =>
        Texts.Select(text => text.ResourceKey).ToArray();

    public static AutosaveRecoveryText Resolve(Func<string, string?>? getText = null) =>
        new(
            Texts[0].Resolve(getText),
            Texts[1].Resolve(getText),
            Texts[2].Resolve(getText),
            Texts[3].Resolve(getText),
            Texts[4].Resolve(getText));
}

public static class AutosaveRecoveryPlanner
{
    public static AutosaveRecoveryPlan? PlanLatest(AutosaveSnapshotStore store)
    {
        ArgumentNullException.ThrowIfNull(store);

        return PlanLatest(store.ExcludeLiveOwned(store.EnumerateCandidates()));
    }

    public static AutosaveRecoveryPlan? PlanLatest(
        IEnumerable<AutosaveRecoveryCandidate> candidates)
    {
        var candidate = SelectLatest(candidates);
        return candidate is null
            ? null
            : new AutosaveRecoveryPlan(candidate, DisplayName(candidate));
    }

    public static IReadOnlyList<AutosaveRecoveryPlan> PlanAll(AutosaveSnapshotStore store)
    {
        ArgumentNullException.ThrowIfNull(store);

        return SelectAllOrdered(store.ExcludeLiveOwned(store.EnumerateCandidates()))
            .Select(candidate => new AutosaveRecoveryPlan(candidate, DisplayName(candidate)))
            .ToList();
    }

    public static AutosaveRecoveryCandidate? SelectLatest(
        IEnumerable<AutosaveRecoveryCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        return candidates
            .OrderByDescending(candidate => ParseTimestamp(candidate.Sidecar.TimestampUtc))
            .FirstOrDefault();
    }

    /// <summary>
    /// R133-remediation: crash recovery must enumerate and offer EVERY pending snapshot, not just
    /// the single latest one (<see cref="SelectLatest"/>) — otherwise a crash with two or more
    /// windows open only ever recovers one of them and the rest are orphaned on disk. Returns all
    /// candidates newest-first so callers can offer them one at a time in the same order
    /// <see cref="SelectLatest"/> would have picked the first from.
    /// </summary>
    public static IReadOnlyList<AutosaveRecoveryCandidate> SelectAllOrdered(
        IEnumerable<AutosaveRecoveryCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        return candidates
            .OrderByDescending(candidate => ParseTimestamp(candidate.Sidecar.TimestampUtc))
            .ToList();
    }

    public static string DisplayName(AutosaveRecoveryCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        return string.IsNullOrWhiteSpace(candidate.Sidecar.DisplayName)
            ? "a document"
            : candidate.Sidecar.DisplayName!;
    }

    public static AutosaveRecoveryDisposition ResolveDisposition(bool accepted, bool recovered) =>
        !accepted
            ? AutosaveRecoveryDisposition.Keep
            : recovered
                ? AutosaveRecoveryDisposition.Delete
                : AutosaveRecoveryDisposition.Quarantine;

    public static AutosaveRecoveryDisposition Complete(
        AutosaveRecoveryPlan plan,
        bool accepted,
        bool recovered)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var disposition = ResolveDisposition(accepted, recovered);
        ApplyDisposition(plan.Candidate, disposition);
        return disposition;
    }

    private static void ApplyDisposition(
        AutosaveRecoveryCandidate candidate,
        AutosaveRecoveryDisposition disposition)
    {
        switch (disposition)
        {
            case AutosaveRecoveryDisposition.Delete:
                AutosaveSnapshotStore.DeleteCandidate(candidate);
                break;
            case AutosaveRecoveryDisposition.Quarantine:
                AutosaveSnapshotStore.QuarantineCandidate(candidate);
                break;
        }
    }

    private static DateTimeOffset ParseTimestamp(string? timestampUtc) =>
        DateTimeOffset.TryParse(timestampUtc, out var timestamp)
            ? timestamp
            : DateTimeOffset.MinValue;
}
