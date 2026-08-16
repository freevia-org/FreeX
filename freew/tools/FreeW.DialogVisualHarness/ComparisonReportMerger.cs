using System.Collections.Generic;

public static class ComparisonReportMerger
{
    public static MergedComparison Merge(
        ComparisonReport baseline,
        IReadOnlyList<ComparisonRow> refreshedRows,
        string refreshRoute)
    {
        var refreshedById = refreshedRows
            .Where(row => BelongsToRoute(row.ScenarioId, refreshRoute))
            .ToDictionary(row => row.ScenarioId, StringComparer.OrdinalIgnoreCase);
        var merged = new List<ComparisonRow>(Math.Max(baseline.Rows.Count, refreshedById.Count));
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var baselineRow in baseline.Rows)
        {
            if (BelongsToRoute(baselineRow.ScenarioId, refreshRoute))
            {
                if (refreshedById.TryGetValue(baselineRow.ScenarioId, out var refreshed))
                {
                    merged.Add(refreshed);
                    seen.Add(refreshed.ScenarioId);
                }
                continue;
            }

            merged.Add(baselineRow);
            seen.Add(baselineRow.ScenarioId);
        }

        foreach (var refreshedRow in refreshedRows)
        {
            if (BelongsToRoute(refreshedRow.ScenarioId, refreshRoute) && seen.Add(refreshedRow.ScenarioId))
                merged.Add(refreshedRow);
        }

        var counts = merged
            .GroupBy(row => row.Classification, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        var baselineRouteRows = baseline.Rows.Where(row => BelongsToRoute(row.ScenarioId, refreshRoute)).ToArray();
        var refreshedRouteRows = refreshedById.Values.ToArray();
        return new MergedComparison(
            merged,
            baseline.InventoryScenarioCount,
            baseline.WpfCaptureCount + CaptureDelta(baselineRouteRows, refreshedRouteRows, hostIndex: 0),
            baseline.AvaloniaCaptureCount + CaptureDelta(baselineRouteRows, refreshedRouteRows, hostIndex: 1),
            baseline.GeneratedFromSha256,
            baseline.TargetDpi,
            counts);
    }

    private static int CaptureDelta(IReadOnlyList<ComparisonRow> baselineRows, IReadOnlyList<ComparisonRow> refreshedRows, int hostIndex) =>
        CountCaptured(refreshedRows, hostIndex) - CountCaptured(baselineRows, hostIndex);

    private static int CountCaptured(IEnumerable<ComparisonRow> rows, int hostIndex) =>
        rows.Count(row =>
        {
            if (hostIndex == 1 && row.Classification.Equals("avalonia-extension", StringComparison.OrdinalIgnoreCase))
                return true;
            var statuses = row.CaptureStatus.Split('/', StringSplitOptions.TrimEntries);
            return statuses.Length == 2 && statuses[hostIndex].Equals("captured", StringComparison.OrdinalIgnoreCase);
        });

    private static bool BelongsToRoute(string scenarioId, string route)
    {
        var normalizedRoute = route.Trim().Trim('.');
        return scenarioId.Equals(normalizedRoute, StringComparison.OrdinalIgnoreCase)
            || scenarioId.StartsWith(normalizedRoute + ".", StringComparison.OrdinalIgnoreCase);
    }
}

public sealed record MergedComparison(
    IReadOnlyList<ComparisonRow> Rows,
    int InventoryScenarioCount,
    int WpfCaptureCount,
    int AvaloniaCaptureCount,
    string GeneratedFromSha256,
    int TargetDpi,
    IReadOnlyDictionary<string, int> Counts);
