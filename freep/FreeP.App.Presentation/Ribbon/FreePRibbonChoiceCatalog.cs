using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public sealed record FreePRibbonChoice<TDescriptor>(
    string Token,
    string Label,
    TDescriptor Descriptor,
    IReadOnlyList<string> CompatibilityValues);

public readonly record struct FreePRibbonColorChoiceDescriptor(ThemeAwareColor? Color);

public readonly record struct FreePRibbonTableCellAnchorChoiceDescriptor(TableCellAnchor? Anchor);

public readonly record struct FreePRibbonTableCellBorderChoiceDescriptor(
    TableCellBorderSide Side,
    ShapeOutline? Outline);

public readonly record struct FreePRibbonTableCellInsetChoiceDescriptor(
    TableCellInsetSide Side,
    double? InsetPt);

/// <summary>
/// Product-portable ribbon choices. Tokens are command protocol; labels are presentation only.
/// </summary>
public static class FreePRibbonChoiceCatalog
{
    private const long EmuPerPoint = 12_700;
    private const long EmuPerInch = 914_400;

    public static IReadOnlyList<FreePRibbonChoice<FreePRibbonColorChoiceDescriptor>> ColorChoices { get; } =
        ReadOnly(
            Choice("color.automatic", "Automatic", new FreePRibbonColorChoiceDescriptor(null), "Auto", "Default"),
            Choice("color.black", "Black", new FreePRibbonColorChoiceDescriptor(ThemeAwareColor.Black)),
            Choice("color.white", "White", new FreePRibbonColorChoiceDescriptor(ThemeAwareColor.White)),
            Choice("color.red", "Red", new FreePRibbonColorChoiceDescriptor(Color(0xC0, 0x00, 0x00))),
            Choice("color.green", "Green", new FreePRibbonColorChoiceDescriptor(Color(0x00, 0x80, 0x00))),
            Choice("color.blue", "Blue", new FreePRibbonColorChoiceDescriptor(Color(0x00, 0x00, 0xFF))),
            Choice("color.yellow", "Yellow", new FreePRibbonColorChoiceDescriptor(Color(0xFF, 0xFF, 0x00))),
            Choice("color.orange", "Orange", new FreePRibbonColorChoiceDescriptor(Color(0xF4, 0xB1, 0x83))),
            Choice("color.purple", "Purple", new FreePRibbonColorChoiceDescriptor(Color(0x70, 0x30, 0xA0))),
            Choice("color.dark-red", "Dark Red", new FreePRibbonColorChoiceDescriptor(Color(0x80, 0x00, 0x00)), "dark-red"),
            Choice("color.dark-blue", "Dark Blue", new FreePRibbonColorChoiceDescriptor(Color(0x1F, 0x4E, 0x79)), "dark-blue"));

    public static IReadOnlyList<FreePRibbonChoice<TextAutoFitKind>> TextAutoFitChoices { get; } =
        ReadOnly(
            Choice("text-autofit.none", "Do not autofit", TextAutoFitKind.None, "No autofit", "None"),
            Choice("text-autofit.normal", "Shrink text on overflow", TextAutoFitKind.Normal, "Normal"),
            Choice("text-autofit.shape", "Resize shape to fit text", TextAutoFitKind.Shape, "Shape"));

    public static IReadOnlyList<FreePRibbonChoice<TextVerticalType>> TextVerticalTypeChoices { get; } =
        ReadOnly(
            Choice("text-direction.horizontal", "Horizontal", TextVerticalType.Horizontal, "Normal"),
            Choice("text-direction.vertical", "Rotate 90 degrees", TextVerticalType.Vertical, "Vertical", "vert"),
            Choice("text-direction.vertical-270", "Rotate 270 degrees", TextVerticalType.Vertical270, "Vertical 270", "vert270"),
            Choice("text-direction.east-asian-vertical", "East Asian vertical", TextVerticalType.EastAsianVertical, "eaVert"),
            Choice("text-direction.wordart-vertical", "WordArt vertical", TextVerticalType.WordArtVertical, "wordArtVert"),
            Choice("text-direction.wordart-vertical-rtl", "WordArt vertical RTL", TextVerticalType.WordArtVerticalRtl, "wordArtVertRtl"));

