using FreeX.Core.Model;

namespace FreeX.App.Services;

/// <summary>
/// Portable, persisted "recent colors" list shared by the color pickers across the
/// WPF, Avalonia and (future) macOS shells. Colors are stored as RGB hex strings in a
/// small JSON file alongside <c>options.json</c>. Promotion / dedupe / capacity rules are
/// delegated to <see cref="CellColorPalettePlanner.PromoteRecentColor"/> so the ordering
/// logic lives in exactly one place.
/// </summary>
public sealed class RecentColorsStore
{
    public const string RecentColorsPathEnvironmentVariable = "FREEX_RECENT_COLORS_PATH";

    private readonly JsonSettingsStore<List<string>> _store;
    private readonly int _capacity;
    private List<CellColor> _colors;

    public RecentColorsStore(
        string? storePath = null,
        int capacity = CellColorPalettePlanner.DefaultRecentColorCapacity)
    {
        _capacity = capacity > 0 ? capacity : CellColorPalettePlanner.DefaultRecentColorCapacity;
        _store = JsonSettingsStore<List<string>>.ForPath(
            !string.IsNullOrWhiteSpace(storePath) ? storePath : DefaultStorePath);
        _colors = ParseStoredColors(_store.Load(), _capacity);
    }

    public static string DefaultStorePath =>
        AppStoragePathPlanner.ResolveRecentColorsFilePath(
            PlatformApplicationDataPathProvider.Instance,
            Environment.GetEnvironmentVariable(RecentColorsPathEnvironmentVariable));

    public string StorePath => _store.StorePath;

    public int Capacity => _capacity;

    /// <summary>Most-recent-first list of remembered colors.</summary>
    public IReadOnlyList<CellColor> Colors => _colors;

    /// <summary>Recent swatches in display order (most recent first), capped to capacity.</summary>
    public IReadOnlyList<CellColorSwatch> Swatches =>
        CellColorPalettePlanner.BuildRecentSwatches(_colors, _capacity);

    /// <summary>
    /// Moves <paramref name="color"/> to the front of the recent list (dedupe + cap) and
    /// persists the result. Returns the new most-recent-first list.
    /// </summary>
    public IReadOnlyList<CellColor> Remember(CellColor color)
    {
        _colors = CellColorPalettePlanner.PromoteRecentColor(_colors, color, _capacity).ToList();
        _store.Save(_colors.Select(CellColorPalettePlanner.FormatHexColor).ToList());
        return _colors;
    }

    private static List<CellColor> ParseStoredColors(IReadOnlyList<string> hexes, int capacity)
    {
        var colors = new List<CellColor>(hexes.Count);
        foreach (var hex in hexes)
        {
            if (CellColorPalettePlanner.TryParseHexColor(hex, out var color))
                colors.Add(color);
        }

        // De-dupe and cap through the shared planner so loaded and newly promoted colors agree.
        return DedupeAndCap(colors, capacity);
    }

    private static List<CellColor> DedupeAndCap(IReadOnlyList<CellColor> colors, int capacity)
    {
        var result = new List<CellColor>(Math.Min(colors.Count, capacity));
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var color in colors)
        {
            if (result.Count == capacity)
                break;

            if (seen.Add(CellColorPalettePlanner.FormatHexColor(color)))
                result.Add(color);
        }

        return result;
    }
}
