using FluentAssertions;
using Freexcel.App.UI;
using Freexcel.Core.Model;
using System.IO;

namespace Freexcel.App.UI.Tests;

public sealed class GridViewEditingCellTests
{
    [Fact]
    public void ShouldDrawCellContent_HidesEditedCellDisplayText()
    {
        var sheetId = SheetId.New();
        var cell = new DisplayCell(4, 5, null, "=SUM(E5:E7)", null, StyleId.Default, null);

        GridView.ShouldDrawCellContent(cell, new CellAddress(sheetId, 4, 5))
            .Should().BeFalse();
    }

    [Fact]
    public void ShouldDrawCellContent_DrawsOtherCells()
    {
        var sheetId = SheetId.New();
        var cell = new DisplayCell(5, 5, new NumberValue(1), "1", null, StyleId.Default, null);

        GridView.ShouldDrawCellContent(cell, new CellAddress(sheetId, 4, 5))
            .Should().BeTrue();
    }

    [Fact]
    public void BuildOccupiedCellSet_TreatsEmptyEditedCellAsOccupied()
    {
        var sheetId = SheetId.New();
        var cells = new[]
        {
            new DisplayCell(1, 1, new TextValue("long text"), "long text", null, StyleId.Default, null),
            new DisplayCell(1, 2, null, "", null, StyleId.Default, null),
        };

        var occupied = GridView.BuildOccupiedCellSet(cells, new CellAddress(sheetId, 1, 2));

        occupied.Should().Contain((1u, 1u));
        occupied.Should().Contain((1u, 2u));
    }

    [Fact]
    public void BuildOccupiedCellSet_TreatsIconOnlyCellsAsOccupied()
    {
        var cells = new[]
        {
            new DisplayCell(
                2,
                3,
                null,
                "",
                null,
                StyleId.Default,
                null,
                ConditionalIcon: new ConditionalFormatIcon("3Arrows", 0, 3, true)),
        };

        GridView.BuildOccupiedCellSet(cells, editingCell: null)
            .Should()
            .Contain((2u, 3u));
    }

    [Fact]
    public void BuildOccupiedCellSet_AvoidsLinqPipelines()
    {
        var source = File.ReadAllText(FindWorkspaceFile(
            "src", "Freexcel.App.UI", "GridView.ConditionalIcons.cs"));
        var buildOccupied = source[
            source.IndexOf("public static HashSet<(uint Row, uint Col)> BuildOccupiedCellSet", StringComparison.Ordinal)..
            source.IndexOf("private static void DrawConditionalIcon", StringComparison.Ordinal)];

        buildOccupied.Should().Contain("foreach (var cell in cells)");
        buildOccupied.Should().Contain("occupied.Add((cell.Row, cell.Col))");
        buildOccupied.Should().NotContain(".Where(");
        buildOccupied.Should().NotContain(".Select(");
    }

    private static string FindWorkspaceFile(params string[] relativeParts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine([directory.FullName, .. relativeParts]);
            if (File.Exists(candidate))
                return candidate;
            directory = directory.Parent;
        }

        throw new FileNotFoundException("Could not locate workspace file.", Path.Combine(relativeParts));
    }
}
