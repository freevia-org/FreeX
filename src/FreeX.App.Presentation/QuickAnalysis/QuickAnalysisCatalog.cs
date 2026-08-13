using FreeX.Core.Model;

namespace FreeX.App.Presentation.QuickAnalysis;

/// <summary>
/// Shared Quick Analysis catalog metadata. Hosts decide which entries to surface and how to render them;
/// this keeps labels, action descriptors, command ids, and hover-preview metadata in one portable source.
/// </summary>
internal static class QuickAnalysisCatalog
{
    private static readonly QuickAnalysisCatalogEntry[] Entries =
    [
        Formatting(
            "format.databars",
            "Data Bars",
            "Data Bars",
            QuickAnalysisCommand.DataBar,
            QuickAnalysisFormatKind.DataBars,
            CfRuleType.DataBar,
            "Preview data bars across the selected values.",
            QuickAnalysisPreviewVisualKind.DataBars),
        Formatting(
            "format.colorscale",
            "Color Scale",
            "Color Scale",
            QuickAnalysisCommand.ColorScale,
            QuickAnalysisFormatKind.ColorScale,
            CfRuleType.ColorScale,
            "Preview a two-color scale across the selected values.",
            QuickAnalysisPreviewVisualKind.ColorScale),
        Formatting(
            "format.iconset",
            "Icon Set",
            "Icon Set",
            QuickAnalysisCommand.IconSet,
            QuickAnalysisFormatKind.IconSet,
            CfRuleType.IconSet,
            "Preview icon indicators for high, middle, and low values.",
            QuickAnalysisPreviewVisualKind.IconSet),
        Formatting(
            "format.greaterthan",
            "Greater Than...",
            "Greater Than",
            QuickAnalysisCommand.GreaterThan,
            QuickAnalysisFormatKind.GreaterThan,
            CfRuleType.CellValue,
            "Preview a greater-than conditional format.",
            QuickAnalysisPreviewVisualKind.Highlight),
        Formatting(
            "format.lessthan",
            "Less Than...",
            "Less Than",
            QuickAnalysisCommand.LessThan,
            QuickAnalysisFormatKind.LessThan,
            CfRuleType.CellValue,
            "Preview a less-than conditional format.",
            QuickAnalysisPreviewVisualKind.Highlight),
        Formatting(
            "format.between",
            "Between...",
            "Between",
            QuickAnalysisCommand.Between,
            QuickAnalysisFormatKind.Between,
            CfRuleType.CellValue,
            "Preview a between conditional format.",
            QuickAnalysisPreviewVisualKind.Highlight),
        Formatting(
            "format.equalto",
            "Equal To...",
            "Equal To",
            QuickAnalysisCommand.EqualTo,
            QuickAnalysisFormatKind.EqualTo,
            CfRuleType.CellValue,
            "Preview an equal-to conditional format.",
            QuickAnalysisPreviewVisualKind.Highlight),
        Formatting(
            "format.textcontains",
            "Text that Contains...",
            "Text that Contains",
            QuickAnalysisCommand.TextContains,
            QuickAnalysisFormatKind.TextContains,
            CfRuleType.ContainsText,
            "Preview a text-containing conditional format.",
            QuickAnalysisPreviewVisualKind.Highlight),
        Formatting(
            "format.dateoccurring",
            "A Date Occurring...",
            "A Date Occurring",
            QuickAnalysisCommand.DateOccurring,
            QuickAnalysisFormatKind.DateOccurring,
            CfRuleType.DateOccurring,
            "Preview a date-occurring conditional format.",
            QuickAnalysisPreviewVisualKind.Highlight),
        Formatting(
            "format.duplicatevalues",
            "Duplicate Values...",
            "Duplicate Values",
            QuickAnalysisCommand.DuplicateValues,
            QuickAnalysisFormatKind.DuplicateValues,
            CfRuleType.DuplicateValues,
            "Preview duplicate-value conditional formatting.",
            QuickAnalysisPreviewVisualKind.Highlight),
        Formatting(
            "format.top10",
            "Top 10...",
            "Top 10%",
            QuickAnalysisCommand.Top10,
            QuickAnalysisFormatKind.Top10,
            CfRuleType.Top10,
            "Preview highlighting for the top ten selected values.",
            QuickAnalysisPreviewVisualKind.Highlight),
        Formatting(
            "format.top10percent",
            "Top 10%",
            "Top 10%",
            QuickAnalysisCommand.Top10Percent,
            QuickAnalysisFormatKind.Top10Percent,
            CfRuleType.Top10,
            "Preview highlighting for the top ten percent of selected values.",
            QuickAnalysisPreviewVisualKind.Highlight),
        Formatting(
            "format.bottom10",
            "Bottom 10...",
            "Bottom 10",
            QuickAnalysisCommand.Bottom10,
            QuickAnalysisFormatKind.Bottom10,
            CfRuleType.Top10,
            "Preview highlighting for the bottom ten selected values.",
            QuickAnalysisPreviewVisualKind.Highlight),
        Formatting(
            "format.bottom10percent",
            "Bottom 10%",
            "Bottom 10%",
            QuickAnalysisCommand.Bottom10Percent,
            QuickAnalysisFormatKind.Bottom10Percent,
            CfRuleType.Top10,
            "Preview highlighting for the bottom ten percent of selected values.",
            QuickAnalysisPreviewVisualKind.Highlight),
        Formatting(
            "format.aboveaverage",
            "Above Average",
            "Above Average",
            QuickAnalysisCommand.AboveAverage,
            QuickAnalysisFormatKind.AboveAverage,
            CfRuleType.AboveAverage,
            "Preview highlighting for above-average selected values.",
            QuickAnalysisPreviewVisualKind.Highlight),
        Formatting(
            "format.belowaverage",
            "Below Average",
            "Below Average",
            QuickAnalysisCommand.BelowAverage,
            QuickAnalysisFormatKind.BelowAverage,
            CfRuleType.AboveAverage,
            "Preview highlighting for below-average selected values.",
            QuickAnalysisPreviewVisualKind.Highlight),
        CommandOnly(
            QuickAnalysisGroup.Formatting,
            "format.clear",
            "Clear Conditional Formatting",
            "Clear Conditional Formatting",
            QuickAnalysisCommand.ClearConditionalFormatting,
            new QuickAnalysisCommandRoute(QuickAnalysisCommandKind.ClearConditionalFormatting),
            QuickAnalysisPreviewKind.ConditionalFormat,
            "Preview removing conditional formats from the selection.",
            QuickAnalysisPreviewVisualKind.ClearFormat),

        Chart(
            "chart.clusteredcolumn",
            "Column",
            "Clustered Column",
            QuickAnalysisCommand.ColumnChart,
            ChartType.Column,
            "Preview a clustered column chart from the selected range.",
            QuickAnalysisPreviewVisualKind.ColumnChart),
        Chart(
            "chart.stackedcolumn",
            "Stacked Column",
            "Stacked Column",
            QuickAnalysisCommand.StackedColumnChart,
            ChartType.StackedColumn,
            "Preview a stacked column chart from the selected range.",
            QuickAnalysisPreviewVisualKind.StackedColumnChart),
        Chart(
            "chart.percentstackedcolumn",
            "100% Stacked Column",
            "100% Stacked Column",
            QuickAnalysisCommand.PercentStackedColumnChart,
            ChartType.PercentStackedColumn,
            "Preview a 100% stacked column chart from the selected range.",
            QuickAnalysisPreviewVisualKind.StackedColumnChart),
        Chart(
            "chart.line",
            "Line",
            "Line",
            QuickAnalysisCommand.LineChart,
            ChartType.Line,
            "Preview a line chart from the selected range.",
            QuickAnalysisPreviewVisualKind.LineChart),
        Chart(
            "chart.pie",
            "Pie",
            "Pie",
            QuickAnalysisCommand.PieChart,
            ChartType.Pie,
            "Preview a pie chart from the selected range.",
            QuickAnalysisPreviewVisualKind.PieChart),
        Chart(
            "chart.doughnut",
            "Doughnut",
            "Doughnut",
            QuickAnalysisCommand.DoughnutChart,
            ChartType.Doughnut,
            "Preview a doughnut chart from the selected range.",
            QuickAnalysisPreviewVisualKind.PieChart),
        Chart(
            "chart.bar",
            "Bar",
            "Clustered Bar",
            QuickAnalysisCommand.BarChart,
            ChartType.Bar,
            "Preview a clustered bar chart from the selected range.",
            QuickAnalysisPreviewVisualKind.BarChart),
        Chart(
            "chart.stackedbar",
            "Stacked Bar",
            "Stacked Bar",
            QuickAnalysisCommand.StackedBarChart,
            ChartType.StackedBar,
            "Preview a stacked bar chart from the selected range.",
            QuickAnalysisPreviewVisualKind.BarChart),
        Chart(
            "chart.percentstackedbar",
            "100% Stacked Bar",
            "100% Stacked Bar",
            QuickAnalysisCommand.PercentStackedBarChart,
            ChartType.PercentStackedBar,
            "Preview a 100% stacked bar chart from the selected range.",
            QuickAnalysisPreviewVisualKind.BarChart),
        Chart(
            "chart.area",
            "Area",
            "Area",
            QuickAnalysisCommand.AreaChart,
            ChartType.Area,
            "Preview an area chart from the selected range.",
            QuickAnalysisPreviewVisualKind.AreaChart),
        Chart(
            "chart.scatter",
            "Scatter",
            "Scatter",
            QuickAnalysisCommand.ScatterChart,
            ChartType.Scatter,
            "Preview a scatter chart from the selected range.",
            QuickAnalysisPreviewVisualKind.ScatterChart),
        Chart(
            "chart.bubble",
            "Bubble",
            "Bubble",
            QuickAnalysisCommand.BubbleChart,
            ChartType.Bubble,
            "Preview a bubble chart from the selected range.",
            QuickAnalysisPreviewVisualKind.ScatterChart),
        Chart(
            "chart.radar",
            "Radar",
            "Radar",
            QuickAnalysisCommand.RadarChart,
            ChartType.Radar,
            "Preview a radar chart from the selected range.",
            QuickAnalysisPreviewVisualKind.LineChart),
        Chart(
            "chart.stock",
            "Stock",
            "Stock",
            QuickAnalysisCommand.StockChart,
            ChartType.Stock,
            "Preview a stock chart from the selected range.",
            QuickAnalysisPreviewVisualKind.ColumnChart),
        CommandOnly(
            QuickAnalysisGroup.Charts,
            "chart.more",
            "More Charts...",
            "More Charts...",
            QuickAnalysisCommand.MoreCharts,
            new QuickAnalysisCommandRoute(QuickAnalysisCommandKind.MoreCharts),
            QuickAnalysisPreviewKind.Chart,
            "Open the full Insert Chart dialog for every supported chart subtype.",
            QuickAnalysisPreviewVisualKind.ColumnChart),

        Total("total.sum", "Sum", "Sum", QuickAnalysisCommand.Sum, QuickAnalysisTotalFunction.Sum, QuickAnalysisTotalOrientation.Column),
        Total("total.average", "Average", "Average", QuickAnalysisCommand.Average, QuickAnalysisTotalFunction.Average, QuickAnalysisTotalOrientation.Column),
        Total("total.count", "Count", "Count", QuickAnalysisCommand.Count, QuickAnalysisTotalFunction.Count, QuickAnalysisTotalOrientation.Column),
        Total("total.percenttotal", "% Total", "% Total", QuickAnalysisCommand.PercentTotal, QuickAnalysisTotalFunction.PercentTotal, QuickAnalysisTotalOrientation.Column),
        Total("total.runningtotal", "Running Total", "Running Total", QuickAnalysisCommand.RunningTotal, QuickAnalysisTotalFunction.RunningTotal, QuickAnalysisTotalOrientation.Column),
        Total("total.max", "Max", "Max", QuickAnalysisCommand.Max, QuickAnalysisTotalFunction.Max, QuickAnalysisTotalOrientation.Column),
        Total("total.min", "Min", "Min", QuickAnalysisCommand.Min, QuickAnalysisTotalFunction.Min, QuickAnalysisTotalOrientation.Column),
        ModelOnlyTotal("total.sum.row", "Sum (column)", QuickAnalysisTotalFunction.Sum, QuickAnalysisTotalOrientation.Row),
        ModelOnlyTotal("total.average.row", "Average (column)", QuickAnalysisTotalFunction.Average, QuickAnalysisTotalOrientation.Row),

        Table(
            "table.table",
            "Format as Table",
            "Table",
            QuickAnalysisCommand.FormatAsTable,
            QuickAnalysisTableKind.Table,
            "Preview formatting the selection as a table."),
        Table(
            "table.pivottable",
            "PivotTable",
            "PivotTable",
            QuickAnalysisCommand.PivotTable,
            QuickAnalysisTableKind.PivotTable,
            "Preview creating a PivotTable from the selected range."),

        Sparkline(
            "sparkline.line",
            "Line",
            "Line",
            QuickAnalysisCommand.LineSparkline,
            QuickAnalysisSparklineKind.Line,
            "Preview line sparklines beside the selected range.",
            QuickAnalysisPreviewVisualKind.LineSparkline),
        Sparkline(
            "sparkline.column",
            "Column",
            "Column",
            QuickAnalysisCommand.ColumnSparkline,
            QuickAnalysisSparklineKind.Column,
            "Preview column sparklines beside the selected range.",
            QuickAnalysisPreviewVisualKind.ColumnSparkline),
        Sparkline(
            "sparkline.winloss",
            "Win/Loss",
            "Win/Loss",
            QuickAnalysisCommand.WinLossSparkline,
            QuickAnalysisSparklineKind.WinLoss,
            "Preview win/loss sparklines beside the selected range.",
            QuickAnalysisPreviewVisualKind.WinLossSparkline),
    ];

