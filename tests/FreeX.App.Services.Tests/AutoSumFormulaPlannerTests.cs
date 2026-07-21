using FluentAssertions;
using FreeX.App.Services;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

public sealed class AutoSumFormulaPlannerTests
{
    public static TheoryData<string> AggregateFunctions => new()
    {
        "SUM",
        "AVERAGE",
        "COUNT",
        "COUNTA",
        "MAX",
        "MIN"
    };

    [Theory]
    [MemberData(nameof(AggregateFunctions))]
    public void TryCreatePlan_PlacesAggregateBelowVerticalSelection(string functionName)
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var selection = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 1));

        AutoSumFormulaPlanner.TryCreatePlan(sheet, functionName, selection, out var plan)
            .Should()
            .BeTrue();

        plan.Target.Should().Be(new CellAddress(sheet.Id, 4, 1));
        plan.Formula.Should().Be($"{functionName}(A1:A3)");
    }

    [Fact]
    public void TryCreatePlan_PlacesAggregateToRightOfHorizontalSelection()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var selection = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 3));

        AutoSumFormulaPlanner.TryCreatePlan(sheet, "SUM", selection, out var plan)
            .Should()
            .BeTrue();

        plan.Target.Should().Be(new CellAddress(sheet.Id, 1, 4));
        plan.Formula.Should().Be("SUM(A1:C1)");
    }

    [Fact]
    public void TryCreatePlan_SingleCellInfersContiguousNumbersAbove()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(20));
        var selection = new GridRange(new CellAddress(sheet.Id, 3, 1), new CellAddress(sheet.Id, 3, 1));

        AutoSumFormulaPlanner.TryCreatePlan(sheet, "MAX", selection, out var plan)
            .Should()
            .BeTrue();

        plan.Target.Should().Be(new CellAddress(sheet.Id, 3, 1));
        plan.Formula.Should().Be("MAX(A1:A2)");
    }

    [Fact]
    public void TryCreatePlan_ReturnsFalseWhenSelectionTargetWouldExceedWorksheet()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var selection = new GridRange(
            new CellAddress(sheet.Id, CellAddress.MaxRow - 1, 1),
            new CellAddress(sheet.Id, CellAddress.MaxRow, 1));

        AutoSumFormulaPlanner.TryCreatePlan(sheet, "SUM", selection, out _)
            .Should()
            .BeFalse();
    }

    [Fact]
    public void BuildFormula_UsesContiguousNumbersAboveTheTargetCell()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 2, 3), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 3), new NumberValue(20));

        AutoSumFormulaPlanner.BuildFormula(sheet, "SUM", new CellAddress(sheet.Id, 4, 3))
            .Should()
            .Be("SUM(C2:C3)");
    }

    [Fact]
    public void BuildFormula_FallsBackToContiguousNumbersOnTheLeft()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 5, 1), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 5, 2), new NumberValue(20));

        AutoSumFormulaPlanner.BuildFormula(sheet, "AVERAGE", new CellAddress(sheet.Id, 5, 3))
            .Should()
            .Be("AVERAGE(A5:B5)");
    }

    [Fact]
    public void BuildFormula_UsesExcelFallbackRangeWhenNoAdjacentNumbersExist()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");

        AutoSumFormulaPlanner.BuildFormula(sheet, "COUNT", new CellAddress(sheet.Id, 1, 2))
            .Should()
            .Be("COUNT(B1:B1)");
    }

    [Fact]
    public void BuildFormula_StopsUpwardScanAtPreExistingSumRow_DoesNotDoubleCountDataAbove()
    {
        // R50-commands-autosum-quickanalysis-3-1: B2=10, B3=20, B4=30, B5=SUM(B2:B4) (a subtotal the
        // user already entered), B6=5, B7=15. AutoSum from B8 must use B5's subtotal row as the upper
        // boundary (=SUM(B6:B7)) instead of walking straight through it and re-summing B2:B4 again.
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 2), new NumberValue(30));
        var subtotalCell = Cell.FromFormula("SUM(B2:B4)");
        subtotalCell.Value = new NumberValue(60);
        sheet.SetCell(new CellAddress(sheet.Id, 5, 2), subtotalCell);
        sheet.SetCell(new CellAddress(sheet.Id, 6, 2), new NumberValue(5));
        sheet.SetCell(new CellAddress(sheet.Id, 7, 2), new NumberValue(15));

        AutoSumFormulaPlanner.BuildFormula(sheet, "SUM", new CellAddress(sheet.Id, 8, 2))
            .Should()
            .Be("SUM(B6:B7)", "AutoSum must stop at the existing subtotal row rather than re-summing B2:B4");
    }

    [Fact]
    public void BuildFormula_WithoutPreExistingSubtotal_StillSumsTheFullContiguousBlock()
    {
        // Sibling no-regression case: when there is no aggregate formula in the column above, the
        // walk must still climb through the entire contiguous run of numbers, unaffected by the
        // new subtotal-row boundary check.
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 2), new NumberValue(30));
        sheet.SetCell(new CellAddress(sheet.Id, 5, 2), new NumberValue(60));
        sheet.SetCell(new CellAddress(sheet.Id, 6, 2), new NumberValue(5));
        sheet.SetCell(new CellAddress(sheet.Id, 7, 2), new NumberValue(15));

        AutoSumFormulaPlanner.BuildFormula(sheet, "SUM", new CellAddress(sheet.Id, 8, 2))
            .Should()
            .Be("SUM(B2:B7)", "with no pre-existing aggregate in the column, the full contiguous block is summed");
    }

    [Fact]
    public void BuildFormula_StopsLeftwardScanAtPreExistingSumColumn_DoesNotDoubleCountDataToTheLeft()
    {
        // R62-commands-autosum-6-2: Row 5: B5=10, C5=20, D5=SUM(B5:C5) (a pre-existing running
        // subtotal), E5=5, F5=15, G5 is empty with nothing above it. AutoSum from G5 must use D5's
        // subtotal as the left boundary (=SUM(E5:F5)) instead of walking straight through it and
        // re-summing B5:C5 again (which would wrongly produce SUM(B5:F5) = 80 instead of 20).
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 5, 2), new NumberValue(10)); // B5
        sheet.SetCell(new CellAddress(sheet.Id, 5, 3), new NumberValue(20)); // C5
        var subtotalCell = Cell.FromFormula("SUM(B5:C5)");
        subtotalCell.Value = new NumberValue(30);
        sheet.SetCell(new CellAddress(sheet.Id, 5, 4), subtotalCell); // D5
        sheet.SetCell(new CellAddress(sheet.Id, 5, 5), new NumberValue(5)); // E5
        sheet.SetCell(new CellAddress(sheet.Id, 5, 6), new NumberValue(15)); // F5

        AutoSumFormulaPlanner.BuildFormula(sheet, "SUM", new CellAddress(sheet.Id, 5, 7))
            .Should()
            .Be("SUM(E5:F5)", "AutoSum must stop at the existing subtotal column rather than re-summing B5:C5");
    }

    [Fact]
    public void BuildFormula_LeftwardScanWithoutPreExistingSubtotal_StillSumsTheFullContiguousRow()
    {
        // Sibling no-regression case: with no aggregate formula in the row to the left, the
        // leftward walk must still climb through the entire contiguous run of numbers.
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 5, 2), new NumberValue(10)); // B5
        sheet.SetCell(new CellAddress(sheet.Id, 5, 3), new NumberValue(20)); // C5
        sheet.SetCell(new CellAddress(sheet.Id, 5, 4), new NumberValue(30)); // D5
        sheet.SetCell(new CellAddress(sheet.Id, 5, 5), new NumberValue(5)); // E5
        sheet.SetCell(new CellAddress(sheet.Id, 5, 6), new NumberValue(15)); // F5

        AutoSumFormulaPlanner.BuildFormula(sheet, "SUM", new CellAddress(sheet.Id, 5, 7))
            .Should()
            .Be("SUM(B5:F5)", "with no pre-existing aggregate in the row, the full contiguous block is summed");
    }

    [Fact]
    public void TryCreatePlan_MultiCellSelectionWithBlankTrailingCell_FillsTheBlankCellInPlace()
    {
        // R62-commands-autosum-6-3: A1=10, A2=20, A3=30, A4 is blank. Selecting A1:A4 (numbers
        // plus a trailing blank cell -- the classic Excel AutoSum workflow) and pressing Alt+=
        // must fill =SUM(A1:A3) into A4 itself, not append a new formula to A5.
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(30));
        var selection = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 1));

        AutoSumFormulaPlanner.TryCreatePlan(sheet, "SUM", selection, out var plan)
            .Should()
            .BeTrue();

        plan.Target.Should().Be(new CellAddress(sheet.Id, 4, 1));
        plan.Formula.Should().Be("SUM(A1:A3)");
    }

    [Fact]
    public void TryCreatePlan_MultiCellSelectionWithoutBlankTrailingCell_StillAppendsBelowSelection()
    {
        // Sibling no-regression case: when the selection's own trailing cell already has data (no
        // blank cell to fill in place), AutoSum keeps appending the aggregate one row past the
        // selection, exactly as before this fix.
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(30));
        var selection = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 1));

        AutoSumFormulaPlanner.TryCreatePlan(sheet, "SUM", selection, out var plan)
            .Should()
            .BeTrue();

        plan.Target.Should().Be(new CellAddress(sheet.Id, 4, 1));
        plan.Formula.Should().Be("SUM(A1:A3)");
    }
}
