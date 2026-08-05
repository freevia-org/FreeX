namespace FreeP.Core.Model;

/// <summary>Shared bounded vocabulary for explicitly supported DrawingML pattern fills.</summary>
public static class ZoomFrameBorderPatternCatalog
{
    public static IReadOnlyList<string> Presets { get; } =
        new[]
        {
            "pct0", "pct5", "pct10", "pct20", "pct25", "pct30", "pct40", "pct50", "pct60", "pct75", "pct90", "pct100",
            "horzStripe", "vertStripe", "ltHorz", "ltVert", "dashHorz", "dashVert",
            "diagStripe", "ltDnDiag", "dnDiag", "upDiag", "ltUpDiag", "cross", "diagCross",
            "smConfetti", "smGrid", "wave", "trellis",
        };

    public static bool IsSupported(string? preset) =>
        Normalize(preset) is not null;

    public static string? Normalize(string? preset)
    {
        if (string.IsNullOrWhiteSpace(preset))
            return null;

        return Presets.FirstOrDefault(candidate =>
            string.Equals(candidate, preset.Trim(), StringComparison.OrdinalIgnoreCase));
    }
}