    private static readonly IReadOnlyDictionary<QuickAnalysisCommand, QuickAnalysisCatalogEntry> EntriesByCommand =
        Entries
            .Where(entry => entry.Command is not null)
            .ToDictionary(entry => entry.Command!.Value);

    private static readonly IReadOnlyDictionary<string, QuickAnalysisCatalogEntry> EntriesById =
        Entries.ToDictionary(entry => entry.Id, StringComparer.Ordinal);

    public static IReadOnlyList<QuickAnalysisOption> BuildOptions() =>
        Entries.Where(entry => entry.IncludeAsOption).Select(entry => entry.ToOption()).ToArray();

    public static IReadOnlyList<QuickAnalysisDisplayItem> BuildOptionDisplayItems() =>
        Entries.Where(entry => entry.IncludeAsOption).Select(entry => entry.ToOptionDisplayItem()).ToArray();

    public static IReadOnlyList<QuickAnalysisSuggestion> BuildSuggestions(params QuickAnalysisCommand[] commands) =>
        commands.Select(command => EntryFor(command).ToSuggestion()).ToArray();

    public static QuickAnalysisSuggestion BuildSuggestion(string id) =>
        EntryFor(id).ToSuggestion();

    public static QuickAnalysisCommandRoute Route(QuickAnalysisCommand command) =>
        EntryFor(command).Route;

