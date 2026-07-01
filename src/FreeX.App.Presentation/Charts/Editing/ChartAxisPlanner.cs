using FreeX.App.Presentation;
using FreeX.App.Presentation.Charts;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Charts.Editing;

/// <summary>A number-format choice for the "Format Axis" dialog: the value plus its English label.</summary>
public sealed record ChartAxisNumberFormatChoice(ChartDataLabelNumberFormat NumberFormat, string DisplayName);

public enum ChartAxisDialogControlKind
{
    CheckBox,
    ComboBox,
    Color,
    Number,
}

public enum ChartAxisDialogFieldId
{
    Minimum,
    Maximum,
    MajorUnit,
    MinorUnit,
    LogScale,
    NumberFormat,
    MajorGridlines,
    MinorGridlines,
    MajorGridlineColor,
    MinorGridlineColor,
    GridlineThickness,
    MajorTickMarks,
    MinorTickMarks,
    ShowLabels,
    LabelTextColor,
    LabelFontSize,
    LabelAngle,
    LineColor,
    LineThickness,
}

public sealed record ChartAxisDialogFieldDescriptor(
    ChartAxisDialogFieldId Id,
    ChartAxisDialogControlKind ControlKind,
    string LabelResourceKey,
    string AutomationId,
    string? HelpResourceKey = null);

public sealed record ChartAxisDialogSectionDescriptor(
    string HeaderResourceKey,
    IReadOnlyList<ChartAxisDialogFieldDescriptor> Fields,
    string? HelpResourceKey = null);

/// <summary>
/// The axis bounds / number-format / gridline state read from a chart and edited back through the dialog.
/// A null <see cref="Minimum"/> / <see cref="Maximum"/> means "auto" (let the renderer pick). The first
/// parameters are the original cross-platform axis dialog surface; the optional tail carries the fuller WPF
/// format-axis surface without breaking existing callers.
/// </summary>
public readonly record struct ChartAxisInput(
    bool UseXAxis,
    double? Minimum,
    double? Maximum,
    double? MajorUnit,
    bool LogScale,
    ChartDataLabelNumberFormat NumberFormat,
    bool ShowMajorGridlines,
    bool ShowMinorGridlines,
    double? MinorUnit = null,
    CellColor? MajorGridlineColor = null,
    CellColor? MinorGridlineColor = null,
    double? GridlineThickness = null,
    ChartAxisTickStyle? MajorTickStyle = null,
    ChartAxisTickStyle? MinorTickStyle = null,
    bool? ShowLabels = null,
    CellColor? LabelTextColor = null,
    double? LabelFontSize = null,
    double? LabelAngle = null,
    CellColor? LineColor = null,
    double? LineThickness = null);

/// <summary>A semantic validation failure for the "Format Axis" dialog input.</summary>
public enum ChartAxisValidationIssue
{
    MinimumNotBelowMaximum,
    MajorUnitNotPositive,
    MinorUnitNotPositive,
    GridlineThicknessNotPositive,
    LabelFontSizeOutOfRange,
    LabelAngleOutOfRange,
    LineThicknessOutOfRange,
}

public enum ChartAxisFormatParseIssue
{
    None,
    Minimum,
    Maximum,
    MajorUnit,
    MinorUnit,
    MajorGridlineColor,
    MinorGridlineColor,
    GridlineThickness,
    LabelTextColor,
    LabelFontSize,
    LabelAngle,
    LineColor,
    LineThickness,
}

public enum ChartAxisQuickCommand
{
    TickMarks,
    Labels,
    LabelFont,
    LabelAngle,
    AxisLine,
    Gridlines,
    GridlineStyle,
    NumberFormat,
}

public enum ChartAxisCommandIssue
{
    None,
    UnsupportedLogScale,
    UnsupportedBounds,
    NumericBoundsRequired,
}

public readonly record struct ChartAxisCommandPlan(ChartLayoutOptions? Options, ChartAxisCommandIssue Issue)
{
    public bool Success => Options is not null && Issue == ChartAxisCommandIssue.None;

    public static ChartAxisCommandPlan Succeeded(ChartLayoutOptions options) =>
        new(options, ChartAxisCommandIssue.None);

    public static ChartAxisCommandPlan Failed(ChartAxisCommandIssue issue) =>
        new(null, issue);
}

/// <summary>
/// Portable (no UI) planner for the "Format Axis" editing dialog: per-axis (X/Y) bounds (min/max, with null
/// meaning auto), major unit, log scale, axis number format, and major/minor gridline visibility.
/// Single-sources the offered number formats and validates the bounds (min must be below max; major unit
/// must be positive) before projecting an edited <see cref="ChartAxisInput"/> into the
/// <see cref="ChartLayoutOptions"/> the shell hands to the Core <see cref="SetChartLayoutCommand"/>. When
/// both bounds are auto the matching <c>ClearAxisBounds</c> flag is set so the command clears any stale
/// numeric bounds. Reused across every shell.
/// </summary>
public static class ChartAxisPlanner
{
    public const double MinGridlineThickness = 0.25;
    public const double MaxGridlineThickness = 10;
    public const double MinLabelFontSize = 6;
    public const double MaxLabelFontSize = 72;
    public const double MinLabelAngle = -90;
    public const double MaxLabelAngle = 90;
    public const double MinLineThickness = 0.5;
    public const double MaxLineThickness = 10;

