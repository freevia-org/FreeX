namespace Free.Shared.AppServices;

/// <summary>
/// Applies the shared startup-recovery policy to autosave candidates before a host offers them.
/// Superseded candidates are deleted immediately so they cannot be offered again on a later launch.
/// </summary>
public static class AutosaveRecoveryCandidateProcessor
{
    public static IReadOnlyList<AutosaveRecoveryCandidate> PrepareForRecovery(
        IReadOnlyList<AutosaveRecoveryCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        return FilterSupersededByNewerOriginal(DeduplicateByDocument(candidates));
    }

    /// <summary>
    /// Keeps the newest snapshot for each document identity within one process launch. Candidates
    /// without a document id remain distinct because there is not enough evidence to merge them.
    /// </summary>
    public static IReadOnlyList<AutosaveRecoveryCandidate> DeduplicateByDocument(
        IReadOnlyList<AutosaveRecoveryCandidate> candidates)
    {
        return DeduplicateByDocument(candidates, AutosaveSnapshotStore.DeleteCandidate);
    }

    internal static IReadOnlyList<AutosaveRecoveryCandidate> DeduplicateByDocument(
        IReadOnlyList<AutosaveRecoveryCandidate> candidates,
        Action<AutosaveRecoveryCandidate> deleteCandidate)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(deleteCandidate);
        if (candidates.Count <= 1)
            return candidates;

        var newestByDocument = new Dictionary<string, AutosaveRecoveryCandidate>(StringComparer.OrdinalIgnoreCase);
        var orderedDocumentKeys = new List<string>();

        foreach (var candidate in candidates)
        {
            var documentKey = GetDocumentIdentityKey(candidate);
            if (!newestByDocument.TryGetValue(documentKey, out var existing))
            {
                newestByDocument[documentKey] = candidate;
                orderedDocumentKeys.Add(documentKey);
                continue;
            }

            if (ResolveTimestamp(candidate) > ResolveTimestamp(existing))
            {
                TryDeleteCandidate(existing, deleteCandidate);
                newestByDocument[documentKey] = candidate;
            }
            else
            {
                TryDeleteCandidate(candidate, deleteCandidate);
            }
        }

        return orderedDocumentKeys.Select(key => newestByDocument[key]).ToList();
    }

    /// <summary>
    /// Removes snapshots whose original file was saved more recently. Recovering one would replace
    /// newer on-disk work with stale autosaved content.
    /// </summary>
    public static IReadOnlyList<AutosaveRecoveryCandidate> FilterSupersededByNewerOriginal(
        IReadOnlyList<AutosaveRecoveryCandidate> candidates)
    {
        return FilterSupersededByNewerOriginal(candidates, AutosaveSnapshotStore.DeleteCandidate);
    }

    internal static IReadOnlyList<AutosaveRecoveryCandidate> FilterSupersededByNewerOriginal(
        IReadOnlyList<AutosaveRecoveryCandidate> candidates,
        Action<AutosaveRecoveryCandidate> deleteCandidate)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(deleteCandidate);
        if (candidates.Count == 0)
            return candidates;

        List<AutosaveRecoveryCandidate>? kept = null;
        for (var index = 0; index < candidates.Count; index++)
        {
            var candidate = candidates[index];
            if (IsOriginalNewerThanSnapshot(candidate))
            {
                kept ??= new List<AutosaveRecoveryCandidate>(candidates.Take(index));
                TryDeleteCandidate(candidate, deleteCandidate);
                continue;
            }

            kept?.Add(candidate);
        }

        return kept ?? candidates;
    }

    private static void TryDeleteCandidate(
        AutosaveRecoveryCandidate candidate,
        Action<AutosaveRecoveryCandidate> deleteCandidate)
    {
        try
        {
            deleteCandidate(candidate);
        }
        catch
        {
            // Stale cleanup must not prevent otherwise valid recovery candidates from being offered.
        }
    }

    public static DateTimeOffset ResolveTimestamp(AutosaveRecoveryCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        if (DateTimeOffset.TryParse(candidate.Sidecar.TimestampUtc, out var parsed))
            return parsed;

        try
        {
            return new DateTimeOffset(File.GetLastWriteTimeUtc(candidate.SnapshotPath), TimeSpan.Zero);
        }
        catch
        {
            return DateTimeOffset.MinValue;
        }
    }

    public static string GetDocumentIdentityKey(AutosaveRecoveryCandidate candidate)
    {
        if (!string.IsNullOrWhiteSpace(candidate.Sidecar.OriginalFilePath))
        {
            return "path:" + GetLaunchScope(candidate) + ":" + candidate.Sidecar.OriginalFilePath
                + ":" + GetDocumentIdentityComponent(candidate);
        }

        if (!string.IsNullOrWhiteSpace(candidate.Sidecar.DisplayName))
        {
            return "name:" + GetLaunchScope(candidate) + ":" + candidate.Sidecar.DisplayName
                + ":" + GetDocumentIdentityComponent(candidate);
        }

        return "snapshot:" + candidate.SnapshotPath;
    }

    public static string GetDocumentIdentityComponent(AutosaveRecoveryCandidate candidate) =>
        string.IsNullOrWhiteSpace(candidate.Sidecar.DocumentId)
            ? "unknown:" + candidate.SnapshotPath
            : candidate.Sidecar.DocumentId;

    public static string GetLaunchScope(AutosaveRecoveryCandidate candidate)
    {
        var baseName = Path.GetFileNameWithoutExtension(candidate.SnapshotPath);
        var parts = baseName.Split('-');
        if (!string.Equals(parts.Length > 0 ? parts[0] : null, "recovery", StringComparison.OrdinalIgnoreCase))
            return candidate.SnapshotPath;

        if (parts.Length >= 4)
            return parts[1] + "-" + parts[2];
        if (parts.Length == 3)
            return parts[1];

        return candidate.SnapshotPath;
    }

    public static bool IsOriginalNewerThanSnapshot(AutosaveRecoveryCandidate candidate)
    {
        var originalPath = candidate.Sidecar.OriginalFilePath;
        if (string.IsNullOrWhiteSpace(originalPath))
            return false;

        try
        {
            return File.Exists(originalPath) &&
                File.GetLastWriteTimeUtc(originalPath) > ResolveTimestamp(candidate).UtcDateTime;
        }
        catch
        {
            return false;
        }
    }
}
