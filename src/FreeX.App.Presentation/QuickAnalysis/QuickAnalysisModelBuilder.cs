using FreeX.Core.Model;

namespace FreeX.App.Presentation.QuickAnalysis;

/// <summary>
/// Pure decision model for Quick Analysis: given a portable description of the selected range, decides
/// which suggestion groups apply and what concrete action each suggestion maps to. It mirrors the
/// desktop hosts' rule for which suggestions appear, then refines it with the content of the selection
/// (numeric vs text vs date columns, header detection, a total-able numeric area). It produces no
/// geometry and performs no rendering — preview layout lives separately.
/// </summary>
public static class QuickAnalysisModelBuilder
{
    /// <summary>
    /// Builds the suggestion model for a selection. Returns <see cref="QuickAnalysisModel.Empty"/> for
    /// degenerate selections (a single cell or an empty range), matching the desktop hosts, which offer
    /// nothing for a single cell.
    /// </summary>
    public static QuickAnalysisModel Build(QuickAnalysisSelectionDescription selection)
    {
        ArgumentNullException.ThrowIfNull(selection);

        if (selection.IsEmpty)
            return QuickAnalysisModel.Empty;

        var groups = new List<QuickAnalysisSuggestionGroup>(5);

        AddGroup(groups, QuickAnalysisGroup.Formatting, BuildFormatting(selection));
        AddGroup(groups, QuickAnalysisGroup.Charts, BuildCharts(selection));
        AddGroup(groups, QuickAnalysisGroup.Totals, BuildTotals(selection));
        AddGroup(groups, QuickAnalysisGroup.Tables, BuildTables(selection));
        AddGroup(groups, QuickAnalysisGroup.Sparklines, BuildSparklines(selection));

        return groups.Count == 0 ? QuickAnalysisModel.Empty : new QuickAnalysisModel(groups);
    }

    private static void AddGroup(
        List<QuickAnalysisSuggestionGroup> groups,
        QuickAnalysisGroup group,
        IReadOnlyList<QuickAnalysisSuggestion> suggestions)
    {
        if (suggestions.Count > 0)
            groups.Add(new QuickAnalysisSuggestionGroup(group, suggestions));
    }

    /// <summary>
    /// Formatting applies only when the selection has numeric cells (data bars, color scales, icon sets,
    /// greater-than and top-10 are number-driven). A text-only selection gets no Formatting group.
    /// </summary>
    private static IReadOnlyList<QuickAnalysisSuggestion> BuildFormatting(QuickAnalysisSelectionDescription selection)
    {
        if (!selection.HasNumericColumn)
            return [];

        return
        [
            QuickAnalysisSuggestion.Formatting(
                "format.databars",
                "Data Bars",
                new QuickAnalysisConditionalFormatAction(QuickAnalysisFormatKind.DataBars, CfRuleType.DataBar)),
            QuickAnalysisSuggestion.Formatting(
                "format.colorscale",
                "Color Scale",
                new QuickAnalysisConditionalFormatAction(QuickAnalysisFormatKind.ColorScale, CfRuleType.ColorScale)),
            QuickAnalysisSuggestion.Formatting(
                "format.iconset",
                "Icon Set",
                new QuickAnalysisConditionalFormatAction(QuickAnalysisFormatKind.IconSet, CfRuleType.IconSet)),
            QuickAnalysisSuggestion.Formatting(
                "format.greaterthan",
                "Greater Than",
                new QuickAnalysisConditionalFormatAction(QuickAnalysisFormatKind.GreaterThan, CfRuleType.CellValue)),
            QuickAnalysisSuggestion.Formatting(
                "format.top10",
                "Top 10%",
                new QuickAnalysisConditionalFormatAction(QuickAnalysisFormatKind.Top10, CfRuleType.Top10))
        ];
    }

    /// <summary>
    /// Charts are recommended for the data shape: numeric data yields clustered column, line and bar; a
    /// single numeric series also offers pie. Charts need at least one numeric column and a data row.
    /// </summary>
    private static IReadOnlyList<QuickAnalysisSuggestion> BuildCharts(QuickAnalysisSelectionDescription selection)
    {
        if (!selection.HasNumericColumn || !selection.HasDataRows)
            return [];

        var suggestions = new List<QuickAnalysisSuggestion>(4)
        {
            QuickAnalysisSuggestion.ChartSuggestion(
                "chart.clusteredcolumn",
                "Clustered Column",
                new QuickAnalysisChartAction(ChartType.Column)),
            QuickAnalysisSuggestion.ChartSuggestion(
                "chart.line",
                "Line",
                new QuickAnalysisChartAction(ChartType.Line)),
            QuickAnalysisSuggestion.ChartSuggestion(
                "chart.bar",
                "Clustered Bar",
                new QuickAnalysisChartAction(ChartType.Bar))
        };

        // A pie chart can only depict a single series, so offer it only for one numeric column.
        if (selection.NumericColumnCount == 1)
        {
            suggestions.Add(QuickAnalysisSuggestion.ChartSuggestion(
                "chart.pie",
                "Pie",
                new QuickAnalysisChartAction(ChartType.Pie)));
        }

        return suggestions;
    }