    private static readonly ChartAxisNumberFormatChoice[] NumberFormatCatalog =
    [
        new(ChartDataLabelNumberFormat.General, "General"),
        new(ChartDataLabelNumberFormat.Number, "Number"),
        new(ChartDataLabelNumberFormat.Currency, "Currency"),
        new(ChartDataLabelNumberFormat.Percent, "Percentage"),
    ];

    private static readonly ChartAxisTickStyle[] TickStyleCatalog =
    [
        ChartAxisTickStyle.None,
        ChartAxisTickStyle.Inside,
        ChartAxisTickStyle.Outside,
        ChartAxisTickStyle.Cross,
    ];

    private static readonly ChartAxisDialogFieldDescriptor[] AxisOptionFields =
    [
        new(ChartAxisDialogFieldId.Minimum, ChartAxisDialogControlKind.Number, "ChartAxisFormat_MinimumLabel", "ChartAxisMinimumBox", "ChartAxisFormat_MinimumHelpText"),
        new(ChartAxisDialogFieldId.Maximum, ChartAxisDialogControlKind.Number, "ChartAxisFormat_MaximumLabel", "ChartAxisMaximumBox", "ChartAxisFormat_MaximumHelpText"),
        new(ChartAxisDialogFieldId.MajorUnit, ChartAxisDialogControlKind.Number, "ChartAxisFormat_MajorUnitLabel", "ChartAxisMajorUnitBox", "ChartAxisFormat_MajorUnitHelpText"),
        new(ChartAxisDialogFieldId.MinorUnit, ChartAxisDialogControlKind.Number, "ChartAxisFormat_MinorUnitLabel", "ChartAxisMinorUnitBox", "ChartAxisFormat_MinorUnitHelpText"),
        new(ChartAxisDialogFieldId.LogScale, ChartAxisDialogControlKind.CheckBox, "ChartAxisFormat_LogScale", "ChartAxisLogScaleCheck"),
        new(ChartAxisDialogFieldId.NumberFormat, ChartAxisDialogControlKind.ComboBox, "ChartAxisFormat_NumberFormatLabel", "ChartAxisNumberFormatCombo"),
    ];

    private static readonly ChartAxisDialogFieldDescriptor[] GridlineFields =
    [
        new(ChartAxisDialogFieldId.MajorGridlines, ChartAxisDialogControlKind.CheckBox, "ChartAxisFormat_MajorGridlines", "ChartAxisMajorGridlinesCheck"),
        new(ChartAxisDialogFieldId.MinorGridlines, ChartAxisDialogControlKind.CheckBox, "ChartAxisFormat_MinorGridlines", "ChartAxisMinorGridlinesCheck"),
        new(ChartAxisDialogFieldId.MajorGridlineColor, ChartAxisDialogControlKind.Color, "ChartAxisFormat_MajorGridlineColorLabel", "ChartAxisMajorGridlineColorBox"),
        new(ChartAxisDialogFieldId.MinorGridlineColor, ChartAxisDialogControlKind.Color, "ChartAxisFormat_MinorGridlineColorLabel", "ChartAxisMinorGridlineColorBox"),
        new(ChartAxisDialogFieldId.GridlineThickness, ChartAxisDialogControlKind.Number, "ChartAxisFormat_GridlineWidthLabel", "ChartAxisGridlineWidthBox", "ChartAxisFormat_GridlineWidthHelpText"),
    ];

    private static readonly ChartAxisDialogFieldDescriptor[] TickMarkFields =
    [
        new(ChartAxisDialogFieldId.MajorTickMarks, ChartAxisDialogControlKind.ComboBox, "ChartAxisFormat_MajorTickMarksLabel", "ChartAxisMajorTickMarksCombo"),
        new(ChartAxisDialogFieldId.MinorTickMarks, ChartAxisDialogControlKind.ComboBox, "ChartAxisFormat_MinorTickMarksLabel", "ChartAxisMinorTickMarksCombo"),
        new(ChartAxisDialogFieldId.ShowLabels, ChartAxisDialogControlKind.CheckBox, "ChartAxisFormat_ShowLabels", "ChartAxisLabelsCheck"),
        new(ChartAxisDialogFieldId.LabelTextColor, ChartAxisDialogControlKind.Color, "ChartAxisFormat_LabelColorLabel", "ChartAxisLabelColorBox"),
        new(ChartAxisDialogFieldId.LabelFontSize, ChartAxisDialogControlKind.Number, "ChartAxisFormat_LabelFontSizeLabel", "ChartAxisLabelFontSizeBox", "ChartAxisFormat_LabelFontSizeHelpText"),
        new(ChartAxisDialogFieldId.LabelAngle, ChartAxisDialogControlKind.Number, "ChartAxisFormat_LabelAngleLabel", "ChartAxisLabelAngleBox", "ChartAxisFormat_LabelAngleHelpText"),
        new(ChartAxisDialogFieldId.LineColor, ChartAxisDialogControlKind.Color, "ChartAxisFormat_AxisLineColorLabel", "ChartAxisLineColorBox"),
        new(ChartAxisDialogFieldId.LineThickness, ChartAxisDialogControlKind.Number, "ChartAxisFormat_AxisLineWidthLabel", "ChartAxisLineWidthBox", "ChartAxisFormat_AxisLineWidthHelpText"),
    ];