    public static IReadOnlyList<FreePRibbonChoice<int>> TextColumnCountChoices { get; } =
        ReadOnly(
            Choice("text-columns.1", "1", 1),
            Choice("text-columns.2", "2", 2),
            Choice("text-columns.3", "3", 3),
            Choice("text-columns.4", "4", 4),
            Choice("text-columns.5", "5", 5),
            Choice("text-columns.6", "6", 6));

    public static IReadOnlyList<FreePRibbonChoice<long>> TextColumnSpacingChoices { get; } =
        ReadOnly(
            Choice("text-column-spacing.0pt", "0 pt", 0L),
            Choice("text-column-spacing.4pt", "4 pt", 4L * EmuPerPoint),
            Choice("text-column-spacing.8pt", "8 pt", 8L * EmuPerPoint),
            Choice("text-column-spacing.12pt", "12 pt", 12L * EmuPerPoint),
            Choice("text-column-spacing.16pt", "16 pt", 16L * EmuPerPoint),
            Choice("text-column-spacing.24pt", "24 pt", 24L * EmuPerPoint),
            Choice("text-column-spacing.36pt", "36 pt", 36L * EmuPerPoint));

    public static IReadOnlyList<FreePRibbonChoice<FreePRibbonTableCellAnchorChoiceDescriptor>> TableCellAnchorChoices { get; } =
        ReadOnly(
            Choice("table-cell-anchor.automatic", "Automatic", new FreePRibbonTableCellAnchorChoiceDescriptor(null), "Auto", "Default"),
            Choice("table-cell-anchor.top", "Top", new FreePRibbonTableCellAnchorChoiceDescriptor(TableCellAnchor.Top)),
            Choice("table-cell-anchor.middle", "Middle", new FreePRibbonTableCellAnchorChoiceDescriptor(TableCellAnchor.Middle), "Center", "Centre"),
            Choice("table-cell-anchor.bottom", "Bottom", new FreePRibbonTableCellAnchorChoiceDescriptor(TableCellAnchor.Bottom)));

    public static IReadOnlyList<FreePRibbonChoice<FreePRibbonTableCellBorderChoiceDescriptor>> TableCellBorderChoices { get; } =
        BuildTableCellBorderChoices();

    public static IReadOnlyList<FreePRibbonChoice<FreePRibbonTableCellInsetChoiceDescriptor>> TableCellInsetChoices { get; } =
        BuildTableCellInsetChoices();

    public static IReadOnlyList<FreePRibbonChoice<long>> TableRowHeightChoices { get; } =
        ReadOnly(
            Choice("table-row-height.automatic", "Automatic", 0L, "Auto", "Default"),
            Choice("table-row-height.0.25in", "0.25in", EmuPerInch / 4),
            Choice("table-row-height.0.5in", "0.5in", EmuPerInch / 2),
            Choice("table-row-height.0.75in", "0.75in", EmuPerInch * 3 / 4),
            Choice("table-row-height.1in", "1in", EmuPerInch),
            Choice("table-row-height.1.5in", "1.5in", EmuPerInch * 3 / 2));

    public static string[] Labels<TDescriptor>(IReadOnlyList<FreePRibbonChoice<TDescriptor>> choices) =>
        choices.Select(static choice => choice.Label).ToArray();

    public static bool TryResolve<TDescriptor>(
        object? value,
        IReadOnlyList<FreePRibbonChoice<TDescriptor>> choices,
        out TDescriptor descriptor)
    {
        if (value is FreePRibbonChoice<TDescriptor> choice)
        {
            descriptor = choice.Descriptor;
            return true;
        }

        if (value is TDescriptor typedDescriptor)
        {
            descriptor = typedDescriptor;
            return true;
        }

        if (value is string text)
        {
            var candidate = text.Trim();
            foreach (var item in choices)
            {
                if (Matches(candidate, item.Token) ||
                    Matches(candidate, item.Label) ||
                    item.CompatibilityValues.Any(alias => Matches(candidate, alias)))
                {
                    descriptor = item.Descriptor;
                    return true;
                }
            }
        }

        descriptor = default!;
        return false;
    }

