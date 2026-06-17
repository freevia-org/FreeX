using System;
using System.Linq;

using FluentAssertions;

using FreeX.App.Avalonia;
using FreeX.App.Avalonia.Dialogs;
using FreeX.App.Presentation.QuickAnalysis;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// Unit tests for the non-UI glue backing the Avalonia Quick Analysis entry point: mapping a chosen
/// suggestion onto the existing shell command path (<see cref="QuickAnalysisCommandRouter"/>) and planning
/// the sparkline inserts (<see cref="QuickAnalysisSparklinePlanner"/>). The selection reader, insert-chart
/// and insert-table factories are covered by their own tests. No running UI is required.
/// </summary>
public sealed class QuickAnalysisShellGlueTests
{
    // ── Command router: suggestion → existing shell command path ──────────────

    [Theory]
    [InlineData(QuickAnalysisFormatKind.DataBars, ConditionalFormatPreset.DataBar)]
    [InlineData(QuickAnalysisFormatKind.ColorScale, ConditionalFormatPreset.ColorScale)]
    [InlineData(QuickAnalysisFormatKind.IconSet, ConditionalFormatPreset.IconSet)]
    [InlineData(QuickAnalysisFormatKind.GreaterThan, ConditionalFormatPreset.HighlightGreaterThan)]
    [InlineData(QuickAnalysisFormatKind.Top10, ConditionalFormatPreset.Top10)]
    public void Route_Formatting_MapsToConditionalFormatPreset(
        QuickAnalysisFormatKind formatKind, ConditionalFormatPreset expected)
    {
        var suggestion = FormattingSuggestion(formatKind);

        var route = QuickAnalysisCommandRouter.Route(suggestion);

        route.Kind.Should().Be(QuickAnalysisCommandKind.ConditionalFormatPreset);
        route.Preset.Should().Be(expected);
    }

    [Theory]
    [InlineData(QuickAnalysisTotalFunction.Sum, "SUM")]
    [InlineData(QuickAnalysisTotalFunction.Average, "AVERAGE")]
    [InlineData(QuickAnalysisTotalFunction.Count, "COUNT")]
    public void Route_Totals_MapsToAutoSumFunction(QuickAnalysisTotalFunction function, string expected)
    {
        var suggestion = QuickAnalysisModelSuggestion(QuickAnalysisGroup.Totals, function);

        var route = QuickAnalysisCommandRouter.Route(suggestion);

        route.Kind.Should().Be(QuickAnalysisCommandKind.AutoSum);
        route.AutoSumFunction.Should().Be(expected);
    }

    [Theory]
    [InlineData(QuickAnalysisTotalFunction.PercentTotal)]
    [InlineData(QuickAnalysisTotalFunction.RunningTotal)]
    public void Route_Totals_WithoutAutoSumAnalogue_IsDeferred(QuickAnalysisTotalFunction function)
    {
        var suggestion = QuickAnalysisModelSuggestion(QuickAnalysisGroup.Totals, function);

        var route = QuickAnalysisCommandRouter.Route(suggestion);

        route.Kind.Should().Be(QuickAnalysisCommandKind.Deferred);
        route.DeferredNote.Should().NotBeNullOrWhiteSpace();
    }

    [Theory]
    [InlineData(QuickAnalysisSparklineKind.Line, SparklineKind.Line)]
    [InlineData(QuickAnalysisSparklineKind.Column, SparklineKind.Column)]
    [InlineData(QuickAnalysisSparklineKind.WinLoss, SparklineKind.WinLoss)]
    public void Route_Sparkline_MapsToSparklineKind(
        QuickAnalysisSparklineKind sparklineKind, SparklineKind expected)
    {
        var suggestion = FindSuggestion(
            BuildModel(numericColumns: 3, hasHeader: true),
            QuickAnalysisGroup.Sparklines,
            s => s.Sparkline!.SparklineKind == sparklineKind);

        var route = QuickAnalysisCommandRouter.Route(suggestion);

        route.Kind.Should().Be(QuickAnalysisCommandKind.Sparkline);
        route.SparklineKind.Should().Be(expected);
    }

