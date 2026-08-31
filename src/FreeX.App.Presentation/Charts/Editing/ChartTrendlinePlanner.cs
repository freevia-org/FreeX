using FreeX.Core.Commands;
using FreeX.Core.Model;
using FreeX.App.Presentation;
using FreeX.App.Presentation.Charts;

namespace FreeX.App.Presentation.Charts.Editing;

/// <summary>A trendline-type choice for the "Trendline" dialog: the value plus its English label.</summary>
public sealed record ChartTrendlineTypeChoice(ChartTrendlineType Type, string DisplayName);

/// <summary>
/// The trendline show/type/parameter state read from a chart and edited back through the dialog. The period
/// applies to a moving-average trendline; the order applies to a polynomial trendline; both are ignored for
/// the other types. Carries only the fields the cross-platform "Trendline" dialog exposes.
/// </summary>
public readonly record struct ChartTrendlineInput(
    bool ShowTrendline,
    ChartTrendlineType Type,
    int Period,
    int Order,
    bool ShowEquation,
    bool ShowRSquared,
    CellColor? Color = null,
    double? Thickness = null,
    ChartLineDashStyle? DashStyle = null);

public enum ChartTrendlineDialogControlKind
{
    CheckBox,
    ComboBox,
    Color,
    Number,
}

public enum ChartTrendlineDialogFieldId
{
    ShowTrendline,
    Type,
    Period,
    Order,
    ShowEquation,
    ShowRSquared,
    LineColor,
    LineThickness,
    DashStyle,
}

public sealed record ChartTrendlineDialogFieldDescriptor(
    ChartTrendlineDialogFieldId Id,
    ChartTrendlineDialogControlKind ControlKind,
    string LabelResourceKey,
    string AutomationId,
    string? HelpResourceKey = null);

public sealed record ChartTrendlineDialogSectionDescriptor(
    string HeaderResourceKey,
    IReadOnlyList<ChartTrendlineDialogFieldDescriptor> Fields);

public enum ChartTrendlineDialogParseIssue
{
    None,
    Period,
    Order,
    Color,
    Thickness,
}

/// <summary>
/// Portable (no UI) planner for the "Trendline" editing dialog (linear / exponential / logarithmic / power /
/// moving-average / polynomial, plus the equation and R-squared readouts and optional line style). Single-
/// sources the offered trendline types, clamps the moving-average period and polynomial order into Excel's
/// ranges, and projects an edited <see cref="ChartTrendlineInput"/> into the <see cref="ChartLayoutOptions"/>
/// the shell hands to the Core <see cref="SetChartLayoutCommand"/>. Whether a chart can carry a trendline at
/// all is gated by <see cref="SupportsTrendlines"/> (column/line/bar/scatter/bubble/area). Reused across
/// every shell.
/// </summary>
public static class ChartTrendlinePlanner
{
    /// <summary>Excel's smallest/largest moving-average period.</summary>
    public const int MinPeriod = 2;
    public const int MaxPeriod = 255;

    /// <summary>Excel's smallest/largest polynomial order.</summary>
    public const int MinOrder = 2;
    public const int MaxOrder = 6;

    public const double MinLineThickness = 0.5;
    public const double MaxLineThickness = 10;

    private static readonly ChartTrendlineTypeChoice[] TypeCatalog =
    [
        new(ChartTrendlineType.Linear, "Linear"),
        new(ChartTrendlineType.Exponential, "Exponential"),
        new(ChartTrendlineType.Logarithmic, "Logarithmic"),
        new(ChartTrendlineType.Power, "Power"),
        new(ChartTrendlineType.MovingAverage, "Moving Average"),
        new(ChartTrendlineType.Polynomial, "Polynomial"),
    ];

    private static readonly ChartLineDashStyle[] DashStyleCatalog = Enum.GetValues<ChartLineDashStyle>();

