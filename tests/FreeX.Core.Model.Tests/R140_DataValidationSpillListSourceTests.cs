using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// R140-DV-1: a List data-validation rule whose Source range/name overlaps a dynamic-array
/// spill's non-anchor member cells (which live only in Sheet._spillValues, never in
/// Sheet._cells -- see Sheet.SetSpillRange) was spill-blind: the dropdown showed blank items for
/// every spilled cell after the anchor, and typing/selecting one of the real spilled values was
/// rejected as invalid, because DataValidationService.ListSources.cs's range readers
/// (RangeListItems indexer, RangeContainsValue) called Sheet.GetCell(row, col)?.Value (which only
/// looks in Sheet._cells) instead of the spill-aware Sheet.GetValue(row, col).
///
/// Covers both real product entry points that resolve a List source
/// (<see cref="DataValidationService.GetListItems"/> for the dropdown and the 4-arg
/// <see cref="DataValidationService.Validate"/> for entry acceptance/rejection), a literal
/// same-sheet range source, a defined-name source, the A1# spill-anchor-operator source (which
/// already routed through the spill-aware FormulaEvaluator before this fix, via a different code
/// path -- covered here as a non-regression sibling), and a plain static range with no spill
/// involved at all (also a non-regression sibling).
/// </summary>
public sealed class R140_DataValidationSpillListSourceTests
{
    /// <summary>Anchors a UNIQUE-shaped spill at A2:A5 = 10,20,30,40 the same way
    /// R22_DataValidationSpillAndListErrorTests does: SetFormula + manual SetSpillRange, which
    /// exercises the real Sheet spill-overlay storage (Sheet._spillValues) without needing a live
    /// recalc engine.</summary>
    private static (Workbook workbook, Sheet sheet, CellAddress anchor) BuildSheetWithSpilledList()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var anchor = new CellAddress(sheet.Id, 2, 1); // A2

        sheet.SetFormula(anchor, "UNIQUE(B2:B10)");
        sheet.GetCell(anchor)!.Value = new NumberValue(10);
        var spillCells = new ScalarValue[4, 1]
        {
            { new NumberValue(10) },
            { new NumberValue(20) },
            { new NumberValue(30) },
            { new NumberValue(40) },
        };
        sheet.SetSpillRange(anchor, new RangeValue(spillCells)); // spills A2:A5 = 10,20,30,40

        return (workbook, sheet, anchor);
    }

    [Fact]
    public void GetListItems_LiteralRangeSourceOverlappingSpill_ReturnsRealSpilledValuesNotBlanks()
    {
        var (workbook, sheet, _) = BuildSheetWithSpilledList();
        var target = new CellAddress(sheet.Id, 1, 4); // D1
        var dv = new DataValidation
        {
            Type = DvType.List,
            Formula1 = "=$A$2:$A$5",
            AppliesTo = new GridRange(target, target),
        };

        var items = DataValidationService.GetListItems(dv, sheet, target, workbook);

        items.Should().Equal(
            new[] { "10", "20", "30", "40" },
            "every cell in the source range must read its real value, including the three spill " +
            "member cells (A3:A5) that live only in the spill overlay, not Sheet._cells");
    }

    [Fact]
    public void Validate_LiteralRangeSourceOverlappingSpill_AcceptsSpilledMemberValue()
    {
        var (workbook, sheet, _) = BuildSheetWithSpilledList();
        var target = new CellAddress(sheet.Id, 1, 4); // D1
        var dv = new DataValidation
        {
            Type = DvType.List,
            Formula1 = "=$A$2:$A$5",
            AppliesTo = new GridRange(target, target),
            ErrorMessage = "No match",
        };

        // 30 lives at A4, a spill member cell (not the formula anchor A2) -- it must be accepted.
        DataValidationService.Validate(dv, new NumberValue(30), sheet, target, workbook)
            .Should().BeNull("30 is visibly present in the A2:A5 source range (as a spill member)");
    }

    [Fact]
    public void GetListItems_NamedRangeSourceOverlappingSpill_ReturnsRealSpilledValues()
    {
        var (workbook, sheet, anchor) = BuildSheetWithSpilledList();
        workbook.DefineNamedRange(
            "SpillSource",
            new GridRange(anchor, new CellAddress(sheet.Id, 5, 1)), // A2:A5
            null,
            sheet.Id);
        var target = new CellAddress(sheet.Id, 1, 4); // D1
        var dv = new DataValidation
        {
            Type = DvType.List,
            Formula1 = "=SpillSource",
            AppliesTo = new GridRange(target, target),
        };

        var items = DataValidationService.GetListItems(dv, sheet, target, workbook);

        items.Should().Equal("10", "20", "30", "40");
    }

    /// <summary>Non-regression sibling: the A1# spill-anchor-operator source already resolved
    /// through the spill-aware FormulaEvaluator (a different code path -- ANCHORARRAY is not a
    /// RangeRefNode/NamedRangeNode, so it never took the buggy GetCell-based fast paths this fix
    /// touches) and must keep working the same way after this fix.</summary>
    [Fact]
    public void GetListItems_SpillAnchorOperatorSource_ReturnsRealSpilledValues()
    {
        var (workbook, sheet, _) = BuildSheetWithSpilledList();
        var target = new CellAddress(sheet.Id, 1, 4); // D1
        var dv = new DataValidation
        {
            Type = DvType.List,
            Formula1 = "=A2#",
            AppliesTo = new GridRange(target, target),
        };

        var items = DataValidationService.GetListItems(dv, sheet, target, workbook);

        items.Should().Equal("10", "20", "30", "40");
    }

    /// <summary>Non-regression sibling: a plain static range with no spill involved at all must
    /// keep behaving exactly as before this fix, for both the dropdown and validation entry
    /// points.</summary>
    [Fact]
    public void ListSource_PlainStaticRangeWithNoSpill_StillWorksForDropdownAndValidation()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), Cell.FromValue(new TextValue("Red")));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), Cell.FromValue(new TextValue("Green")));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), Cell.FromValue(new TextValue("Blue")));
        var target = new CellAddress(sheet.Id, 1, 4); // D1
        var dv = new DataValidation
        {
            Type = DvType.List,
            Formula1 = "=$A$1:$A$3",
            AppliesTo = new GridRange(target, target),
            ErrorMessage = "No match",
        };

        DataValidationService.GetListItems(dv, sheet, target, workbook)
            .Should().Equal("Red", "Green", "Blue");

        DataValidationService.Validate(dv, new TextValue("Green"), sheet, target, workbook)
            .Should().BeNull();

        DataValidationService.Validate(dv, new TextValue("Purple"), sheet, target, workbook)
            .Should().Be("No match");
    }
}
