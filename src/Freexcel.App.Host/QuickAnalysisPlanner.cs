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
            new("Formatting", "Data Bars", QuickAnalysisCommand.DataBar),
            new("Formatting", "Color Scale", QuickAnalysisCommand.ColorScale),
            new("Formatting", "Icon Set", QuickAnalysisCommand.IconSet),
            new("Formatting", "Greater Than...", QuickAnalysisCommand.GreaterThan),
            new("Formatting", "Top 10...", QuickAnalysisCommand.Top10),
            new("Formatting", "Clear Conditional Formatting", QuickAnalysisCommand.ClearConditionalFormatting),
            new("Charts", "Column", QuickAnalysisCommand.ColumnChart),
            new("Charts", "Line", QuickAnalysisCommand.LineChart),
            new("Charts", "Pie", QuickAnalysisCommand.PieChart),
            new("Totals", "Sum", QuickAnalysisCommand.Sum),
            new("Totals", "Average", QuickAnalysisCommand.Average),
            new("Totals", "Count", QuickAnalysisCommand.Count),
            new("Tables", "Format as Table", QuickAnalysisCommand.FormatAsTable),
            new("Tables", "PivotTable", QuickAnalysisCommand.PivotTable),
            new("Sparklines", "Line", QuickAnalysisCommand.LineSparkline),
            new("Sparklines", "Column", QuickAnalysisCommand.ColumnSparkline),
            new("Sparklines", "Win/Loss", QuickAnalysisCommand.WinLossSparkline)
        ];
    }
}

public sealed record QuickAnalysisOption(string Group, string Label, QuickAnalysisCommand Command);

public enum QuickAnalysisCommand
{
    DataBar,
    ColorScale,
    IconSet,
    GreaterThan,
    Top10,
    ClearConditionalFormatting,
    ColumnChart,
    LineChart,
    PieChart,
    Sum,
    Average,
    Count,
    FormatAsTable,
    PivotTable,
    LineSparkline,
    ColumnSparkline,
    WinLossSparkline
}
