using FluentAssertions;
using Freexcel.Core.Model;

namespace Freexcel.App.Host.Tests;

public sealed class QuickAnalysisPlannerTests
{
    [Fact]
    public void BuildOptions_ReturnsNoOptionsForSingleCellSelection()
    {
        var sheetId = SheetId.New();
        var selection = new GridRange(new CellAddress(sheetId, 4, 2), new CellAddress(sheetId, 4, 2));

        QuickAnalysisPlanner.BuildOptions(selection).Should().BeEmpty();
    }

    [Fact]
    public void BuildOptions_ReturnsExcelLikeGroupsForMultiCellSelection()
    {
        var sheetId = SheetId.New();
        var selection = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 5, 4));

        var options = QuickAnalysisPlanner.BuildOptions(selection);

        options.Select(option => option.Group)
            .Distinct()
            .Should()
            .Equal("Formatting", "Charts", "Totals", "Tables", "Sparklines");
        options.Select(option => option.Command)
            .Should()
            .Contain([
                QuickAnalysisCommand.DataBar,
                QuickAnalysisCommand.ColumnChart,
                QuickAnalysisCommand.Sum,
                QuickAnalysisCommand.FormatAsTable,
                QuickAnalysisCommand.LineSparkline
            ]);
    }
}
