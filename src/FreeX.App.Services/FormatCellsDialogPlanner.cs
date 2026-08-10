using System.Globalization;
using FreeX.App.Presentation;
using FreeX.App.Presentation.FormatCells;
using FreeX.Core.Model;
using CellHAlign = FreeX.Core.Model.HorizontalAlignment;
using CellVAlign = FreeX.Core.Model.VerticalAlignment;

namespace FreeX.App.Services;

public enum FormatCellsDialogPlannerTab
{
    Number,
    Alignment,
    Font,
    Fill,
    Border,
    Protection
}

public enum FormatCellsDialogValidationTarget
{
    NumberDecimalPlaces,
    NumberFormat,
    FontSize,
    FontColor,
    FillColor,
    FillPatternColor,
    IndentLevel,
    TextRotation,
    BorderLineColor,
    BorderTopColor,
    BorderRightColor,
    BorderBottomColor,
    BorderLeftColor
}

public sealed record FormatCellsDialogValidation(
    FormatCellsDialogPlannerTab Tab,
    FormatCellsDialogValidationTarget Target,
    string MessageResourceKey);

public sealed record FormatCellsDialogFontLabels(
    string Regular,
    string Italic,
    string Bold,
    string BoldItalic,
    string UnderlineNone,
    string UnderlineSingle,
    string UnderlineDouble,
    string UnderlineSingleAccounting,
    string UnderlineDoubleAccounting);

public sealed record FormatCellsDialogNumberInput(
    string? Category,
    string? FormatText,
    int FormatSelectedIndex,
    string? DecimalPlacesText,
    string? Symbol,
    int NegativeIndex);

public sealed record FormatCellsDialogFontInput(
    FormatCellsDialogFontLabels Labels,
    string? FontNameText,
    string? SelectedFontName,
    string? FontSizeText,
    string? FontStyleLabel,
    string? UnderlineLabel,
    bool? DoubleUnderline,
    bool? Strikethrough,
    bool? Superscript,
    bool? Subscript,
    string? FontColorText);

public sealed record FormatCellsDialogFillInput(
    string? FillColorText,
    string? FillPatternColorText,
    CellFillPatternStyle FillPatternStyle,
    bool ClearFill);

public sealed record FormatCellsDialogAlignmentInput(
    string? HorizontalAlignmentText,
    string? VerticalAlignmentText,
    bool? WrapText,
    bool? ShrinkToFit,
    string? IndentLevelText,
    string? TextRotationText,
    bool InitialMergeCells,
    bool? MergeCells);

public sealed record FormatCellsDialogBorderSideInput(string? StyleText, string? ColorText);

public sealed record FormatCellsDialogBorderInput(
    string? LineColorText,
    FormatCellsDialogBorderSideInput Top,
    FormatCellsDialogBorderSideInput Right,
    FormatCellsDialogBorderSideInput Bottom,
    FormatCellsDialogBorderSideInput Left,
    bool ClearPresetRequested,
    CellBorder? OutlinePreset,
    CellBorder? InsidePreset);

public sealed record FormatCellsDialogProtectionInput(bool? Locked, bool? Hidden);

public sealed record FormatCellsDialogInput(
    FormatCellsDialogNumberInput Number,
    FormatCellsDialogFontInput Font,
    FormatCellsDialogFillInput Fill,
    FormatCellsDialogAlignmentInput Alignment,
    FormatCellsDialogBorderInput Border,
    FormatCellsDialogProtectionInput Protection);

public sealed record FormatCellsDialogBorderSelection(
    bool Clear,
    CellBorder? Outline,
    CellBorder? Inside)
{
    public static FormatCellsDialogBorderSelection None { get; } = new(false, null, null);

    public bool HasRangeOperations => Clear || Outline is not null || Inside is not null;
}

public sealed record FormatCellsDialogResult(
    StyleDiff Diff,
    FormatCellsDialogBorderSelection BorderSelection,
    bool? MergeCells);

/// <summary>
/// Shared WPF-authority measurements for the Format Cells Alignment tab.
/// Hosts consume these values instead of inventing platform-specific vertical spacing.
/// </summary>
public static class FormatCellsDialogAlignmentLayout
{
    public const double ContentInset = 8;
    public const double TabHeaderHeight = 20;
    public const double TabHeaderMinWidth = 54;
    public const double LabelTopMargin = 4;
    public const double LabelBottomMargin = 2;
    public const double FollowupLabelTopMargin = 8;
    public const double CheckBoxTopMargin = 8;
    public const double FollowupCheckBoxTopMargin = 6;
    public const double CheckBoxHeight = 16;
    public const double ControlHeight = 24;
}

