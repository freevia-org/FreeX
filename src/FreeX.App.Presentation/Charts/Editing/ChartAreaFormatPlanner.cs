using FreeX.App.Presentation;
using FreeX.App.Presentation.Charts;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Charts.Editing;

/// <summary>
/// The chart-area / plot-area fill-and-border state plus the legend state read from a chart and edited
/// back through the Format Chart Area dialog. <c>null</c> fill colors mean "no explicit fill" (use the
/// theme/default); the plot-area border thickness is always carried so a chosen width round-trips. The
/// legend fields mirror the WPF <c>ChartAreaLegendDialog</c> "Legend" group so the dialog reaches full
/// parity with Windows (show/position/overlay + text/fill/border colors, border width, font size).
/// </summary>
public readonly record struct ChartAreaFormatInput(
    CellColor? ChartAreaFillColor,
    CellColor? PlotAreaFillColor,
    CellColor? PlotAreaBorderColor,
    double PlotAreaBorderThickness,
    bool ShowLegend = true,
    ChartLegendPosition LegendPosition = ChartLegendPosition.Right,
    bool LegendOverlay = false,
    CellColor? LegendTextColor = null,
    CellColor? LegendFillColor = null,
    CellColor? LegendBorderColor = null,
    double LegendBorderThickness = 0,
    double LegendFontSize = 12);

public enum ChartAreaFormatParseIssue
{
    None,
    ChartAreaFillColor,
    PlotAreaFillColor,
    PlotAreaBorderColor,
    PlotAreaBorderThickness,
    LegendTextColor,
    LegendFillColor,
    LegendBorderColor,
    LegendBorderThickness,
    LegendFontSize,
}

public enum ChartAreaFormatValidationIssue
{
    PlotAreaBorderThicknessOutOfRange,
    LegendBorderThicknessOutOfRange,
    LegendFontSizeOutOfRange,
}

/// <summary>
/// Portable (no UI) planner for the "Format Chart Area" dialog: chart-area fill plus plot-area fill and
/// border (color + width). Single-sources the read/validate/project rules and maps an edited
/// <see cref="ChartAreaFormatInput"/> onto the <see cref="ChartLayoutOptions"/> the shell hands to the Core
/// <see cref="SetChartLayoutCommand"/>. Every field here is already represented on
/// <see cref="ChartModel"/> and applied by <c>ApplyOptions</c>, so no Core change is needed. Reused across
/// every shell. (The WPF host's <c>ChartAreaLegendDialog</c> is the behavior reference for the chart/plot
/// area fields.)
/// </summary>
public static class ChartAreaFormatPlanner
{
    /// <summary>The plot-area / legend border width bounds Core clamps to (see <c>ApplyOptions</c>).</summary>
    public const double MinBorderThickness = 0;
    public const double MaxBorderThickness = 10;

    /// <summary>The legend font-size bounds Core clamps to.</summary>
    public const double MinLegendFontSize = 6;
    public const double MaxLegendFontSize = 72;

    private static readonly ChartLegendPosition[] LegendPositionCatalog = Enum.GetValues<ChartLegendPosition>();

    public static IReadOnlyList<ChartLegendPosition> GetLegendPositionChoices() => LegendPositionCatalog;

    /// <summary>
    /// Reads the chart's current chart-area / plot-area fill-and-border state plus the legend state into
    /// the dialog input shape.
    /// </summary>
    public static ChartAreaFormatInput Read(ChartModel chart)
    {
        ArgumentNullException.ThrowIfNull(chart);
        return Normalize(new ChartAreaFormatInput(
            chart.ChartAreaFillColor,
            chart.PlotAreaFillColor,
            chart.PlotAreaBorderColor,
            chart.PlotAreaBorderThickness,
            chart.ShowLegend,
            chart.LegendPosition,
            chart.LegendOverlay,
            chart.LegendTextColor,
            chart.LegendFillColor,
            chart.LegendBorderColor,
            chart.LegendBorderThickness,
            chart.LegendFontSize));
    }