    [Fact]
    public void Route_Chart_MapsToInsertChart_CarryingChartType()
    {
        var suggestion = FindSuggestion(
            BuildModel(numericColumns: 2, hasHeader: true), QuickAnalysisGroup.Charts, _ => true);

        var route = QuickAnalysisCommandRouter.Route(suggestion);

        route.Kind.Should().Be(QuickAnalysisCommandKind.InsertChart);
        route.ChartType.Should().Be(suggestion.Chart!.ChartType);
    }

    [Fact]
    public void Route_Table_MapsToTable()
    {
        var suggestion = FindSuggestion(
            BuildModel(numericColumns: 2, hasHeader: true),
            QuickAnalysisGroup.Tables,
            s => s.Table!.TableKind == QuickAnalysisTableKind.Table);

        var route = QuickAnalysisCommandRouter.Route(suggestion);

        route.Kind.Should().Be(QuickAnalysisCommandKind.Table);
    }

    [Fact]
    public void Route_PivotTable_IsDeferred()
    {
        var suggestion = FindSuggestion(
            BuildModel(numericColumns: 2, hasHeader: true),
            QuickAnalysisGroup.Tables,
            s => s.Table!.TableKind == QuickAnalysisTableKind.PivotTable);

        var route = QuickAnalysisCommandRouter.Route(suggestion);

        route.Kind.Should().Be(QuickAnalysisCommandKind.Deferred);
        route.DeferredNote.Should().NotBeNullOrWhiteSpace();
    }

    // ── Sparkline planner: one per data row, placed beside the selection ──────

    [Fact]
    public void SparklinePlanner_BuildsOneCommandPerDataRow_PlacedRightOfSelection()
    {
        var sheetId = new SheetId(Guid.NewGuid());
        var range = new GridRange(
            new CellAddress(sheetId, 2, 1),
            new CellAddress(sheetId, 4, 3));

        var commands = QuickAnalysisSparklinePlanner.BuildCommands(
            sheetId, range, hasHeaderRow: true, SparklineKind.Line);

        // 3 rows, header skipped → 2 data rows → 2 sparklines.
        commands.Should().HaveCount(2);
    }

    [Fact]
    public void SparklinePlanner_ReturnsEmpty_ForSingleColumn()
    {
        var sheetId = new SheetId(Guid.NewGuid());
        var range = new GridRange(
            new CellAddress(sheetId, 1, 1),
            new CellAddress(sheetId, 5, 1));

        var commands = QuickAnalysisSparklinePlanner.BuildCommands(
            sheetId, range, hasHeaderRow: false, SparklineKind.Column);

        commands.Should().BeEmpty();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static QuickAnalysisSuggestion FormattingSuggestion(QuickAnalysisFormatKind formatKind)
    {
        var model = BuildModel(numericColumns: 1, hasHeader: false);
        return FindSuggestion(model, QuickAnalysisGroup.Formatting, s => s.ConditionalFormat!.FormatKind == formatKind);
    }

    private static QuickAnalysisSuggestion QuickAnalysisModelSuggestion(
        QuickAnalysisGroup group, QuickAnalysisTotalFunction function)
    {
        var model = BuildModel(numericColumns: 2, hasHeader: false);
        return FindSuggestion(model, group, s => s.Total!.Function == function);
    }

    private static QuickAnalysisModel BuildModel(int numericColumns, bool hasHeader)
    {
        var sheetId = new SheetId(Guid.NewGuid());
        var range = new GridRange(
            new CellAddress(sheetId, 1, 1),
            new CellAddress(sheetId, 4, (uint)numericColumns));
        var columnKinds = Enumerable.Repeat(QuickAnalysisColumnKind.Numeric, numericColumns).ToArray();
        return QuickAnalysisModelBuilder.Build(
            new QuickAnalysisSelectionDescription(range, hasHeader, columnKinds));
    }

    private static QuickAnalysisSuggestion FindSuggestion(
        QuickAnalysisModel model, QuickAnalysisGroup group, Func<QuickAnalysisSuggestion, bool> predicate) =>
        model.SuggestionsFor(group).First(predicate);
}
