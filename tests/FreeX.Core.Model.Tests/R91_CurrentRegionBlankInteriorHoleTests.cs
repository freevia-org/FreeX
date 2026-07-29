using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// R91-calc-selection-semantics-5-1: Current Region (Ctrl+A / Ctrl+Shift+8) must not bail out
/// to null just because the ACTIVE cell itself is blank. Excel's CurrentRegion is a purely
/// geometric notion -- the block bounded by fully blank rows/columns -- so a blank "hole" nested
/// inside a solid data block still expands to the surrounding block.
/// </summary>
public sealed class R91_CurrentRegionBlankInteriorHoleTests
{
    [Fact]
    public void GetCurrentRegion_FromBlankHoleInsideDataBlock_ExpandsToWholeBlock()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");

        // A1:C3 fully populated except B2, which is never written (a true "hole": GetCell
        // returns null there, not a stored BlankValue).
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(1));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new NumberValue(2));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new NumberValue(3));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(4));
        // (2,2) == B2 intentionally left unset.
        sheet.SetCell(new CellAddress(sheet.Id, 2, 3), new NumberValue(6));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(7));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(8));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 3), new NumberValue(9));

        var activeCell = new CellAddress(sheet.Id, 2, 2);
        sheet.GetCell(activeCell).Should().BeNull("B2 was never written -- this is the 'hole' scenario");

        var region = SelectionRangeService.GetCurrentRegion(sheet, activeCell);

        region.Should().Be(new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 3, 3)));
    }

    [Fact]
    public void GetCurrentRegion_FromFilledCellInSameBlock_StillExpandsToWholeBlock()
    {
        // No-regression sibling: starting from a FILLED cell in the same block must still
        // produce the identical region as starting from the blank hole above.
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(1));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new NumberValue(2));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new NumberValue(3));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(4));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 3), new NumberValue(6));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(7));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(8));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 3), new NumberValue(9));

        var region = SelectionRangeService.GetCurrentRegion(
            sheet,
            new CellAddress(sheet.Id, 1, 1));

        region.Should().Be(new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 3, 3)));
    }
}
