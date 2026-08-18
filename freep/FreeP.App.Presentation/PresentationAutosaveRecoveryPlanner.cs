using Free.Shared.AppServices;

namespace FreeP.App.Compositor;

public sealed record AutosaveRecoveryPlan(
    AutosaveRecoveryCandidate Candidate,
    string DisplayName);

public sealed record AutosaveRecoveryText(
    string Title,
    string RecoverButton,
    string SkipButton,
    string NoDocumentsMessage,
    string FailureMessageFormat,
    string BackstageLabel);

public static class AutosaveRecoveryTextCatalog
{
    private static readonly ResourceTextDescriptor[] Texts =
    [
        new("Autosave_Recovery_Title", "FreeP - Recover"),
        new("Autosave_Recovery_Recover_Button", "Recover"),
        new("Autosave_Recovery_Skip_Button", "Skip"),
        new("Autosave_Recovery_None_Message", "No unsaved presentations were found."),
        new("Autosave_Recovery_Failure_Message_Format", "Could not recover the presentation.\n\n{0}"),
        new("Autosave_Recovery_Backstage_Label", "Recover Unsaved Presentations"),
    ];

    public static IReadOnlyList<string> RequiredResourceKeys =>
        Texts.Select(text => text.ResourceKey).ToArray();

    public static AutosaveRecoveryText Resolve(Func<string, string?>? getText = null) =>
        new(
            Texts[0].Resolve(getText),
            Texts[1].Resolve(getText),
            Texts[2].Resolve(getText),
            Texts[3].Resolve(getText),
            Texts[4].Resolve(getText),
            Texts[5].Resolve(getText));
}

public static class AutosaveRecoveryPlanner
{
    private const string UnnamedPresentationFallback = "a presentation";

    public static AutosaveRecoveryPlan? PlanLatest(AutosaveSnapshotStore store)
    {
        ArgumentNullException.ThrowIfNull(store);

        return PlanLatest(PrepareCandidates(store));
    }

    public static AutosaveRecoveryPlan? PlanLatest(
        IEnumerable<AutosaveRecoveryCandidate> candidates)
    {
        var candidate = AutosaveRecoveryPolicy.SelectLatest(candidates);
        return candidate is null ? null : CreatePlan(candidate);
    }

    public static IReadOnlyList<AutosaveRecoveryPlan> PlanAll(AutosaveSnapshotStore store)
    {
        ArgumentNullException.ThrowIfNull(store);

        return AutosaveRecoveryPolicy
            .OrderNewestFirst(PrepareCandidates(store))
            .Select(CreatePlan)
            .ToList();
    }

    /// <summary>
    /// Applies the shared candidate-preparation policy (same one FreeX's
    /// <c>AutosaveRecoveryOfferPlanner</c> uses) before a candidate is ever offered: it drops any
    /// snapshot whose original file was saved more recently than the snapshot itself, so recovery is
    /// never offered for a snapshot that is now staler than what the user already has on disk.
    /// </summary>
    private static IReadOnlyList<AutosaveRecoveryCandidate> PrepareCandidates(AutosaveSnapshotStore store) =>
        AutosaveRecoveryCandidateProcessor.PrepareForRecovery(store.ExcludeLiveOwned(store.EnumerateCandidates()));

    public static AutosaveRecoveryDisposition Complete(
        AutosaveRecoveryPlan plan,
        bool accepted,
        bool recovered)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var disposition = AutosaveRecoveryPolicy.ResolveDisposition(accepted, recovered);
        AutosaveRecoveryPolicy.ApplyDisposition(plan.Candidate, disposition);
        return disposition;
    }

    private static AutosaveRecoveryPlan CreatePlan(AutosaveRecoveryCandidate candidate) =>
        new(
            candidate,
            AutosaveRecoveryPolicy.ResolveDisplayName(candidate, UnnamedPresentationFallback));
}
