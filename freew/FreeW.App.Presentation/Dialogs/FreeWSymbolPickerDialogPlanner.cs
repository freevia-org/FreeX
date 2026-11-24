namespace FreeW.App.Presentation.Dialogs;

public sealed record FreeWSymbolPickerSemantic(
    string AutomationId,
    string AutomationName,
    string CodePointLabel);

/// <summary>
/// Shared content and code-point labels for FreeW's compact Symbol dialog. The glyph catalog (Unicode subsets)
/// is the shared <see cref="SymbolPickerCatalogPlanner"/> FreeX's Insert Symbol dialog also uses; the dialog opens on the
/// curated <see cref="CommonSubsetName"/> set and lets the user switch to any catalog subset.
/// </summary>
public static class FreeWSymbolPickerDialogPlanner
{
    public const string Title = "Symbol";
    public const string CancelText = "Cancel";
    public const int Columns = 6;
    public const int ButtonSize = 36;
    public const int ButtonMargin = 2;
    public const int ButtonFontSize = 18;
    public const int OuterMargin = 8;
    public const int FooterTopMargin = 8;
    public const int FooterButtonMinWidth = 72;
    public const string DialogAutomationId = "SymbolPickerDialog";
    public const string CancelAutomationId = "SymbolPickerCancelButton";
    public const string CommonSubsetName = "Common";
    public const string SubsetAutomationId = "SymbolPickerSubsetComboBox";
    public const string SubsetAutomationName = "Subset";
    public const int GridMaxHeight = 320;

    public static readonly IReadOnlyList<string> Glyphs =
    [
        "\u00a9", "\u00ae", "\u2122", "\u00a7", "\u00b6", "\u2022",
        "\u2013", "\u2014", "\u2026", "\u00b0", "\u00b1", "\u00d7",
        "\u00f7", "\u2264", "\u2265", "\u2260", "\u2248", "\u221e",
        "\u2192", "\u2190", "\u2191", "\u2193", "\u20ac", "\u00a3",
        "\u00a5", "\u00a2", "\u00bd", "\u00bc", "\u00be", "\u2030",
        "\u03b1", "\u03b2", "\u03b3", "\u03c0", "\u03a3", "\u03a9",
    ];

    /// <summary>The picker's subset choices: the curated common set first, then every shared catalog subset.</summary>
    public static IReadOnlyList<string> GetSubsetNames() =>
        [CommonSubsetName, .. SymbolPickerCatalogPlanner.GetSubsetNames()];

    /// <summary>The glyphs shown for a subset; an unknown/blank name falls back to the common set.</summary>
    public static IReadOnlyList<string> GetGlyphs(string? subset) =>
        string.IsNullOrWhiteSpace(subset) || string.Equals(subset, CommonSubsetName, StringComparison.Ordinal)
            ? Glyphs
            : SymbolPickerCatalogPlanner.GetSymbolsForSubset(subset);

    public static string BuildCodePointLabel(string glyph)
    {
        ArgumentException.ThrowIfNullOrEmpty(glyph);
        return $"U+{char.ConvertToUtf32(glyph, 0):X4}";
    }

    public static FreeWSymbolPickerSemantic BuildSemantic(string glyph)
    {
        var codePoint = BuildCodePointLabel(glyph);
        return new FreeWSymbolPickerSemantic(
            $"SymbolPicker{codePoint[2..]}Button",
            glyph,
            codePoint);
    }
}
