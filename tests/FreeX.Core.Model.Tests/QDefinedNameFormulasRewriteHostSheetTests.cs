using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// Regression tests for review-5 finding K36: rewriting a workbook-scope (global)
/// <see cref="Workbook.NamedFormulas"/> entry for a structural row/column insert/delete must use
/// the *edited sheet* as the unqualified-reference "host sheet" — matching how the formula
/// evaluator (<c>FormulaEvaluator.References.cs</c>) and the dependency tracker
/// (<c>RecalcEngine.CollectReferences</c>) resolve an unqualified reference inside a global named
/// formula relative to whichever sheet the calling cell lives on — instead of always hardcoding
/// the workbook's first sheet (<c>Sheets[0]</c>). Before the fix, a structural edit on any sheet
/// other than the first sheet silently failed to shift an unqualified reference inside a global
/// named formula (or an edit on the first sheet wrongly shifted a reference meant for a different
/// sheet's callers).
/// </summary>
public sealed class QDefinedNameFormulasRewriteHostSheetTests
{
    private static (Workbook Workbook, Sheet Sheet1, Sheet Sheet2, ICommandContext Ctx) Setup()
    {
        var wb = new Workbook("test");
        var sheet1 = wb.AddSheet("Sheet1");
        var sheet2 = wb.AddSheet("Sheet2");
        return (wb, sheet1, sheet2, new TestCommandContext(wb));
    }

    // ── Row insert on the SECOND sheet must rewrite an unqualified ref in a global name ──

    [Fact]
    public void InsertRows_OnNonFirstSheet_ShiftsUnqualifiedReferenceInGlobalNamedFormula()
    {
        // "Total" = "A1:A10" (no sheet qualifier) — evaluated relative to the caller's own sheet.
        // A Sheet2 cell doing "=Total" means Sheet2!A1:A10 (per FormulaEvaluator/RecalcEngine).
        var (wb, sheet1, sheet2, ctx) = Setup();
        wb.NamedFormulas["Total"] = "A1:A10";

        // Insert a row above row 1 on Sheet2 (NOT Sheets[0]).
        new InsertRowsCommand(sheet2.Id, beforeRow: 1, count: 1).Apply(ctx);

        wb.NamedFormulas["Total"].Should().Be("A2:A11",
            because: "the edit on Sheet2 (not Sheets[0]=Sheet1) must actually rewrite the unqualified " +
                     "reference: both endpoints shift down by the 1 inserted row");
        _ = sheet1;
    }

    [Fact]
    public void InsertRows_OnNonFirstSheet_BoundedReference_Shifts()
    {
        var (wb, sheet1, sheet2, ctx) = Setup();
        wb.NamedFormulas["Total"] = "$A$5*2";

        new InsertRowsCommand(sheet2.Id, beforeRow: 1, count: 3).Apply(ctx);

        wb.NamedFormulas["Total"].Should().Contain("$A$8",
            because: "inserting 3 rows above row 5 on Sheet2 must shift the unqualified $A$5 reference " +
                     "to $A$8, because the evaluator resolves it against Sheet2 for a Sheet2 caller");
        wb.NamedFormulas["Total"].Should().NotContain("$A$5");
        _ = sheet1;
    }

    [Fact]
    public void InsertRows_OnNonFirstSheet_ThenRevert_RestoresOriginalText()
    {
        var (wb, _, sheet2, ctx) = Setup();
        wb.NamedFormulas["Total"] = "$A$5*2";

        var cmd = new InsertRowsCommand(sheet2.Id, beforeRow: 1, count: 3);
        cmd.Apply(ctx);
        cmd.Revert(ctx);

        wb.NamedFormulas["Total"].Should().Be("$A$5*2");
    }

    // ── Row insert on the FIRST sheet is still correctly rewritten for a Sheet1 caller ──

    [Fact]
    public void InsertRows_OnFirstSheet_StillShiftsUnqualifiedReference_ForASheet1Caller()
    {
        // The fix must not simply flip the old bug (always Sheets[0]) into "never Sheets[0]" — an
        // edit on Sheet1 legitimately shifts the unqualified reference for a Sheet1 caller too, since
        // the host sheet is now driven by whichever sheet the structural edit itself targets.
        var (wb, sheet1, _, ctx) = Setup();
        wb.NamedFormulas["Total"] = "$A$5*2";

        new InsertRowsCommand(sheet1.Id, beforeRow: 1, count: 3).Apply(ctx);

        wb.NamedFormulas["Total"].Should().Contain("$A$8",
            because: "a Sheet1 caller's unqualified $A$5 reference must shift when rows are inserted " +
                     "on Sheet1 — the fix must still rewrite when the edited sheet IS the correct host");
    }

    // ── End-to-end: evaluated value from the referencing sheet stays correct after the edit ──

    [Fact]
    public void InsertRows_OnNonFirstSheet_KeepsEvaluatedNamedFormulaValueCorrect()
    {
        var (wb, _, sheet2, ctx) = Setup();
        // Sheet2 has values in A1..A3; an unqualified named formula sums A1:A3 for whichever sheet calls it.
        sheet2.SetCell(new CellAddress(sheet2.Id, 1, 1), new NumberValue(10));
        sheet2.SetCell(new CellAddress(sheet2.Id, 2, 1), new NumberValue(20));
        sheet2.SetCell(new CellAddress(sheet2.Id, 3, 1), new NumberValue(30));
        wb.NamedFormulas["Total"] = "SUM($A$1:$A$3)";

        var evaluator = new FormulaEvaluator();
        evaluator.Evaluate("=Total", sheet2, wb).Should().Be(new NumberValue(60));

        // Insert a row above row 1 on Sheet2: InsertRowsCommand shifts the stored cell values (10/20/30)
        // from rows 1-3 down to rows 2-4 on its own; only the named formula's own text rewrite is at stake.
        new InsertRowsCommand(sheet2.Id, beforeRow: 1, count: 1).Apply(ctx);

        // The rewritten named formula must now sum rows 2-4 (where the data actually is), not the
        // stale rows 1-3 (which would now include a blank cell and miss the last data row).
        wb.NamedFormulas["Total"].Should().Be("SUM($A$2:$A$4)");
        evaluator.Evaluate("=Total", sheet2, wb).Should().Be(new NumberValue(60));
    }
}
