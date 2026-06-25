using FreeX.Core.Commands;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Model.Tests;

public partial class InsertDeleteColumnsTests
{
    // K7 regression: InsertColumns and DeleteColumns must rewrite Workbook.NamedFormulas
    // the same way they rewrite cell formulas.

    [Fact]
    public void InsertColumns_ShiftsNamedFormulaReferenceRight()
    {
        // Named formula "Rate" = Sheet1!$C$1 — inserting 2 cols before col C (3) → $E$1
        var (wb, sheet, ctx) = Setup();
        wb.NamedFormulas["Rate"] = "Sheet1!$C$1";

        new InsertColumnsCommand(sheet.Id, beforeCol: 3, count: 2).Apply(ctx);

        wb.NamedFormulas["Rate"].Should().Contain("$E$1",
            because: "inserting 2 columns before C must shift the reference from $C$1 to $E$1");
        wb.NamedFormulas["Rate"].Should().NotContain("$C$1",
            because: "the original column-C reference must have been updated");
    }

    [Fact]
    public void InsertColumnsRevert_RestoresNamedFormula()
    {
        var (wb, sheet, ctx) = Setup();
        wb.NamedFormulas["Rate"] = "Sheet1!$C$1";

        var cmd = new InsertColumnsCommand(sheet.Id, beforeCol: 3, count: 2);
        cmd.Apply(ctx);
        cmd.Revert(ctx);

        wb.NamedFormulas["Rate"].Should().Be("Sheet1!$C$1",
            because: "undo must restore the original named formula text");
    }

    [Fact]
    public void DeleteColumns_NamedFormulaReferencingDeletedColumnBecomesRefError()
    {
        var (wb, sheet, ctx) = Setup();
        wb.NamedFormulas["Rate"] = "Sheet1!$C$1";

        new DeleteColumnsCommand(sheet.Id, startCol: 3, count: 1).Apply(ctx);

        wb.NamedFormulas["Rate"].Should().Contain("#REF!",
            because: "deleting the referenced column must mark the named formula as #REF!");
    }

    [Fact]
    public void DeleteColumnsRevert_RestoresNamedFormula()
    {
        var (wb, sheet, ctx) = Setup();
        wb.NamedFormulas["Rate"] = "Sheet1!$C$1";

        var cmd = new DeleteColumnsCommand(sheet.Id, startCol: 3, count: 1);
        cmd.Apply(ctx);
        cmd.Revert(ctx);

        wb.NamedFormulas["Rate"].Should().Be("Sheet1!$C$1",
            because: "undo must restore the original named formula text even after a #REF! rewrite");
    }
}
