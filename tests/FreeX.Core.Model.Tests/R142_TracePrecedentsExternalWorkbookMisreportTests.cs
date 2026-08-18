using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// Regression tests for finding trace-precedents-external-workbook-misreport (round 142):
/// FormulaAuditingService.ResolveSheet only resolves sheet names against the CURRENT workbook's
/// own sheet list, so a formula referencing another workbook via the bracketed
/// <c>'[Book.xlsx]Sheet'!A1</c> syntax is silently dropped from GetDirectPrecedents /
/// GetDirectPrecedentRegions -- a formula that genuinely has a precedent can report an EMPTY
/// precedent list, which callers (WPF's TracePrecedentsForCell, Avalonia's TraceFormulaPrecedents)
/// read as "no direct precedents", a false statement. HasExternalPrecedentReference lets callers
/// detect that case instead of silently misreporting it.
/// </summary>
public sealed class R142_TracePrecedentsExternalWorkbookMisreportTests
{
    [Fact]
    public void HasExternalPrecedentReference_TrueForQuotedExternalWorkbookCellReference()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var formulaAddress = new CellAddress(sheet.Id, 1, 3);
        sheet.SetCell(formulaAddress, Cell.FromFormula("'[Budget.xlsx]Sheet1'!A1"));

        // The core misreport: the formula genuinely has a precedent, but GetDirectPrecedents can't
        // represent a cell in another workbook, so it comes back empty.
        FormulaAuditingService.GetDirectPrecedents(wb, formulaAddress).Should().BeEmpty(
            "GetDirectPrecedentRegions/GetDirectPrecedents cannot address a cell outside this workbook");

        // The new flag must say the empty list is NOT "no precedents at all".
        FormulaAuditingService.HasExternalPrecedentReference(wb, formulaAddress).Should().BeTrue(
            "the formula's only precedent lives in another workbook and must not be reported as " +
            "'no direct precedents'");
    }

    [Fact]
    public void HasExternalPrecedentReference_TrueForQuotedExternalWorkbookRangeReference()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var formulaAddress = new CellAddress(sheet.Id, 1, 3);
        sheet.SetCell(formulaAddress, Cell.FromFormula("SUM('[Budget.xlsx]Sheet1'!A1:A5)"));

        FormulaAuditingService.GetDirectPrecedents(wb, formulaAddress).Should().BeEmpty();
        FormulaAuditingService.HasExternalPrecedentReference(wb, formulaAddress).Should().BeTrue();
    }

    [Fact]
    public void HasExternalPrecedentReference_TrueWhenFormulaCombinesLocalAndExternalReferences()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var formulaAddress = new CellAddress(sheet.Id, 1, 3);
        var localPrecedent = new CellAddress(sheet.Id, 2, 2);
        sheet.SetCell(formulaAddress, Cell.FromFormula("B2+'[Budget.xlsx]Sheet1'!A1"));

        // The local half of the reference must still resolve exactly as before -- the new flag is
        // additive, it must not change GetDirectPrecedents' existing per-cell contract.
        FormulaAuditingService.GetDirectPrecedents(wb, formulaAddress).Should().Equal(localPrecedent);

        // But the external half must still be detected even though the cell isn't reported empty.
        FormulaAuditingService.HasExternalPrecedentReference(wb, formulaAddress).Should().BeTrue(
            "the formula also references an external workbook even though it has a local precedent too");
    }

    [Fact]
    public void HasExternalPrecedentReference_FalseForOrdinaryLocalFormula()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var formulaAddress = new CellAddress(sheet.Id, 1, 3);
        sheet.SetCell(formulaAddress, Cell.FromFormula("A1+B1"));

        FormulaAuditingService.HasExternalPrecedentReference(wb, formulaAddress).Should().BeFalse();
    }

    [Fact]
    public void HasExternalPrecedentReference_FalseForOrdinaryCrossSheetLocalFormula()
    {
        // Sibling/no-regression check: an ordinary same-workbook cross-sheet reference (no
        // brackets) must NOT be misclassified as external -- only the bracketed
        // '[Book.xlsx]Sheet'-shaped syntax counts.
        var wb = new Workbook("test");
        var sheet1 = wb.AddSheet("Sheet1");
        var sheet2 = wb.AddSheet("Sheet2");
        var formulaAddress = new CellAddress(sheet1.Id, 1, 3);
        sheet2.SetCell(new CellAddress(sheet2.Id, 3, 3), new NumberValue(7));
        sheet1.SetCell(formulaAddress, Cell.FromFormula("Sheet2!C3"));

        FormulaAuditingService.HasExternalPrecedentReference(wb, formulaAddress).Should().BeFalse();
        FormulaAuditingService.GetDirectPrecedents(wb, formulaAddress).Should().Equal(
            new CellAddress(sheet2.Id, 3, 3));
    }

    [Fact]
    public void HasExternalPrecedentReference_FalseWhenCellHasNoFormula()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var formulaAddress = new CellAddress(sheet.Id, 1, 3);
        sheet.SetCell(formulaAddress, new NumberValue(5));

        FormulaAuditingService.HasExternalPrecedentReference(wb, formulaAddress).Should().BeFalse();
    }

    [Fact]
    public void GetDirectPrecedents_UnaffectedRegressionCheck_StillResolvesRefsRangesCrossSheetAndNamedRanges()
    {
        // Guards that adding HasExternalPrecedentReference did not disturb the neighbouring
        // CollectReferences/ResolveSheet logic GetDirectPrecedents depends on.
        var wb = new Workbook("test");
        var sheet1 = wb.AddSheet("Sheet1");
        var sheet2 = wb.AddSheet("Sheet2");
        var formulaAddress = new CellAddress(sheet1.Id, 5, 1);
        var namedStart = new CellAddress(sheet1.Id, 10, 1);
        var namedEnd = new CellAddress(sheet1.Id, 11, 1);
        wb.DefineNamedRange("Rates", new GridRange(namedStart, namedEnd));

        sheet1.SetCell(formulaAddress, Cell.FromFormula("SUM(A1:B2,Sheet2!C3,Rates)"));

        var precedents = FormulaAuditingService.GetDirectPrecedents(wb, formulaAddress);

        precedents.Should().Equal(
            new CellAddress(sheet1.Id, 1, 1),
            new CellAddress(sheet1.Id, 1, 2),
            new CellAddress(sheet1.Id, 2, 1),
            new CellAddress(sheet1.Id, 2, 2),
            namedStart,
            namedEnd,
            new CellAddress(sheet2.Id, 3, 3));
    }
}
