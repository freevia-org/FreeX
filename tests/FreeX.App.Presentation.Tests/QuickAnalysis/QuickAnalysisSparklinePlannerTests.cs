using FluentAssertions;
using FreeX.App.Presentation.QuickAnalysis;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.QuickAnalysis;

public sealed class QuickAnalysisSparklinePlannerTests
{
    [Fact]
    public void BuildCommands_BuildsOneCommandPerDataRow_PlacedRightOfSelection()
    {
        var sheetId = new SheetId(Guid.NewGuid());
        var range = new GridRange(
            new CellAddress(sheetId, 2, 1),
            new CellAddress(sheetId, 4, 3));

        var commands = QuickAnalysisSparklinePlanner.BuildCommands(
            sheetId, range, hasHeaderRow: true, SparklineKind.Line);

        commands.Should().HaveCount(2);
    }

    [Fact]
    public void BuildCommands_ReturnsEmpty_ForSingleColumn()
    {
        var sheetId = new SheetId(Guid.NewGuid());
        var range = new GridRange(
            new CellAddress(sheetId, 1, 1),
            new CellAddress(sheetId, 5, 1));

        var commands = QuickAnalysisSparklinePlanner.BuildCommands(
            sheetId, range, hasHeaderRow: false, SparklineKind.Column);

        commands.Should().BeEmpty();
    }
}
