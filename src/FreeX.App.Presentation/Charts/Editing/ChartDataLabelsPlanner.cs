using FreeX.App.Presentation;
using FreeX.App.Presentation.Charts;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Charts.Editing;

/// <summary>A data-label position choice for the "Data Labels" dialog: the value plus its English label.</summary>
public sealed record ChartDataLabelPositionChoice(ChartDataLabelPosition Position, string DisplayName);

/// <summary>
/// The data-label show/position/which-values state read from a chart and edited back through the dialog.
/// The first parameters are the original cross-platform data-label dialog surface; the optional tail carries
/// separator, number format, callout, and label styling state for fuller shells without breaking existing
/// callers.
/// </summary>
public readonly record struct ChartDataLabelsInput(
    bool ShowDataLabels,
    ChartDataLabelPosition Position,
    bool ShowValue,
    bool ShowCategoryName,
    bool ShowSeriesName,
    bool ShowPercentage,
    bool ShowLegendKey,
    ChartDataLabelSeparator? Separator = null,
    ChartDataLabelNumberFormat? NumberFormat = null,
    bool? ShowCallouts = null,
    CellColor? FillColor = null,
    CellColor? BorderColor = null,
    CellColor? TextColor = null,
    double? BorderThickness = null,
    double? FontSize = null,
    double? Angle = null);

/// <summary>A semantic validation failure for the "Data Labels" dialog input.</summary>
public enum ChartDataLabelsValidationIssue
{
    BorderThicknessOutOfRange,
    FontSizeOutOfRange,
    AngleOutOfRange,
}

public enum ChartDataLabelsParseIssue
{
    None,
    FillColor,
    BorderColor,
    TextColor,
    BorderThickness,
    FontSize,
    Angle,
}

/// <summary>
/// Portable (no UI) planner for the "Data Labels" editing dialog (show/hide, position, and which values
/// each label prints). Single-sources the offered positions and projects an edited
/// <see cref="ChartDataLabelsInput"/> into the <see cref="ChartLayoutOptions"/> the shell hands to the Core
/// <see cref="SetChartLayoutCommand"/>. When labels are hidden the position and value toggles are still set
/// so re-showing restores the chosen configuration. Reused across every shell.
/// </summary>
public static class ChartDataLabelsPlanner
{
    public const double MinBorderThickness = 0;
    public const double MaxBorderThickness = 10;
    public const double MinFontSize = 6;
    public const double MaxFontSize = 72;
    public const double MinAngle = -90;
    public const double MaxAngle = 90;

    // Excel's data-label placements. Order mirrors the position cycler used by the ribbon toggle.
    private static readonly ChartDataLabelPositionChoice[] PositionCatalog =
    [
        new(ChartDataLabelPosition.BestFit, "Best Fit"),
        new(ChartDataLabelPosition.OutsideEnd, "Outside End"),
        new(ChartDataLabelPosition.InsideEnd, "Inside End"),
        new(ChartDataLabelPosition.Center, "Center"),
    ];

    private static readonly ChartDataLabelSeparator[] SeparatorCatalog =
    [
        ChartDataLabelSeparator.Comma,
        ChartDataLabelSeparator.Semicolon,
        ChartDataLabelSeparator.NewLine,
        ChartDataLabelSeparator.Space,
    ];

    private static readonly ChartDataLabelNumberFormat[] NumberFormatCatalog =
    [
        ChartDataLabelNumberFormat.General,
        ChartDataLabelNumberFormat.Number,
        ChartDataLabelNumberFormat.Currency,
        ChartDataLabelNumberFormat.Percent,
    ];

    /// <summary>The selectable data-label positions, in display order.</summary>
    public static IReadOnlyList<ChartDataLabelPositionChoice> GetPositionChoices() => PositionCatalog;

    public static IReadOnlyList<ChartDataLabelSeparator> GetSeparatorChoices() => SeparatorCatalog;

    public static IReadOnlyList<ChartDataLabelNumberFormat> GetNumberFormatChoices() => NumberFormatCatalog;

    /// <summary>The English display label for <paramref name="position"/> (falls back to the enum name).</summary>
    public static string DisplayName(ChartDataLabelPosition position)
    {
        foreach (var choice in PositionCatalog)
        {
            if (choice.Position == position)
                return choice.DisplayName;
        }

        return position.ToString();
    }

    /// <summary>Reads the chart's current data-label state into the dialog input shape.</summary>
    public static ChartDataLabelsInput Read(ChartModel chart) =>
        Normalize(new ChartDataLabelsInput(
            chart.ShowDataLabels,
            chart.DataLabelPosition,
            chart.ShowDataLabelValue,
            chart.ShowDataLabelCategoryName,
            chart.ShowDataLabelSeriesName,
            chart.ShowDataLabelPercentage,
            chart.ShowDataLabelLegendKey,
            chart.DataLabelSeparator,
            chart.DataLabelNumberFormat,
            chart.ShowDataLabelCallouts,
            chart.DataLabelFillColor,
            chart.DataLabelBorderColor,
            chart.DataLabelTextColor,
            chart.DataLabelBorderThickness,
            chart.DataLabelFontSize,
            chart.DataLabelAngle));

