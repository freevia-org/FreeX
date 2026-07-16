using FreeX.Core.Model;
using FluentAssertions;
using Xunit;

namespace FreeX.Core.Formula.Tests;

// R44-formula-array-spill-3-1: Sheet.IsSpillBlocked's occupancy check was a pure
// _cells.ContainsKey(key) dictionary-membership test, so a cell that was cleared via
// Clear Contents (or had formatting pasted onto it) but still carries a live _cells entry
// with Value == BlankValue and no formula -- i.e. formatted but genuinely empty -- was
// wrongly treated as "occupied" and permanently blocked a dynamic-array spill into it with
// #SPILL!, even though real Excel's spill-blocking rule looks only at actual cell content
// (a value or a formula), never at formatting alone.
public sealed class R44_SpillBlockedFormattedEmptyCellTests
{
    [Fact]
    public void FormattedButBlankCell_DoesNotBlockSpill()
    {
        var sheet = new Sheet(SheetId.New(), "S");
        var anchor = new CellAddress(sheet.Id, 1, 1);

        // Simulate ClearContentsCommand's "preserve formatting on an emptied cell" pattern:
        // a live _cells entry whose Value is BlankValue.Instance but whose StyleId is non-default
        // (e.g. B1 was typed "x" + bold/fill, then Delete/Clear Contents was pressed).
        var clearedButStyled = Cell.FromValue(BlankValue.Instance);
        clearedButStyled.StyleId = new StyleId(5);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), clearedButStyled);

        // A1 wants to spill a 1x2 result into A1:B1. B1 is formatted-but-empty, so Excel would
        // let this spill through -- it must not be reported as blocked.
        sheet.IsSpillBlocked(anchor, rows: 1, cols: 2).Should().BeFalse();
    }

    [Fact]
    public void ClearingAnOccupyingCell_UnblocksAndAllowsRespill()
    {
        var sheet = new Sheet(SheetId.New(), "S");
        var anchor = new CellAddress(sheet.Id, 1, 1);
        var blockerAddr = new CellAddress(sheet.Id, 1, 2);

        // B1 genuinely occupied with real content -> blocks the spill.
        sheet.SetCell(blockerAddr, Cell.FromValue(new TextValue("x")));
        sheet.IsSpillBlocked(anchor, rows: 1, cols: 2).Should().BeTrue();

        // Clearing it the way ClearContentsCommand does (value -> Blank, style preserved/reset)
        // must re-enable the spill.
        sheet.SetCell(blockerAddr, Cell.FromValue(BlankValue.Instance));
        sheet.IsSpillBlocked(anchor, rows: 1, cols: 2).Should().BeFalse();
    }

    [Fact]
    public void CellWithActualValue_StillBlocksSpill()
    {
        // Sibling no-regression: real content (not blank) must still block, exactly as before.
        var sheet = new Sheet(SheetId.New(), "S");
        var anchor = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), Cell.FromValue(new NumberValue(42)));

        sheet.IsSpillBlocked(anchor, rows: 1, cols: 2).Should().BeTrue();
    }

    [Fact]
    public void CellWithFormula_StillBlocksSpillEvenWithBlankCachedValue()
    {
        // Sibling no-regression: a formula cell is "occupied" by virtue of having a formula, even
        // before it has been recalculated (Value still defaults to BlankValue at construction) --
        // this must not be conflated with a genuinely empty, formatted-only cell.
        var sheet = new Sheet(SheetId.New(), "S");
        var anchor = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), Cell.FromFormula("1"));

        sheet.IsSpillBlocked(anchor, rows: 1, cols: 2).Should().BeTrue();
    }

    [Fact]
    public void EmptyUnwrittenCell_DoesNotBlockSpill()
    {
        // Sibling no-regression: a cell with no _cells entry at all (never written) is not
        // occupied, matching the pre-existing behavior for genuinely untouched cells.
        var sheet = new Sheet(SheetId.New(), "S");
        var anchor = new CellAddress(sheet.Id, 1, 1);

        sheet.IsSpillBlocked(anchor, rows: 1, cols: 2).Should().BeFalse();
    }
}
