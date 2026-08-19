using FreeX.Core.Model;
using FluentAssertions;
using Xunit;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// R147 defined-name-scope F1: <c>Workbook.TryGetNamedRange(name, contextSheetId, out range)</c>
/// checked <c>_scopedNamedRanges</c> first, then fell straight back to the workbook-global
/// <c>NamedRanges</c> dictionary -- it never consulted <c>_scopedNamedFormulas</c>. Per Excel's
/// own scope precedence (§18.2.6, per-NAME not per-kind, already honored by
/// FormulaEvaluator/RecalcEngine/NamedRangeNodeScopeResolver/FormControlListResolver/
/// DefineNamedRangeCommand -- each of which explicitly guards against this same method's gap
/// before calling it), a sheet-scoped name that happens to be formula-kind shadows a same-named
/// workbook-global RANGE on that sheet. Because this method is wired up directly (with no such
/// guard) as the "scope-aware" resolver behind F5 Go To, the Name Box, "Place in This Document"
/// hyperlink navigation, and conditional-format "applies to" editing, the bug meant every one of
/// those UI surfaces would silently resolve/navigate to the wrong, shadowed workbook-global
/// location instead of correctly reporting that no range is available (the formula must be read
/// via <see cref="Workbook.TryGetNamedFormulaText"/> instead).
/// </summary>
public class R147_TryGetNamedRangeScopedFormulaShadowTests
{
    [Fact]
    public void TryGetNamedRange_SheetScopedFormula_ShadowsGlobalRange_ReturnsFalseNotWrongRange()
    {
        // Repro straight from the finding's own probe evidence: a workbook-global "Foo" pointing
        // at Sheet1!A1:A3, and Sheet2 additionally defining ITS OWN sheet-scoped "Foo" as a
        // formula/expression pointing at Sheet2!B1:B3 (e.g. a Ctrl-click multi-area selection or
        // an OFFSET-based dynamic name entered via Name Manager with Scope=Sheet2).
        var wb = new Workbook("test");
        var sheet1 = wb.AddSheet("Sheet1");
        var sheet2 = wb.AddSheet("Sheet2");

        var globalRange = new GridRange(new CellAddress(sheet1.Id, 1, 1), new CellAddress(sheet1.Id, 3, 1));
        wb.DefineNamedRange("Foo", globalRange);
        wb.DefineNamedFormula("Foo", "Sheet2!$B$1:$B$3", sheet2.Id);

        // On Sheet2, "Foo" is shadowed by the local formula: the range-only API must not silently
        // hand back Sheet1's workbook-global range (the pre-fix bug -- F5 Go To / Name Box /
        // hyperlink navigation / CF "applies to" editing all resolve scope through this exact
        // call and would otherwise navigate to the WRONG sheet with no error at all).
        var found = wb.TryGetNamedRange("Foo", sheet2.Id, out var range);

        found.Should().BeFalse(
            "Sheet2 defines its own \"Foo\" as a FORMULA, which shadows the workbook-global " +
            "RANGE named \"Foo\" on Sheet2 -- this method must not fall through to the shadowed " +
            "global range");
        range.Should().Be(default(GridRange));

        // The formula text is still correctly reachable via the formula-aware accessor, so a
        // caller that wants the actual shadowing definition has somewhere to get it.
        wb.TryGetNamedFormulaText("Foo", sheet2.Id).Should().Be("Sheet2!$B$1:$B$3");
    }

    // ── No-regression siblings ─────────────────────────────────────────────────────────────

    [Fact]
    public void TryGetNamedRange_SheetScopedRange_StillShadowsGlobalRange_SiblingNoRegression()
    {
        // The adjacent, already-correct case: a sheet-scoped name that is itself RANGE-kind (not
        // formula-kind) must keep shadowing the workbook-global range exactly as before -- the
        // new formula-shadow check must not interfere with the pre-existing range-shadow path.
        var wb = new Workbook("test");
        var sheet1 = wb.AddSheet("Sheet1");
        var sheet2 = wb.AddSheet("Sheet2");

        var globalRange = new GridRange(new CellAddress(sheet1.Id, 1, 1), new CellAddress(sheet1.Id, 1, 1));
        var scopedRange = new GridRange(new CellAddress(sheet2.Id, 2, 2), new CellAddress(sheet2.Id, 2, 2));

        wb.DefineNamedRange("X", globalRange);
        wb.DefineNamedRange("X", scopedRange, metadata: null, sheet2.Id);

        wb.TryGetNamedRange("X", sheet2.Id, out var found2).Should().BeTrue();
        found2.Should().Be(scopedRange);

        // No scope collision on Sheet1 -- correctly falls back to the workbook-global range.
        wb.TryGetNamedRange("X", sheet1.Id, out var found1).Should().BeTrue();
        found1.Should().Be(globalRange);
    }

    [Fact]
    public void TryGetNamedRange_NoScopedNameAtAll_StillFallsBackToGlobalRange_SiblingNoRegression()
    {
        // Baseline case with neither a scoped range nor a scoped formula for this sheet: the
        // workbook-global range must still resolve normally (unaffected by the shadow check).
        var wb = new Workbook("test");
        var sheet1 = wb.AddSheet("Sheet1");
        var sheet2 = wb.AddSheet("Sheet2");

        var globalRange = new GridRange(new CellAddress(sheet1.Id, 5, 1), new CellAddress(sheet1.Id, 5, 1));
        wb.DefineNamedRange("PlainGlobal", globalRange);

        wb.TryGetNamedRange("PlainGlobal", sheet2.Id, out var found).Should().BeTrue();
        found.Should().Be(globalRange);
    }

    [Fact]
    public void TryGetNamedRange_SheetScopedFormulaOnOtherSheet_DoesNotShadowGlobalRangeHere_SiblingNoRegression()
    {
        // A scoped formula defined on a DIFFERENT sheet must not shadow the global range when
        // resolving in this sheet's context -- the (name, sheet) key must be matched exactly.
        var wb = new Workbook("test");
        var sheet1 = wb.AddSheet("Sheet1");
        var sheet2 = wb.AddSheet("Sheet2");
        var sheet3 = wb.AddSheet("Sheet3");

        var globalRange = new GridRange(new CellAddress(sheet1.Id, 1, 1), new CellAddress(sheet1.Id, 1, 1));
        wb.DefineNamedRange("Foo", globalRange);
        wb.DefineNamedFormula("Foo", "Sheet2!$B$1:$B$3", sheet2.Id);

        // Sheet3 has no local "Foo" definition of any kind -- must resolve the global range.
        wb.TryGetNamedRange("Foo", sheet3.Id, out var found).Should().BeTrue();
        found.Should().Be(globalRange);
    }
}