    private static IReadOnlyList<FreePRibbonChoice<FreePRibbonTableCellBorderChoiceDescriptor>> BuildTableCellBorderChoices()
    {
        var choices = new List<FreePRibbonChoice<FreePRibbonTableCellBorderChoiceDescriptor>>();
        var sides = new[]
        {
            (Token: "left", Label: "Left", Side: TableCellBorderSide.Left),
            (Token: "right", Label: "Right", Side: TableCellBorderSide.Right),
            (Token: "top", Label: "Top", Side: TableCellBorderSide.Top),
            (Token: "bottom", Label: "Bottom", Side: TableCellBorderSide.Bottom),
        };

        foreach (var side in sides)
        {
            choices.Add(Choice(
                $"table-cell-border.{side.Token}.automatic",
                $"{side.Label}:Automatic",
                new FreePRibbonTableCellBorderChoiceDescriptor(side.Side, null)));
            choices.Add(Choice(
                $"table-cell-border.{side.Token}.none",
                $"{side.Label}:None",
                new FreePRibbonTableCellBorderChoiceDescriptor(side.Side, ShapeOutline.None.Instance)));
            choices.Add(Choice(
                $"table-cell-border.{side.Token}.black-0.5pt",
                $"{side.Label}:Black 0.5pt",
                new FreePRibbonTableCellBorderChoiceDescriptor(
                    side.Side,
                    new ShapeOutline.Visible(ThemeAwareColor.Black, 0.5))));
            choices.Add(Choice(
                $"table-cell-border.{side.Token}.black-1pt",
                $"{side.Label}:Black 1pt",
                new FreePRibbonTableCellBorderChoiceDescriptor(
                    side.Side,
                    new ShapeOutline.Visible(ThemeAwareColor.Black, 1.0))));
        }

        return choices.AsReadOnly();
    }

    private static IReadOnlyList<FreePRibbonChoice<FreePRibbonTableCellInsetChoiceDescriptor>> BuildTableCellInsetChoices()
    {
        var choices = new List<FreePRibbonChoice<FreePRibbonTableCellInsetChoiceDescriptor>>();
        var sides = new[]
        {
            (Token: "all", Label: "All", Side: TableCellInsetSide.All),
            (Token: "left", Label: "Left", Side: TableCellInsetSide.Left),
            (Token: "right", Label: "Right", Side: TableCellInsetSide.Right),
            (Token: "top", Label: "Top", Side: TableCellInsetSide.Top),
            (Token: "bottom", Label: "Bottom", Side: TableCellInsetSide.Bottom),
        };

        foreach (var side in sides)
        {
            choices.Add(Choice(
                $"table-cell-inset.{side.Token}.automatic",
                $"{side.Label}:Automatic",
                new FreePRibbonTableCellInsetChoiceDescriptor(side.Side, null)));

            foreach (var points in new[] { 0, 2, 4, 6, 8 })
            {
                choices.Add(Choice(
                    $"table-cell-inset.{side.Token}.{points}pt",
                    $"{side.Label}:{points}pt",
                    new FreePRibbonTableCellInsetChoiceDescriptor(side.Side, points)));
            }
        }

        return choices.AsReadOnly();
    }

    private static FreePRibbonChoice<TDescriptor> Choice<TDescriptor>(
        string token,
        string label,
        TDescriptor descriptor,
        params string[] compatibilityValues) =>
        new(token, label, descriptor, Array.AsReadOnly(compatibilityValues));

    private static IReadOnlyList<FreePRibbonChoice<TDescriptor>> ReadOnly<TDescriptor>(
        params FreePRibbonChoice<TDescriptor>[] choices) =>
        Array.AsReadOnly(choices);

    private static bool Matches(string left, string right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private static ThemeAwareColor Color(byte red, byte green, byte blue) =>
        new(new SrgbColor(red, green, blue));
}
