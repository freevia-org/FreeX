using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Calc.Tests;

public partial class DataValidationTests
{
    // ─── helpers ──────────────────────────────────────────────────────────────

    private static (Workbook workbook, Sheet sheet) MakeWorkbook() =>
        TestWorkbookFixture.CreateWorkbook();

    private static GridRange MakeSingleCellRange(Sheet sheet, uint row, uint col)
    {
        var addr = new CellAddress(sheet.Id, row, col);
        return new GridRange(addr, addr);
    }

    // ─── List validation ──────────────────────────────────────────────────────

}