public sealed record FormatCellsDialogFillPatternChoice(CellFillPatternStyle Style, string ResourceKey);

public sealed record FormatCellsDialogFillPatternDisplayChoice(CellFillPatternStyle Style, string Label);

public static class FormatCellsDialogPlanner
{
    public static IReadOnlyList<FormatCellsDialogFillPatternChoice> FillPatternChoices { get; } =
    [
        new(CellFillPatternStyle.None, "FormatCells_FillPatternNone"),
        new(CellFillPatternStyle.Solid, "FormatCells_FillPatternSolid"),
        new(CellFillPatternStyle.Gray0625, "FormatCells_FillPatternGray0625"),
        new(CellFillPatternStyle.Gray125, "FormatCells_FillPatternGray125"),
        new(CellFillPatternStyle.LightGray, "FormatCells_FillPatternLightGray"),
        new(CellFillPatternStyle.MediumGray, "FormatCells_FillPatternMediumGray"),
        new(CellFillPatternStyle.DarkGray, "FormatCells_FillPatternDarkGray"),
        new(CellFillPatternStyle.LightHorizontal, "FormatCells_FillPatternLightHorizontal"),
        new(CellFillPatternStyle.LightVertical, "FormatCells_FillPatternLightVertical"),
        new(CellFillPatternStyle.LightDown, "FormatCells_FillPatternLightDown"),
        new(CellFillPatternStyle.LightUp, "FormatCells_FillPatternLightUp"),
        new(CellFillPatternStyle.LightGrid, "FormatCells_FillPatternLightGrid"),
        new(CellFillPatternStyle.LightTrellis, "FormatCells_FillPatternLightTrellis"),
        new(CellFillPatternStyle.DarkHorizontal, "FormatCells_FillPatternDarkHorizontal"),
        new(CellFillPatternStyle.DarkVertical, "FormatCells_FillPatternDarkVertical"),
        new(CellFillPatternStyle.DarkDown, "FormatCells_FillPatternDarkDown"),
        new(CellFillPatternStyle.DarkUp, "FormatCells_FillPatternDarkUp"),
        new(CellFillPatternStyle.DarkGrid, "FormatCells_FillPatternDarkGrid"),
        new(CellFillPatternStyle.DarkTrellis, "FormatCells_FillPatternDarkTrellis")
    ];

    public static IReadOnlyList<FormatCellsDialogFillPatternDisplayChoice> CreateFillPatternDisplayChoices(
        Func<string, string> getText)
    {
        ArgumentNullException.ThrowIfNull(getText);

        return FillPatternChoices
            .Select(choice => new FormatCellsDialogFillPatternDisplayChoice(choice.Style, getText(choice.ResourceKey)))
            .ToArray();
    }

    public static string GetFillPatternResourceKey(CellFillPatternStyle style)
    {
        foreach (var choice in FillPatternChoices)
        {
            if (choice.Style == style)
                return choice.ResourceKey;
        }

        return "FormatCells_FillPatternNone";
    }

    public static CellFillPatternStyle ResolveFillPatternStyle(
        string? selectedLabel,
        IReadOnlyList<FormatCellsDialogFillPatternDisplayChoice> choices)
    {
        if (!string.IsNullOrWhiteSpace(selectedLabel))
        {
            foreach (var choice in choices)
            {
                if (string.Equals(choice.Label, selectedLabel, StringComparison.Ordinal))
                    return choice.Style;
            }
        }

        return CellFillPatternStyle.None;
    }

    public static string FontStyleLabel(bool bold, bool italic, FormatCellsDialogFontLabels labels) => (bold, italic) switch
    {
        (true, true) => labels.BoldItalic,
        (true, false) => labels.Bold,
        (false, true) => labels.Italic,
        _ => labels.Regular
    };

    public static bool IsFontStyleBold(string? selectedStyle, FormatCellsDialogFontLabels labels) =>
        string.Equals(selectedStyle, labels.Bold, StringComparison.Ordinal) ||
        string.Equals(selectedStyle, labels.BoldItalic, StringComparison.Ordinal);