    private static readonly ChartTrendlineDialogFieldDescriptor[] TrendlineOptionFields =
    [
        new(ChartTrendlineDialogFieldId.ShowTrendline, ChartTrendlineDialogControlKind.CheckBox, "ChartTrendline_ShowTrendline", "ChartTrendlineShowCheck"),
        new(ChartTrendlineDialogFieldId.Type, ChartTrendlineDialogControlKind.ComboBox, "ChartTrendline_TypeLabel", "ChartTrendlineTypeCombo"),
        new(ChartTrendlineDialogFieldId.Period, ChartTrendlineDialogControlKind.Number, "ChartTrendline_PeriodLabel", "ChartTrendlinePeriodBox", "ChartTrendline_PeriodHelpText"),
        new(ChartTrendlineDialogFieldId.Order, ChartTrendlineDialogControlKind.Number, "ChartTrendline_OrderLabel", "ChartTrendlineOrderBox", "ChartTrendline_OrderHelpText"),
        new(ChartTrendlineDialogFieldId.ShowEquation, ChartTrendlineDialogControlKind.CheckBox, "ChartTrendline_DisplayEquation", "ChartTrendlineEquationCheck"),
        new(ChartTrendlineDialogFieldId.ShowRSquared, ChartTrendlineDialogControlKind.CheckBox, "ChartTrendline_DisplayRSquared", "ChartTrendlineRSquaredCheck"),
    ];

    private static readonly ChartTrendlineDialogFieldDescriptor[] LineFields =
    [
        new(ChartTrendlineDialogFieldId.LineColor, ChartTrendlineDialogControlKind.Color, "ChartTrendline_LineColorLabel", "ChartTrendlineLineColorButton"),
        new(ChartTrendlineDialogFieldId.LineThickness, ChartTrendlineDialogControlKind.Number, "ChartTrendline_LineWidthLabel", "ChartTrendlineLineWidthBox", "ChartTrendline_LineWidthHelpText"),
        new(ChartTrendlineDialogFieldId.DashStyle, ChartTrendlineDialogControlKind.ComboBox, "ChartTrendline_DashStyleLabel", "ChartTrendlineDashStyleCombo"),
    ];

    private static readonly ChartTrendlineDialogSectionDescriptor[] DialogSections =
    [
        new("ChartTrendline_OptionsGroup", TrendlineOptionFields),
        new("ChartDialog_FillLineGroup", LineFields),
    ];

    /// <summary>The selectable trendline types, in display order.</summary>
    public static IReadOnlyList<ChartTrendlineTypeChoice> GetTypeChoices() => TypeCatalog;

    public static IReadOnlyList<ChartLineDashStyle> GetDashStyleChoices() => DashStyleCatalog;

    public static IReadOnlyList<ChartTrendlineDialogSectionDescriptor> GetDialogSections() => DialogSections;

    public static ChartTrendlineDialogSectionDescriptor GetOptionsSection() => DialogSections[0];

    public static ChartTrendlineDialogSectionDescriptor GetLineSection() => DialogSections[1];

    public static ChartTrendlineDialogFieldDescriptor GetDialogField(ChartTrendlineDialogFieldId id)
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

    /// <summary>The English display label for <paramref name="type"/> (falls back to the enum name).</summary>
    public static string DisplayName(ChartTrendlineType type)
    {
        foreach (var choice in TypeCatalog)
        {
            if (choice.Type == type)
                return choice.DisplayName;
        }

        return type.ToString();
    }

    /// <summary>True when <paramref name="type"/> can carry a trendline (column/line/bar/scatter/bubble/area).</summary>
    public static bool SupportsTrendlines(ChartType type) => ChartTypeSupport.SupportsTrendlines(type);

    /// <summary>Reads the chart's current trendline state into the dialog input shape.</summary>
    public static ChartTrendlineInput Read(ChartModel chart) =>
        Normalize(new ChartTrendlineInput(
            chart.ShowLinearTrendline,
            chart.TrendlineType,
            chart.TrendlinePeriod,
            chart.TrendlineOrder,
            chart.ShowTrendlineEquation,
            chart.ShowTrendlineRSquared,
            chart.TrendlineColor,
            chart.TrendlineThickness,
            chart.TrendlineDashStyle));

