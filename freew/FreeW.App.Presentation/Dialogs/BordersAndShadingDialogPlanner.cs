using System.Globalization;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Dialogs;

public sealed record BordersAndShadingDialogResult(
    ParagraphBorder? ParagraphBorder,
    PageBorder? PageBorder,
    string? ShadingHex,
    ShadingPattern ShadingPattern);

public sealed record PageBorderArtOption(string Label, int ArtId);

public sealed record BorderSettingPlan(bool? EdgeValue, bool EdgesEnabled);

public sealed record BordersAndShadingDialogInput(
    int ParagraphSettingIndex,
    int ParagraphLineStyleIndex,
    string? ParagraphColorHex,
    string? ParagraphWidthText,
    bool Top,
    bool Left,
    bool Bottom,
    bool Right,
    int PageSettingIndex,
    int PageLineStyleIndex,
    string? PageColorHex,
    string? PageWidthText,
    int PageArtIndex,
    string? ShadingColorHex,
    int ShadingPatternIndex);

public static class BordersAndShadingDialogPlanner
{
    public const string WidthValidationMessage = "Enter a border width between 0 and 12 points.";

    public static readonly IReadOnlyList<string> SettingNames = ["None", "Box", "Shadow", "3-D", "Custom"];
    public static readonly IReadOnlyList<string> LineStyleNames = ["Single", "Dotted", "Dashed", "Double", "Thick", "Wave"];
    public static readonly IReadOnlyList<BorderLineStyle> LineStyleValues =
        [BorderLineStyle.Single, BorderLineStyle.Dotted, BorderLineStyle.Dashed, BorderLineStyle.Double, BorderLineStyle.Thick, BorderLineStyle.Wave];

    public static readonly IReadOnlyList<string> PatternNames = ["Clear (none)", "Solid (100%)", "10%", "25%", "50%"];
    public static readonly IReadOnlyList<ShadingPattern> PatternValues =
        [ShadingPattern.Clear, ShadingPattern.Solid, ShadingPattern.Pct10, ShadingPattern.Pct25, ShadingPattern.Pct50];

    public static readonly IReadOnlyList<PageBorderArtOption> ArtBorders =
        [new("(none)", 0), .. PageBorderArtStyles.Curated.Select(style =>
            new PageBorderArtOption($"{style.Label} ({style.ArtId})", style.ArtId))];

    public static readonly IReadOnlyList<string> Palette =
    [
        "#000000", "#808080", "#C00000", "#FF0000", "#FFC000", "#FFFF00",
        "#92D050", "#00B050", "#00B0F0", "#0070C0", "#7030A0", "#FFFFFF",
    ];

    public static int SettingIndexFor(ParagraphBorder? border)
    {
        if (border is null)
            return 0;

        var fullBox = border is { Top: true, Left: true, Bottom: true, Right: true } && !border.BottomOnly;
        return fullBox ? 1 : 4;
    }

    public static BorderSettingPlan PlanParagraphSetting(int settingIndex) =>
        settingIndex switch
        {
            0 => new BorderSettingPlan(EdgeValue: false, EdgesEnabled: false),
            4 => new BorderSettingPlan(EdgeValue: null, EdgesEnabled: true),
            _ => new BorderSettingPlan(EdgeValue: true, EdgesEnabled: false),
        };

    public static int IndexOfLineStyle(BorderLineStyle value) =>
        Math.Max(0, IndexOf(LineStyleValues, value));

    public static int IndexOfPattern(ShadingPattern value) =>
        Math.Max(0, IndexOf(PatternValues, value));

    public static int ArtIndexFor(int artId)
    {
        for (var i = 0; i < ArtBorders.Count; i++)
        {
            if (ArtBorders[i].ArtId == artId)
                return i;
        }

        return 0;
    }

    public static bool TryBuildResult(
        BordersAndShadingDialogInput input,
        CultureInfo culture,
        out BordersAndShadingDialogResult? result,
        out string? errorMessage)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(culture);

        result = null;
        errorMessage = null;

        if (!TryReadWidth(input.ParagraphWidthText, culture, out var paragraphWidth) ||
            !TryReadWidth(input.PageWidthText, culture, out var pageWidth))
        {
            errorMessage = WidthValidationMessage;
            return false;
        }

        result = new BordersAndShadingDialogResult(
            ParagraphBorder: BuildParagraphBorder(input, paragraphWidth),
            PageBorder: BuildPageBorder(input, pageWidth),
            ShadingHex: input.ShadingColorHex,
            ShadingPattern: ValueAtOrDefault(PatternValues, input.ShadingPatternIndex));
        return true;
    }

    public static string FormatPoints(double value, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        return value.ToString("0.##", culture);
    }

    private static ParagraphBorder? BuildParagraphBorder(BordersAndShadingDialogInput input, double width)
    {
        if (input.ParagraphSettingIndex == 0)
            return null;

        if (!input.Top && !input.Left && !input.Bottom && !input.Right)
            return null;

        return new ParagraphBorder(input.ParagraphColorHex ?? "#000000", width)
        {
            LineStyle = ValueAtOrDefault(LineStyleValues, input.ParagraphLineStyleIndex),
            Top = input.Top,
            Left = input.Left,
            Bottom = input.Bottom,
            Right = input.Right,
        };
    }

    private static PageBorder? BuildPageBorder(BordersAndShadingDialogInput input, double width)
    {
        if (input.PageSettingIndex == 0)
            return null;

        var artIndex = Math.Clamp(input.PageArtIndex, 0, ArtBorders.Count - 1);
        return new PageBorder(input.PageColorHex ?? "#000000", width)
        {
            LineStyle = ValueAtOrDefault(LineStyleValues, input.PageLineStyleIndex),
            ArtId = ArtBorders[artIndex].ArtId,
        };
    }

    private static bool TryReadWidth(string? text, CultureInfo culture, out double width) =>
        double.TryParse((text ?? string.Empty).Trim(), NumberStyles.Float, culture, out width) &&
        width > 0 &&
        width <= 12;

    private static int IndexOf<T>(IReadOnlyList<T> values, T value)
    {
        for (var i = 0; i < values.Count; i++)
        {
            if (EqualityComparer<T>.Default.Equals(values[i], value))
                return i;
        }

        return -1;
    }

    private static T ValueAtOrDefault<T>(IReadOnlyList<T> values, int index) =>
        values[Math.Clamp(index, 0, values.Count - 1)];
}