    /// <summary>Normalizes dialog defaults and result values to the same ranges the WPF dialog used.</summary>
    public static ChartAreaFormatInput Normalize(ChartAreaFormatInput input) =>
        input with
        {
            PlotAreaBorderThickness = ClampFiniteOrDefault(
                input.PlotAreaBorderThickness,
                fallback: 1,
                MinBorderThickness,
                MaxBorderThickness),
            LegendPosition = Enum.IsDefined(input.LegendPosition)
                ? input.LegendPosition
                : ChartLegendPosition.Right,
            LegendBorderThickness = ClampFiniteOrDefault(
                input.LegendBorderThickness,
                fallback: 0,
                MinBorderThickness,
                MaxBorderThickness),
            LegendFontSize = ClampFiniteOrDefault(
                input.LegendFontSize,
                fallback: 12,
                MinLegendFontSize,
                MaxLegendFontSize),
        };

    /// <summary>
    /// Validates the edited input: the plot-area border width and legend border width must be finite and
    /// within the Core-clamped range, and the legend font size within its range. Returns null when valid,
    /// else an English message.
    /// </summary>
    public static string? Validate(ChartAreaFormatInput input)
    {
        return ValidateIssue(input) switch
        {
            ChartAreaFormatValidationIssue.PlotAreaBorderThicknessOutOfRange => $"Enter a plot-area border width between {MinBorderThickness} and {MaxBorderThickness}.",
            ChartAreaFormatValidationIssue.LegendBorderThicknessOutOfRange => $"Enter a legend border width between {MinBorderThickness} and {MaxBorderThickness}.",
            ChartAreaFormatValidationIssue.LegendFontSizeOutOfRange => $"Enter a legend font size between {MinLegendFontSize} and {MaxLegendFontSize}.",
            _ => null,
        };
    }

    public static ChartAreaFormatValidationIssue? ValidateIssue(ChartAreaFormatInput input)
    {
        if (!IsInBorderRange(input.PlotAreaBorderThickness))
            return ChartAreaFormatValidationIssue.PlotAreaBorderThicknessOutOfRange;

        if (!IsInBorderRange(input.LegendBorderThickness))
            return ChartAreaFormatValidationIssue.LegendBorderThicknessOutOfRange;

        if (!double.IsFinite(input.LegendFontSize)
            || input.LegendFontSize < MinLegendFontSize
            || input.LegendFontSize > MaxLegendFontSize)
        {
            return ChartAreaFormatValidationIssue.LegendFontSizeOutOfRange;
        }

        return null;
    }

