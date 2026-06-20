using FreeX.Core.Model;

namespace FreeX.App.Presentation.PivotUI;

/// <summary>
/// Portable carrier for the built-in PivotTable style the styles-gallery dialog picks.
/// </summary>
public sealed record PivotStyleGalleryValues(string StyleName);

/// <summary>
/// Portable, UI-free planning for the PivotTable Styles gallery: the built-in style-name catalog (the 28
/// Light / 28 Medium / 28 Dark names Excel ships), normalizing a current/blank style name to the default,
/// folding a non-built-in current style into the offered list, finding the current selection index, and
/// building the resulting style value. Single-sourced here so every desktop host shows the same gallery and
/// the same default; building the picker dialog and running the command (the host hands
/// <see cref="PivotStyleGalleryValues.StyleName"/> to <c>ConfigurePivotTableOptionsCommand</c>, leaving every
/// other option untouched) stays with each shell's command glue.
/// </summary>
public static class PivotStyleGalleryPlanner
{
    /// <summary>The style applied when a pivot has no style name set yet (matches the WPF host default).</summary>
    public const string DefaultStyleName = "PivotStyleLight16";

    /// <summary>The 84 built-in style names in gallery order (Light 1-28, Medium 1-28, Dark 1-28).</summary>
    public static readonly IReadOnlyList<string> BuiltInStyleNames =
    [
        ..Enumerable.Range(1, 28).Select(index => $"PivotStyleLight{index}"),
        ..Enumerable.Range(1, 28).Select(index => $"PivotStyleMedium{index}"),
        ..Enumerable.Range(1, 28).Select(index => $"PivotStyleDark{index}"),
    ];

    /// <summary>Normalizes a (possibly null/blank) style name to a non-empty name, defaulting when unset.</summary>
    public static string NormalizeStyleName(string? styleName) =>
        string.IsNullOrWhiteSpace(styleName) ? DefaultStyleName : styleName.Trim();

    /// <summary>
    /// The style names to show in the gallery for the given current style: the built-in catalog, plus the
    /// current style appended when it is a custom (non-built-in) name so the user can see what is in effect.
    /// </summary>
    public static IReadOnlyList<string> GetStyleNames(string? currentStyleName = null)
    {
        var normalizedCurrent = NormalizeStyleName(currentStyleName);
        if (BuiltInStyleNames.Contains(normalizedCurrent, StringComparer.OrdinalIgnoreCase))
            return BuiltInStyleNames;

        return [..BuiltInStyleNames, normalizedCurrent];
    }

    /// <summary>Snapshots the pivot's current style as the gallery's initial selection.</summary>
    public static PivotStyleGalleryValues Capture(PivotTableModel pivotTable)
    {
        ArgumentNullException.ThrowIfNull(pivotTable);
        return new PivotStyleGalleryValues(NormalizeStyleName(pivotTable.StyleName));
    }

    /// <summary>The index of the current style within <paramref name="styleNames"/> (0 when not found).</summary>
    public static int FindStyleIndex(IReadOnlyList<string> styleNames, string? currentStyleName)
    {
        ArgumentNullException.ThrowIfNull(styleNames);
        var normalizedCurrent = NormalizeStyleName(currentStyleName);
        for (var index = 0; index < styleNames.Count; index++)
        {
            if (string.Equals(styleNames[index], normalizedCurrent, StringComparison.OrdinalIgnoreCase))
                return index;
        }

        return 0;
    }

    /// <summary>Builds the resulting style value from the gallery's selected name.</summary>
    public static PivotStyleGalleryValues CreateResult(string? selectedStyleName) =>
        new(NormalizeStyleName(selectedStyleName));
}
