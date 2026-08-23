using Free.Shared.AppServices;

namespace FreeW.App.Presentation.Shell;

public sealed record AutosaveRecoveryPlan(
    AutosaveRecoveryCandidate Candidate,
    string DisplayName) : IAutosaveRecoveryPlan;

public sealed record AutosaveRecoveryText(
    string Title,
    string RecoverButton,
    string SkipButton,
    string NoDocumentsMessage,
    string FailureMessageFormat);

public static class AutosaveRecoveryTextCatalog
{
    private static readonly AutosaveRecoveryTextResolver Common = new(
        new AutosaveRecoveryTextDefaults(
            "FreeW - Recover",
            "Recover",
            "Skip",
            "No unsaved documents were found.",
            "Could not recover the document.\n\n{0}"));

    public static IReadOnlyList<string> RequiredResourceKeys => Common.RequiredResourceKeys;

    public static AutosaveRecoveryText Resolve(Func<string, string?>? getText = null)
    {
        var common = Common.Resolve(getText);
        return new AutosaveRecoveryText(
            common.Title,
            common.RecoverButton,
            common.SkipButton,
            common.NoDocumentsMessage,
            common.FailureMessageFormat);
    }
}

public static class AutosaveRecoveryPlanner
{
    private const string UnnamedDocumentFallback = "a document";

    public static AutosaveRecoveryPlan? PlanLatest(AutosaveSnapshotStore store) =>
        AutosaveRecoveryPlannerCore.PlanLatest(
            store,
            UnnamedDocumentFallback,
            CreatePlan);

    public static AutosaveRecoveryPlan? PlanLatest(
        IEnumerable<AutosaveRecoveryCandidate> candidates) =>
        AutosaveRecoveryPlannerCore.PlanLatest(
            candidates,
            UnnamedDocumentFallback,
            CreatePlan);

    public static IReadOnlyList<AutosaveRecoveryPlan> PlanAll(AutosaveSnapshotStore store) =>
        AutosaveRecoveryPlannerCore.PlanAll(
            store,
            UnnamedDocumentFallback,
            CreatePlan);

    public static AutosaveRecoveryDisposition Complete(
        AutosaveRecoveryPlan plan,
        bool accepted,
        bool recovered) =>
        AutosaveRecoveryPlannerCore.Complete(plan, accepted, recovered);

    private static AutosaveRecoveryPlan CreatePlan(
        AutosaveRecoveryCandidate candidate,
        string displayName) =>
        new(candidate, displayName);
}
