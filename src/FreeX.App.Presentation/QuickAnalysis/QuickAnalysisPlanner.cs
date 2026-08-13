using FreeX.Core.Model;

namespace FreeX.App.Presentation.QuickAnalysis;

public static class QuickAnalysisPlanner
{
    public static IReadOnlyList<QuickAnalysisOption> BuildOptions(GridRange selection)
    {
        if (selection.RowCount == 1 && selection.ColCount == 1)
            return [];

        return QuickAnalysisCatalog.BuildOptions();
    }

    public static QuickAnalysisDisplayModel BuildDisplayModel(GridRange selection)
    {
        if (selection.RowCount == 1 && selection.ColCount == 1)
            return QuickAnalysisDisplayModel.Empty;

        return QuickAnalysisDisplayModel.FromItems(QuickAnalysisCatalog.BuildOptionDisplayItems());
    }

    public static QuickAnalysisHoverPreview BuildHoverPreview(GridRange selection, QuickAnalysisOption option)
    {
        var previewRange = BuildPreviewRange(selection, option.PreviewKind);

        return new QuickAnalysisHoverPreview(
            previewRange,
            option.PreviewKind,
            option.Label,
            option.PreviewText,
            option.Command,
            option.PreviewVisual);
    }

    public static QuickAnalysisDisplayHoverPreview BuildHoverPreview(GridRange selection, QuickAnalysisDisplayItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        var previewRange = BuildPreviewRange(selection, item.PreviewKind);

        return new QuickAnalysisDisplayHoverPreview(
            previewRange,
            item.PreviewKind,
            item.Label,
            item.PreviewText,
            item.Route,
            item.PreviewVisual);
    }

    private static GridRange BuildPreviewRange(GridRange selection, QuickAnalysisPreviewKind previewKind)
    {
        if (previewKind is not (QuickAnalysisPreviewKind.Total or QuickAnalysisPreviewKind.Sparkline) ||
            selection.End.Col >= CellAddress.MaxCol)
        {
            return selection;
        }

        return new GridRange(
            new CellAddress(selection.Start.Sheet, selection.Start.Row, selection.End.Col + 1),
            new CellAddress(selection.Start.Sheet, selection.End.Row, selection.End.Col + 1));
    }
}

public sealed record QuickAnalysisOption(
    QuickAnalysisGroup Group,
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
    ColumnSparkline,
    WinLossSparkline
}

public sealed record QuickAnalysisHoverPreview(
    GridRange Range,
    QuickAnalysisPreviewKind PreviewKind,
    string Label,
    string StatusText,
    QuickAnalysisCommand Command,
    QuickAnalysisPreviewVisual PreviewVisual);

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
    PercentTotal,
    RunningTotal,
    Max,
    Min,
    FormatAsTable,
    PivotTable,
    LineSparkline,
    ColumnSparkline,
    WinLossSparkline
}
