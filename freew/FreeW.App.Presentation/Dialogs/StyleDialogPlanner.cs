using FreeW.Core.Model;

namespace FreeW.App.Presentation.Dialogs;

public enum StyleDialogSortOrder
{
    Alphabetical,
    ByType,
    ByUse,
}

public enum StyleDialogValidationError
{
    EmptyName,
}

public sealed record StyleDialogFontSizeChoice(string Label, double? Points);

public sealed record StyleDialogColorChoice(string Label, string? Hex);

public sealed record StyleDialogInput(
    string? Name,
    string? BasedOnId,
    string? NextStyleId,
    bool Bold,
    bool Italic,
    bool Underline,
    int FontSizeIndex,
    int ColorIndex,
    int AlignmentIndex);

public sealed record StyleDefinitionResult(
    string Name,
    string? BasedOnId,
    RunFormatting Run,
    ParagraphFormatting Paragraph,
    string? NextStyleId);

public sealed record StyleDialogRow(string Id, string Display, bool IsBuiltIn);

/// <summary>
/// Shared geometry for the compact New/Modify Style dialog. Keeping these values in the
/// presentation layer makes the WPF and Avalonia shells consume the same layout contract while
/// retaining their native control implementations.
/// </summary>
public static class StyleDialogMetrics
{
    public const double DialogMargin = 16;
    public const double FieldBottomMargin = 10;
    public const double NameTextBoxHeight = 20;
    // WPF's native ComboBox template measures these fields at 22 logical pixels.
    public const double ComboBoxHeight = 22;
    // The three formatting toggles occupy a 15-pixel WPF checkbox row.
    public const double CheckBoxHeight = 15;
    // The WPF shared button row paints a 20-pixel button surface in this dialog.
    public const double ButtonHeight = 20;
    public const double ActionRowTopMargin = 12;
}

public abstract record ManageStyleAction
{
    public sealed record Apply(string StyleId) : ManageStyleAction;
    public sealed record Modify(string StyleId) : ManageStyleAction;
    public sealed record Delete(string StyleId) : ManageStyleAction;
}

/// <summary>
/// Shared planning for Word-style New Style / Modify Style / Manage Styles dialog surfaces.
/// It keeps validation, palette choices, and sort semantics independent of WPF or Avalonia UI.
/// </summary>
public static class StyleDialogPlanner
{
    public static readonly IReadOnlyList<StyleDialogFontSizeChoice> FontSizes =
    [
        new("(default)", null),
        new("8", 8),
        new("9", 9),
        new("10", 10),
        new("11", 11),
        new("12", 12),
        new("14", 14),
        new("16", 16),
        new("18", 18),
        new("24", 24),
        new("28", 28),
        new("36", 36),
    ];

    public static readonly IReadOnlyList<StyleDialogColorChoice> Colors =
    [
        new("Automatic", null),
        new("Black", "#000000"),
        new("Dark Red", "#C00000"),
        new("Red", "#FF0000"),
        new("Blue accent", "#2F5496"),
        new("Blue", "#0070C0"),
        new("Green", "#00B050"),
        new("Purple", "#7030A0"),
        new("Grey", "#7F7F7F"),
    ];

    public static readonly IReadOnlyList<string> AlignmentLabels =
        ["Left", "Center", "Right", "Justify"];

    public static IReadOnlyList<KeyValuePair<string, string>> BuildStyleOptions(
        IReadOnlyDictionary<string, string> styleNamesById,
        string emptyLabel)
    {
        ArgumentNullException.ThrowIfNull(styleNamesById);

        var result = new List<KeyValuePair<string, string>> { new(emptyLabel, string.Empty) };
        result.AddRange(styleNamesById
            .OrderBy(kv => kv.Value, StringComparer.OrdinalIgnoreCase)
            .Select(kv => new KeyValuePair<string, string>(kv.Value, kv.Key)));
        return result;
    }

    public static IReadOnlyList<StyleDialogRow> BuildRows(TextDocument model, StyleDialogSortOrder order)
    {
        ArgumentNullException.ThrowIfNull(model);

        var usageCounts = order == StyleDialogSortOrder.ByUse
            ? model.Paragraphs
                .GroupBy(paragraph => NormalizeStyleId(paragraph.StyleId) ?? "Normal", StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal)
            : new Dictionary<string, int>(StringComparer.Ordinal);
        var rows = model.Styles.Values
            .Select(style =>
            {
                var builtIn = StyleManager.IsBuiltIn(style.Id);
                var display = builtIn ? $"{style.Name}  (built-in)" : style.Name;
                return new StyleDialogRow(style.Id, display, builtIn);
            });

        return order switch
        {
            StyleDialogSortOrder.ByType =>
                rows.OrderBy(row => row.IsBuiltIn ? 0 : 1)
                    .ThenBy(row => row.Display, StringComparer.OrdinalIgnoreCase)
                    .ToArray(),

            StyleDialogSortOrder.ByUse =>
                rows.OrderByDescending(row => usageCounts.TryGetValue(row.Id, out var count) ? count : 0)
                    .ThenBy(row => row.Display, StringComparer.OrdinalIgnoreCase)
                    .ToArray(),

            _ =>
                rows.OrderBy(row => row.Display, StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
        };
    }

    public static bool TryBuildDefinition(
        StyleDialogInput input,
        RunFormatting seedRun,
        ParagraphFormatting seedParagraph,
        out StyleDefinitionResult? result,
        out StyleDialogValidationError? validation)
    {
        var name = (input.Name ?? string.Empty).Trim();
        if (name.Length == 0)
        {
            result = null;
            validation = StyleDialogValidationError.EmptyName;
            return false;
        }

        var size = FontSizes[Math.Clamp(input.FontSizeIndex, 0, FontSizes.Count - 1)].Points;
        var color = Colors[Math.Clamp(input.ColorIndex, 0, Colors.Count - 1)].Hex;
        var alignment = (TextAlignment)Math.Clamp(input.AlignmentIndex, 0, AlignmentLabels.Count - 1);

        result = new StyleDefinitionResult(
            name,
            NormalizeStyleId(input.BasedOnId),
            seedRun with
            {
                Bold = input.Bold,
                Italic = input.Italic,
                Underline = input.Underline,
                FontSizePt = size,
                ColorHex = color,
            },
            seedParagraph with { Alignment = alignment },
            NormalizeStyleId(input.NextStyleId));
        validation = null;
        return true;
    }

    public static string ValidationMessageFor(StyleDialogValidationError? validation) =>
        validation switch
        {
            StyleDialogValidationError.EmptyName => "Please enter a style name.",
            _ => string.Empty,
        };

    public static int IndexOfSize(double? sizePt)
    {
        if (sizePt is not { } pt)
            return 0;

        for (var i = 0; i < FontSizes.Count; i++)
        {
            if (FontSizes[i].Points is { } candidate && Math.Abs(candidate - pt) < 0.01)
                return i;
        }

        return 0;
    }

    public static int IndexOfColor(string? hex)
    {
        if (hex is null)
            return 0;

        for (var i = 0; i < Colors.Count; i++)
        {
            if (string.Equals(Colors[i].Hex, hex, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return 0;
    }

    private static string? NormalizeStyleId(string? styleId) =>
        string.IsNullOrWhiteSpace(styleId) ? null : styleId;
}
