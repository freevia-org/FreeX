using Free.Shared.AppServices;

namespace FreeW.App.Host;

internal static class AutosaveRecoveryCandidatePlanner
{
    public static AutosaveRecoveryCandidate? SelectLatest(IEnumerable<AutosaveRecoveryCandidate> candidates)
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

    private static DateTimeOffset ParseTimestamp(string? timestampUtc) =>
        DateTimeOffset.TryParse(timestampUtc, out var timestamp)
            ? timestamp
            : DateTimeOffset.MinValue;
}
