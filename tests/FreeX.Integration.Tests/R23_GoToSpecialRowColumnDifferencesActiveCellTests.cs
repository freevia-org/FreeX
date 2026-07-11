using FreeX.Core.Commands;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Integration.Tests;

/// <summary>
/// Regression test for R23-navigation-selection-2: GoToSpecialService.Find's RowDifferences and
/// ColumnDifferences branches dispatched to FindRowDifferences/FindColumnDifferences without ever
/// forwarding the activeCell parameter, even though Find() accepts one and threads it through for
/// CurrentRegion/Precedents/Dependents. FindRowDifferences hardcoded range.Start.Col (and
/// FindColumnDifferences hardcoded range.Start.Row) as the comparison baseline, which only matches
/// real Excel when the active cell happens to be the selection's top-left corner. Real Excel's
/// Row differences always compares each row against the cell in the ACTIVE cell's column (Column
/// differences: the ACTIVE cell's row), regardless of where in the selection that active cell is.
/// </summary>
public class R23_GoToSpecialRowColumnDifferencesActiveCellTests
{
    private static void Set(Sheet sheet, uint row, uint col, object value)
    {
        var address = new CellAddress(sheet.Id, row, col);
        ScalarValue scalar = value switch
        {
            string s => new TextValue(s),
            double d => new NumberValue(d),
            int i => new NumberValue(i),
            _ => throw new ArgumentException("Unsupported value type", nameof(value))
        };
        sheet.SetCell(address, scalar);
    }

    [Fact]
    public void FindRowDifferences_ActiveCellNotTopLeft_ComparesAgainstActiveCellsColumn()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 3));

        // Row 1: all equal -- no differences regardless of comparison column.
        Set(sheet, 1, 1, 5);
        Set(sheet, 1, 2, 5);
        Set(sheet, 1, 3, 5);
        // Row 2: only column B (col 2) differs from column C (col 3, the active column).
        Set(sheet, 2, 1, 5);
        Set(sheet, 2, 2, 9);
        Set(sheet, 2, 3, 5);
        // Row 3: columns A and B (cols 1,2) both differ from column C (col 3).
        Set(sheet, 3, 1, 1);
        Set(sheet, 3, 2, 1);
        Set(sheet, 3, 3, 2);

        // Active cell is C1 -- column C (col 3), NOT the selection's top-left column (A, col 1).
        var activeCell = new CellAddress(sheet.Id, 1, 3);

        var result = GoToSpecialService.Find(sheet, range, GoToSpecialKind.RowDifferences, activeCell);

        result.Should().Equal(
            new CellAddress(sheet.Id, 2, 2),
            new CellAddress(sheet.Id, 3, 1),
            new CellAddress(sheet.Id, 3, 2));
    }

    [Fact]
    public void FindColumnDifferences_ActiveCellNotTopLeft_ComparesAgainstActiveCellsRow()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 3));

        // Column A: all equal -- no differences regardless of comparison row.
        Set(sheet, 1, 1, 5);
        Set(sheet, 2, 1, 5);
        Set(sheet, 3, 1, 5);
        // Column B: only row 2 differs from row 3 (the active row).
        Set(sheet, 1, 2, 5);
        Set(sheet, 2, 2, 9);
        Set(sheet, 3, 2, 5);
        // Column C: rows 1 and 2 both differ from row 3.
        Set(sheet, 1, 3, 1);
        Set(sheet, 2, 3, 1);
        Set(sheet, 3, 3, 2);

        // Active cell is A3 -- row 3, NOT the selection's top-left row (row 1).
        var activeCell = new CellAddress(sheet.Id, 3, 1);

        var result = GoToSpecialService.Find(sheet, range, GoToSpecialKind.ColumnDifferences, activeCell);

        result.Should().Equal(
            new CellAddress(sheet.Id, 2, 2),
            new CellAddress(sheet.Id, 1, 3),
            new CellAddress(sheet.Id, 2, 3));
    }

    [Fact]
    public void FindRowDifferences_ActiveCellOutsideRange_FallsBackToSelectionTopLeftColumn()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 3));
        Set(sheet, 1, 1, "A");
        Set(sheet, 1, 2, "A");
        Set(sheet, 1, 3, "B");

        // Active cell is outside the searched range entirely -- must fall back to range.Start.Col.
        var activeCell = new CellAddress(sheet.Id, 10, 10);

        var result = GoToSpecialService.Find(sheet, range, GoToSpecialKind.RowDifferences, activeCell);

        result.Should().Equal(new CellAddress(sheet.Id, 1, 3));
    }
}