    public static QuickAnalysisCommandRoute Route(string id) =>
        EntryFor(id).Route;

    public static QuickAnalysisDisplayItem BuildDisplayItem(string id) =>
        EntryFor(id).ToSuggestionDisplayItem();

    private static QuickAnalysisCatalogEntry EntryFor(QuickAnalysisCommand command) =>
        EntriesByCommand.TryGetValue(command, out var entry)
            ? entry
            : throw new ArgumentOutOfRangeException(nameof(command), command, "No Quick Analysis catalog entry exists for this command.");

    private static QuickAnalysisCatalogEntry EntryFor(string id) =>
        EntriesById.TryGetValue(id, out var entry)
            ? entry
            : throw new ArgumentOutOfRangeException(nameof(id), id, "No Quick Analysis catalog entry exists for this id.");

    private static QuickAnalysisCatalogEntry Formatting(
        string id,
        string optionLabel,
        string suggestionLabel,
        QuickAnalysisCommand command,
        QuickAnalysisFormatKind formatKind,
        CfRuleType ruleType,
        string previewText,
        QuickAnalysisPreviewVisualKind visualKind) =>
        new(
            QuickAnalysisGroup.Formatting,
            id,
            optionLabel,
            suggestionLabel,
            command,
            QuickAnalysisPreviewKind.ConditionalFormat,
            previewText,
            visualKind,
            new QuickAnalysisCommandRoute(
                QuickAnalysisCommandKind.ConditionalFormat,
                ConditionalFormat: QuickAnalysisConditionalFormatCatalog.ForFormatKind(formatKind).Command),
            QuickAnalysisActionKind.ConditionalFormat,
            ConditionalFormat: new QuickAnalysisConditionalFormatAction(formatKind, ruleType));