    public static bool IsFontStyleItalic(string? selectedStyle, FormatCellsDialogFontLabels labels) =>
        string.Equals(selectedStyle, labels.Italic, StringComparison.Ordinal) ||
        string.Equals(selectedStyle, labels.BoldItalic, StringComparison.Ordinal);

    public static bool IsSingleUnderlineSelected(string? selectedUnderline, FormatCellsDialogFontLabels labels) =>
        string.Equals(selectedUnderline, labels.UnderlineSingle, StringComparison.Ordinal) ||
        string.Equals(selectedUnderline, labels.UnderlineSingleAccounting, StringComparison.Ordinal);

    public static bool IsDoubleUnderlineSelected(string? selectedUnderline, FormatCellsDialogFontLabels labels) =>
        string.Equals(selectedUnderline, labels.UnderlineDouble, StringComparison.Ordinal) ||
        string.Equals(selectedUnderline, labels.UnderlineDoubleAccounting, StringComparison.Ordinal);

    public static string? ResolveSelectedFontName(string? typedFontName, string? selectedFontName)
    {
        var typed = typedFontName?.Trim();
        return string.IsNullOrWhiteSpace(typed) ? selectedFontName : typed;
    }

    public static bool TryCreateResult(
        CellStyle current,
        FormatCellsDialogInput input,
        out FormatCellsDialogResult? result,
        out FormatCellsDialogValidation? validation)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(input);

        result = null;
        validation = null;

        if (!TryParseRequiredColor(input.Font.FontColorText, out var fontColor))
            return Fail(FontColorValidation(), out validation);

        if (!TryParseOptionalColor(input.Fill.FillColorText, out var fillColor))
            return Fail(FillColorValidation(), out validation);

        if (!TryParseOptionalColor(input.Fill.FillPatternColorText, out var fillPatternColor))
            return Fail(FillPatternColorValidation(), out validation);

        var numberAvailability = FormatCellsNumberControlPlanner.Plan(input.Number.Category);
        if (numberAvailability.UsesDecimals && !IsValidDecimalPlaces(input.Number.DecimalPlacesText))
            return Fail(NumberDecimalPlacesValidation(), out validation);

        if (!numberAvailability.GeneratesFormat
            && !FormatCellsInputParser.IsSupportedCustomNumberFormat(input.Number.FormatText ?? string.Empty))
        {
            return Fail(NumberFormatValidation(), out validation);
        }

        var numberFormat = FormatCellsNumberFormatPlanner.ResolveSelectedNumberFormat(
            input.Number.Category,
            input.Number.FormatText ?? string.Empty,
            input.Number.FormatSelectedIndex,
            input.Number.DecimalPlacesText,
            input.Number.Symbol,
            input.Number.NegativeIndex);

        var fontSize = FormatCellsInputParser.TryParseFontSize(input.Font.FontSizeText ?? string.Empty);
        if (fontSize is null)
            return Fail(FontSizeValidation(), out validation);

        var indentLevel = FormatCellsInputParser.TryParseIndentLevel(input.Alignment.IndentLevelText ?? string.Empty);
        if (indentLevel is null)
            return Fail(IndentLevelValidation(), out validation);

        var textRotation = FormatCellsInputParser.TryParseSupportedTextRotation(input.Alignment.TextRotationText ?? string.Empty);
        if (textRotation is null)
            return Fail(TextRotationValidation(), out validation);

        if (!TryValidateBorder(input.Border, out validation))
            return false;

        var borderTop = ParseBorder(input.Border.Top, current.BorderTop);
        var borderRight = ParseBorder(input.Border.Right, current.BorderRight);
        var borderBottom = ParseBorder(input.Border.Bottom, current.BorderBottom);
        var borderLeft = ParseBorder(input.Border.Left, current.BorderLeft);

