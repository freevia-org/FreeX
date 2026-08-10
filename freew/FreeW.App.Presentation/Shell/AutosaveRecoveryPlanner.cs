using Free.Shared.AppServices;

namespace FreeW.App.Presentation.Shell;

public enum AutosaveRecoveryDisposition
{
    Keep,
    Delete,
    Quarantine
}

public static class AutosaveRecoveryPlanner
{
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

    private static DateTimeOffset ParseTimestamp(string? timestampUtc) =>
        DateTimeOffset.TryParse(timestampUtc, out var timestamp)
            ? timestamp
            : DateTimeOffset.MinValue;
}
