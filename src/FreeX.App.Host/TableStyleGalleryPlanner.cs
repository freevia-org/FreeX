using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public sealed record TableStyleGalleryOption(
    string Label,
    string StyleName,
    StructuredTableStyleBanding Banding);

public static class TableStyleGalleryPlanner
{
    public static IReadOnlyList<TableStyleGalleryOption> GetOptions() =>
        GetOptions(WorkbookTheme.Office);

    public static IReadOnlyList<TableStyleGalleryOption> GetOptions(WorkbookTheme theme) =>
    [
        ..CreateStyleGroup("Light", 21, theme),
        ..CreateStyleGroup("Medium", 28, theme),
        ..CreateStyleGroup("Dark", 11, theme)
    ];

    public static TableStyleGalleryOption GetOption(int index)
        => GetOption(index, WorkbookTheme.Office);

    public static TableStyleGalleryOption GetOption(int index, WorkbookTheme theme)
    {
        var options = GetOptions(theme);
        return options[Math.Clamp(index, 0, options.Count - 1)];
    }

    public static bool TryGetOption(string? styleName, out TableStyleGalleryOption option)
        => TryGetOption(styleName, WorkbookTheme.Office, out option);

    public static bool TryGetOption(string? styleName, WorkbookTheme theme, out TableStyleGalleryOption option)
    {
        option = null!;
        if (string.IsNullOrWhiteSpace(styleName))
            return false;

        foreach (var candidate in GetOptions(theme))
        {
            if (!string.Equals(candidate.StyleName, styleName, StringComparison.OrdinalIgnoreCase))
                continue;

            option = candidate;
            break;
        }

        return option is not null;
    }

    // The gallery's labels and grouping live here; the banding colors are resolved by the shared
    // StructuredTableStyleBandingResolver so the swatches, table creation, and the load-time
    // materializer all agree on every color for a given style name + theme.
    private static IEnumerable<TableStyleGalleryOption> CreateStyleGroup(string family, int count, WorkbookTheme theme)
    {
        for (var index = 1; index <= count; index++)
        {
            var styleName = $"TableStyle{family}{index}";
            var banding = StructuredTableStyleBandingResolver.Resolve(styleName, theme);
            yield return new TableStyleGalleryOption($"{family} {index}", styleName, banding);
        }
    }
}
