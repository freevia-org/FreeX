using FreeX.App.Presentation.Consolidate;
using FreeX.Core.Commands;
using FreeX.Core.Model;

using FluentAssertions;

namespace FreeX.App.Presentation.Tests.Consolidate;

/// <summary>
/// Unit tests for the UI-free Consolidate apply planner: reading a sheet range into the portable
/// <see cref="ConsolidateCellValue"/> grid the planner consumes, mapping a planned <see cref="ConsolidateResult"/>
/// over a destination anchor into cell edits (by-position and by-labels), and reporting the non-empty cells an
/// apply would overwrite. No running shell required.
/// </summary>
public sealed class ConsolidateApplyPlannerTests
{
    [Fact]
    public void ReadSource_ClassifiesNumbersLabelsAndBlanks()
    {
        var (_, sheet) = BuildWorkbook();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("North"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new NumberValue(10));
        // (1,2)/(2,1) populated, (2,2) left blank.
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("South"));

        var grid = ConsolidateApplyPlanner.ReadSource(sheet, Range(sheet.Id, 1, 1, 2, 2));

        grid.GetLength(0).Should().Be(2);
        grid.GetLength(1).Should().Be(2);
        grid[0, 0].IsNumber.Should().BeFalse();
        grid[0, 0].LabelText().Should().Be("North");
        grid[0, 1].IsNumber.Should().BeTrue();
        grid[0, 1].Number.Should().Be(10);
        grid[1, 1].IsBlank.Should().BeTrue();
    }

    [Fact]
    public void ReadSource_WholeSheetSource_ClampsToUsedRangeInsteadOfCrashing()
    {
        var (_, sheet) = BuildWorkbook();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(3));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(4));

        // A whole-sheet source (R66-int-overflow-wrap-sweep-3): dense-allocating rowCount x
        // colCount (up to ~17 billion cells for A1:XFD1048576) must not be attempted -- the read
        // is clamped to the sheet's populated (used) area instead, mirroring Excel.
        var wholeSheet = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, CellAddress.MaxRow, CellAddress.MaxCol));

        var grid = ConsolidateApplyPlanner.ReadSource(sheet, wholeSheet);

        (grid.GetLength(0) * (long)grid.GetLength(1)).Should().BeLessThanOrEqualTo(ConsolidateApplyPlanner.MaxSourceRangeCells);
        grid.GetLength(0).Should().Be(2);
        grid.GetLength(1).Should().Be(1);
        grid[0, 0].Number.Should().Be(3);
        grid[1, 0].Number.Should().Be(4);
    }

    [Fact]
    public void ReadSource_NormalSmallSource_StillConsolidatesCorrectly()
    {
        var (_, sheet) = BuildWorkbook();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(1));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new NumberValue(2));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(3));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(4));

        var grid = ConsolidateApplyPlanner.ReadSource(sheet, Range(sheet.Id, 1, 1, 2, 2));

        grid.GetLength(0).Should().Be(2);
        grid.GetLength(1).Should().Be(2);
        grid[0, 0].Number.Should().Be(1);
        grid[0, 1].Number.Should().Be(2);
        grid[1, 0].Number.Should().Be(3);
        grid[1, 1].Number.Should().Be(4);
    }

    [Fact]
    public void ToCellValue_MapsScalarKinds()
    {
        ConsolidateApplyPlanner.ToCellValue(new NumberValue(3.5)).IsNumber.Should().BeTrue();
        ConsolidateApplyPlanner.ToCellValue(new TextValue("x")).LabelText().Should().Be("x");
        ConsolidateApplyPlanner.ToCellValue(new BoolValue(true)).LabelText().Should().Be("TRUE");
        ConsolidateApplyPlanner.ToCellValue(null).IsBlank.Should().BeTrue();
    }

    [Fact]
    public void MapToEdits_ByPosition_SumsAlignedCellsAtDestination()
    {
        var (_, sheet) = BuildWorkbook();
        // Two 2x2 numeric blocks summed position-wise.
        var sourceA = Source(new double[,] { { 1, 2 }, { 3, 4 } });
        var sourceB = Source(new double[,] { { 10, 20 }, { 30, 40 } });
        var options = new ConsolidateOptions { Function = ConsolidateFunction.Sum };
        var result = ConsolidatePlanner.Plan([sourceA, sourceB], options);

        var destination = new CellAddress(sheet.Id, 5, 3); // C5
        var edits = ConsolidateApplyPlanner.MapToEdits(sheet.Id, result, destination);

        NumberAt(edits, sheet.Id, 5, 3).Should().Be(11);
        NumberAt(edits, sheet.Id, 5, 4).Should().Be(22);
        NumberAt(edits, sheet.Id, 6, 3).Should().Be(33);
        NumberAt(edits, sheet.Id, 6, 4).Should().Be(44);
    }

    [Fact]
    public void MapToEdits_ByLabels_WritesLabelHeadersAndAggregates()
    {
        var (_, sheet) = BuildWorkbook();
        // Each source has a top-row label and a left-column label, with one numeric body cell.
        var sourceA = LabeledSource("Q1", "North", 5);
        var sourceB = LabeledSource("Q1", "North", 7);
        var options = new ConsolidateOptions
        {
            Function = ConsolidateFunction.Sum,
            UseTopRowLabels = true,
            UseLeftColumnLabels = true,
        };
        var result = ConsolidatePlanner.Plan([sourceA, sourceB], options);

        var destination = new CellAddress(sheet.Id, 1, 1);
        var edits = ConsolidateApplyPlanner.MapToEdits(sheet.Id, result, destination);

        // The column/row labels are emitted as label cells, and the aligned body sums to 12.
        var labels = edits.Select(e => Label(e.NewCell)).ToList();
        labels.Should().Contain("Q1");
        labels.Should().Contain("North");
        edits.Select(e => Number(e.NewCell)).Should().Contain(12);
    }

    [Fact]
    public void FindOverwriteTargets_CountsNonEmptyDestinationCells()
    {
        var (_, sheet) = BuildWorkbook();
        sheet.SetCell(new CellAddress(sheet.Id, 5, 3), new TextValue("old"));
        var source = Source(new double[,] { { 1 } });
        var result = ConsolidatePlanner.Plan([source], new ConsolidateOptions { Function = ConsolidateFunction.Sum });
        var destination = new CellAddress(sheet.Id, 5, 3);
        var edits = ConsolidateApplyPlanner.MapToEdits(sheet.Id, result, destination);

        var overwrites = ConsolidateApplyPlanner.FindOverwriteTargets(sheet, edits);

        overwrites.Should().ContainSingle()
            .Which.Should().Be(new CellAddress(sheet.Id, 5, 3));
    }

    private static ConsolidateSource Source(double[,] values)
    {
        var rows = values.GetLength(0);
        var cols = values.GetLength(1);
        var grid = new ConsolidateCellValue[rows, cols];
        for (var r = 0; r < rows; r++)
        for (var c = 0; c < cols; c++)
            grid[r, c] = ConsolidateCellValue.FromNumber(values[r, c]);

        return ConsolidateSource.FromGrid(grid);
    }

    private static ConsolidateSource LabeledSource(string topLabel, string leftLabel, double value)
    {
        var grid = new ConsolidateCellValue[2, 2];
        grid[0, 0] = ConsolidateCellValue.Blank;
        grid[0, 1] = ConsolidateCellValue.FromLabel(topLabel);
        grid[1, 0] = ConsolidateCellValue.FromLabel(leftLabel);
        grid[1, 1] = ConsolidateCellValue.FromNumber(value);
        return ConsolidateSource.FromGrid(grid);
    }

    private static double? NumberAt(
        IReadOnlyList<(CellAddress Address, Cell NewCell)> edits,
        SheetId sheetId,
        uint row,
        uint col) =>
        edits
            .Where(e => e.Address == new CellAddress(sheetId, row, col))
            .Select(e => Number(e.NewCell))
            .FirstOrDefault();

    private static double? Number(Cell cell) => (cell.Value as NumberValue)?.Value;

    private static string? Label(Cell cell) => (cell.Value as TextValue)?.Value;

    private static (Workbook Workbook, Sheet Sheet) BuildWorkbook()
    {
        var workbook = new Workbook("Consolidate");
        var sheet = workbook.AddSheet("Data");
        return (workbook, sheet);
    }

    private static GridRange Range(SheetId sheetId, uint startRow, uint startCol, uint endRow, uint endCol) =>
        new(new CellAddress(sheetId, startRow, startCol), new CellAddress(sheetId, endRow, endCol));
}