    /// <summary>
    /// Totals apply when there are numeric columns and at least one data row to aggregate. Column totals
    /// (a total row beneath the data) cover sum/average/count/running/percent; row totals (a total column
    /// beside the data) are offered for sum and average when more than one numeric column exists.
    /// </summary>
    private static IReadOnlyList<QuickAnalysisSuggestion> BuildTotals(QuickAnalysisSelectionDescription selection)
    {
        if (!selection.HasNumericColumn || !selection.HasDataRows)
            return [];

        var suggestions = new List<QuickAnalysisSuggestion>
        {
            QuickAnalysisSuggestion.TotalSuggestion(
                "total.sum",
                "Sum",
                new QuickAnalysisTotalAction(QuickAnalysisTotalFunction.Sum, QuickAnalysisTotalOrientation.Column)),
            QuickAnalysisSuggestion.TotalSuggestion(
                "total.average",
                "Average",
                new QuickAnalysisTotalAction(QuickAnalysisTotalFunction.Average, QuickAnalysisTotalOrientation.Column)),
            QuickAnalysisSuggestion.TotalSuggestion(
                "total.count",
                "Count",
                new QuickAnalysisTotalAction(QuickAnalysisTotalFunction.Count, QuickAnalysisTotalOrientation.Column)),
            QuickAnalysisSuggestion.TotalSuggestion(
                "total.percenttotal",
                "% Total",
                new QuickAnalysisTotalAction(QuickAnalysisTotalFunction.PercentTotal, QuickAnalysisTotalOrientation.Column)),
            QuickAnalysisSuggestion.TotalSuggestion(
                "total.runningtotal",
                "Running Total",
                new QuickAnalysisTotalAction(QuickAnalysisTotalFunction.RunningTotal, QuickAnalysisTotalOrientation.Column))
        };

        if (selection.NumericColumnCount > 1)
        {
            suggestions.Add(QuickAnalysisSuggestion.TotalSuggestion(
                "total.sum.row",
                "Sum (column)",
                new QuickAnalysisTotalAction(QuickAnalysisTotalFunction.Sum, QuickAnalysisTotalOrientation.Row)));
            suggestions.Add(QuickAnalysisSuggestion.TotalSuggestion(
                "total.average.row",
                "Average (column)",
                new QuickAnalysisTotalAction(QuickAnalysisTotalFunction.Average, QuickAnalysisTotalOrientation.Row)));
        }

        return suggestions;
    }

    /// <summary>
    /// Tables apply when the selection looks tabular: a header row, or a grid of at least two columns.
    /// Convert-to-table is always offered for tabular data; PivotTable needs a header row to name fields.
    /// </summary>
    private static IReadOnlyList<QuickAnalysisSuggestion> BuildTables(QuickAnalysisSelectionDescription selection)
    {
        var looksTabular = selection.HasHeaderRow || selection.ColCount >= 2;
        if (!looksTabular)
            return [];

        var suggestions = new List<QuickAnalysisSuggestion>(2)
        {
            QuickAnalysisSuggestion.TableSuggestion(
                "table.table",
                "Table",
                new QuickAnalysisTableAction(QuickAnalysisTableKind.Table))
        };

        if (selection.HasHeaderRow)
        {
            suggestions.Add(QuickAnalysisSuggestion.TableSuggestion(
                "table.pivottable",
                "PivotTable",
                new QuickAnalysisTableAction(QuickAnalysisTableKind.PivotTable)));
        }

        return suggestions;
    }

    /// <summary>
    /// Sparklines depict a numeric series per row, so they apply when there are numeric columns and at
    /// least two data points per row (more than one column) to draw a trend across.
    /// </summary>
    private static IReadOnlyList<QuickAnalysisSuggestion> BuildSparklines(QuickAnalysisSelectionDescription selection)
    {
        if (!selection.HasNumericColumn || !selection.HasDataRows || selection.NumericColumnCount < 2)
            return [];

        return
        [
            QuickAnalysisSuggestion.SparklineSuggestion(
                "sparkline.line",
                "Line",
                new QuickAnalysisSparklineAction(QuickAnalysisSparklineKind.Line)),
            QuickAnalysisSuggestion.SparklineSuggestion(
                "sparkline.column",
                "Column",
                new QuickAnalysisSparklineAction(QuickAnalysisSparklineKind.Column)),
            QuickAnalysisSuggestion.SparklineSuggestion(
                "sparkline.winloss",
                "Win/Loss",
                new QuickAnalysisSparklineAction(QuickAnalysisSparklineKind.WinLoss))
        ];
    }
}