    private static QuickAnalysisCatalogEntry Chart(
        string id,
        string optionLabel,
        string suggestionLabel,
        QuickAnalysisCommand command,
        ChartType chartType,
        string previewText,
        QuickAnalysisPreviewVisualKind visualKind) =>
        new(
            QuickAnalysisGroup.Charts,
            id,
            optionLabel,
            suggestionLabel,
            command,
            QuickAnalysisPreviewKind.Chart,
            previewText,
            visualKind,
            new QuickAnalysisCommandRoute(QuickAnalysisCommandKind.InsertChart, ChartType: chartType),
            QuickAnalysisActionKind.InsertChart,
            Chart: new QuickAnalysisChartAction(chartType));

    private static QuickAnalysisCatalogEntry Total(
        string id,
        string optionLabel,
        string suggestionLabel,
        QuickAnalysisCommand command,
        QuickAnalysisTotalFunction function,
        QuickAnalysisTotalOrientation orientation) =>
        new(
            QuickAnalysisGroup.Totals,
            id,
            optionLabel,
            suggestionLabel,
            command,
            QuickAnalysisPreviewKind.Total,
            TotalPreviewText(command),
            QuickAnalysisPreviewVisualKind.TotalFormula,
            ToTotalRoute(function),
            QuickAnalysisActionKind.InsertTotals,
            Total: new QuickAnalysisTotalAction(function, orientation));

