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

public enum ChartAreaFormatDialogControlKind
{
    CheckBox,
    ComboBox,
    Color,
    Number,
}

public enum ChartAreaFormatDialogFieldId
{
    ChartAreaFillColor,
    PlotAreaFillColor,
    PlotAreaBorderColor,
    PlotAreaBorderThickness,
    ShowLegend,
    LegendPosition,
    LegendOverlay,
    LegendTextColor,
    LegendFillColor,
    LegendBorderColor,
    LegendBorderThickness,
    LegendFontSize,
}

public sealed record ChartAreaFormatDialogFieldDescriptor(
    ChartAreaFormatDialogFieldId Id,
    ChartAreaFormatDialogControlKind ControlKind,
    string LabelResourceKey,
    string AutomationId,
    string? HelpResourceKey = null);

public sealed record ChartAreaFormatDialogSectionDescriptor(
    string HeaderResourceKey,
    IReadOnlyList<ChartAreaFormatDialogFieldDescriptor> Fields,
    string? HelpResourceKey = null);

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
    /// <summary>Logical dialog width shared by cross-platform parity captures.</summary>
    public const double DialogWidth = 420;

    /// <summary>Logical dialog height shared by cross-platform parity captures.</summary>
    public const double DialogHeight = 590;

    /// <summary>The plot-area / legend border width bounds Core clamps to (see <c>ApplyOptions</c>).</summary>
    public const double MinBorderThickness = 0;
    public const double MaxBorderThickness = 10;

    /// <summary>The legend font-size bounds Core clamps to.</summary>
    public const double MinLegendFontSize = 6;
    public const double MaxLegendFontSize = 72;

    private static readonly ChartLegendPosition[] LegendPositionCatalog =
    [
        ChartLegendPosition.Right,
        ChartLegendPosition.Top,
        ChartLegendPosition.Left,
        ChartLegendPosition.Bottom,
    ];

    private static readonly ChartAreaFormatDialogFieldDescriptor[] FillLineFields =
    [
        new(ChartAreaFormatDialogFieldId.ChartAreaFillColor, ChartAreaFormatDialogControlKind.Color, "ChartAreaLegend_ChartAreaFillColorLabel", "ChartAreaFillButton"),
        new(ChartAreaFormatDialogFieldId.PlotAreaFillColor, ChartAreaFormatDialogControlKind.Color, "ChartAreaLegend_PlotAreaFillColorLabel", "ChartAreaPlotFillButton"),
        new(ChartAreaFormatDialogFieldId.PlotAreaBorderColor, ChartAreaFormatDialogControlKind.Color, "ChartAreaLegend_PlotAreaBorderColorLabel", "ChartAreaPlotBorderButton"),
        new(ChartAreaFormatDialogFieldId.PlotAreaBorderThickness, ChartAreaFormatDialogControlKind.Number, "ChartAreaLegend_PlotAreaBorderWidthLabel", "ChartAreaPlotBorderWidthBox", "ChartDialog_LineWidthHelpText"),
    ];

    private static readonly ChartAreaFormatDialogFieldDescriptor[] LegendFields =
    [
        new(ChartAreaFormatDialogFieldId.ShowLegend, ChartAreaFormatDialogControlKind.CheckBox, "ChartAreaLegend_ShowLegend", "ChartAreaShowLegendCheck"),
        new(ChartAreaFormatDialogFieldId.LegendPosition, ChartAreaFormatDialogControlKind.ComboBox, "ChartAreaLegend_LegendPositionLabel", "ChartAreaLegendPositionCombo"),
        new(ChartAreaFormatDialogFieldId.LegendOverlay, ChartAreaFormatDialogControlKind.CheckBox, "ChartAreaLegend_OverlayLegend", "ChartAreaLegendOverlayCheck"),
        new(ChartAreaFormatDialogFieldId.LegendTextColor, ChartAreaFormatDialogControlKind.Color, "ChartAreaLegend_LegendTextColorLabel", "ChartAreaLegendTextColorButton"),
        new(ChartAreaFormatDialogFieldId.LegendFillColor, ChartAreaFormatDialogControlKind.Color, "ChartAreaLegend_LegendFillColorLabel", "ChartAreaLegendFillColorButton"),
        new(ChartAreaFormatDialogFieldId.LegendBorderColor, ChartAreaFormatDialogControlKind.Color, "ChartAreaLegend_LegendBorderColorLabel", "ChartAreaLegendBorderColorButton"),
        new(ChartAreaFormatDialogFieldId.LegendBorderThickness, ChartAreaFormatDialogControlKind.Number, "ChartAreaLegend_LegendBorderWidthLabel", "ChartAreaLegendBorderWidthBox", "ChartDialog_LineWidthHelpText"),
        new(ChartAreaFormatDialogFieldId.LegendFontSize, ChartAreaFormatDialogControlKind.Number, "ChartAreaLegend_LegendFontSizeLabel", "ChartAreaLegendFontSizeBox", "ChartAreaLegend_LegendFontSizeHelpText"),
    ];

    private static readonly ChartAreaFormatDialogSectionDescriptor[] DialogSections =
    [
        new("ChartDialog_FillLineGroup", FillLineFields, "ChartAreaLegend_FillLineHelpText"),
        new("ChartAreaLegend_LegendGroup", LegendFields),
    ];

    public static IReadOnlyList<ChartLegendPosition> GetLegendPositionChoices() => LegendPositionCatalog;

    public static IReadOnlyList<ChartAreaFormatDialogSectionDescriptor> GetDialogSections() => DialogSections;

    public static ChartAreaFormatDialogSectionDescriptor GetFillLineSection() => DialogSections[0];

    public static ChartAreaFormatDialogSectionDescriptor GetLegendSection() => DialogSections[1];

    public static ChartAreaFormatDialogFieldDescriptor GetDialogField(ChartAreaFormatDialogFieldId id)
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
            LegendPosition = IsSelectableLegendPosition(input.LegendPosition)
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
            selectedLegendPosition is { } position && IsSelectableLegendPosition(position)
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

    private static bool IsSelectableLegendPosition(ChartLegendPosition position)
    {
        foreach (var candidate in LegendPositionCatalog)
        {
            if (candidate == position)
                return true;
        }

        return false;
    }

    private static bool IsInBorderRange(double value) =>
        double.IsFinite(value) && value >= MinBorderThickness && value <= MaxBorderThickness;

    private static double ClampFiniteOrDefault(double value, double fallback, double min, double max) =>
        Math.Clamp(double.IsFinite(value) ? value : fallback, min, max);

    /// <summary>
    /// Builds the <see cref="ChartLayoutOptions"/> delta for the edited chart-area / plot-area format and
    /// legend. Fill colors are passed through (null leaves the existing fill untouched in Core); the
    /// plot-area border color and width are always set so a cleared/changed border round-trips. The legend
    /// fields map directly from the shared <see cref="ChartAreaFormatInput"/> returned by each renderer.
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