    /// <summary>
    /// Normalizes result/default state for the data-label dialog: unknown enum values fall back to
    /// Excel-like defaults, and numeric styling values are clamped to accepted command ranges.
    /// </summary>
    public static ChartDataLabelsInput Normalize(ChartDataLabelsInput input) =>
        input with
        {
            Position = IsSelectablePosition(input.Position) ? input.Position : ChartDataLabelPosition.BestFit,
            Separator = NormalizeSeparator(input.Separator),
            NumberFormat = NormalizeNumberFormat(input.NumberFormat),
            BorderThickness = ClampFiniteOrNull(input.BorderThickness, 0, MinBorderThickness, MaxBorderThickness),
            FontSize = ClampFiniteOrNull(input.FontSize, 11, MinFontSize, MaxFontSize),
            Angle = ClampFiniteOrNull(input.Angle, 0, MinAngle, MaxAngle),
        };

    /// <summary>Returns an English validation reason for the edited data-label styling, or null when valid.</summary>
    public static string? Validate(ChartDataLabelsInput input)
    {
        var issue = ValidateIssue(input);
        return issue switch
        {
            ChartDataLabelsValidationIssue.BorderThicknessOutOfRange => "The data-label border width must be between 0 and 10.",
            ChartDataLabelsValidationIssue.FontSizeOutOfRange => "The data-label font size must be between 6 and 72.",
            ChartDataLabelsValidationIssue.AngleOutOfRange => "The data-label angle must be between -90 and 90.",
            _ => null,
        };
    }

    /// <summary>
    /// Validates the edited data-label input and returns the semantic field that failed, if any. Text parsing
    /// and localized messages stay with the shell; the domain limits live here.
    /// </summary>
    public static ChartDataLabelsValidationIssue? ValidateIssue(ChartDataLabelsInput input)
    {
        if (input.BorderThickness is { } borderThickness && !IsFiniteInRange(borderThickness, MinBorderThickness, MaxBorderThickness))
            return ChartDataLabelsValidationIssue.BorderThicknessOutOfRange;

        if (input.FontSize is { } fontSize && !IsFiniteInRange(fontSize, MinFontSize, MaxFontSize))
            return ChartDataLabelsValidationIssue.FontSizeOutOfRange;

        if (input.Angle is { } angle && !IsFiniteInRange(angle, MinAngle, MaxAngle))
            return ChartDataLabelsValidationIssue.AngleOutOfRange;

        return null;
    }

    /// <summary>
    /// Builds the <see cref="ChartLayoutOptions"/> delta for the edited data-label state. An invalid/unknown
    /// position falls back to Best Fit. When the labels are shown but no value toggle is selected the planner
    /// turns on the plain value so a shown label always prints something. The position and value toggles are
    /// always set (even when hiding) so re-showing keeps the chosen configuration.
    /// </summary>
    public static ChartLayoutOptions Plan(ChartDataLabelsInput input)
    {
        var normalized = Normalize(input);

        var showValue = normalized.ShowValue;
        var anyValueSelected = showValue || normalized.ShowCategoryName || normalized.ShowSeriesName
            || normalized.ShowPercentage || normalized.ShowLegendKey;
        if (normalized.ShowDataLabels && !anyValueSelected)
            showValue = true;

        return new ChartLayoutOptions(
            ShowDataLabels: normalized.ShowDataLabels,
            DataLabelPosition: normalized.Position,
            ShowDataLabelValue: showValue,
            ShowDataLabelCategoryName: normalized.ShowCategoryName,
            ShowDataLabelSeriesName: normalized.ShowSeriesName,
            ShowDataLabelPercentage: normalized.ShowPercentage,
            ShowDataLabelLegendKey: normalized.ShowLegendKey,
            DataLabelSeparator: normalized.Separator,
            DataLabelNumberFormat: normalized.NumberFormat,
            ShowDataLabelCallouts: normalized.ShowCallouts,
            DataLabelFillColor: normalized.FillColor,
            DataLabelBorderColor: normalized.BorderColor,
            DataLabelTextColor: normalized.TextColor,
            DataLabelBorderThickness: normalized.BorderThickness,
            DataLabelFontSize: normalized.FontSize,
            DataLabelAngle: normalized.Angle);
    }

