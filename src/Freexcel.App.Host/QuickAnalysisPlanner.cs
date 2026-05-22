using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public static class QuickAnalysisPlanner
{
    public static IReadOnlyList<QuickAnalysisOption> BuildOptions(GridRange selection)
    {
        if (selection.RowCount == 1 && selection.ColCount == 1)
            return [];

        return
        [
            Format("Data Bars", QuickAnalysisCommand.DataBar, "Preview data bars across the selected values.", QuickAnalysisPreviewVisualKind.DataBars),
            Format("Color Scale", QuickAnalysisCommand.ColorScale, "Preview a two-color scale across the selected values.", QuickAnalysisPreviewVisualKind.ColorScale),
            Format("Icon Set", QuickAnalysisCommand.IconSet, "Preview icon indicators for high, middle, and low values.", QuickAnalysisPreviewVisualKind.IconSet),
            Format("Greater Than...", QuickAnalysisCommand.GreaterThan, "Preview a greater-than conditional format.", QuickAnalysisPreviewVisualKind.Highlight),
            Format("Less Than...", QuickAnalysisCommand.LessThan, "Preview a less-than conditional format.", QuickAnalysisPreviewVisualKind.Highlight),
            Format("Between...", QuickAnalysisCommand.Between, "Preview a between conditional format.", QuickAnalysisPreviewVisualKind.Highlight),
            Format("Equal To...", QuickAnalysisCommand.EqualTo, "Preview an equal-to conditional format.", QuickAnalysisPreviewVisualKind.Highlight),
            Format("Text that Contains...", QuickAnalysisCommand.TextContains, "Preview a text-containing conditional format.", QuickAnalysisPreviewVisualKind.Highlight),
            Format("A Date Occurring...", QuickAnalysisCommand.DateOccurring, "Preview a date-occurring conditional format.", QuickAnalysisPreviewVisualKind.Highlight),
            Format("Duplicate Values...", QuickAnalysisCommand.DuplicateValues, "Preview duplicate-value conditional formatting.", QuickAnalysisPreviewVisualKind.Highlight),
            Format("Top 10...", QuickAnalysisCommand.Top10, "Preview highlighting for the top ten selected values.", QuickAnalysisPreviewVisualKind.Highlight),
            Format("Top 10%", QuickAnalysisCommand.Top10Percent, "Preview highlighting for the top ten percent of selected values.", QuickAnalysisPreviewVisualKind.Highlight),
            Format("Bottom 10...", QuickAnalysisCommand.Bottom10, "Preview highlighting for the bottom ten selected values.", QuickAnalysisPreviewVisualKind.Highlight),
            Format("Bottom 10%", QuickAnalysisCommand.Bottom10Percent, "Preview highlighting for the bottom ten percent of selected values.", QuickAnalysisPreviewVisualKind.Highlight),
            Format("Above Average", QuickAnalysisCommand.AboveAverage, "Preview highlighting for above-average selected values.", QuickAnalysisPreviewVisualKind.Highlight),
            Format("Below Average", QuickAnalysisCommand.BelowAverage, "Preview highlighting for below-average selected values.", QuickAnalysisPreviewVisualKind.Highlight),
            Format("Clear Conditional Formatting", QuickAnalysisCommand.ClearConditionalFormatting, "Preview removing conditional formats from the selection.", QuickAnalysisPreviewVisualKind.ClearFormat),
            Chart("Column", QuickAnalysisCommand.ColumnChart, "Preview a clustered column chart from the selected range.", QuickAnalysisPreviewVisualKind.ColumnChart),
            Chart("Stacked Column", QuickAnalysisCommand.StackedColumnChart, "Preview a stacked column chart from the selected range.", QuickAnalysisPreviewVisualKind.StackedColumnChart),
            Chart("100% Stacked Column", QuickAnalysisCommand.PercentStackedColumnChart, "Preview a 100% stacked column chart from the selected range.", QuickAnalysisPreviewVisualKind.StackedColumnChart),
            Chart("Line", QuickAnalysisCommand.LineChart, "Preview a line chart from the selected range.", QuickAnalysisPreviewVisualKind.LineChart),
            Chart("Pie", QuickAnalysisCommand.PieChart, "Preview a pie chart from the selected range.", QuickAnalysisPreviewVisualKind.PieChart),
            Chart("Doughnut", QuickAnalysisCommand.DoughnutChart, "Preview a doughnut chart from the selected range.", QuickAnalysisPreviewVisualKind.PieChart),
            Chart("Bar", QuickAnalysisCommand.BarChart, "Preview a clustered bar chart from the selected range.", QuickAnalysisPreviewVisualKind.BarChart),
            Chart("Stacked Bar", QuickAnalysisCommand.StackedBarChart, "Preview a stacked bar chart from the selected range.", QuickAnalysisPreviewVisualKind.BarChart),
            Chart("100% Stacked Bar", QuickAnalysisCommand.PercentStackedBarChart, "Preview a 100% stacked bar chart from the selected range.", QuickAnalysisPreviewVisualKind.BarChart),
            Chart("Area", QuickAnalysisCommand.AreaChart, "Preview an area chart from the selected range.", QuickAnalysisPreviewVisualKind.AreaChart),
            Chart("Scatter", QuickAnalysisCommand.ScatterChart, "Preview a scatter chart from the selected range.", QuickAnalysisPreviewVisualKind.ScatterChart),
            Chart("Bubble", QuickAnalysisCommand.BubbleChart, "Preview a bubble chart from the selected range.", QuickAnalysisPreviewVisualKind.ScatterChart),
            Chart("Radar", QuickAnalysisCommand.RadarChart, "Preview a radar chart from the selected range.", QuickAnalysisPreviewVisualKind.LineChart),
            Chart("Stock", QuickAnalysisCommand.StockChart, "Preview a stock chart from the selected range.", QuickAnalysisPreviewVisualKind.ColumnChart),
            Chart("More Charts...", QuickAnalysisCommand.MoreCharts, "Open the full Insert Chart dialog for every supported chart subtype.", QuickAnalysisPreviewVisualKind.ColumnChart),
            Total("Sum", QuickAnalysisCommand.Sum, "Preview sum totals next to the selected range."),
            Total("Average", QuickAnalysisCommand.Average, "Preview average totals next to the selected range."),
            Total("Count", QuickAnalysisCommand.Count, "Preview count totals next to the selected range."),
            Total("Max", QuickAnalysisCommand.Max, "Preview maximum totals next to the selected range."),
            Total("Min", QuickAnalysisCommand.Min, "Preview minimum totals next to the selected range."),
            Table("Format as Table", QuickAnalysisCommand.FormatAsTable, "Preview formatting the selection as a table."),
            Table("PivotTable", QuickAnalysisCommand.PivotTable, "Preview creating a PivotTable from the selected range."),
            Sparkline("Line", QuickAnalysisCommand.LineSparkline, "Preview line sparklines beside the selected range.", QuickAnalysisPreviewVisualKind.LineSparkline),
            Sparkline("Column", QuickAnalysisCommand.ColumnSparkline, "Preview column sparklines beside the selected range.", QuickAnalysisPreviewVisualKind.ColumnChart),
            Sparkline("Win/Loss", QuickAnalysisCommand.WinLossSparkline, "Preview win/loss sparklines beside the selected range.", QuickAnalysisPreviewVisualKind.WinLossSparkline)
        ];
    }

    private static QuickAnalysisOption Format(
        string label,
        QuickAnalysisCommand command,
        string previewText,
        QuickAnalysisPreviewVisualKind visualKind) =>
        new("Formatting", label, command, QuickAnalysisPreviewKind.ConditionalFormat, previewText, new QuickAnalysisPreviewVisual(visualKind));

    private static QuickAnalysisOption Chart(
        string label,
        QuickAnalysisCommand command,
        string previewText,
        QuickAnalysisPreviewVisualKind visualKind) =>
        new("Charts", label, command, QuickAnalysisPreviewKind.Chart, previewText, new QuickAnalysisPreviewVisual(visualKind));

    private static QuickAnalysisOption Total(string label, QuickAnalysisCommand command, string previewText) =>
        new("Totals", label, command, QuickAnalysisPreviewKind.Total, previewText, new QuickAnalysisPreviewVisual(QuickAnalysisPreviewVisualKind.TotalFormula));

    private static QuickAnalysisOption Table(string label, QuickAnalysisCommand command, string previewText) =>
        new("Tables", label, command, QuickAnalysisPreviewKind.Table, previewText, new QuickAnalysisPreviewVisual(QuickAnalysisPreviewVisualKind.Table));

    private static QuickAnalysisOption Sparkline(
        string label,
        QuickAnalysisCommand command,
        string previewText,
        QuickAnalysisPreviewVisualKind visualKind) =>
        new("Sparklines", label, command, QuickAnalysisPreviewKind.Sparkline, previewText, new QuickAnalysisPreviewVisual(visualKind));

    public static QuickAnalysisHoverPreview BuildHoverPreview(GridRange selection, QuickAnalysisOption option)
    {
        var previewRange = option.PreviewKind is QuickAnalysisPreviewKind.Total or QuickAnalysisPreviewKind.Sparkline
            ? new GridRange(
                new CellAddress(selection.Start.Sheet, selection.Start.Row, selection.End.Col + 1),
                new CellAddress(selection.Start.Sheet, selection.End.Row, selection.End.Col + 1))
            : selection;

        return new QuickAnalysisHoverPreview(
            previewRange,
            option.PreviewKind,
            option.Label,
            option.PreviewText);
    }
}

