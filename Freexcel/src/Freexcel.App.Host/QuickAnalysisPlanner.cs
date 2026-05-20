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
            Format("Data Bars", QuickAnalysisCommand.DataBar, "Preview data bars across the selected values."),
            Format("Color Scale", QuickAnalysisCommand.ColorScale, "Preview a two-color scale across the selected values."),
            Format("Icon Set", QuickAnalysisCommand.IconSet, "Preview icon indicators for high, middle, and low values."),
            Format("Greater Than...", QuickAnalysisCommand.GreaterThan, "Preview a greater-than conditional format."),
            Format("Top 10...", QuickAnalysisCommand.Top10, "Preview highlighting for the top ten selected values."),
            Format("Clear Conditional Formatting", QuickAnalysisCommand.ClearConditionalFormatting, "Preview removing conditional formats from the selection."),
            Chart("Column", QuickAnalysisCommand.ColumnChart, "Preview a clustered column chart from the selected range."),
            Chart("Stacked Column", QuickAnalysisCommand.StackedColumnChart, "Preview a stacked column chart from the selected range."),
            Chart("100% Stacked Column", QuickAnalysisCommand.PercentStackedColumnChart, "Preview a 100% stacked column chart from the selected range."),
            Chart("Line", QuickAnalysisCommand.LineChart, "Preview a line chart from the selected range."),
            Chart("Pie", QuickAnalysisCommand.PieChart, "Preview a pie chart from the selected range."),
            Chart("Doughnut", QuickAnalysisCommand.DoughnutChart, "Preview a doughnut chart from the selected range."),
            Chart("Bar", QuickAnalysisCommand.BarChart, "Preview a clustered bar chart from the selected range."),
            Chart("Stacked Bar", QuickAnalysisCommand.StackedBarChart, "Preview a stacked bar chart from the selected range."),
            Chart("100% Stacked Bar", QuickAnalysisCommand.PercentStackedBarChart, "Preview a 100% stacked bar chart from the selected range."),
            Chart("Area", QuickAnalysisCommand.AreaChart, "Preview an area chart from the selected range."),
            Chart("Scatter", QuickAnalysisCommand.ScatterChart, "Preview a scatter chart from the selected range."),
            Chart("Bubble", QuickAnalysisCommand.BubbleChart, "Preview a bubble chart from the selected range."),
            Chart("Radar", QuickAnalysisCommand.RadarChart, "Preview a radar chart from the selected range."),
            Chart("Stock", QuickAnalysisCommand.StockChart, "Preview a stock chart from the selected range."),
            Total("Sum", QuickAnalysisCommand.Sum, "Preview sum totals next to the selected range."),
            Total("Average", QuickAnalysisCommand.Average, "Preview average totals next to the selected range."),
            Total("Count", QuickAnalysisCommand.Count, "Preview count totals next to the selected range."),
            Total("Max", QuickAnalysisCommand.Max, "Preview maximum totals next to the selected range."),
            Total("Min", QuickAnalysisCommand.Min, "Preview minimum totals next to the selected range."),
            Table("Format as Table", QuickAnalysisCommand.FormatAsTable, "Preview formatting the selection as a table."),
            Table("PivotTable", QuickAnalysisCommand.PivotTable, "Preview creating a PivotTable from the selected range."),
            Sparkline("Line", QuickAnalysisCommand.LineSparkline, "Preview line sparklines beside the selected range."),
            Sparkline("Column", QuickAnalysisCommand.ColumnSparkline, "Preview column sparklines beside the selected range."),
            Sparkline("Win/Loss", QuickAnalysisCommand.WinLossSparkline, "Preview win/loss sparklines beside the selected range.")
        ];
    }

    private static QuickAnalysisOption Format(string label, QuickAnalysisCommand command, string previewText) =>
        new("Formatting", label, command, QuickAnalysisPreviewKind.ConditionalFormat, previewText);

    private static QuickAnalysisOption Chart(string label, QuickAnalysisCommand command, string previewText) =>
        new("Charts", label, command, QuickAnalysisPreviewKind.Chart, previewText);

    private static QuickAnalysisOption Total(string label, QuickAnalysisCommand command, string previewText) =>
        new("Totals", label, command, QuickAnalysisPreviewKind.Total, previewText);

    private static QuickAnalysisOption Table(string label, QuickAnalysisCommand command, string previewText) =>
        new("Tables", label, command, QuickAnalysisPreviewKind.Table, previewText);

    private static QuickAnalysisOption Sparkline(string label, QuickAnalysisCommand command, string previewText) =>
        new("Sparklines", label, command, QuickAnalysisPreviewKind.Sparkline, previewText);

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
    string PreviewText);

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
    Top10,
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