        result = new FormatCellsDialogResult(
            new StyleDiff(
                Bold: IsFontStyleBold(input.Font.FontStyleLabel, input.Font.Labels),
                Italic: IsFontStyleItalic(input.Font.FontStyleLabel, input.Font.Labels),
                Underline: IsSingleUnderlineSelected(input.Font.UnderlineLabel, input.Font.Labels),
                Strikethrough: input.Font.Strikethrough,
                Superscript: input.Font.Superscript,
                Subscript: input.Font.Subscript,
                FontName: ResolveSelectedFontName(input.Font.FontNameText, input.Font.SelectedFontName),
                FontSize: fontSize,
                FontColor: fontColor,
                FillColor: input.Fill.ClearFill ? null : fillColor,
                FillPatternStyle: input.Fill.ClearFill ? CellFillPatternStyle.None : input.Fill.FillPatternStyle,
                FillPatternColor: input.Fill.ClearFill ? null : fillPatternColor,
                HAlign: TryParseEnum<CellHAlign>(input.Alignment.HorizontalAlignmentText),
                VAlign: TryParseEnum<CellVAlign>(input.Alignment.VerticalAlignmentText),
                WrapText: input.Alignment.WrapText,
                ShrinkToFit: input.Alignment.ShrinkToFit,
                NumberFormat: numberFormat,
                DoubleUnderline: input.Font.DoubleUnderline == true ||
                    IsDoubleUnderlineSelected(input.Font.UnderlineLabel, input.Font.Labels),
                IndentLevel: indentLevel,
                TextRotation: textRotation,
                BorderTop: borderTop,
                BorderRight: borderRight,
                BorderBottom: borderBottom,
                BorderLeft: borderLeft,
                Locked: input.Protection.Locked,
                Hidden: input.Protection.Hidden,
                ClearFill: input.Fill.ClearFill ? true : null),
            new FormatCellsDialogBorderSelection(
                input.Border.ClearPresetRequested,
                input.Border.OutlinePreset,
                input.Border.InsidePreset),
            input.Alignment.MergeCells == input.Alignment.InitialMergeCells
                ? null
                : input.Alignment.MergeCells == true);
        return true;
    }

    public static CellBorder ParseBorder(FormatCellsDialogBorderSideInput input, CellBorder current)
    {
        var style = TryParseEnum<BorderStyle>(input.StyleText) ?? current.Style;
        var color = TryParseColor(input.ColorText) ?? current.Color;
        return new CellBorder(style, color);
    }

    public static BorderStyle ResolveBorderLineStyle(string? primaryStyleText, string? fallbackStyleText = null) =>
        TryParseEnum<BorderStyle>(primaryStyleText)
        ?? TryParseEnum<BorderStyle>(fallbackStyleText)
        ?? BorderStyle.Thin;

    public static CellBorder CreateSelectedBorderLine(string? styleText, string? colorText) =>
        new(ResolveBorderLineStyle(styleText), TryParseColor(colorText) ?? CellColor.Black);

    public static BorderStyle NextBorderSideStyle(string? currentSideStyleText, string? selectedLineStyleText) =>
        BorderSideNeedsColor(currentSideStyleText)
            ? BorderStyle.None
            : ResolveBorderLineStyle(selectedLineStyleText);

    public static bool BorderSideNeedsColor(string? styleText) =>
        !string.Equals(styleText, nameof(BorderStyle.None), StringComparison.Ordinal);

    public static double? TryParseFontSize(string text) =>
        FormatCellsInputParser.TryParseFontSize(text);

    public static int? TryParseIndentLevel(string text) =>
        FormatCellsInputParser.TryParseIndentLevel(text);

    public static int? TryParseSupportedTextRotation(string text) =>
        FormatCellsInputParser.TryParseSupportedTextRotation(text);

    public static bool IsSupportedCustomNumberFormat(string text) =>
        FormatCellsInputParser.IsSupportedCustomNumberFormat(text);

    private static bool TryValidateBorder(
        FormatCellsDialogBorderInput input,
        out FormatCellsDialogValidation? validation)
    {
        validation = null;

        if (!TryParseRequiredColor(input.LineColorText, out _))
            return Fail(BorderLineColorValidation(), out validation);

        if (BorderSideNeedsColor(input.Top.StyleText) && !TryParseRequiredColor(input.Top.ColorText, out _))
            return Fail(BorderTopColorValidation(), out validation);

        if (BorderSideNeedsColor(input.Right.StyleText) && !TryParseRequiredColor(input.Right.ColorText, out _))
            return Fail(BorderRightColorValidation(), out validation);

        if (BorderSideNeedsColor(input.Bottom.StyleText) && !TryParseRequiredColor(input.Bottom.ColorText, out _))
            return Fail(BorderBottomColorValidation(), out validation);

        if (BorderSideNeedsColor(input.Left.StyleText) && !TryParseRequiredColor(input.Left.ColorText, out _))
            return Fail(BorderLeftColorValidation(), out validation);

        return true;
    }

    private static bool IsValidDecimalPlaces(string? text) =>
        int.TryParse(text?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var decimals)
        && decimals is >= 0 and <= 30;

    private static bool TryParseRequiredColor(string? text, out CellColor color) =>
        ColorInputParser.TryParseColorText(text ?? string.Empty, out color);

    private static bool TryParseOptionalColor(string? text, out CellColor? color)
    {
        color = null;
        if (string.IsNullOrWhiteSpace(text))
            return true;

        if (!ColorInputParser.TryParseColorText(text, out var parsed))
            return false;

        color = parsed;
        return true;
    }

    private static CellColor? TryParseColor(string? text) =>
        ColorInputParser.TryParseColorText(text ?? string.Empty, out var color)
            ? color
            : null;

    private static TEnum? TryParseEnum<TEnum>(string? text)
        where TEnum : struct, Enum =>
        !string.IsNullOrWhiteSpace(text) &&
        Enum.TryParse<TEnum>(text, out var parsed) &&
        Enum.IsDefined(parsed)
            ? parsed
            : null;

    private static bool Fail(
        FormatCellsDialogValidation failure,
        out FormatCellsDialogValidation validation)
    {
        validation = failure;
        return false;
    }

    private static FormatCellsDialogValidation NumberDecimalPlacesValidation() =>
        new(FormatCellsDialogPlannerTab.Number, FormatCellsDialogValidationTarget.NumberDecimalPlaces, "FormatCells_InvalidDecimalPlacesMessage");

    private static FormatCellsDialogValidation NumberFormatValidation() =>
        new(FormatCellsDialogPlannerTab.Number, FormatCellsDialogValidationTarget.NumberFormat, "FormatCells_InvalidCustomNumberFormatMessage");

    private static FormatCellsDialogValidation FontSizeValidation() =>
        new(FormatCellsDialogPlannerTab.Font, FormatCellsDialogValidationTarget.FontSize, "FormatCells_InvalidFontSizeMessage");

    private static FormatCellsDialogValidation FontColorValidation() =>
        new(FormatCellsDialogPlannerTab.Font, FormatCellsDialogValidationTarget.FontColor, "FormatCells_InvalidFontColorMessage");

    private static FormatCellsDialogValidation FillColorValidation() =>
        new(FormatCellsDialogPlannerTab.Fill, FormatCellsDialogValidationTarget.FillColor, "FormatCells_InvalidFillColorMessage");

    private static FormatCellsDialogValidation FillPatternColorValidation() =>
        new(FormatCellsDialogPlannerTab.Fill, FormatCellsDialogValidationTarget.FillPatternColor, "FormatCells_InvalidPatternColorMessage");

    private static FormatCellsDialogValidation IndentLevelValidation() =>
        new(FormatCellsDialogPlannerTab.Alignment, FormatCellsDialogValidationTarget.IndentLevel, "FormatCells_InvalidIndentLevelMessage");

    private static FormatCellsDialogValidation TextRotationValidation() =>
        new(FormatCellsDialogPlannerTab.Alignment, FormatCellsDialogValidationTarget.TextRotation, "FormatCells_InvalidTextRotationMessage");

    private static FormatCellsDialogValidation BorderLineColorValidation() =>
        new(FormatCellsDialogPlannerTab.Border, FormatCellsDialogValidationTarget.BorderLineColor, "FormatCells_InvalidBorderColorMessage");

    private static FormatCellsDialogValidation BorderTopColorValidation() =>
        new(FormatCellsDialogPlannerTab.Border, FormatCellsDialogValidationTarget.BorderTopColor, "FormatCells_InvalidTopBorderColorMessage");

    private static FormatCellsDialogValidation BorderRightColorValidation() =>
        new(FormatCellsDialogPlannerTab.Border, FormatCellsDialogValidationTarget.BorderRightColor, "FormatCells_InvalidRightBorderColorMessage");

    private static FormatCellsDialogValidation BorderBottomColorValidation() =>
        new(FormatCellsDialogPlannerTab.Border, FormatCellsDialogValidationTarget.BorderBottomColor, "FormatCells_InvalidBottomBorderColorMessage");

    private static FormatCellsDialogValidation BorderLeftColorValidation() =>
        new(FormatCellsDialogPlannerTab.Border, FormatCellsDialogValidationTarget.BorderLeftColor, "FormatCells_InvalidLeftBorderColorMessage");
}
