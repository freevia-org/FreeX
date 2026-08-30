using FreeX.Core.Commands;
using FreeX.Core.Model;
using System.Globalization;

namespace FreeX.App.Presentation.Charts.Editing;

/// <summary>
/// The stock-chart up/down-bar and high-low-line state read from a chart and edited back through the dialog.
/// <c>null</c> colors mean "use the default"; the high-low line thickness is always carried so a chosen width
/// round-trips.
/// </summary>
public readonly record struct ChartStockFormatInput(
    int UpDownBarGapWidth,
    CellColor? UpBarFillColor,
    CellColor? UpBarBorderColor,
    CellColor? DownBarFillColor,
    CellColor? DownBarBorderColor,
    CellColor? HighLowLineColor,
    double HighLowLineThickness);

public enum ChartStockFormatParseIssue
{
    None,
    UpDownBarGapWidth,
    HighLowLineThickness
}

public enum ChartStockFormatDialogControlKind
{
    Number,
    Color,
}

public enum ChartStockFormatDialogFieldId
{
    GapWidth,
    UpBarFill,
    UpBarBorder,
    DownBarFill,
    DownBarBorder,
    HighLowLineColor,
    HighLowLineThickness,
}

public sealed record ChartStockFormatDialogFieldDescriptor(
    ChartStockFormatDialogFieldId Id,
    ChartStockFormatDialogControlKind ControlKind,
    string LabelResourceKey,
    string AutomationId,
    string? HelpResourceKey = null);

public sealed record ChartStockFormatDialogSectionDescriptor(
    string HeaderResourceKey,
    IReadOnlyList<ChartStockFormatDialogFieldDescriptor> Fields);

/// <summary>
/// Portable (no UI) planner for the "Format Stock Chart" editing dialog: the up/down-bar gap width, the
/// up-bar and down-bar fill/border colors, and the high-low connector line color/thickness. Single-sources
/// the read/validate/project rules and maps an edited <see cref="ChartStockFormatInput"/> onto the
/// <see cref="ChartLayoutOptions"/> the shell hands to the Core <see cref="SetChartLayoutCommand"/>. Every
/// field already exists on <see cref="ChartModel"/> and is clamped by Core's <c>ApplyOptions</c>, so no Core
/// change is needed. Reused across every shell. (The WPF host's <c>ChartStockFormatDialog</c> is the behavior
/// reference.)
/// </summary>
public static class ChartStockFormatPlanner
{
    public const string TitleResourceKey = "ChartStockFormat_Title";
    public const string DialogAutomationId = "ChartStockFormatDialog";
    public const string BarsGroupResourceKey = "ChartFmt_StockBarsLabel";
    public const string HighLowGroupResourceKey = "ChartFmt_StockHighLowLabel";

    public const int MinGapWidth = 0;
    public const int MaxGapWidth = 500;

    public const double MinLineThickness = 0.5;
    public const double MaxLineThickness = 10.0;

    private static readonly ChartStockFormatDialogFieldDescriptor[] OptionFields =
    [
        new(ChartStockFormatDialogFieldId.GapWidth, ChartStockFormatDialogControlKind.Number, "ChartStockFormat_GapWidthLabel", "ChartStockFormatGapWidthBox", "ChartStockFormat_GapWidthHelpText"),
        new(ChartStockFormatDialogFieldId.UpBarFill, ChartStockFormatDialogControlKind.Color, "ChartStockFormat_UpBarFillLabel", "ChartStockFormatUpFillButton"),
        new(ChartStockFormatDialogFieldId.UpBarBorder, ChartStockFormatDialogControlKind.Color, "ChartStockFormat_UpBarBorderLabel", "ChartStockFormatUpBorderButton"),
        new(ChartStockFormatDialogFieldId.DownBarFill, ChartStockFormatDialogControlKind.Color, "ChartStockFormat_DownBarFillLabel", "ChartStockFormatDownFillButton"),
        new(ChartStockFormatDialogFieldId.DownBarBorder, ChartStockFormatDialogControlKind.Color, "ChartStockFormat_DownBarBorderLabel", "ChartStockFormatDownBorderButton"),
        new(ChartStockFormatDialogFieldId.HighLowLineColor, ChartStockFormatDialogControlKind.Color, "ChartStockFormat_HighLowLineColorLabel", "ChartStockFormatHighLowButton"),
        new(ChartStockFormatDialogFieldId.HighLowLineThickness, ChartStockFormatDialogControlKind.Number, "ChartStockFormat_LineThicknessLabel", "ChartStockFormatThicknessBox", "ChartStockFormat_LineThicknessHelpText"),
    ];

    private static readonly ChartStockFormatDialogSectionDescriptor[] DialogSections =
    [
        new("ChartStockFormat_OptionsGroup", OptionFields),
    ];

