using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests;

public sealed class MergedCellNavigationOwnershipTests
{
    [Theory]
    [InlineData(2u, 2u, 3u, 2u, 5u, 2u)]
    [InlineData(4u, 2u, 3u, 2u, 1u, 2u)]
    [InlineData(2u, 2u, 2u, 3u, 2u, 5u)]
    [InlineData(2u, 4u, 2u, 3u, 2u, 1u)]
    public void AdjustTargetPastMerge_StepsBeyondTheMergeEdge(
        uint fromRow,
        uint fromColumn,
        uint nextRow,
        uint nextColumn,
        uint expectedRow,
        uint expectedColumn)
    {
        var workbook = new Workbook("Book");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.AddMergedRegion(Range(sheet, 2, 2, 4, 4));
        var from = new CellAddress(sheet.Id, fromRow, fromColumn);
        var next = new CellAddress(sheet.Id, nextRow, nextColumn);

        ExcelWorksheetNavigationPlanner.AdjustTargetPastMerge(sheet, from, next)
            .Should()
            .Be(new CellAddress(sheet.Id, expectedRow, expectedColumn));
    }

    [Fact]
    public void WpfNavigation_DelegatesMergedCellSteppingToPresentationPlanner()
    {
        var repoRoot = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx");
        var editing = Read(repoRoot, "src", "FreeX.App.Host", "MainWindow.Editing.cs");
        var selection = Read(repoRoot, "src", "FreeX.App.Host", "MainWindow.Selection.cs");
        var adapter = Read(repoRoot, "src", "FreeX.App.Host", "ExcelWorksheetNavigationPlanner.cs");

        editing.Should().Contain("ExcelWorksheetNavigationPlanner.AdjustTargetPastMerge(");
        editing.Should().NotContain("private static CellAddress AdjustTargetPastMerge(");
        selection.Should().Contain("ExcelWorksheetNavigationPlanner.AdjustTargetPastMerge(");
        adapter.Should().Contain("FreeX.App.Presentation.ExcelWorksheetNavigationPlanner.AdjustTargetPastMerge(");
    }

    private static GridRange Range(Sheet sheet, uint startRow, uint startColumn, uint endRow, uint endColumn) =>
        new(
            new CellAddress(sheet.Id, startRow, startColumn),
            new CellAddress(sheet.Id, endRow, endColumn));

    private static string Read(string root, params string[] parts) =>
        File.ReadAllText(Path.Combine([root, .. parts]));
}
