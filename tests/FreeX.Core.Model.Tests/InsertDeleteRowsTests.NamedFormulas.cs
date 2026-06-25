using FreeX.Core.Commands;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Model.Tests;

public partial class InsertDeleteRowsTests
{
    // K7 regression: InsertRows and DeleteRows must rewrite Workbook.NamedFormulas
    // the same way they rewrite cell formulas.

    [Fact]
    public void InsertRows_ShiftsNamedFormulaReferenceDown()
    {
        // Named formula "Tax" = Sheet1!$A$5*0.2
        // Inserting 3 rows before row 3 should shift $A$5 → $A$8.
        var (wb, sheet, ctx) = Setup();
        wb.NamedFormulas["Tax"] = "Sheet1!$A$5*0.2";

        new InsertRowsCommand(sheet.Id, beforeRow: 3, count: 3).Apply(ctx);

        wb.NamedFormulas["Tax"].Should().Contain("$A$8",
            because: "inserting 3 rows above row 5 must shift the reference from $A$5 to $A$8");
        wb.NamedFormulas["Tax"].Should().NotContain("$A$5",
            because: "the original row-5 reference must have been updated");
    }

    [Fact]
    public void InsertRowsRevert_RestoresNamedFormula()
    {
        var (wb, sheet, ctx) = Setup();
        wb.NamedFormulas["Tax"] = "Sheet1!$A$5*0.2";

        var cmd = new InsertRowsCommand(sheet.Id, beforeRow: 3, count: 3);
        cmd.Apply(ctx);
        cmd.Revert(ctx);

        wb.NamedFormulas["Tax"].Should().Be("Sheet1!$A$5*0.2",
            because: "undo must restore the original named formula text");
    }

    [Fact]
    public void DeleteRows_NamedFormulaReferencingDeletedRowBecomesRefError()
    {
        // Named formula "Tax" = Sheet1!$A$5*0.2; delete row 5 → reference becomes #REF!
        var (wb, sheet, ctx) = Setup();
        wb.NamedFormulas["Tax"] = "Sheet1!$A$5*0.2";

        new DeleteRowsCommand(sheet.Id, startRow: 5, count: 1).Apply(ctx);

        // The FormulaRewriter converts a reference to a deleted row to #REF!
        wb.NamedFormulas["Tax"].Should().Contain("#REF!",
            because: "deleting the referenced row must mark the named formula as #REF!");
    }

    [Fact]
    public void DeleteRows_NamedFormulaReferenceAboveDeletedRowIsUnchanged()
    {
        // Delete row 8; the named formula referencing row 5 should not change.
        var (wb, sheet, ctx) = Setup();
        wb.NamedFormulas["Tax"] = "Sheet1!$A$5*0.2";

        new DeleteRowsCommand(sheet.Id, startRow: 8, count: 1).Apply(ctx);

        wb.NamedFormulas["Tax"].Should().Contain("$A$5",
            because: "a reference above the deleted rows must remain unchanged");
    }

    [Fact]
    public void DeleteRowsRevert_RestoresNamedFormula()
    {
        var (wb, sheet, ctx) = Setup();
        wb.NamedFormulas["Tax"] = "Sheet1!$A$5*0.2";

        var cmd = new DeleteRowsCommand(sheet.Id, startRow: 5, count: 1);
        cmd.Apply(ctx);
        cmd.Revert(ctx);

        wb.NamedFormulas["Tax"].Should().Be("Sheet1!$A$5*0.2",
            because: "undo must restore the original named formula text even after a #REF! rewrite");
    }
}