    public static bool TryParseDialogInput(
        bool showDataLabels,
        ChartDataLabelPosition? selectedPosition,
        bool showValue,
        bool showLegendKey,
        bool showCategoryName,
        bool showSeriesName,
        bool showPercentage,
        ChartDataLabelSeparator? selectedSeparator,
        ChartDataLabelNumberFormat? selectedNumberFormat,
        bool showCallouts,
        string? fillColorText,
        string? borderColorText,
        string? textColorText,
        string? borderThicknessText,
        string? fontSizeText,
        string? angleText,
        out ChartDataLabelsInput input,
        out ChartDataLabelsParseIssue issue)
    {
        input = default;

        if (!ColorInputParser.TryParseOptionalHexColor(fillColorText ?? string.Empty, out var fillColor))
        {
            issue = ChartDataLabelsParseIssue.FillColor;
            return false;
        }

        if (!ColorInputParser.TryParseOptionalHexColor(borderColorText ?? string.Empty, out var borderColor))
        {
            issue = ChartDataLabelsParseIssue.BorderColor;
            return false;
        }

        if (!ColorInputParser.TryParseOptionalHexColor(textColorText ?? string.Empty, out var textColor))
        {
            issue = ChartDataLabelsParseIssue.TextColor;
            return false;
        }

        if (!ChartDialogValueParser.TryParseClampedDouble(
                borderThicknessText ?? string.Empty,
                MinBorderThickness,
                MaxBorderThickness,
                out var borderThickness))
        {
            issue = ChartDataLabelsParseIssue.BorderThickness;
            return false;
        }

        if (!ChartDialogValueParser.TryParseClampedDouble(
                fontSizeText ?? string.Empty,
                MinFontSize,
                MaxFontSize,
                out var fontSize))
        {
            issue = ChartDataLabelsParseIssue.FontSize;
            return false;
        }

        if (!ChartDialogValueParser.TryParseClampedDouble(
                angleText ?? string.Empty,
                MinAngle,
                MaxAngle,
                out var angle))
        {
            issue = ChartDataLabelsParseIssue.Angle;
            return false;
        }

        input = new ChartDataLabelsInput(
            showDataLabels,
            selectedPosition is { } position && IsSelectablePosition(position)
                ? position
                : ChartDataLabelPosition.BestFit,
            showValue,
            showCategoryName,
            showSeriesName,
            showPercentage,
            showLegendKey,
            selectedSeparator is { } separator && IsKnownSeparator(separator)
                ? separator
                : ChartDataLabelSeparator.Comma,
            selectedNumberFormat is { } numberFormat && IsKnownNumberFormat(numberFormat)
                ? numberFormat
                : ChartDataLabelNumberFormat.General,
            showCallouts,
            fillColor,
            borderColor,
            textColor,
            borderThickness,
            fontSize,
            angle);

        if (ValidateIssue(input) is { } validationIssue)
        {
            issue = validationIssue switch
            {
                ChartDataLabelsValidationIssue.BorderThicknessOutOfRange => ChartDataLabelsParseIssue.BorderThickness,
                ChartDataLabelsValidationIssue.FontSizeOutOfRange => ChartDataLabelsParseIssue.FontSize,
                ChartDataLabelsValidationIssue.AngleOutOfRange => ChartDataLabelsParseIssue.Angle,
                _ => ChartDataLabelsParseIssue.BorderThickness,
            };
            return false;
        }

        input = Normalize(input);
        issue = ChartDataLabelsParseIssue.None;
        return true;
    }

    private static ChartDataLabelSeparator? NormalizeSeparator(ChartDataLabelSeparator? separator) =>
        separator is null ? null : IsKnownSeparator(separator.Value) ? separator.Value : ChartDataLabelSeparator.Comma;

    private static ChartDataLabelNumberFormat? NormalizeNumberFormat(ChartDataLabelNumberFormat? numberFormat) =>
        numberFormat is null ? null : IsKnownNumberFormat(numberFormat.Value) ? numberFormat.Value : ChartDataLabelNumberFormat.General;

    private static bool IsKnownSeparator(ChartDataLabelSeparator separator)
    {
        foreach (var candidate in SeparatorCatalog)
        {
            if (candidate == separator)
                return true;
        }

        return false;
    }

    private static bool IsKnownNumberFormat(ChartDataLabelNumberFormat numberFormat)
    {
        foreach (var candidate in NumberFormatCatalog)
        {
            if (candidate == numberFormat)
                return true;
        }

        return false;
    }

    private static bool IsFiniteInRange(double value, double min, double max) =>
        double.IsFinite(value) && value >= min && value <= max;

    private static double? ClampFiniteOrNull(double? value, double fallback, double min, double max) =>
        value is null ? null : Math.Clamp(double.IsFinite(value.Value) ? value.Value : fallback, min, max);

    private static bool IsSelectablePosition(ChartDataLabelPosition position)
    {
        foreach (var choice in PositionCatalog)
        {
            if (choice.Position == position)
                return true;
        }

        return false;
    }
}