    public static IReadOnlyList<ChartStockFormatDialogSectionDescriptor> GetDialogSections() => DialogSections;

    public static ChartStockFormatDialogSectionDescriptor GetOptionsSection() => DialogSections[0];

    public static ChartStockFormatDialogFieldDescriptor GetDialogField(ChartStockFormatDialogFieldId id)
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

    public static string InvalidInputMessageResourceKey(ChartStockFormatParseIssue issue) =>
        issue == ChartStockFormatParseIssue.HighLowLineThickness
            ? "ChartStockFormat_InvalidLineThicknessMessage"
            : "ChartStockFormat_InvalidGapWidthMessage";

    /// <summary>True when the chart is a stock chart that has up/down bars and high-low lines.</summary>
    public static bool Supports(ChartModel chart)
    {
        ArgumentNullException.ThrowIfNull(chart);
        return chart.Type == ChartType.Stock;
    }

    /// <summary>Reads the chart's current stock formatting into the dialog input shape.</summary>
    public static ChartStockFormatInput Read(ChartModel chart)
    {
        ArgumentNullException.ThrowIfNull(chart);
        return Normalize(new ChartStockFormatInput(
            chart.UpDownBarGapWidth ?? 150,
            chart.UpBarFillColor,
            chart.UpBarBorderColor,
            chart.DownBarFillColor,
            chart.DownBarBorderColor,
            chart.HighLowLineColor,
            chart.HighLowLineThickness));
    }

    /// <summary>Validates the edited input. Returns null when valid, else an English reason.</summary>
    public static string? Validate(ChartStockFormatInput input)
    {
        if (input.UpDownBarGapWidth < MinGapWidth || input.UpDownBarGapWidth > MaxGapWidth)
            return $"Enter an up/down-bar gap width between {MinGapWidth} and {MaxGapWidth}.";

        if (!double.IsFinite(input.HighLowLineThickness)
            || input.HighLowLineThickness < MinLineThickness
            || input.HighLowLineThickness > MaxLineThickness)
        {
            return $"Enter a high-low line thickness between {MinLineThickness} and {MaxLineThickness}.";
        }

        return null;
    }

    public static bool TryParseDialogInput(
        string upDownBarGapWidthText,
        CellColor? upBarFillColor,
        CellColor? upBarBorderColor,
        CellColor? downBarFillColor,
        CellColor? downBarBorderColor,
        CellColor? highLowLineColor,
        string highLowLineThicknessText,
        out ChartStockFormatInput input,
        out ChartStockFormatParseIssue issue)
    {
        if (!NumericInputParser.TryParseInt32InRange(
                upDownBarGapWidthText,
                MinGapWidth,
                MaxGapWidth,
                out var gapWidth))
        {
            input = default;
            issue = ChartStockFormatParseIssue.UpDownBarGapWidth;
            return false;
        }

        if (!TryParseClampedDouble(highLowLineThicknessText, MinLineThickness, MaxLineThickness, out var thickness))
        {
            input = default;
            issue = ChartStockFormatParseIssue.HighLowLineThickness;
            return false;
        }

        input = new ChartStockFormatInput(
            gapWidth,
            upBarFillColor,
            upBarBorderColor,
            downBarFillColor,
            downBarBorderColor,
            highLowLineColor,
            thickness);
        issue = ChartStockFormatParseIssue.None;
        return true;
    }

    public static ChartStockFormatInput Normalize(ChartStockFormatInput input) =>
        new(
            Math.Clamp(input.UpDownBarGapWidth, MinGapWidth, MaxGapWidth),
            input.UpBarFillColor,
            input.UpBarBorderColor,
            input.DownBarFillColor,
            input.DownBarBorderColor,
            input.HighLowLineColor,
            double.IsFinite(input.HighLowLineThickness)
                ? Math.Clamp(input.HighLowLineThickness, MinLineThickness, MaxLineThickness)
                : MinLineThickness);

    /// <summary>Builds the <see cref="ChartLayoutOptions"/> delta for the edited stock formatting.</summary>
    public static ChartLayoutOptions Plan(ChartStockFormatInput input)
    {
        var normalized = Normalize(input);
        return new(
            UpDownBarGapWidth: normalized.UpDownBarGapWidth,
            UpBarFillColor: normalized.UpBarFillColor,
            UpBarBorderColor: normalized.UpBarBorderColor,
            DownBarFillColor: normalized.DownBarFillColor,
            DownBarBorderColor: normalized.DownBarBorderColor,
            HighLowLineColor: normalized.HighLowLineColor,
            HighLowLineThickness: normalized.HighLowLineThickness);
    }

    private static bool TryParseClampedDouble(string text, double min, double max, out double value) =>
        NumericInputParser.TryParseFiniteDouble(
            text.Trim(),
            CultureInfo.CurrentCulture,
            CultureInfo.InvariantCulture,
            out value)
        && value >= min
        && value <= max;
}
