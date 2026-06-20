using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.TableUI;

/// <summary>
/// One built-in table style offered by the gallery: the display <see cref="Label"/> (e.g. "Medium 2"), the
/// persisted <see cref="StyleName"/> (e.g. "TableStyleMedium2"), and the resolved
/// <see cref="StructuredTableStyleBanding"/> the apply command paints with.
/// </summary>
public sealed record TableStyleGalleryOption(
    string Label,
    string StyleName,
    StructuredTableStyleBanding Banding);

/// <summary>
/// Portable, UI-free planning for the structured-table "Table Styles" gallery: the built-in style catalog Excel
/// ships (21 Light / 28 Medium / 11 Dark names) in gallery order, with the banding colors resolved by the shared
/// <see cref="StructuredTableStyleBandingResolver"/> so the gallery, table creation, and the load-time
/// materializer all agree on every color for a given style name + theme. Also normalizes a blank current style
/// to the default, folds a non-built-in current style into the offered list, and finds the current selection
/// index. Single-sourced here so every desktop host shows the same gallery and the same default; building the
/// picker dialog and running the command (the host hands the chosen option's banding + style name to
/// <c>ApplyStructuredTableStyleCommand</c> with <c>updateStyleName: true</c>) stays with each shell's glue.
/// </summary>
public static class TableStyleGalleryPlanner
{
    /// <summary>The style applied when a table has no style name set yet (matches the WPF host default).</summary>
    public const string DefaultStyleName = "TableStyleMedium2";

    private const int LightCount = 21;
    private const int MediumCount = 28;
    private const int DarkCount = 11;

    /// <summary>The built-in style options in gallery order, with banding resolved for the default theme.</summary>
    public static IReadOnlyList<TableStyleGalleryOption> GetOptions() =>
        GetOptions(WorkbookTheme.Office);

    /// <summary>The built-in style options in gallery order, with banding resolved for <paramref name="theme"/>.</summary>
    public static IReadOnlyList<TableStyleGalleryOption> GetOptions(WorkbookTheme theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        return
        [
            ..CreateStyleGroup("Light", LightCount, theme),
            ..CreateStyleGroup("Medium", MediumCount, theme),
            ..CreateStyleGroup("Dark", DarkCount, theme),
        ];
    }

    /// <summary>The option at <paramref name="index"/>, clamped into range, for the default theme.</summary>
    public static TableStyleGalleryOption GetOption(int index) =>
        GetOption(index, WorkbookTheme.Office);

    /// <summary>The option at <paramref name="index"/>, clamped into range, for <paramref name="theme"/>.</summary>
    public static TableStyleGalleryOption GetOption(int index, WorkbookTheme theme)
    {
        var options = GetOptions(theme);
        return options[Math.Clamp(index, 0, options.Count - 1)];
    }

    /// <summary>Normalizes a (possibly null/blank) style name to a non-empty name, defaulting when unset.</summary>
    public static string NormalizeStyleName(string? styleName) =>
        string.IsNullOrWhiteSpace(styleName) ? DefaultStyleName : styleName.Trim();

    /// <summary>
    /// The index of the table's current style within the offered options (0 — the first Light style — when the
    /// current style is not one of the built-in names).
    /// </summary>
    public static int FindStyleIndex(IReadOnlyList<TableStyleGalleryOption> options, string? currentStyleName)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(currentStyleName))
            return 0;

        for (var index = 0; index < options.Count; index++)
        {
            if (string.Equals(options[index].StyleName, currentStyleName, StringComparison.OrdinalIgnoreCase))
                return index;
        }

        return 0;
    }

    /// <summary>Finds the option for <paramref name="styleName"/> (false when it is blank or not built-in).</summary>
    public static bool TryGetOption(string? styleName, out TableStyleGalleryOption option) =>
        TryGetOption(styleName, WorkbookTheme.Office, out option);

    /// <summary>Finds the option for <paramref name="styleName"/> under <paramref name="theme"/>.</summary>
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
