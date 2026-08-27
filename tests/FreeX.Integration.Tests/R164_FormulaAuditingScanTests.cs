using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Integration.Tests;

/// <summary>
/// r164 remediation, second pass. Trace Precedents expands every referenced range into individual
/// addresses, so a formula over a whole-sheet reference or named range walked all 17,179,869,184
/// addresses on the synchronous UI thread (measured past a 15s budget; the whole-COLUMN case took
/// 279ms and was never the problem). An unbounded reference is now narrowed to the data it covers,
/// which is what the trace arrow points at anyway -- a reference the formula author bounded is still
/// expanded exactly as written, blank cells included.
/// </summary>
public class R164_FormulaAuditingScanTests
{
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(30);

    private static T Within<T>(Func<T> run)
    {
        var task = Task.Run(run);
        task.Wait(Budget).Should().BeTrue("the whole-sheet reference expansion must not hang the UI thread");
        return task.Result;
    }

    [Fact]
    public void TracePrecedents_WholeSheetNamedRange_ReturnsThePopulatedCellsInsteadOfHanging()
    {
        var workbook = new Workbook("R164Audit");
        var sheet = workbook.AddSheet("Sheet1");
        workbook.NamedRanges["Everything"] = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, CellAddress.MaxRow, CellAddress.MaxCol));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(5));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(6));
        sheet.SetCell(new CellAddress(sheet.Id, 5, 1), Cell.FromFormula("=SUM(Everything)"));

        var precedents = Within(() =>
            FormulaAuditingService.GetDirectPrecedents(workbook, new CellAddress(sheet.Id, 5, 1)));

        precedents.Should().Contain(new CellAddress(sheet.Id, 1, 1));
        precedents.Should().Contain(new CellAddress(sheet.Id, 2, 1));
    }

    [Fact]
    public void TracePrecedents_WholeSheetRangeReference_ReturnsThePopulatedCellsInsteadOfHanging()
    {
        var workbook = new Workbook("R164AuditRange");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(5));
        sheet.SetCell(new CellAddress(sheet.Id, 5, 1), Cell.FromFormula("=SUM(A1:XFD1048576)"));

        var precedents = Within(() =>
            FormulaAuditingService.GetDirectPrecedents(workbook, new CellAddress(sheet.Id, 5, 1)));

        precedents.Should().Contain(new CellAddress(sheet.Id, 1, 1));
    }

    [Fact]
    public void TracePrecedents_ABoundedReference_StillReportsItsBlankCells()
    {
        // Sibling/no-regression: only an UNBOUNDED reference is narrowed. A bounded one keeps every
        // address the author named, empty or not.
        var workbook = new Workbook("R164AuditBounded");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 5, 1), Cell.FromFormula("=SUM(B1:B3)"));

        var precedents = FormulaAuditingService.GetDirectPrecedents(workbook, new CellAddress(sheet.Id, 5, 1));

        precedents.Should().HaveCount(3);
        precedents.Should().Contain(new CellAddress(sheet.Id, 2, 2));
    }
}
