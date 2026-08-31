using System.Diagnostics;
using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

public sealed partial class FormulaAuditingServiceTests
{
    [Fact]
    public void GetDirectPrecedents_ReturnsCellsFromRefsRangesCrossSheetRefsAndNamedRanges()
    {
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

    [Fact]
    public void GetPrecedentTraceArrows_ReturnsMultiLevelFormulaChain()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);
        var c1 = new CellAddress(sheet.Id, 1, 3);
        sheet.SetCell(a1, new NumberValue(5));
        sheet.SetCell(b1, Cell.FromFormula("A1+1"));
        sheet.SetCell(c1, Cell.FromFormula("B1*2"));

        var arrows = FormulaAuditingService.GetPrecedentTraceArrows(wb, c1);

        arrows.Should().Equal(
            new FormulaTraceArrow(b1, c1, FormulaTraceArrowKind.Precedent),
            new FormulaTraceArrow(a1, b1, FormulaTraceArrowKind.Precedent));
    }

    [Fact]
    public void GetDirectDependents_ReturnsFormulaCellsThatReferenceAddress()
    {
        var wb = new Workbook("test");
        var sheet1 = wb.AddSheet("Sheet1");
        var sheet2 = wb.AddSheet("Sheet2");
        var target = new CellAddress(sheet1.Id, 2, 1);
        var localDependent = new CellAddress(sheet1.Id, 1, 2);
        var rangeDependent = new CellAddress(sheet1.Id, 4, 1);
        var crossSheetDependent = new CellAddress(sheet2.Id, 1, 1);

        sheet1.SetCell(localDependent, Cell.FromFormula("A2*2"));
        sheet1.SetCell(rangeDependent, Cell.FromFormula("SUM(A1:A3)"));
        sheet2.SetCell(crossSheetDependent, Cell.FromFormula("Sheet1!A2"));
        sheet2.SetCell(new CellAddress(sheet2.Id, 2, 1), Cell.FromFormula("Sheet1!A3"));

        var dependents = FormulaAuditingService.GetDirectDependents(wb, target);

        dependents.Should().Equal(localDependent, rangeDependent, crossSheetDependent);
    }

    [Fact]
    public void GetDirectDependents_ReturnsFormulaCellsWithAbsoluteLocalReferences()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var target = new CellAddress(sheet.Id, 2, 1);
        var dependent = new CellAddress(sheet.Id, 5, 3);

        sheet.SetCell(dependent, Cell.FromFormula("$A$2*2"));

        FormulaAuditingService.GetDirectDependents(wb, target)
            .Should()
            .Equal(dependent);
    }

    [Fact]
    public void GetDirectDependents_ForRangeReturnsFormulaCellsReferencingAnyCellInRange()
    {
        var wb = new Workbook("test");
        var sheet1 = wb.AddSheet("Sheet1");
        var sheet2 = wb.AddSheet("Sheet2");
        var watchedRange = new GridRange(
            new CellAddress(sheet1.Id, 2, 2),
            new CellAddress(sheet1.Id, 4, 3));
        var localDependent = new CellAddress(sheet1.Id, 6, 1);
        var crossSheetDependent = new CellAddress(sheet2.Id, 1, 1);
        var outsideDependent = new CellAddress(sheet1.Id, 7, 1);

        sheet1.SetCell(localDependent, Cell.FromFormula("SUM(B2:C4)"));
        sheet2.SetCell(crossSheetDependent, Cell.FromFormula("Sheet1!C3*2"));
        sheet1.SetCell(outsideDependent, Cell.FromFormula("SUM(D2:D4)"));

        FormulaAuditingService.GetDirectDependents(wb, watchedRange)
            .Should()
            .Equal(localDependent, crossSheetDependent);
    }

    [Fact]
    public void GetDirectDependents_CrossSheetLargeRangeUsesRegionOverlapWithoutPerCellAllocation()
    {
        const uint referencedRows = 100_000;
        var wb = new Workbook("test");
        var source = wb.AddSheet("Source");
        var formulas = wb.AddSheet("Formulas");
        var dependent = new CellAddress(formulas.Id, 1, 1);
        formulas.SetCell(dependent, Cell.FromFormula($"SUM(Source!A1:A{referencedRows})"));

        var target = new CellAddress(source.Id, referencedRows, 1);
        FormulaAuditingService.GetDirectDependents(wb, target).Should().Equal(dependent);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var actual = FormulaAuditingService.GetDirectDependents(wb, target);
        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

        actual.Should().Equal(dependent);
        allocatedBytes.Should().BeLessThan(
            1_000_000,
            "dependent matching should compare the referenced region directly instead of " +
            "materializing every address in the 100,000-cell range");
    }

    [Fact]
    public void GetDirectDependents_SourceGuardUsesPrecedentRegionsForFallbackMatching()
    {
        var source = ModelSourceTestSupport.ReadCommandsSource("FormulaAuditingService.cs");

        source.Should().Contain(
            "var precedentRegions = ExtractPrecedentRegions(workbook, sheet.Id, cell.FormulaText);");
        source.Should().Contain("if (OverlapsAny(precedentRegions, precedentRange))");
    }

    [Fact]
    public void GetDependentTraceArrows_ReturnsMultiLevelFormulaChain()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);
        var c1 = new CellAddress(sheet.Id, 1, 3);
        sheet.SetCell(a1, new NumberValue(5));
        sheet.SetCell(b1, Cell.FromFormula("A1+1"));
        sheet.SetCell(c1, Cell.FromFormula("B1*2"));

        var arrows = FormulaAuditingService.GetDependentTraceArrows(wb, a1);

        arrows.Should().Equal(
            new FormulaTraceArrow(a1, b1, FormulaTraceArrowKind.Dependent),
            new FormulaTraceArrow(b1, c1, FormulaTraceArrowKind.Dependent));
    }

    [Fact]
    public void FormulaTraceArrowPlanner_PrecedentsExpandOneLevelPerRibbonInvocation()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);
        var c1 = new CellAddress(sheet.Id, 1, 3);
        sheet.SetCell(a1, new NumberValue(5));
        sheet.SetCell(b1, Cell.FromFormula("A1+1"));
        sheet.SetCell(c1, Cell.FromFormula("B1*2"));

        var firstClick = FormulaTraceArrowPlanner.GetNextPrecedentTraceArrows(wb, c1, []);
        var secondClick = FormulaTraceArrowPlanner.GetNextPrecedentTraceArrows(wb, c1, firstClick);
        var allArrows = firstClick.Concat(secondClick).ToList();

        firstClick.Should().Equal(new FormulaTraceArrow(b1, c1, FormulaTraceArrowKind.Precedent));
        secondClick.Should().Equal(new FormulaTraceArrow(a1, b1, FormulaTraceArrowKind.Precedent));
        FormulaTraceArrowPlanner.GetNextPrecedentTraceArrows(wb, c1, allArrows)
            .Should()
            .BeEmpty();
    }

    [Fact]
    public void FormulaTraceArrowPlanner_DependentsExpandOneLevelPerRibbonInvocation()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);
        var c1 = new CellAddress(sheet.Id, 1, 3);
        sheet.SetCell(a1, new NumberValue(5));
        sheet.SetCell(b1, Cell.FromFormula("A1+1"));
        sheet.SetCell(c1, Cell.FromFormula("B1*2"));

        var firstClick = FormulaTraceArrowPlanner.GetNextDependentTraceArrows(wb, a1, []);
        var secondClick = FormulaTraceArrowPlanner.GetNextDependentTraceArrows(wb, a1, firstClick);
        var allArrows = firstClick.Concat(secondClick).ToList();

        firstClick.Should().Equal(new FormulaTraceArrow(a1, b1, FormulaTraceArrowKind.Dependent));
        secondClick.Should().Equal(new FormulaTraceArrow(b1, c1, FormulaTraceArrowKind.Dependent));
        FormulaTraceArrowPlanner.GetNextDependentTraceArrows(wb, a1, allArrows)
            .Should()
            .BeEmpty();
    }
}