    private static readonly ChartAxisDialogSectionDescriptor[] DialogSections =
    [
        new("ChartAxisFormat_AxisOptionsGroup", AxisOptionFields, "ChartAxisFormat_BoundsHelpText"),
        new("ChartAxisFormat_GridlinesGroup", GridlineFields),
        new("ChartAxisFormat_TickMarksGroup", TickMarkFields),
    ];

    /// <summary>The selectable axis number formats, in display order.</summary>
    public static IReadOnlyList<ChartAxisNumberFormatChoice> GetNumberFormatChoices() => NumberFormatCatalog;

    /// <summary>The selectable tick-mark styles, in display order.</summary>
    public static IReadOnlyList<ChartAxisTickStyle> GetTickStyleChoices() => TickStyleCatalog;

    public static IReadOnlyList<ChartAxisDialogSectionDescriptor> GetDialogSections() => DialogSections;

    public static ChartAxisDialogSectionDescriptor GetAxisOptionsSection() => DialogSections[0];

    public static ChartAxisDialogSectionDescriptor GetGridlinesSection() => DialogSections[1];

    public static ChartAxisDialogSectionDescriptor GetTickMarksSection() => DialogSections[2];

    public static ChartAxisDialogFieldDescriptor GetDialogField(ChartAxisDialogFieldId id)
    {
        foreach (var section in DialogSections)
        {
            foreach (var field in section.Fields)
            {
                if (field.Id == id)
                    return field;
            }
        }

        throw new ArgumentOutOfRangeException(nameof(id), id, null);
    }

    /// <summary>The English display label for <paramref name="numberFormat"/> (falls back to the enum name).</summary>
    public static string DisplayName(ChartDataLabelNumberFormat numberFormat)
    {
        foreach (var choice in NumberFormatCatalog)
        {
            if (choice.NumberFormat == numberFormat)
                return choice.DisplayName;
        }

        return numberFormat.ToString();
    }

    /// <summary>True when <paramref name="type"/> has axes to format (false for pie/doughnut).</summary>
    public static bool SupportsAxes(ChartType type) => ChartTypeSupport.SupportsAxes(type);

    /// <summary>Reads the chart's current state for the chosen axis into the dialog input shape.</summary>
    public static ChartAxisInput Read(ChartModel chart, bool useXAxis) => Normalize(useXAxis
        ? new ChartAxisInput(
            UseXAxis: true,
            Minimum: chart.XAxisMinimum,
            Maximum: chart.XAxisMaximum,
            MajorUnit: chart.XAxisMajorUnit,
            LogScale: chart.XAxisLogScale,
            NumberFormat: chart.XAxisNumberFormat,
            ShowMajorGridlines: chart.ShowXAxisMajorGridlines,
            ShowMinorGridlines: chart.ShowXAxisMinorGridlines,
            MinorUnit: chart.XAxisMinorUnit,
            MajorGridlineColor: chart.XAxisMajorGridlineColor,
            MinorGridlineColor: chart.XAxisMinorGridlineColor,
            GridlineThickness: chart.XAxisGridlineThickness,
            MajorTickStyle: chart.XAxisMajorTickStyle,
            MinorTickStyle: chart.XAxisMinorTickStyle,
            ShowLabels: chart.ShowXAxisLabels,
            LabelTextColor: chart.XAxisLabelTextColor,
            LabelFontSize: chart.XAxisLabelFontSize,
            LabelAngle: chart.XAxisLabelAngle,
            LineColor: chart.XAxisLineColor,
            LineThickness: chart.XAxisLineThickness)
        : new ChartAxisInput(
            UseXAxis: false,
            Minimum: chart.YAxisMinimum,
            Maximum: chart.YAxisMaximum,
            MajorUnit: chart.YAxisMajorUnit,
            LogScale: chart.YAxisLogScale,
            NumberFormat: chart.YAxisNumberFormat,
            ShowMajorGridlines: chart.ShowYAxisMajorGridlines,
            ShowMinorGridlines: chart.ShowYAxisMinorGridlines,
            MinorUnit: chart.YAxisMinorUnit,
            MajorGridlineColor: chart.YAxisMajorGridlineColor,
            MinorGridlineColor: chart.YAxisMinorGridlineColor,
            GridlineThickness: chart.YAxisGridlineThickness,
            MajorTickStyle: chart.YAxisMajorTickStyle,
            MinorTickStyle: chart.YAxisMinorTickStyle,
            ShowLabels: chart.ShowYAxisLabels,
            LabelTextColor: chart.YAxisLabelTextColor,
            LabelFontSize: chart.YAxisLabelFontSize,
            LabelAngle: chart.YAxisLabelAngle,
            LineColor: chart.YAxisLineColor,
            LineThickness: chart.YAxisLineThickness));

    /// <summary>
    /// Normalizes result/default state for the axis dialog: unknown enum values fall back to Excel-like
    /// defaults, non-finite optional numbers become auto, and style dimensions are clamped to accepted
    /// command ranges before projection.
    /// </summary>
    public static ChartAxisInput Normalize(ChartAxisInput input) =>
        input with
        {
            Minimum = FiniteOrNull(input.Minimum),
            Maximum = FiniteOrNull(input.Maximum),
            MajorUnit = PositiveFiniteOrNull(input.MajorUnit),
            MinorUnit = PositiveFiniteOrNull(input.MinorUnit),
            NumberFormat = IsKnownNumberFormat(input.NumberFormat) ? input.NumberFormat : ChartDataLabelNumberFormat.General,
            GridlineThickness = ClampFiniteOrNull(input.GridlineThickness, 1, MinGridlineThickness, MaxGridlineThickness),
            MajorTickStyle = NormalizeTickStyle(input.MajorTickStyle, ChartAxisTickStyle.Outside),
            MinorTickStyle = NormalizeTickStyle(input.MinorTickStyle, ChartAxisTickStyle.None),
            LabelFontSize = ClampFiniteOrNull(input.LabelFontSize, 11, MinLabelFontSize, MaxLabelFontSize),
            LabelAngle = ClampFiniteOrNull(input.LabelAngle, 0, MinLabelAngle, MaxLabelAngle),
            LineThickness = ClampFiniteOrNull(input.LineThickness, 1, MinLineThickness, MaxLineThickness),
        };

