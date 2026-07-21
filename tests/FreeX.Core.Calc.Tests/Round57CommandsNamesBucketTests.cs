using FreeX.Core.Commands;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Calc.Tests;

/// <summary>
/// Round-57 commands-names bucket findings:
///
/// R57-commands-name-manager-5-1: a UNION (multi-area) defined name — RefersTo stored verbatim in
/// <see cref="Workbook.NamedFormulas"/> as e.g. "Sheet1!$A$1:$A$5,Sheet1!$C$1:$C$5" because
/// <see cref="GridRange"/> cannot represent more than one rectangle — never got its RefersTo
/// adjusted on row/column insert or delete, because <see cref="FormulaRewriter.Rewrite"/> parses
/// the whole comma-joined text as ONE formula and a top-level comma always throws inside the
/// parser, so the rewrite silently came back null ("unchanged"). Fixed by splitting the union text
/// on top-level commas and rewriting each area independently before rejoining.
///
/// R57-commands-name-manager-5-2: <c>NamedDefinitionRecalcHelper.FindCellsReferencingName</c> only
/// scanned the name's own scope sheet for referencing formulas, missing formulas on OTHER sheets
/// that reference a sheet-scoped name via an explicit cross-sheet qualifier (e.g. "Sheet2!Rate"
/// written on Sheet1, referencing a name scoped to Sheet2) — a reference shape the evaluator
/// (<c>FormulaEvaluator.References.cs</c>'s <c>TryResolveSheetQualifiedName</c>) fully supports.
/// Fixed by scanning every sheet and matching a <c>NamedRangeNode</c> whose <c>SheetQualifier</c>
/// resolves to the name's scope sheet (in addition to an unqualified reference on that sheet
/// itself), so delete/redefine correctly reports the cross-sheet-qualified referrer as an
/// AffectedCell to recalculate.
/// </summary>
public sealed class Round57CommandsNamesBucketTests
{
    // ── R57-commands-name-manager-5-1 ─────────────────────────────────────────────────────────

    [Fact]
    public void InsertRows_UnionNamedFormula_ShiftsBothAreasIndependently()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        wb.NamedFormulas["UnionRange"] = "Sheet1!$A$1:$A$5,Sheet1!$C$1:$C$5";

        // Insert 1 row before row 2 — the insertion point falls inside both areas, so real Excel
        // (and every single-area name already) grows both areas by 1 row.
        new InsertRowsCommand(sheet.Id, beforeRow: 2, count: 1).Apply(ctx);

        wb.NamedFormulas["UnionRange"].Should().Be("Sheet1!$A$1:$A$6,Sheet1!$C$1:$C$6",
            because: "pre-fix, FormulaRewriter.Rewrite threw on the top-level comma and " +
                     "RewriteNamedFormulas treated the null result as unchanged, leaving the stale " +
                     "pre-insert addresses in place for a union name");
    }

    [Fact]
    public void InsertRows_UnionNamedFormula_ThenRevert_RestoresOriginalUnionText()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        wb.NamedFormulas["UnionRange"] = "Sheet1!$A$1:$A$5,Sheet1!$C$1:$C$5";

        var cmd = new InsertRowsCommand(sheet.Id, beforeRow: 2, count: 1);
        cmd.Apply(ctx);
        cmd.Revert(ctx);

        wb.NamedFormulas["UnionRange"].Should().Be("Sheet1!$A$1:$A$5,Sheet1!$C$1:$C$5",
            because: "undo must restore the original verbatim union text");
    }

    // Sibling no-regression: a single-area (non-union) global named formula must keep shifting
    // exactly as before — the new per-area split path must not change plain, single-area text.
    [Fact]
    public void InsertRows_SingleAreaNamedFormula_StillShiftsAsBefore()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        wb.NamedFormulas["Tax"] = "Sheet1!$A$5*0.2";

        new InsertRowsCommand(sheet.Id, beforeRow: 3, count: 3).Apply(ctx);

        wb.NamedFormulas["Tax"].Should().Contain("$A$8",
            because: "single-area named formulas must keep shifting the same as before this fix");
        wb.NamedFormulas["Tax"].Should().NotContain("$A$5");
    }

    // ── R57-commands-name-manager-5-2 ─────────────────────────────────────────────────────────

    [Fact]
    public void RemoveNamedRangeCommand_ScopedName_ReportsCrossSheetQualifiedReferrerAsAffected()
    {
        var wb = new Workbook("test");
        var sheet1 = wb.AddSheet("Sheet1");
        var sheet2 = wb.AddSheet("Sheet2");
        var ctx = new TestCommandContext(wb);

        // "Rate" is scoped to Sheet2, referring to Sheet2!$A$1.
        wb.DefineNamedRange("Rate", new GridRange(
            new CellAddress(sheet2.Id, 0, 0), new CellAddress(sheet2.Id, 0, 0)), null, sheet2.Id);

        // Sheet1!B1 references it via an explicit cross-sheet qualifier: =Sheet2!Rate*2.
        var referrer = new CellAddress(sheet1.Id, 0, 1);
        sheet1.SetCell(referrer, Cell.FromFormula("=Sheet2!Rate*2"));

        var cmd = new RemoveNamedRangeCommand("Rate", sheet2.Id);
        var outcome = cmd.Apply(ctx);

        outcome.Success.Should().BeTrue();
        outcome.AffectedCells.Should().Contain(referrer,
            because: "pre-fix, FindCellsReferencingName only scanned Sheet2 (the name's own scope " +
                     "sheet) and so never saw Sheet1!B1's Sheet2!Rate reference, leaving it stuck " +
                     "showing its stale cached value instead of recalculating to #NAME?");
    }

    // Sibling no-regression: an unrelated formula on another sheet that does NOT reference the
    // scoped name (even via a same-name qualifier to a different sheet) must not be reported.
    [Fact]
    public void RemoveNamedRangeCommand_ScopedName_DoesNotReportUnrelatedCrossSheetFormula()
    {
        var wb = new Workbook("test");
        var sheet1 = wb.AddSheet("Sheet1");
        var sheet2 = wb.AddSheet("Sheet2");
        var sheet3 = wb.AddSheet("Sheet3");
        var ctx = new TestCommandContext(wb);

        wb.DefineNamedRange("Rate", new GridRange(
            new CellAddress(sheet2.Id, 0, 0), new CellAddress(sheet2.Id, 0, 0)), null, sheet2.Id);

        // Sheet1!B1 qualifies "Rate" against Sheet3, not Sheet2 — must NOT match Sheet2's "Rate".
        var unrelated = new CellAddress(sheet1.Id, 0, 1);
        sheet1.SetCell(unrelated, Cell.FromFormula("=Sheet3!Rate*2"));

        var cmd = new RemoveNamedRangeCommand("Rate", sheet2.Id);
        var outcome = cmd.Apply(ctx);

        outcome.Success.Should().BeTrue();
        outcome.AffectedCells.Should().NotContain(unrelated,
            because: "a formula qualified against a different sheet must not be treated as a " +
                     "referrer of Sheet2's scoped name");
    }
}
