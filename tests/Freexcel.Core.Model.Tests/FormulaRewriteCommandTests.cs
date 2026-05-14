using Freexcel.Core.Commands;
using Freexcel.Core.Model;
using FluentAssertions;

namespace Freexcel.Core.Model.Tests;

public class FormulaRewriteCommandTests
{
    private static (Workbook wb, Sheet sheet, ICommandContext ctx) Setup()
    {
        var wb    = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        return (wb, sheet, new SimpleCtx(wb));
    }

    private sealed class SimpleCtx(Workbook wb) : ICommandContext
    {
        public Workbook Workbook { get; } = wb;
        public Sheet GetSheet(SheetId id) => Workbook.GetSheet(id)!;
    }

    // ── InsertRows ────────────────────────────────────────────────────────────

    [Fact]
    public void InsertRows_ShiftsRelativeFormulaRef()
    {
        var (_, sheet, ctx) = Setup();
        sheet.SetFormula(new CellAddress(sheet.Id, 1, 2), "A5");

        var cmd = new InsertRowsCommand(sheet.Id, 3, 1);
        cmd.Apply(ctx);

        sheet.GetCell(1, 2)!.FormulaText.Should().Be("A6");
    }

    [Fact]
    public void InsertRows_AbsoluteRowRef_NotShifted()
    {
        var (_, sheet, ctx) = Setup();
        sheet.SetFormula(new CellAddress(sheet.Id, 1, 2), "$A$5");

        new InsertRowsCommand(sheet.Id, 3, 1).Apply(ctx);

        sheet.GetCell(1, 2)!.FormulaText.Should().Be("$A$5");
    }

    [Fact]
    public void InsertRows_Undo_RestoresOriginalFormula()
    {
        var (_, sheet, ctx) = Setup();
        sheet.SetFormula(new CellAddress(sheet.Id, 1, 2), "A5");

        var cmd = new InsertRowsCommand(sheet.Id, 3, 1);
        cmd.Apply(ctx);
        sheet.GetCell(1, 2)!.FormulaText.Should().Be("A6");

        cmd.Revert(ctx);
        sheet.GetCell(1, 2)!.FormulaText.Should().Be("A5");
    }

    [Fact]
    public void InsertRows_CrossSheetFormula_ShiftedOnCorrectSheet()
    {
        var (wb, sheet, _) = Setup();
        var ctx = new SimpleCtx(wb);
        var sheet2 = wb.AddSheet("Sheet2");
        sheet2.SetFormula(new CellAddress(sheet2.Id, 1, 2), "Sheet1!A5");

        new InsertRowsCommand(sheet.Id, 3, 1).Apply(ctx);

        sheet2.GetCell(1, 2)!.FormulaText.Should().Be("Sheet1!A6");
    }

    // ── DeleteRows ───────────────────────────────────────────────────────────

    [Fact]
    public void DeleteRows_RefInDeletedRow_BecomesRefError()
    {
        var (_, sheet, ctx) = Setup();
        sheet.SetFormula(new CellAddress(sheet.Id, 1, 2), "A3");

        new DeleteRowsCommand(sheet.Id, 3, 1).Apply(ctx);

        sheet.GetCell(1, 2)!.FormulaText.Should().Be("#REF!");
    }

    [Fact]
    public void DeleteRows_RefBelowDeleted_ShiftsUp()
    {
        var (_, sheet, ctx) = Setup();
        sheet.SetFormula(new CellAddress(sheet.Id, 1, 2), "A5");

        new DeleteRowsCommand(sheet.Id, 3, 1).Apply(ctx);

        sheet.GetCell(1, 2)!.FormulaText.Should().Be("A4");
    }

    [Fact]
    public void DeleteRows_Undo_RestoresOriginalFormula()
    {
        var (_, sheet, ctx) = Setup();
        sheet.SetFormula(new CellAddress(sheet.Id, 1, 2), "A5");

        var cmd = new DeleteRowsCommand(sheet.Id, 3, 1);
        cmd.Apply(ctx);
        cmd.Revert(ctx);

        sheet.GetCell(1, 2)!.FormulaText.Should().Be("A5");
    }

    // ── InsertColumns ─────────────────────────────────────────────────────────

    [Fact]
    public void InsertCols_ShiftsRelativeFormulaRef()
    {
        var (_, sheet, ctx) = Setup();
        sheet.SetFormula(new CellAddress(sheet.Id, 1, 1), "B1");

        new InsertColumnsCommand(sheet.Id, 2, 1).Apply(ctx);

        sheet.GetCell(1, 1)!.FormulaText.Should().Be("C1");
    }

    [Fact]
    public void InsertCols_AbsoluteColRef_NotShifted()
    {
        var (_, sheet, ctx) = Setup();
        sheet.SetFormula(new CellAddress(sheet.Id, 1, 1), "$B1");

        new InsertColumnsCommand(sheet.Id, 2, 1).Apply(ctx);

        sheet.GetCell(1, 1)!.FormulaText.Should().Be("$B1");
    }

    [Fact]
    public void InsertCols_Undo_RestoresOriginalFormula()
    {
        var (_, sheet, ctx) = Setup();
        sheet.SetFormula(new CellAddress(sheet.Id, 1, 1), "B1");

        var cmd = new InsertColumnsCommand(sheet.Id, 2, 1);
        cmd.Apply(ctx);
        cmd.Revert(ctx);

        sheet.GetCell(1, 1)!.FormulaText.Should().Be("B1");
    }

    // ── DeleteColumns ─────────────────────────────────────────────────────────

    [Fact]
    public void DeleteCols_RefInDeletedCol_BecomesRefError()
    {
        var (_, sheet, ctx) = Setup();
        sheet.SetFormula(new CellAddress(sheet.Id, 1, 1), "B1");

        new DeleteColumnsCommand(sheet.Id, 2, 1).Apply(ctx);

        sheet.GetCell(1, 1)!.FormulaText.Should().Be("#REF!");
    }

    [Fact]
    public void DeleteCols_RefRightOfDeleted_ShiftsLeft()
    {
        var (_, sheet, ctx) = Setup();
        sheet.SetFormula(new CellAddress(sheet.Id, 1, 1), "D1");

        new DeleteColumnsCommand(sheet.Id, 2, 1).Apply(ctx);

        sheet.GetCell(1, 1)!.FormulaText.Should().Be("C1");
    }

    [Fact]
    public void DeleteCols_Undo_RestoresOriginalFormula()
    {
        var (_, sheet, ctx) = Setup();
        sheet.SetFormula(new CellAddress(sheet.Id, 1, 1), "D1");

        var cmd = new DeleteColumnsCommand(sheet.Id, 2, 1);
        cmd.Apply(ctx);
        cmd.Revert(ctx);

        sheet.GetCell(1, 1)!.FormulaText.Should().Be("D1");
    }
}