    /// <summary>
    /// Validates the edited axis bounds. Returns null when the input is valid, otherwise an English reason
    /// the change is rejected (min not below max, or a non-positive major unit). Auto (null) bounds are
    /// always valid.
    /// </summary>
    public static string? Validate(ChartAxisInput input)
    {
        var issue = ValidateIssue(input);
        return issue switch
        {
            ChartAxisValidationIssue.MinimumNotBelowMaximum => "The axis minimum must be less than the maximum.",
            ChartAxisValidationIssue.MajorUnitNotPositive => "The major unit must be greater than zero.",
            ChartAxisValidationIssue.MinorUnitNotPositive => "The minor unit must be greater than zero.",
            ChartAxisValidationIssue.GridlineThicknessNotPositive => "The gridline width must be greater than zero.",
            ChartAxisValidationIssue.LabelFontSizeOutOfRange => "The label font size must be between 6 and 72.",
            ChartAxisValidationIssue.LabelAngleOutOfRange => "The label angle must be between -90 and 90.",
            ChartAxisValidationIssue.LineThicknessOutOfRange => "The axis line width must be between 0.5 and 10.",
            _ => null,
        };
    }

    /// <summary>
    /// Validates the edited axis input and returns the semantic field that failed, if any. Text parsing and
    /// localized message ownership stay with the shell; the domain limits live here.
    /// </summary>
    public static ChartAxisValidationIssue? ValidateIssue(ChartAxisInput input)
    {
        if (input.Minimum is { } min && input.Maximum is { } max && min >= max)
            return ChartAxisValidationIssue.MinimumNotBelowMaximum;

        if (!IsPositiveOptional(input.MajorUnit))
            return ChartAxisValidationIssue.MajorUnitNotPositive;

        if (!IsPositiveOptional(input.MinorUnit))
            return ChartAxisValidationIssue.MinorUnitNotPositive;

        if (input.GridlineThickness is { } gridlineThickness && !IsPositiveFinite(gridlineThickness))
            return ChartAxisValidationIssue.GridlineThicknessNotPositive;

        if (input.LabelFontSize is { } labelFontSize && !IsFiniteInRange(labelFontSize, MinLabelFontSize, MaxLabelFontSize))
            return ChartAxisValidationIssue.LabelFontSizeOutOfRange;

        if (input.LabelAngle is { } labelAngle && !IsFiniteInRange(labelAngle, MinLabelAngle, MaxLabelAngle))
            return ChartAxisValidationIssue.LabelAngleOutOfRange;

        if (input.LineThickness is { } lineThickness && !IsFiniteInRange(lineThickness, MinLineThickness, MaxLineThickness))
            return ChartAxisValidationIssue.LineThicknessOutOfRange;

        return null;
    }

    /// <summary>
    /// Builds the <see cref="ChartLayoutOptions"/> delta for the edited axis state. An invalid/unknown number
    /// format falls back to General. When both bounds are auto (null) the matching axis-bounds clear flag is
    /// set so the command resets any stale numeric bounds on that axis.
    /// </summary>
    public static ChartLayoutOptions Plan(ChartAxisInput input)
    {
        var normalized = Normalize(input);
        var clearBounds = normalized.Minimum is null && normalized.Maximum is null;

        return normalized.UseXAxis
            ? new ChartLayoutOptions(
                XAxisMinimum: normalized.Minimum,
                XAxisMaximum: normalized.Maximum,
                XAxisMajorUnit: normalized.MajorUnit,
                XAxisMinorUnit: normalized.MinorUnit,
                XAxisLogScale: normalized.LogScale,
                XAxisNumberFormat: normalized.NumberFormat,
                ShowXAxisMajorGridlines: normalized.ShowMajorGridlines,
                ShowXAxisMinorGridlines: normalized.ShowMinorGridlines,
                XAxisMajorGridlineColor: normalized.MajorGridlineColor,
                XAxisMinorGridlineColor: normalized.MinorGridlineColor,
                XAxisGridlineThickness: normalized.GridlineThickness,
                XAxisMajorTickStyle: normalized.MajorTickStyle,
                XAxisMinorTickStyle: normalized.MinorTickStyle,
                ShowXAxisLabels: normalized.ShowLabels,
                XAxisLabelTextColor: normalized.LabelTextColor,
                XAxisLabelFontSize: normalized.LabelFontSize,
                XAxisLabelAngle: normalized.LabelAngle,
                XAxisLineColor: normalized.LineColor,
                XAxisLineThickness: normalized.LineThickness,
                ClearXAxisBounds: clearBounds)
            : new ChartLayoutOptions(
                YAxisMinimum: normalized.Minimum,
                YAxisMaximum: normalized.Maximum,
                YAxisMajorUnit: normalized.MajorUnit,
                YAxisMinorUnit: normalized.MinorUnit,
                YAxisLogScale: normalized.LogScale,
                YAxisNumberFormat: normalized.NumberFormat,
                ShowYAxisMajorGridlines: normalized.ShowMajorGridlines,
                ShowYAxisMinorGridlines: normalized.ShowMinorGridlines,
                YAxisMajorGridlineColor: normalized.MajorGridlineColor,
                YAxisMinorGridlineColor: normalized.MinorGridlineColor,
                YAxisGridlineThickness: normalized.GridlineThickness,
                YAxisMajorTickStyle: normalized.MajorTickStyle,
                YAxisMinorTickStyle: normalized.MinorTickStyle,
                ShowYAxisLabels: normalized.ShowLabels,
                YAxisLabelTextColor: normalized.LabelTextColor,
                YAxisLabelFontSize: normalized.LabelFontSize,
                YAxisLabelAngle: normalized.LabelAngle,
                YAxisLineColor: normalized.LineColor,
                YAxisLineThickness: normalized.LineThickness,
                ClearYAxisBounds: clearBounds);
    }

