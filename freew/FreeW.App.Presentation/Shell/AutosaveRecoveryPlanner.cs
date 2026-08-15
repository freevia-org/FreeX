using Free.Shared.AppServices;

namespace FreeW.App.Presentation.Shell;

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
    private const string UnnamedDocumentFallback = "a document";

    public static AutosaveRecoveryPlan? PlanLatest(AutosaveSnapshotStore store)
    {
        ArgumentNullException.ThrowIfNull(store);

        return PlanLatest(store.ExcludeLiveOwned(store.EnumerateCandidates()));
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
            .OrderNewestFirst(store.ExcludeLiveOwned(store.EnumerateCandidates()))
            .Select(CreatePlan)
            .ToList();
    }

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
            AutosaveRecoveryPolicy.ResolveDisplayName(candidate, UnnamedDocumentFallback));
}