    /// <summary>
    /// Normalizes the trendline dialog state that is safe to normalize before command execution: unknown
    /// trendline types fall back to Linear, and period/order are clamped into Excel's accepted ranges.
    /// Optional line styling is left unchanged so shells that do not surface it can omit those options.
    /// </summary>
    public static ChartTrendlineInput Normalize(ChartTrendlineInput input) =>
        input with
        {
            Type = IsKnownType(input.Type) ? input.Type : ChartTrendlineType.Linear,
            Period = Math.Clamp(input.Period, MinPeriod, MaxPeriod),
            Order = Math.Clamp(input.Order, MinOrder, MaxOrder),
        };

    public static bool TryParseDialogInput(
        bool showTrendline,
        ChartTrendlineType? selectedType,
        string? periodText,
        string? orderText,
        bool showEquation,
        bool showRSquared,
        string? colorText,
        string? thicknessText,
        ChartLineDashStyle? selectedDashStyle,
        out ChartTrendlineInput input,
        out ChartTrendlineDialogParseIssue issue)
    {
        input = default;

        if (!NumericInputParser.TryParseInt32InRange(
                periodText ?? string.Empty,
                MinPeriod,
                MaxPeriod,
                out var period))
        {
            issue = ChartTrendlineDialogParseIssue.Period;
            return false;
        }

        if (!NumericInputParser.TryParseInt32InRange(
                orderText ?? string.Empty,
                MinOrder,
                MaxOrder,
                out var order))
        {
            issue = ChartTrendlineDialogParseIssue.Order;
            return false;
        }

        if (!ColorInputParser.TryParseOptionalHexColor(colorText ?? string.Empty, out var color))
        {
            issue = ChartTrendlineDialogParseIssue.Color;
            return false;
        }

        if (!ChartDialogValueParser.TryParseClampedDouble(
                thicknessText ?? string.Empty,
                MinLineThickness,
                MaxLineThickness,
                out var thickness))
        {
            issue = ChartTrendlineDialogParseIssue.Thickness;
            return false;
        }

        input = Normalize(new ChartTrendlineInput(
            showTrendline,
            selectedType is { } type && IsKnownType(type) ? type : ChartTrendlineType.Linear,
            period,
            order,
            showEquation,
            showRSquared,
            color,
            thickness,
            selectedDashStyle is { } dashStyle && Enum.IsDefined(dashStyle)
                ? dashStyle
                : ChartLineDashStyle.Solid));
        issue = ChartTrendlineDialogParseIssue.None;
        return true;
    }

    /// <summary>
    /// Builds the <see cref="ChartLayoutOptions"/> delta for the edited trendline state. An invalid/unknown
    /// type falls back to Linear; the period and order are clamped into Excel's ranges. The type, period,
    /// order, readout toggles, and any supplied line styling are set (even when hiding) so re-showing keeps
    /// the chosen configuration.
    /// </summary>
    public static ChartLayoutOptions Plan(ChartTrendlineInput input)
    {
        var normalized = Normalize(input);
        return new ChartLayoutOptions(
            ShowLinearTrendline: normalized.ShowTrendline,
            TrendlineType: normalized.Type,
            TrendlinePeriod: normalized.Period,
            TrendlineOrder: normalized.Order,
            ShowTrendlineEquation: normalized.ShowEquation,
            ShowTrendlineRSquared: normalized.ShowRSquared,
            TrendlineColor: normalized.Color,
            TrendlineThickness: normalized.Thickness,
            TrendlineDashStyle: normalized.DashStyle);
    }

    private static bool IsKnownType(ChartTrendlineType type)
    {
        foreach (var choice in TypeCatalog)
        {
            if (choice.Type == type)
                return true;
        }

        return false;
    }

}