    public static bool TryParseDialogInput(
        bool useXAxis,
        string? minimumText,
        string? maximumText,
        string? majorUnitText,
        bool logScale,
        ChartDataLabelNumberFormat? selectedNumberFormat,
        bool showMajorGridlines,
        bool showMinorGridlines,
        out ChartAxisInput input,
        out ChartAxisFormatParseIssue issue)
    {
        input = default;

        if (!ChartDialogValueParser.TryParseNullableDouble(minimumText ?? string.Empty, out var minimum))
        {
            issue = ChartAxisFormatParseIssue.Minimum;
            return false;
        }

        if (!ChartDialogValueParser.TryParseNullableDouble(maximumText ?? string.Empty, out var maximum))
        {
            issue = ChartAxisFormatParseIssue.Maximum;
            return false;
        }

        if (!ChartDialogValueParser.TryParseNullableDouble(majorUnitText ?? string.Empty, out var majorUnit))
        {
            issue = ChartAxisFormatParseIssue.MajorUnit;
            return false;
        }

        input = new ChartAxisInput(
            UseXAxis: useXAxis,
            Minimum: minimum,
            Maximum: maximum,
            MajorUnit: majorUnit,
            LogScale: logScale,
            NumberFormat: selectedNumberFormat is { } numberFormat && IsKnownNumberFormat(numberFormat)
                ? numberFormat
                : ChartDataLabelNumberFormat.General,
            ShowMajorGridlines: showMajorGridlines,
            ShowMinorGridlines: showMinorGridlines);

        if (ValidateIssue(input) is { } validationIssue)
        {
            issue = validationIssue switch
            {
                ChartAxisValidationIssue.MinimumNotBelowMaximum => ChartAxisFormatParseIssue.Maximum,
                ChartAxisValidationIssue.MajorUnitNotPositive => ChartAxisFormatParseIssue.MajorUnit,
                _ => ChartAxisFormatParseIssue.Minimum,
            };
            return false;
        }

        input = Normalize(input);
        issue = ChartAxisFormatParseIssue.None;
        return true;
    }

