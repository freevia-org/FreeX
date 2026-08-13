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

public sealed record BordersAndShadingDialogInitialState(
    int ParagraphSettingIndex,
    int ParagraphLineStyleIndex,
    int ParagraphColorIndex,
    string ParagraphWidthText,
    bool Top,
    bool Left,
    bool Bottom,
    bool Right,
    int PageSettingIndex,
    int PageLineStyleIndex,
    int PageColorIndex,
    string PageWidthText,
    int PageArtIndex,
    int ShadingColorIndex,
    int ShadingPatternIndex);

public sealed record PageBordersDialogInitialState(
    int SettingIndex,
    int LineStyleIndex,
    int ColorIndex,
    string WidthText,
    int ArtIndex);

public sealed record PageBordersDialogInput(
    int SettingIndex,
    int LineStyleIndex,
    int ColorIndex,
    string? WidthText,
    int ArtIndex);

public sealed record PageBordersDialogAcceptance(
    bool IsAccepted,
    PageBorder? PageBorder = null,
    string? ValidationMessage = null);

public sealed record BordersAndShadingDialogAcceptance(
    BordersAndShadingDialogResult? Result,
    string? ValidationMessage)
{
    public bool IsAccepted => Result is not null;
}

/// <summary>
/// Renderer-neutral geometry for the paired WPF/Avalonia Borders and Shading surface.
/// Values prefixed with <c>Avalonia</c> compensate only for native template measurement and remain
/// separate from the cross-renderer authority metrics.
/// </summary>
public readonly record struct BordersAndShadingDialogVisualMetrics(
    double DialogWidth,
    double OuterInset,
    double ActionTopInset,
    double ActionBottomInset,
    double ActionButtonWidth,
    double ValidationTopInset,
    double ContentInset,
    double FieldMinWidth,
    double RowVerticalInset,
    double LabelRightInset,
    double EdgeSpacing,
    double SwatchWidth,
    double SwatchHeight,
    double SwatchBorderThickness,
    double SwatchLabelSpacing,
    double AvaloniaControlHeight,
    double AvaloniaButtonHeight,
    double AvaloniaButtonHorizontalPadding,
    double AvaloniaButtonVerticalPadding,
    double AvaloniaTabPaneHorizontalCompensation);

public sealed class BordersAndShadingDialogSession
{
    private readonly CultureInfo _culture;

    public BordersAndShadingDialogSession(
        ParagraphFormatting paragraph,
        PageBorder? pageBorder,
        CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(paragraph);
        ArgumentNullException.ThrowIfNull(culture);
        _culture = culture;
        InitialState = BordersAndShadingDialogPlanner.BuildInitialState(paragraph, pageBorder, culture);
    }

    public BordersAndShadingDialogInitialState InitialState { get; }

    public BorderSettingPlan PlanParagraphSetting(int settingIndex) =>
        BordersAndShadingDialogPlanner.PlanParagraphSetting(settingIndex);

    public string PaletteHex(int index) => BordersAndShadingDialogPlanner.PaletteHex(index);

    public string? ShadingHex(int index) =>
        index <= 0 ? null : BordersAndShadingDialogPlanner.PaletteHex(index - 1);

    public BordersAndShadingDialogAcceptance PlanAcceptance(BordersAndShadingDialogInput input)
    {
        if (!BordersAndShadingDialogPlanner.TryBuildResult(input, _culture, out var result, out var error))
            return new BordersAndShadingDialogAcceptance(null, error);

        return new BordersAndShadingDialogAcceptance(result, ValidationMessage: null);
    }
}

public static class BordersAndShadingDialogPlanner
{
    public static BordersAndShadingDialogVisualMetrics VisualMetrics { get; } = new(
        DialogWidth: 420,
        OuterInset: 14,
        ActionTopInset: 12,
        ActionBottomInset: 12,
        ActionButtonWidth: 72,
        ValidationTopInset: 8,
        ContentInset: 8,
        FieldMinWidth: 160,
        RowVerticalInset: 4,
        LabelRightInset: 8,
        EdgeSpacing: 16,
        SwatchWidth: 28,
        SwatchHeight: 12,
        SwatchBorderThickness: 1,
        SwatchLabelSpacing: 6,
        AvaloniaControlHeight: 20,
        AvaloniaButtonHeight: 26,
        AvaloniaButtonHorizontalPadding: 10,
        AvaloniaButtonVerticalPadding: 1,
        AvaloniaTabPaneHorizontalCompensation: -12);

