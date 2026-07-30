using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// R92-io-defined-name-scope-eval-5-3: trace-precedents reference collection for a
/// <c>NamedRangeNode</c> with an explicit sheet qualifier (e.g. the "Sheet2" in
/// "=SUM(Sheet2!Data)") must resolve against THAT sheet's own defined-name scope, not the
/// formula's own host sheet -- even when the host sheet has its own, differently-defined,
/// same-named local name. Exercised through the real product entry point,
/// <see cref="FormulaAuditingService.GetDirectPrecedents"/>.
/// </summary>
public sealed class R92_FormulaAuditingServiceScopedNameTests
{
    private static (Workbook workbook, Sheet sheet1, Sheet sheet2) BuildTwoSheetsWithOwnLocalData()
    {
        var workbook = new Workbook("test");
        var sheet1 = workbook.AddSheet("Sheet1");
        var sheet2 = workbook.AddSheet("Sheet2");

        // Sheet1's own local "Data": A1:A5.
        workbook.DefineNamedRange(
            "Data",
            new GridRange(new CellAddress(sheet1.Id, 1, 1), new CellAddress(sheet1.Id, 5, 1)),
            null,
            sheet1.Id);

        // Sheet2's own local "Data": B1:B5.
        workbook.DefineNamedRange(
            "Data",
            new GridRange(new CellAddress(sheet2.Id, 1, 2), new CellAddress(sheet2.Id, 5, 2)),
            null,
            sheet2.Id);

        return (workbook, sheet1, sheet2);
    }

    [Fact]
    public void GetDirectPrecedents_SheetQualifiedNameResolvesToQualifiedSheetsOwnLocalDefinition()
    {
        var (workbook, sheet1, sheet2) = BuildTwoSheetsWithOwnLocalData();
        var formulaAddress = new CellAddress(sheet1.Id, 10, 1);
        sheet1.SetCell(formulaAddress, Cell.FromFormula("SUM(Sheet2!Data)"));

        var precedents = FormulaAuditingService.GetDirectPrecedents(workbook, formulaAddress);

        precedents.Should().Equal(
            new CellAddress(sheet2.Id, 1, 2),
            new CellAddress(sheet2.Id, 2, 2),
            new CellAddress(sheet2.Id, 3, 2),
            new CellAddress(sheet2.Id, 4, 2),
            new CellAddress(sheet2.Id, 5, 2));
    }

    [Fact]
    public void GetDirectPrecedentRegions_SheetQualifiedNameResolvesToQualifiedSheetsOwnLocalDefinition()
    {
        var (workbook, sheet1, sheet2) = BuildTwoSheetsWithOwnLocalData();
        var formulaAddress = new CellAddress(sheet1.Id, 10, 1);
        sheet1.SetCell(formulaAddress, Cell.FromFormula("SUM(Sheet2!Data)"));

        var regions = FormulaAuditingService.GetDirectPrecedentRegions(workbook, formulaAddress);

        regions.Should().Equal(new GridRange(
            new CellAddress(sheet2.Id, 1, 2),
            new CellAddress(sheet2.Id, 5, 2)));
    }

    /// <summary>No-regression sibling: an UNqualified name reference must still resolve against
    /// the formula's own host sheet, exactly as before this fix.</summary>
    [Fact]
    public void GetDirectPrecedents_UnqualifiedNameStillResolvesAgainstHostSheetsOwnLocalDefinition()
    {
        var (workbook, sheet1, _) = BuildTwoSheetsWithOwnLocalData();
        var formulaAddress = new CellAddress(sheet1.Id, 10, 1);
        sheet1.SetCell(formulaAddress, Cell.FromFormula("SUM(Data)"));

        var precedents = FormulaAuditingService.GetDirectPrecedents(workbook, formulaAddress);

        precedents.Should().Equal(
            new CellAddress(sheet1.Id, 1, 1),
            new CellAddress(sheet1.Id, 2, 1),
            new CellAddress(sheet1.Id, 3, 1),
            new CellAddress(sheet1.Id, 4, 1),
            new CellAddress(sheet1.Id, 5, 1));
    }
}