    private static QuickAnalysisCatalogEntry ModelOnlyTotal(
        string id,
        string suggestionLabel,
        QuickAnalysisTotalFunction function,
        QuickAnalysisTotalOrientation orientation) =>
        new(
            QuickAnalysisGroup.Totals,
            id,
            suggestionLabel,
            suggestionLabel,
            Command: null,
            QuickAnalysisPreviewKind.Total,
            $"Preview {suggestionLabel.ToLowerInvariant()} totals next to the selected range.",
            QuickAnalysisPreviewVisualKind.TotalFormula,
            ToTotalRoute(function),
            QuickAnalysisActionKind.InsertTotals,
            Total: new QuickAnalysisTotalAction(function, orientation),
            IncludeAsOption: false);

    private static QuickAnalysisCatalogEntry Table(
        string id,
        string optionLabel,
        string suggestionLabel,
        QuickAnalysisCommand command,
        QuickAnalysisTableKind tableKind,
        string previewText) =>
        new(
            QuickAnalysisGroup.Tables,
            id,
            optionLabel,
            suggestionLabel,
            command,
            QuickAnalysisPreviewKind.Table,
            previewText,
            QuickAnalysisPreviewVisualKind.Table,
            new QuickAnalysisCommandRoute(
                tableKind == QuickAnalysisTableKind.Table
                    ? QuickAnalysisCommandKind.Table
                    : QuickAnalysisCommandKind.PivotTable),
            QuickAnalysisActionKind.Table,
            Table: new QuickAnalysisTableAction(tableKind));

