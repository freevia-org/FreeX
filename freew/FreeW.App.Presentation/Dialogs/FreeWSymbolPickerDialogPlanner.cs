namespace FreeW.App.Presentation.Dialogs;

/// <summary>Shared content and code-point labels for FreeW's compact Symbol dialog.</summary>
public static class FreeWSymbolPickerDialogPlanner
{
    public const string Title = "Symbol";
    public const string CancelText = "Cancel";
    public const int Columns = 6;

    public static readonly IReadOnlyList<string> Glyphs =
    [
        "\u00a9", "\u00ae", "\u2122", "\u00a7", "\u00b6", "\u2022",
        "\u2013", "\u2014", "\u2026", "\u00b0", "\u00b1", "\u00d7",
        "\u00f7", "\u2264", "\u2265", "\u2260", "\u2248", "\u221e",
        "\u2192", "\u2190", "\u2191", "\u2193", "\u20ac", "\u00a3",
        "\u00a5", "\u00a2", "\u00bd", "\u00bc", "\u00be", "\u2030",
        "\u03b1", "\u03b2", "\u03b3", "\u03c0", "\u03a3", "\u03a9",
    ];

    public static string BuildCodePointLabel(string glyph)
    {
        ArgumentException.ThrowIfNullOrEmpty(glyph);
        return $"U+{char.ConvertToUtf32(glyph, 0):X4}";
    }
}