public sealed record QuickAnalysisOption(
    string Group,
    string Label,
    QuickAnalysisCommand Command,
    QuickAnalysisPreviewKind PreviewKind,
    string PreviewText,
    QuickAnalysisPreviewVisual PreviewVisual);

public sealed record QuickAnalysisPreviewVisual(QuickAnalysisPreviewVisualKind Kind);

public enum QuickAnalysisPreviewVisualKind
{
    None,
    DataBars,
    ColorScale,
    IconSet,
    Highlight,
    ClearFormat,
    ColumnChart,
    StackedColumnChart,
    LineChart,
    PieChart,
    BarChart,
    AreaChart,
    ScatterChart,
    TotalFormula,
    Table,
    LineSparkline,
    WinLossSparkline
}

public sealed record QuickAnalysisHoverPreview(
    GridRange Range,
    QuickAnalysisPreviewKind PreviewKind,
    string Label,
    string StatusText);

public enum QuickAnalysisPreviewKind
{
    ConditionalFormat,
    Chart,
    Total,
    Table,
    Sparkline
}

public enum QuickAnalysisCommand
{
    DataBar,
    ColorScale,
    IconSet,
    GreaterThan,
    LessThan,
    Between,
    EqualTo,
    TextContains,
    DateOccurring,
    DuplicateValues,
    Top10,
    Top10Percent,
    Bottom10,
    Bottom10Percent,
    AboveAverage,
    BelowAverage,
    ClearConditionalFormatting,
    ColumnChart,
    StackedColumnChart,
    PercentStackedColumnChart,
    LineChart,
    PieChart,
    DoughnutChart,
    BarChart,
    StackedBarChart,
    PercentStackedBarChart,
    AreaChart,
    ScatterChart,
    BubbleChart,
    RadarChart,
    StockChart,
    MoreCharts,
    Sum,
    Average,
    Count,
    Max,
    Min,
    FormatAsTable,
    PivotTable,
    LineSparkline,
    ColumnSparkline,
    WinLossSparkline
}
