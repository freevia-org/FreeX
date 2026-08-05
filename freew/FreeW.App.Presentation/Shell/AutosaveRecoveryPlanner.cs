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

public static class AutosaveRecoveryPlanner
{
    public static AutosaveRecoveryPlan? PlanLatest(AutosaveSnapshotStore store)
    {
        ArgumentNullException.ThrowIfNull(store);

        return PlanLatest(store.EnumerateCandidates());
    }

    public static AutosaveRecoveryPlan? PlanLatest(
        IEnumerable<AutosaveRecoveryCandidate> candidates)
    {
        var candidate = SelectLatest(candidates);
        return candidate is null
            ? null
            : new AutosaveRecoveryPlan(candidate, DisplayName(candidate));
    }

    public static AutosaveRecoveryCandidate? SelectLatest(
        IEnumerable<AutosaveRecoveryCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        return candidates
            .OrderByDescending(candidate => ParseTimestamp(candidate.Sidecar.TimestampUtc))
            .FirstOrDefault();
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