    private static QuickAnalysisCatalogEntry Sparkline(
        string id,
        string optionLabel,
        string suggestionLabel,
        QuickAnalysisCommand command,
        QuickAnalysisSparklineKind sparklineKind,
        string previewText,
        QuickAnalysisPreviewVisualKind visualKind) =>
        new(
            QuickAnalysisGroup.Sparklines,
            id,
            optionLabel,
            suggestionLabel,
            command,
            QuickAnalysisPreviewKind.Sparkline,
            previewText,
            visualKind,
            new QuickAnalysisCommandRoute(
                QuickAnalysisCommandKind.Sparkline,
                SparklineKind: ToCoreSparklineKind(sparklineKind)),
            QuickAnalysisActionKind.InsertSparklines,
            Sparkline: new QuickAnalysisSparklineAction(sparklineKind));

    private static QuickAnalysisCatalogEntry CommandOnly(
        QuickAnalysisGroup group,
        string id,
        string optionLabel,
        string suggestionLabel,
        QuickAnalysisCommand command,
        QuickAnalysisCommandRoute route,
        QuickAnalysisPreviewKind previewKind,
        string previewText,
        QuickAnalysisPreviewVisualKind visualKind) =>
        new(
            group,
            id,
            optionLabel,
            suggestionLabel,
            command,
            previewKind,
            previewText,
            visualKind,
            route,
            ActionKind: null);

    private static QuickAnalysisCommandRoute ToTotalRoute(QuickAnalysisTotalFunction function) =>
        function switch
        {
            QuickAnalysisTotalFunction.Sum => Aggregate("SUM"),
            QuickAnalysisTotalFunction.Average => Aggregate("AVERAGE"),
            QuickAnalysisTotalFunction.Count => Aggregate("COUNT"),
            QuickAnalysisTotalFunction.PercentTotal => new(QuickAnalysisCommandKind.InsertTotalFormula, TotalFormulaKind: QuickAnalysisTotalFormulaKind.PercentTotal),
            QuickAnalysisTotalFunction.RunningTotal => new(QuickAnalysisCommandKind.InsertTotalFormula, TotalFormulaKind: QuickAnalysisTotalFormulaKind.RunningTotal),
            QuickAnalysisTotalFunction.Max => Aggregate("MAX"),
            QuickAnalysisTotalFunction.Min => Aggregate("MIN"),
            _ => throw new ArgumentOutOfRangeException(nameof(function), function, "Unknown Quick Analysis total function.")
        };

    private static QuickAnalysisCommandRoute Aggregate(string function) =>
        new(
            QuickAnalysisCommandKind.InsertTotalFormula,
            TotalFormulaKind: QuickAnalysisTotalFormulaKind.Aggregate,
            TotalFunction: function);

    private static SparklineKind ToCoreSparklineKind(QuickAnalysisSparklineKind sparklineKind) =>
        sparklineKind switch
        {
            QuickAnalysisSparklineKind.Line => SparklineKind.Line,
            QuickAnalysisSparklineKind.Column => SparklineKind.Column,
            QuickAnalysisSparklineKind.WinLoss => SparklineKind.WinLoss,
            _ => throw new ArgumentOutOfRangeException(nameof(sparklineKind), sparklineKind, "Unknown Quick Analysis sparkline kind.")
        };