    public static bool TryParseDialogInput(
        bool useXAxis,
        string? minimumText,
        string? maximumText,
        string? majorUnitText,
        string? minorUnitText,
        bool logScale,
        ChartDataLabelNumberFormat? selectedNumberFormat,
        bool showMajorGridlines,
        bool showMinorGridlines,
        string? majorGridlineColorText,
        string? minorGridlineColorText,
        string? gridlineThicknessText,
        ChartAxisTickStyle? selectedMajorTickStyle,
        ChartAxisTickStyle? selectedMinorTickStyle,
        bool showLabels,
        string? labelTextColorText,
        string? labelFontSizeText,
        string? labelAngleText,
        string? lineColorText,
        string? lineThicknessText,
        out ChartAxisInput input,
        out ChartAxisFormatParseIssue issue)
    {
        input = default;

        if (!ChartDialogValueParser.TryParseNullableDouble(minimumText ?? string.Empty, out var minimum))
        {
            issue = ChartAxisFormatParseIssue.Minimum;
            return false;
        }

        if (!ChartDialogValueParser.TryParseNullableDouble(maximumText ?? string.Empty, out var maximum))
        {
            issue = ChartAxisFormatParseIssue.Maximum;
            return false;
        }

        if (!ChartDialogValueParser.TryParseNullableDouble(majorUnitText ?? string.Empty, out var majorUnit))
        {
            issue = ChartAxisFormatParseIssue.MajorUnit;
            return false;
        }

        if (!ChartDialogValueParser.TryParseNullableDouble(minorUnitText ?? string.Empty, out var minorUnit))
        {
            issue = ChartAxisFormatParseIssue.MinorUnit;
            return false;
        }

        if (!ColorInputParser.TryParseOptionalHexColor(majorGridlineColorText ?? string.Empty, out var majorGridlineColor))
        {
            issue = ChartAxisFormatParseIssue.MajorGridlineColor;
            return false;
        }

        if (!ColorInputParser.TryParseOptionalHexColor(minorGridlineColorText ?? string.Empty, out var minorGridlineColor))
        {
            issue = ChartAxisFormatParseIssue.MinorGridlineColor;
            return false;
        }

        if (!ChartDialogValueParser.TryParsePositiveDouble(gridlineThicknessText ?? string.Empty, out var gridlineThickness))
        {
            issue = ChartAxisFormatParseIssue.GridlineThickness;
            return false;
        }

        if (!ColorInputParser.TryParseOptionalHexColor(labelTextColorText ?? string.Empty, out var labelTextColor))
        {
            issue = ChartAxisFormatParseIssue.LabelTextColor;
            return false;
        }

        if (!ChartDialogValueParser.TryParseClampedDouble(
                labelFontSizeText ?? string.Empty,
                MinLabelFontSize,
                MaxLabelFontSize,
                out var labelFontSize))
        {
            issue = ChartAxisFormatParseIssue.LabelFontSize;
            return false;
        }

        if (!ChartDialogValueParser.TryParseClampedDouble(
                labelAngleText ?? string.Empty,
                MinLabelAngle,
                MaxLabelAngle,
                out var labelAngle))
        {
            issue = ChartAxisFormatParseIssue.LabelAngle;
            return false;
        }

        if (!ColorInputParser.TryParseOptionalHexColor(lineColorText ?? string.Empty, out var lineColor))
        {
            issue = ChartAxisFormatParseIssue.LineColor;
            return false;
        }

        if (!ChartDialogValueParser.TryParseClampedDouble(
                lineThicknessText ?? string.Empty,
                MinLineThickness,
                MaxLineThickness,
                out var lineThickness))
        {
            issue = ChartAxisFormatParseIssue.LineThickness;
            return false;
        }

        input = new ChartAxisInput(
            UseXAxis: useXAxis,
            Minimum: minimum,
            Maximum: maximum,
            MajorUnit: majorUnit,
            MinorUnit: minorUnit,
            LogScale: logScale,
            NumberFormat: selectedNumberFormat is { } numberFormat && IsKnownNumberFormat(numberFormat)
                ? numberFormat
                : ChartDataLabelNumberFormat.General,
            ShowMajorGridlines: showMajorGridlines,
            ShowMinorGridlines: showMinorGridlines,
            MajorGridlineColor: majorGridlineColor,
            MinorGridlineColor: minorGridlineColor,
            GridlineThickness: gridlineThickness,
            MajorTickStyle: selectedMajorTickStyle is { } majorTickStyle && IsKnownTickStyle(majorTickStyle)
                ? majorTickStyle
                : ChartAxisTickStyle.Outside,
            MinorTickStyle: selectedMinorTickStyle is { } minorTickStyle && IsKnownTickStyle(minorTickStyle)
                ? minorTickStyle
                : ChartAxisTickStyle.None,
            ShowLabels: showLabels,
            LabelTextColor: labelTextColor,
            LabelFontSize: labelFontSize,
            LabelAngle: labelAngle,
            LineColor: lineColor,
            LineThickness: lineThickness);

        if (ValidateIssue(input) is { } validationIssue)
        {
            issue = validationIssue switch
            {
                ChartAxisValidationIssue.MinimumNotBelowMaximum => ChartAxisFormatParseIssue.Maximum,
                ChartAxisValidationIssue.MajorUnitNotPositive => ChartAxisFormatParseIssue.MajorUnit,
                ChartAxisValidationIssue.MinorUnitNotPositive => ChartAxisFormatParseIssue.MinorUnit,
                ChartAxisValidationIssue.GridlineThicknessNotPositive => ChartAxisFormatParseIssue.GridlineThickness,
                ChartAxisValidationIssue.LabelFontSizeOutOfRange => ChartAxisFormatParseIssue.LabelFontSize,
                ChartAxisValidationIssue.LabelAngleOutOfRange => ChartAxisFormatParseIssue.LabelAngle,
                ChartAxisValidationIssue.LineThicknessOutOfRange => ChartAxisFormatParseIssue.LineThickness,
                _ => ChartAxisFormatParseIssue.Maximum,
            };
            return false;
        }

        input = Normalize(input);
        issue = ChartAxisFormatParseIssue.None;
        return true;
    }

    public static bool CanToggleSecondaryAxis(ChartModel chart)
    {
        ArgumentNullException.ThrowIfNull(chart);

        return ChartTypeSupport.SupportsSecondaryAxis(chart.Type) &&
               (chart.ShowSecondaryAxis || ChartOptionCycler.GetSeriesCount(chart) >= 2);
    }

    public static ChartLayoutOptions PlanSecondaryAxisToggle(ChartModel chart)
    {
        ArgumentNullException.ThrowIfNull(chart);

        return new ChartLayoutOptions(
            ShowSecondaryAxis: !chart.ShowSecondaryAxis,
            SecondaryAxisSeriesIndexes: []);
    }

    public static ChartLayoutOptions PlanQuickCommand(
        ChartModel chart,
        bool useXAxis,
        ChartAxisQuickCommand command)
    {
        ArgumentNullException.ThrowIfNull(chart);

        return command switch
        {
            ChartAxisQuickCommand.TickMarks => PlanTickMarks(chart, useXAxis),
            ChartAxisQuickCommand.Labels => useXAxis
                ? new ChartLayoutOptions(ShowXAxisLabels: !chart.ShowXAxisLabels)
                : new ChartLayoutOptions(ShowYAxisLabels: !chart.ShowYAxisLabels),
            ChartAxisQuickCommand.LabelFont => PlanLabelFont(chart, useXAxis),
            ChartAxisQuickCommand.LabelAngle => PlanLabelAngle(chart, useXAxis),
            ChartAxisQuickCommand.AxisLine => PlanAxisLine(chart, useXAxis),
            ChartAxisQuickCommand.Gridlines => PlanGridlines(chart, useXAxis),
            ChartAxisQuickCommand.GridlineStyle => PlanGridlineStyle(chart, useXAxis),
            ChartAxisQuickCommand.NumberFormat => PlanNumberFormat(chart, useXAxis),
            _ => throw new ArgumentOutOfRangeException(nameof(command), command, null),
        };
    }

