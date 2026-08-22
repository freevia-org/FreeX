namespace Free.Shared.AppServices;

public interface IAutosaveRecoveryPlan
{
    AutosaveRecoveryCandidate Candidate { get; }
    string DisplayName { get; }
}

/// <summary>
/// App-neutral candidate preparation, ordering, display naming, and completion policy.
/// </summary>
public static class AutosaveRecoveryPlannerCore
{
    public static TPlan? PlanLatest<TPlan>(
        AutosaveSnapshotStore store,
        string fallbackDisplayName,
        Func<AutosaveRecoveryCandidate, string, TPlan> createPlan)
        where TPlan : class, IAutosaveRecoveryPlan
    {
        ArgumentNullException.ThrowIfNull(store);

        return PlanLatest(PrepareCandidates(store), fallbackDisplayName, createPlan);
    }

    public static TPlan? PlanLatest<TPlan>(
        IEnumerable<AutosaveRecoveryCandidate> candidates,
        string fallbackDisplayName,
        Func<AutosaveRecoveryCandidate, string, TPlan> createPlan)
        where TPlan : class, IAutosaveRecoveryPlan
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ValidateConfiguration(fallbackDisplayName, createPlan);

        var candidate = AutosaveRecoveryPolicy.SelectLatest(candidates);
        return candidate is null
            ? null
            : CreatePlan(candidate, fallbackDisplayName, createPlan);
    }

    public static IReadOnlyList<TPlan> PlanAll<TPlan>(
        AutosaveSnapshotStore store,
        string fallbackDisplayName,
        Func<AutosaveRecoveryCandidate, string, TPlan> createPlan)
        where TPlan : class, IAutosaveRecoveryPlan
    {
        ArgumentNullException.ThrowIfNull(store);
        ValidateConfiguration(fallbackDisplayName, createPlan);

        return AutosaveRecoveryPolicy
            .OrderNewestFirst(PrepareCandidates(store))
            .Select(candidate => CreatePlan(candidate, fallbackDisplayName, createPlan))
            .ToList();
    }

    public static AutosaveRecoveryDisposition Complete(
        IAutosaveRecoveryPlan plan,
        bool accepted,
        bool recovered)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var disposition = AutosaveRecoveryPolicy.ResolveDisposition(accepted, recovered);
        AutosaveRecoveryPolicy.ApplyDisposition(plan.Candidate, disposition);
        return disposition;
    }

    private static IReadOnlyList<AutosaveRecoveryCandidate> PrepareCandidates(
        AutosaveSnapshotStore store) =>
        AutosaveRecoveryCandidateProcessor.PrepareForRecovery(
            store.ExcludeLiveOwned(store.EnumerateCandidates()));

    private static TPlan CreatePlan<TPlan>(
        AutosaveRecoveryCandidate candidate,
        string fallbackDisplayName,
        Func<AutosaveRecoveryCandidate, string, TPlan> createPlan)
        where TPlan : class, IAutosaveRecoveryPlan =>
        createPlan(
            candidate,
            AutosaveRecoveryPolicy.ResolveDisplayName(candidate, fallbackDisplayName));

    private static void ValidateConfiguration<TPlan>(
        string fallbackDisplayName,
        Func<AutosaveRecoveryCandidate, string, TPlan> createPlan)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fallbackDisplayName);
        ArgumentNullException.ThrowIfNull(createPlan);
    }
}