    private static string TotalPreviewText(QuickAnalysisCommand command) =>
        command switch
        {
            QuickAnalysisCommand.PercentTotal => "Preview each row as a percent of the selected total.",
            QuickAnalysisCommand.RunningTotal => "Preview cumulative totals next to the selected range.",
            QuickAnalysisCommand.Max => "Preview maximum totals next to the selected range.",
            QuickAnalysisCommand.Min => "Preview minimum totals next to the selected range.",
            QuickAnalysisCommand.Average => "Preview average totals next to the selected range.",
            QuickAnalysisCommand.Count => "Preview count totals next to the selected range.",
            _ => "Preview sum totals next to the selected range."
        };
}

internal sealed record QuickAnalysisCatalogEntry(
    QuickAnalysisGroup Group,
    string Id,
    string OptionLabel,
    string SuggestionLabel,
    QuickAnalysisCommand? Command,
    QuickAnalysisPreviewKind PreviewKind,
    string PreviewText,
    QuickAnalysisPreviewVisualKind PreviewVisualKind,
    QuickAnalysisCommandRoute Route,
    QuickAnalysisActionKind? ActionKind,
    QuickAnalysisConditionalFormatAction? ConditionalFormat = null,
    QuickAnalysisChartAction? Chart = null,
    QuickAnalysisTotalAction? Total = null,
    QuickAnalysisTableAction? Table = null,
    QuickAnalysisSparklineAction? Sparkline = null,
    bool IncludeAsOption = true)
{
    public QuickAnalysisOption ToOption()
    {
        if (Command is not { } command)
            throw new InvalidOperationException($"Quick Analysis catalog entry '{Id}' is not an option-backed entry.");

        return new QuickAnalysisOption(
            Group,
            OptionLabel,
            command,
            PreviewKind,
            PreviewText,
            new QuickAnalysisPreviewVisual(PreviewVisualKind));
    }

    public QuickAnalysisDisplayItem ToOptionDisplayItem()
    {
        if (Command is not { } command)
            throw new InvalidOperationException($"Quick Analysis catalog entry '{Id}' is not an option-backed entry.");

        return new QuickAnalysisDisplayItem(
            Id,
            Group,
            OptionLabel,
            Route,
            PreviewKind,
            PreviewText,
            new QuickAnalysisPreviewVisual(PreviewVisualKind),
            command);
    }

    public QuickAnalysisDisplayItem ToSuggestionDisplayItem() =>
        new(
            Id,
            Group,
            SuggestionLabel,
            Route,
            PreviewKind,
            PreviewText,
            new QuickAnalysisPreviewVisual(PreviewVisualKind),
            Command);

    public QuickAnalysisSuggestion ToSuggestion() =>
        ActionKind switch
        {
            QuickAnalysisActionKind.ConditionalFormat => QuickAnalysisSuggestion.Formatting(
                Id,
                SuggestionLabel,
                ConditionalFormat ?? throw MissingDescriptor()),
            QuickAnalysisActionKind.InsertChart => QuickAnalysisSuggestion.ChartSuggestion(
                Id,
                SuggestionLabel,
                Chart ?? throw MissingDescriptor()),
            QuickAnalysisActionKind.InsertTotals => QuickAnalysisSuggestion.TotalSuggestion(
                Id,
                SuggestionLabel,
                Total ?? throw MissingDescriptor()),
            QuickAnalysisActionKind.Table => QuickAnalysisSuggestion.TableSuggestion(
                Id,
                SuggestionLabel,
                Table ?? throw MissingDescriptor()),
            QuickAnalysisActionKind.InsertSparklines => QuickAnalysisSuggestion.SparklineSuggestion(
                Id,
                SuggestionLabel,
                Sparkline ?? throw MissingDescriptor()),
            _ => throw new InvalidOperationException($"Quick Analysis catalog entry '{Id}' is not a suggestion-backed entry.")
        };

    private InvalidOperationException MissingDescriptor() =>
        new($"Quick Analysis catalog entry '{Id}' is missing its {ActionKind} descriptor.");
}