    public static ChartAxisCommandPlan PlanLogScaleToggle(Sheet sheet, ChartModel chart, bool useXAxis)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        ArgumentNullException.ThrowIfNull(chart);

        if (useXAxis && !ChartTypeSupport.SupportsXAxisLogScale(chart.Type))
            return ChartAxisCommandPlan.Failed(ChartAxisCommandIssue.UnsupportedLogScale);

        if (!useXAxis && !ChartTypeSupport.SupportsYAxisLogScale(chart.Type))
            return ChartAxisCommandPlan.Failed(ChartAxisCommandIssue.UnsupportedLogScale);

        var enableLog = useXAxis ? !chart.XAxisLogScale : !chart.YAxisLogScale;
        var options = useXAxis
            ? new ChartLayoutOptions(XAxisLogScale: enableLog)
            : new ChartLayoutOptions(YAxisLogScale: enableLog);

        if (enableLog && ChartOptionCycler.TryGetAxisBounds(sheet, chart, useXAxis, out var minimum, out var maximum))
        {
            var positiveMinimum = minimum > 0 ? minimum : 1;
            var positiveMaximum = maximum > positiveMinimum ? maximum : positiveMinimum * 10;
            options = useXAxis
                ? options with { XAxisMinimum = positiveMinimum, XAxisMaximum = positiveMaximum }
                : options with { YAxisMinimum = positiveMinimum, YAxisMaximum = positiveMaximum };
        }

