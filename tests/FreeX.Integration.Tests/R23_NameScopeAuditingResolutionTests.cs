using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Integration.Tests;

/// <summary>
/// R23-name-scope-resolution-3: ExtractPrecedents/CollectReferences (References.cs) and the
/// aggregate-range error-chain resolver (Errors.cs) used to resolve NamedRangeNode via the
/// sheetId-less Workbook.TryGetNamedRange(string, out GridRange) overload, which only checks the
/// workbook-global NamedRanges dictionary and never sees ScopedNamedRanges/ScopedNamedFormulas.
/// A name defined ONLY as a sheet-scoped named range therefore drew no precedent arrow at all,
/// and the aggregate "omits adjacent cells" audit silently missed it too. Both call sites now use
/// the sheet-scope-aware 3-argument overload (mirroring RecalcEngine.CollectReferences's
/// NamedRangeNode handling), guarded so a same-named sheet-scoped named FORMULA still shadows a
/// workbook-global named RANGE rather than falling through to the wrong global range.
/// </summary>
public sealed class R23_NameScopeAuditingResolutionTests
{
    [Fact]
    public void GetDirectPrecedents_ResolvesSheetScopedOnlyNamedRange()
    {
        var wb = new Workbook("test");
        var sheet1 = wb.AddSheet("Sheet1");
        var sheet2 = wb.AddSheet("Sheet2");

        var namedStart = new CellAddress(sheet2.Id, 10, 1);
        var namedEnd = new CellAddress(sheet2.Id, 11, 1);
        // "MyName" is defined ONLY as a sheet-scoped name on Sheet2 -- no workbook-global entry.
        wb.DefineNamedRange("MyName", new GridRange(namedStart, namedEnd), metadata: null, scopeSheetId: sheet2.Id);

        var formulaAddress = new CellAddress(sheet2.Id, 5, 1);
        sheet2.SetCell(formulaAddress, Cell.FromFormula("MyName+1"));

        var precedents = FormulaAuditingService.GetDirectPrecedents(wb, formulaAddress);

        precedents.Should().Equal(namedStart, namedEnd);

        // Sanity: a formula on the OTHER sheet referencing the same bare name must not resolve
        // it (it is out of scope there and there is no global fallback), confirming this isn't
        // accidentally passing via some other unscoped lookup.
        var otherSheetFormulaAddress = new CellAddress(sheet1.Id, 5, 1);
        sheet1.SetCell(otherSheetFormulaAddress, Cell.FromFormula("MyName+1"));
        FormulaAuditingService.GetDirectPrecedents(wb, otherSheetFormulaAddress).Should().BeEmpty();
    }

    [Fact]
    public void GetDirectPrecedents_SheetScopedNamedFormulaShadowsSameNamedGlobalRange()
    {
        var wb = new Workbook("test");
        var sheet1 = wb.AddSheet("Sheet1");
        var sheet2 = wb.AddSheet("Sheet2");

        var globalStart = new CellAddress(sheet1.Id, 1, 1);
        var globalEnd = new CellAddress(sheet1.Id, 1, 1);
        wb.DefineNamedRange("Shadowed", new GridRange(globalStart, globalEnd));

        // A sheet-scoped named FORMULA of the identical name shadows the workbook-global range
        // on Sheet2, so a formula there referencing "Shadowed" must not resolve to the global
        // range's cell (A1 on Sheet1).
        wb.DefineNamedFormula("Shadowed", "B2", sheet2.Id);

        var formulaAddress = new CellAddress(sheet2.Id, 5, 1);
        sheet2.SetCell(formulaAddress, Cell.FromFormula("Shadowed+1"));

        FormulaAuditingService.GetDirectPrecedents(wb, formulaAddress).Should().BeEmpty();
    }

    [Fact]
    public void FindFormulaErrorIssues_DetectsOmittedAdjacentCellForSheetScopedNamedRangeAggregateArgument()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(30));
        // Value immediately below the named range that the audit should flag as omitted.
        sheet.SetCell(new CellAddress(sheet.Id, 4, 2), new NumberValue(40));

        // "MyRange" is defined ONLY as a sheet-scoped name on Sheet1 -- no workbook-global entry.
        wb.DefineNamedRange(
            "MyRange",
            new GridRange(new CellAddress(sheet.Id, 1, 2), new CellAddress(sheet.Id, 3, 2)),
            metadata: null,
            scopeSheetId: sheet.Id);

        sheet.SetCell(new CellAddress(sheet.Id, 5, 2), Cell.FromFormula("SUM(MyRange)"));

        var issue = FormulaAuditingService.FindFormulaErrorIssues(wb, sheet.Id)
            .Should().ContainSingle(i => i.ErrorCode == FormulaAuditingService.FormulaOmitsAdjacentCellsErrorCode).Subject;

        issue.Cell.Should().Be("B5");
        issue.FormulaText.Should().Be("=SUM(MyRange)");
    }
}
