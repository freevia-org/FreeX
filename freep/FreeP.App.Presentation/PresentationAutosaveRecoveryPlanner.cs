using Free.Shared.AppServices;

namespace FreeP.App.Compositor;

public sealed record AutosaveRecoveryPlan(
    AutosaveRecoveryCandidate Candidate,
    string DisplayName) : IAutosaveRecoveryPlan;

public sealed record AutosaveRecoveryText(
    string Title,
    string RecoverButton,
    string SkipButton,
    string NoDocumentsMessage,
    string FailureMessageFormat,
    string BackstageLabel);

public static class AutosaveRecoveryTextCatalog
{
    private static readonly AutosaveRecoveryTextResolver Common = new(
        new AutosaveRecoveryTextDefaults(
            "FreeP - Recover",
            "Recover",
            "Skip",
            "No unsaved presentations were found.",
            "Could not recover the presentation.\n\n{0}"));

    private static readonly ResourceTextDescriptor BackstageLabel =
        new("Autosave_Recovery_Backstage_Label", "Recover Unsaved Presentations");

    public static IReadOnlyList<string> RequiredResourceKeys =>
        [.. Common.RequiredResourceKeys, BackstageLabel.ResourceKey];

    public static AutosaveRecoveryText Resolve(Func<string, string?>? getText = null)
    {
        var common = Common.Resolve(getText);
        return new AutosaveRecoveryText(
            common.Title,
            common.RecoverButton,
            common.SkipButton,
            common.NoDocumentsMessage,
            common.FailureMessageFormat,
            BackstageLabel.Resolve(getText));
    }
}

public static class AutosaveRecoveryPlanner
{
    private const string UnnamedPresentationFallback = "a presentation";

    public static AutosaveRecoveryPlan? PlanLatest(AutosaveSnapshotStore store) =>
        AutosaveRecoveryPlannerCore.PlanLatest(
            store,
            UnnamedPresentationFallback,
            CreatePlan);

    public static AutosaveRecoveryPlan? PlanLatest(
        IEnumerable<AutosaveRecoveryCandidate> candidates) =>
        AutosaveRecoveryPlannerCore.PlanLatest(
            candidates,
            UnnamedPresentationFallback,
            CreatePlan);

    public static IReadOnlyList<AutosaveRecoveryPlan> PlanAll(AutosaveSnapshotStore store) =>
        AutosaveRecoveryPlannerCore.PlanAll(
            store,
            UnnamedPresentationFallback,
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