        return ChartAxisCommandPlan.Succeeded(options);
    }

    public static ChartAxisCommandPlan PlanBoundsToggle(Sheet sheet, ChartModel chart, bool useXAxis)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        ArgumentNullException.ThrowIfNull(chart);

        var hasBounds = useXAxis
            ? chart.XAxisMinimum is not null || chart.XAxisMaximum is not null
            : chart.YAxisMinimum is not null || chart.YAxisMaximum is not null;
        if (!hasBounds &&
            (useXAxis
                ? !ChartTypeSupport.SupportsXAxisBounds(chart.Type)
                : !ChartTypeSupport.SupportsYAxisBounds(chart.Type)))
            return ChartAxisCommandPlan.Failed(ChartAxisCommandIssue.UnsupportedBounds);

        if (hasBounds)
        {
            var clearOptions = useXAxis
                ? new ChartLayoutOptions(ClearXAxisBounds: true)
                : new ChartLayoutOptions(ClearYAxisBounds: true);
            return ChartAxisCommandPlan.Succeeded(clearOptions);
        }

        if (!ChartOptionCycler.TryGetAxisBounds(sheet, chart, useXAxis, out var minimum, out var maximum))
            return ChartAxisCommandPlan.Failed(ChartAxisCommandIssue.NumericBoundsRequired);

        var majorUnit = Math.Max(double.Epsilon, (maximum - minimum) / 5);
        var minorUnit = Math.Max(double.Epsilon, majorUnit / 2);
        var options = useXAxis
            ? new ChartLayoutOptions(
                XAxisMinimum: minimum,
                XAxisMaximum: maximum,
                XAxisMajorUnit: majorUnit,
                XAxisMinorUnit: minorUnit)
            : new ChartLayoutOptions(
                YAxisMinimum: minimum,
                YAxisMaximum: maximum,
                YAxisMajorUnit: majorUnit,
                YAxisMinorUnit: minorUnit);
        return ChartAxisCommandPlan.Succeeded(options);
    }

    private static double? FiniteOrNull(double? value) =>
        value is { } number && double.IsFinite(number) ? number : null;

    private static ChartLayoutOptions PlanTickMarks(ChartModel chart, bool useXAxis)
    {
        var (major, minor) = useXAxis
            ? ChartOptionCycler.NextAxisTickState(chart.XAxisMajorTickStyle, chart.XAxisMinorTickStyle)
            : ChartOptionCycler.NextAxisTickState(chart.YAxisMajorTickStyle, chart.YAxisMinorTickStyle);
        return useXAxis
            ? new ChartLayoutOptions(XAxisMajorTickStyle: major, XAxisMinorTickStyle: minor)
            : new ChartLayoutOptions(YAxisMajorTickStyle: major, YAxisMinorTickStyle: minor);
    }

    private static ChartLayoutOptions PlanLabelFont(ChartModel chart, bool useXAxis)
    {
        var currentColor = useXAxis ? chart.XAxisLabelTextColor : chart.YAxisLabelTextColor;
        var currentSize = useXAxis ? chart.XAxisLabelFontSize : chart.YAxisLabelFontSize;
        var nextColor = ChartQuickFormatCycler.NextSeriesColor(currentColor);
        var nextSize = currentSize >= 14 ? 9 : currentSize + 1;
        return useXAxis
            ? new ChartLayoutOptions(XAxisLabelTextColor: nextColor, XAxisLabelFontSize: nextSize)
            : new ChartLayoutOptions(YAxisLabelTextColor: nextColor, YAxisLabelFontSize: nextSize);
    }

    private static ChartLayoutOptions PlanLabelAngle(ChartModel chart, bool useXAxis)
    {
        var currentAngle = useXAxis ? chart.XAxisLabelAngle : chart.YAxisLabelAngle;
        var nextAngle = ChartOptionCycler.NextAxisLabelAngle(currentAngle);
        return useXAxis
            ? new ChartLayoutOptions(XAxisLabelAngle: nextAngle)
            : new ChartLayoutOptions(YAxisLabelAngle: nextAngle);
    }

    private static ChartLayoutOptions PlanAxisLine(ChartModel chart, bool useXAxis)
    {
        var currentColor = useXAxis ? chart.XAxisLineColor : chart.YAxisLineColor;
        var currentThickness = useXAxis ? chart.XAxisLineThickness : chart.YAxisLineThickness;
        var (nextColor, nextThickness) = ChartOptionCycler.NextAxisLineState(currentColor, currentThickness);
        return useXAxis
            ? new ChartLayoutOptions(XAxisLineColor: nextColor, XAxisLineThickness: nextThickness)
            : new ChartLayoutOptions(YAxisLineColor: nextColor, YAxisLineThickness: nextThickness);
    }

    private static ChartLayoutOptions PlanGridlines(ChartModel chart, bool useXAxis)
    {
        var (showMajor, showMinor) = useXAxis
            ? ChartQuickFormatCycler.NextGridlineState(chart.ShowXAxisMajorGridlines, chart.ShowXAxisMinorGridlines)
            : ChartQuickFormatCycler.NextGridlineState(chart.ShowYAxisMajorGridlines, chart.ShowYAxisMinorGridlines);
        return useXAxis
            ? new ChartLayoutOptions(ShowXAxisMajorGridlines: showMajor, ShowXAxisMinorGridlines: showMinor)
            : new ChartLayoutOptions(ShowYAxisMajorGridlines: showMajor, ShowYAxisMinorGridlines: showMinor);
    }

    private static ChartLayoutOptions PlanGridlineStyle(ChartModel chart, bool useXAxis)
    {
        var currentMajorColor = useXAxis ? chart.XAxisMajorGridlineColor : chart.YAxisMajorGridlineColor;
        var currentMinorColor = useXAxis ? chart.XAxisMinorGridlineColor : chart.YAxisMinorGridlineColor;
        var currentThickness = useXAxis ? chart.XAxisGridlineThickness : chart.YAxisGridlineThickness;
        var nextMajorColor = ChartQuickFormatCycler.NextSeriesColor(currentMajorColor);
        var nextMinorColor = ChartQuickFormatCycler.NextSeriesColor(currentMinorColor ?? currentMajorColor);
        var nextThickness = currentThickness >= 3 ? 1 : currentThickness + 0.5;
        return useXAxis
            ? new ChartLayoutOptions(
                XAxisMajorGridlineColor: nextMajorColor,
                XAxisMinorGridlineColor: nextMinorColor,
                XAxisGridlineThickness: nextThickness,
                ShowXAxisMajorGridlines: true)
            : new ChartLayoutOptions(
                YAxisMajorGridlineColor: nextMajorColor,
                YAxisMinorGridlineColor: nextMinorColor,
                YAxisGridlineThickness: nextThickness,
                ShowYAxisMajorGridlines: true);
    }

    private static ChartLayoutOptions PlanNumberFormat(ChartModel chart, bool useXAxis)
    {
        var next = ChartOptionCycler.NextDataLabelNumberFormat(useXAxis ? chart.XAxisNumberFormat : chart.YAxisNumberFormat);
        return useXAxis
            ? new ChartLayoutOptions(XAxisNumberFormat: next)
            : new ChartLayoutOptions(YAxisNumberFormat: next);
    }

    private static double? PositiveFiniteOrNull(double? value) =>
        value is { } number && IsPositiveFinite(number) ? number : null;

    private static bool IsPositiveOptional(double? value) =>
        value is null || IsPositiveFinite(value.Value);

    private static bool IsPositiveFinite(double value) =>
        double.IsFinite(value) && value > 0;

    private static bool IsFiniteInRange(double value, double min, double max) =>
        double.IsFinite(value) && value >= min && value <= max;

    private static double? ClampFiniteOrNull(double? value, double fallback, double min, double max) =>
        value is null ? null : Math.Clamp(double.IsFinite(value.Value) ? value.Value : fallback, min, max);

    private static bool IsKnownNumberFormat(ChartDataLabelNumberFormat numberFormat)
    {
        foreach (var choice in NumberFormatCatalog)
        {
            if (choice.NumberFormat == numberFormat)
                return true;
        }

        return false;
    }

    private static ChartAxisTickStyle? NormalizeTickStyle(ChartAxisTickStyle? tickStyle, ChartAxisTickStyle fallback) =>
        tickStyle is null ? null : IsKnownTickStyle(tickStyle.Value) ? tickStyle.Value : fallback;

    private static bool IsKnownTickStyle(ChartAxisTickStyle tickStyle) =>
        tickStyle is ChartAxisTickStyle.None
            or ChartAxisTickStyle.Inside
            or ChartAxisTickStyle.Outside
            or ChartAxisTickStyle.Cross;
}