    public const string Title = "Borders and Shading";
    public const string BordersTabLabel = "Borders";
    public const string PageBorderTabLabel = "Page Border";
    public const string ShadingTabLabel = "Shading";
    public const string SettingLabel = "Setting:";
    public const string StyleLabel = "Style:";
    public const string ColorLabel = "Colour:";
    public const string WidthLabel = "Width (pt):";
    public const string EdgesLabel = "Edges:";
    public const string ArtBorderLabel = "Art border:";
    public const string FillLabel = "Fill:";
    public const string PatternLabel = "Pattern:";
    public const string TopLabel = "Top";
    public const string LeftLabel = "Left";
    public const string BottomLabel = "Bottom";
    public const string RightLabel = "Right";
    public const string NoColorLabel = "No Colour";
    public const string AcceptButtonLabel = "OK";
    public const string RemovePageBorderButtonLabel = "None";
    public const string CancelButtonLabel = "Cancel";
    public const string WidthValidationMessage = "Enter a border width between 0 and 12 points.";
    public const string AutomationId = "BordersAndShadingDialog";
    public const string ParagraphSettingAutomationId = "BordersAndShadingParagraphSetting";
    public const string ParagraphStyleAutomationId = "BordersAndShadingParagraphStyle";
    public const string ParagraphColorAutomationId = "BordersAndShadingParagraphColor";
    public const string ParagraphWidthAutomationId = "BordersAndShadingParagraphWidth";
    public const string TopEdgeAutomationId = "BordersAndShadingTopEdge";
    public const string LeftEdgeAutomationId = "BordersAndShadingLeftEdge";
    public const string BottomEdgeAutomationId = "BordersAndShadingBottomEdge";
    public const string RightEdgeAutomationId = "BordersAndShadingRightEdge";
    public const string PageSettingAutomationId = "BordersAndShadingPageSetting";
    public const string PageStyleAutomationId = "BordersAndShadingPageStyle";
    public const string PageColorAutomationId = "BordersAndShadingPageColor";
    public const string PageWidthAutomationId = "BordersAndShadingPageWidth";
    public const string PageArtAutomationId = "BordersAndShadingPageArt";
    public const string ShadingColorAutomationId = "BordersAndShadingShadingColor";
    public const string ShadingPatternAutomationId = "BordersAndShadingShadingPattern";
    public const string ValidationAutomationId = "BordersAndShadingValidationMessage";
    public const string TabsAutomationId = "BordersAndShadingTabs";
    public const string BordersTabAutomationId = "BordersAndShadingBordersTab";
    public const string PageBorderTabAutomationId = "BordersAndShadingPageBorderTab";
    public const string ShadingTabAutomationId = "BordersAndShadingShadingTab";
    public const string AcceptButtonAutomationId = "BordersAndShadingOkButton";
    public const string CancelButtonAutomationId = "BordersAndShadingCancelButton";
    public const string NoShadingColorAutomationId = "BordersAndShadingNoShadingColor";

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

    public static BordersAndShadingDialogInitialState BuildInitialState(
        ParagraphFormatting paragraph,
        PageBorder? pageBorder,
        CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(paragraph);
        ArgumentNullException.ThrowIfNull(culture);

        var border = paragraph.Border;
        return new BordersAndShadingDialogInitialState(
            ParagraphSettingIndex: SettingIndexFor(border),
            ParagraphLineStyleIndex: IndexOfLineStyle(border?.LineStyle ?? BorderLineStyle.Single),
            ParagraphColorIndex: PaletteIndex(border?.ColorHex ?? "#000000"),
            ParagraphWidthText: FormatPoints(border?.WidthPt ?? 0.5, culture),
            Top: border?.Top ?? true,
            Left: border?.Left ?? true,
            Bottom: border?.Bottom ?? true,
            Right: border?.Right ?? true,
            PageSettingIndex: pageBorder is null ? 0 : 1,
            PageLineStyleIndex: IndexOfLineStyle(pageBorder?.LineStyle ?? BorderLineStyle.Single),
            PageColorIndex: PaletteIndex(pageBorder?.ColorHex ?? "#000000"),
            PageWidthText: FormatPoints(pageBorder?.WidthPt ?? 1.0, culture),
            PageArtIndex: ArtIndexFor(pageBorder?.ArtId ?? 0),
            ShadingColorIndex: string.IsNullOrWhiteSpace(paragraph.ShadingColorHex)
                ? 0
                : PaletteIndex(paragraph.ShadingColorHex) + 1,
            ShadingPatternIndex: IndexOfPattern(paragraph.ShadingPattern));
    }

    public static PageBordersDialogInitialState BuildPageBordersInitialState(
        PageBorder? pageBorder,
        CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        return new PageBordersDialogInitialState(
            SettingIndex: pageBorder is null ? 0 : 1,
            LineStyleIndex: IndexOfLineStyle(pageBorder?.LineStyle ?? BorderLineStyle.Single),
            ColorIndex: PaletteIndex(pageBorder?.ColorHex ?? "#000000"),
            WidthText: FormatPoints(pageBorder?.WidthPt ?? 1.0, culture),
            ArtIndex: ArtIndexFor(pageBorder?.ArtId ?? 0));
    }

    public static PageBordersDialogAcceptance SubmitPageBorders(
        PageBordersDialogInput input,
        CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(culture);

        if (!TryReadWidth(input.WidthText, culture, out var width))
            return new PageBordersDialogAcceptance(false, ValidationMessage: WidthValidationMessage);

        if (input.SettingIndex == 0)
            return new PageBordersDialogAcceptance(true);

        var artIndex = Math.Clamp(input.ArtIndex, 0, ArtBorders.Count - 1);
        var pageBorder = new PageBorder(PaletteHex(input.ColorIndex), width)
        {
            LineStyle = ValueAtOrDefault(LineStyleValues, input.LineStyleIndex),
            ArtId = ArtBorders[artIndex].ArtId,
        };
        return new PageBordersDialogAcceptance(true, pageBorder);
    }

    public static string PaletteHex(int index) =>
        Palette[Math.Clamp(index, 0, Palette.Count - 1)];

    public static int PaletteIndex(string? hex)
    {
        for (var i = 0; i < Palette.Count; i++)
        {
            if (string.Equals(Palette[i], hex, StringComparison.OrdinalIgnoreCase))
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
