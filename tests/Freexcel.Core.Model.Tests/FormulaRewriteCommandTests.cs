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
    public void InsertRows_AbsoluteRowRef_IsShifted()
    {
        // Excel adjusts $A$5 → $A$6 when a row is inserted above row 5.
        // The $ only prevents copy-paste offsets, not structural row shifts.
        var (_, sheet, ctx) = Setup();
        sheet.SetFormula(new CellAddress(sheet.Id, 1, 2), "$A$5");

        new InsertRowsCommand(sheet.Id, 3, 1).Apply(ctx);

        sheet.GetCell(1, 2)!.FormulaText.Should().Be("$A$6");
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
    public void InsertCols_AbsoluteColRef_IsShifted()
    {
        // Excel adjusts $B1 → $C1 when a column is inserted before column B.
        var (_, sheet, ctx) = Setup();
        sheet.SetFormula(new CellAddress(sheet.Id, 1, 1), "$B1");

        new InsertColumnsCommand(sheet.Id, 2, 1).Apply(ctx);

        sheet.GetCell(1, 1)!.FormulaText.Should().Be("$C1");
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

    // ── RenameSheet ─────────────────────────────────────────────────────────

    [Fact]
    public void RenameSheet_RewritesCrossSheetFormulaReferences()
    {
        var (wb, sheet, ctx) = Setup();
        var sheet2 = wb.AddSheet("Sheet2");
        sheet2.SetFormula(new CellAddress(sheet2.Id, 1, 1), "Sheet1!A1");

        new RenameSheetCommand(sheet.Id, "Data").Apply(ctx);

        sheet2.GetCell(1, 1)!.FormulaText.Should().Be("Data!A1");
    }

    [Fact]
    public void RenameSheet_RewritesQuotedCrossSheetFormulaReferences()
    {
        var wb = new Workbook("test");
        var data = wb.AddSheet("My Sheet");
        var formulas = wb.AddSheet("Formulas");
        var ctx = new SimpleCtx(wb);
        formulas.SetFormula(new CellAddress(formulas.Id, 1, 1), "'My Sheet'!A1");

        new RenameSheetCommand(data.Id, "New Sheet").Apply(ctx);

        formulas.GetCell(1, 1)!.FormulaText.Should().Be("'New Sheet'!A1");
    }

    [Fact]
    public void RenameSheet_Undo_RestoresFormulaReferences()
    {
        var (wb, sheet, ctx) = Setup();
        var sheet2 = wb.AddSheet("Sheet2");
        sheet2.SetFormula(new CellAddress(sheet2.Id, 1, 1), "Sheet1!A1");

        var cmd = new RenameSheetCommand(sheet.Id, "Data");
        cmd.Apply(ctx);
        cmd.Revert(ctx);

        sheet.Name.Should().Be("Sheet1");
        sheet2.GetCell(1, 1)!.FormulaText.Should().Be("Sheet1!A1");
    }

    [Fact]
    public void RenameSheet_DuplicateName_FailsWithoutChangingSheet()
    {
        var (wb, sheet, ctx) = Setup();
        wb.AddSheet("Data");

        var outcome = new RenameSheetCommand(sheet.Id, "data").Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("already exists");
        sheet.Name.Should().Be("Sheet1");
    }

    [Fact]
    public void RenameSheet_InvalidExcelSheetName_FailsWithoutChangingSheet()
    {
        var (_, sheet, ctx) = Setup();

        var outcome = new RenameSheetCommand(sheet.Id, "Bad/Name").Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("invalid");
        sheet.Name.Should().Be("Sheet1");
    }

    [Fact]
    public void RemoveSheet_Undo_RestoresRemovedSheetContentsAndId()
    {
        var wb = new Workbook("test");
        var first = wb.AddSheet("First");
        var removed = wb.AddSheet("Data");
        var third = wb.AddSheet("Third");
        var ctx = new SimpleCtx(wb);
        var removedId = removed.Id;
        removed.SetCell(new CellAddress(removed.Id, 2, 3), new NumberValue(42));
        removed.SetFormula(new CellAddress(removed.Id, 3, 3), "C2*2");

        var cmd = new RemoveSheetCommand(removed.Id);
        cmd.Apply(ctx);
        cmd.Revert(ctx);

        wb.Sheets.Select(s => s.Name).Should().Equal("First", "Data", "Third");
        wb.Sheets[1].Id.Should().Be(removedId);
        wb.Sheets[0].Id.Should().Be(first.Id);
        wb.Sheets[2].Id.Should().Be(third.Id);
        wb.Sheets[1].GetCell(2, 3)!.Value.Should().Be(new NumberValue(42));
        wb.Sheets[1].GetCell(3, 3)!.FormulaText.Should().Be("C2*2");
    }

    [Fact]
    public void RemoveSheet_RemovesNamedRangesOnDeletedSheetAndUndoRestores()
    {
        var wb = new Workbook("test");
        var keep = wb.AddSheet("Keep");
        var removed = wb.AddSheet("Data");
        var ctx = new SimpleCtx(wb);
        wb.DefineNamedRange("KeepRange", new GridRange(
            new CellAddress(keep.Id, 1, 1),
            new CellAddress(keep.Id, 2, 1)));
        wb.DefineNamedRange("DataRange", new GridRange(
            new CellAddress(removed.Id, 1, 1),
            new CellAddress(removed.Id, 2, 1)));

        var cmd = new RemoveSheetCommand(removed.Id);
        cmd.Apply(ctx);

        wb.NamedRanges.Should().ContainKey("KeepRange");
        wb.NamedRanges.Should().NotContainKey("DataRange");

        cmd.Revert(ctx);

        wb.NamedRanges.Should().ContainKey("DataRange");
        wb.NamedRanges["DataRange"].Start.Sheet.Should().Be(removed.Id);
    }

    [Fact]
    public void RemoveSheet_OnlySheet_FailsWithoutRemovingSheet()
    {
        var wb = new Workbook("test");
        var only = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);

        var outcome = new RemoveSheetCommand(only.Id).Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("only sheet");
        wb.Sheets.Should().ContainSingle().Which.Id.Should().Be(only.Id);
    }

    [Fact]
    public void MoveSheet_MovesSheetAndUndoRestoresOriginalOrder()
    {
        var wb = new Workbook("test");
        var first = wb.AddSheet("First");
        var second = wb.AddSheet("Second");
        var third = wb.AddSheet("Third");
        var ctx = new SimpleCtx(wb);

        var cmd = new MoveSheetCommand(fromIndex: 0, toIndex: 2);
        var outcome = cmd.Apply(ctx);

        outcome.Success.Should().BeTrue();
        wb.Sheets.Select(s => s.Name).Should().Equal("Second", "Third", "First");
        wb.Sheets.Select(s => s.Id).Should().Equal(second.Id, third.Id, first.Id);

        cmd.Revert(ctx);

        wb.Sheets.Select(s => s.Name).Should().Equal("First", "Second", "Third");
        wb.Sheets.Select(s => s.Id).Should().Equal(first.Id, second.Id, third.Id);
    }

    [Fact]
    public void MoveSheet_InvalidIndex_FailsWithoutChangingOrder()
    {
        var wb = new Workbook("test");
        wb.AddSheet("First");
        wb.AddSheet("Second");
        var ctx = new SimpleCtx(wb);

        var outcome = new MoveSheetCommand(fromIndex: 0, toIndex: 5).Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("index");
        wb.Sheets.Select(s => s.Name).Should().Equal("First", "Second");
    }
}
