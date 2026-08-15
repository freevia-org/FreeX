namespace Free.Shared.AppServices;

public enum AutosaveRecoveryDisposition
{
    Keep,
    Delete,
    Quarantine
}

/// <summary>
/// App-neutral ordering, naming, and completion policy for autosave recovery candidates.
/// Hosts retain localized fallback text and document restoration.
/// </summary>
public static class AutosaveRecoveryPolicy
{
    public static AutosaveRecoveryCandidate? SelectLatest(
        IEnumerable<AutosaveRecoveryCandidate> candidates) =>
        OrderNewestFirst(candidates).FirstOrDefault();

    public static IReadOnlyList<AutosaveRecoveryCandidate> OrderNewestFirst(
        IEnumerable<AutosaveRecoveryCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        return candidates
            .OrderByDescending(candidate => ParseTimestamp(candidate.Sidecar.TimestampUtc))
            .ToList();
    }

    public static string ResolveDisplayName(
        AutosaveRecoveryCandidate candidate,
        string fallbackDisplayName)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentException.ThrowIfNullOrWhiteSpace(fallbackDisplayName);

        return string.IsNullOrWhiteSpace(candidate.Sidecar.DisplayName)
            ? fallbackDisplayName
            : candidate.Sidecar.DisplayName!;
    }

    public static AutosaveRecoveryDisposition ResolveDisposition(bool accepted, bool recovered) =>
        !accepted
            ? AutosaveRecoveryDisposition.Keep
            : recovered
                ? AutosaveRecoveryDisposition.Delete
                : AutosaveRecoveryDisposition.Quarantine;

    public static void ApplyDisposition(
        AutosaveRecoveryCandidate candidate,
        AutosaveRecoveryDisposition disposition)
    {
        ArgumentNullException.ThrowIfNull(candidate);

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