    public static bool TryParseDialogInput(
        string? chartAreaFillColorText,
        string? plotAreaFillColorText,
        string? plotAreaBorderColorText,
        string? plotAreaBorderThicknessText,
        bool showLegend,
        ChartLegendPosition? selectedLegendPosition,
        bool legendOverlay,
        string? legendTextColorText,
        string? legendFillColorText,
        string? legendBorderColorText,
        string? legendBorderThicknessText,
        string? legendFontSizeText,
        out ChartAreaFormatInput input,
        out ChartAreaFormatParseIssue issue)
    {
        input = default;

        if (!ColorInputParser.TryParseOptionalHexColor(chartAreaFillColorText ?? string.Empty, out var chartAreaFillColor))
        {
            issue = ChartAreaFormatParseIssue.ChartAreaFillColor;
            return false;
        }

        if (!ColorInputParser.TryParseOptionalHexColor(plotAreaFillColorText ?? string.Empty, out var plotAreaFillColor))
        {
            issue = ChartAreaFormatParseIssue.PlotAreaFillColor;
            return false;
        }

        if (!ColorInputParser.TryParseOptionalHexColor(plotAreaBorderColorText ?? string.Empty, out var plotAreaBorderColor))
        {
            issue = ChartAreaFormatParseIssue.PlotAreaBorderColor;
            return false;
        }

        if (!ChartDialogValueParser.TryParseClampedDouble(
                plotAreaBorderThicknessText ?? string.Empty,
                MinBorderThickness,
                MaxBorderThickness,
                out var plotAreaBorderThickness))
        {
            issue = ChartAreaFormatParseIssue.PlotAreaBorderThickness;
            return false;
        }

        if (!ColorInputParser.TryParseOptionalHexColor(legendTextColorText ?? string.Empty, out var legendTextColor))
        {
            issue = ChartAreaFormatParseIssue.LegendTextColor;
            return false;
        }

        if (!ColorInputParser.TryParseOptionalHexColor(legendFillColorText ?? string.Empty, out var legendFillColor))
        {
            issue = ChartAreaFormatParseIssue.LegendFillColor;
            return false;
        }

        if (!ColorInputParser.TryParseOptionalHexColor(legendBorderColorText ?? string.Empty, out var legendBorderColor))
        {
            issue = ChartAreaFormatParseIssue.LegendBorderColor;
            return false;
        }

        if (!ChartDialogValueParser.TryParseClampedDouble(
                legendBorderThicknessText ?? string.Empty,
                MinBorderThickness,
                MaxBorderThickness,
                out var legendBorderThickness))
        {
            issue = ChartAreaFormatParseIssue.LegendBorderThickness;
            return false;
        }

        if (!ChartDialogValueParser.TryParseClampedDouble(
                legendFontSizeText ?? string.Empty,
                MinLegendFontSize,
                MaxLegendFontSize,
                out var legendFontSize))
        {
            issue = ChartAreaFormatParseIssue.LegendFontSize;
            return false;
        }

        input = Normalize(new ChartAreaFormatInput(
            chartAreaFillColor,
            plotAreaFillColor,
            plotAreaBorderColor,
            plotAreaBorderThickness,
            showLegend,
            selectedLegendPosition is { } position && Enum.IsDefined(position)
                ? position
                : ChartLegendPosition.Right,
            legendOverlay,
            legendTextColor,
            legendFillColor,
            legendBorderColor,
            legendBorderThickness,
            legendFontSize));
        issue = ChartAreaFormatParseIssue.None;
        return true;
    }

    private static bool IsInBorderRange(double value) =>
        double.IsFinite(value) && value >= MinBorderThickness && value <= MaxBorderThickness;

    private static double ClampFiniteOrDefault(double value, double fallback, double min, double max) =>
        Math.Clamp(double.IsFinite(value) ? value : fallback, min, max);

    /// <summary>
    /// Builds the <see cref="ChartLayoutOptions"/> delta for the edited chart-area / plot-area format and
    /// legend. Fill colors are passed through (null leaves the existing fill untouched in Core); the
    /// plot-area border color and width are always set so a cleared/changed border round-trips. The legend
    /// fields mirror the WPF <c>ChartAreaLegendDialogResult.ToOptions()</c>.
    /// </summary>
    public static ChartLayoutOptions Plan(ChartAreaFormatInput input)
    {
        var normalized = Normalize(input);
        return new(
            ChartAreaFillColor: normalized.ChartAreaFillColor,
            PlotAreaFillColor: normalized.PlotAreaFillColor,
            PlotAreaBorderColor: normalized.PlotAreaBorderColor,
            PlotAreaBorderThickness: normalized.PlotAreaBorderThickness,
            ShowLegend: normalized.ShowLegend,
            LegendPosition: normalized.LegendPosition,
            LegendOverlay: normalized.LegendOverlay,
            LegendTextColor: normalized.LegendTextColor,
            LegendFillColor: normalized.LegendFillColor,
            LegendBorderColor: normalized.LegendBorderColor,
            LegendBorderThickness: normalized.LegendBorderThickness,
            LegendFontSize: normalized.LegendFontSize);
    }
}
