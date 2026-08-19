using FreeX.Core.Model;
using FreeX.Core.Commands;
using FluentAssertions;

namespace FreeX.Core.Calc.Tests;

/// <summary>
/// F1-array-spill: SortCommand.CaptureCellPayload used to read a cell's sort key purely through
/// Sheet.GetCell, which only looks in the _cells dictionary. A non-anchor member of a live
/// dynamic-array spill (e.g. row 2/3 of a spilled formula anchored at row 1) has no _cells entry —
/// its value lives only in Sheet's separate _spillValues overlay — so CaptureCellPayload captured
/// Cell = null for every spill-member row and IsBlankOrError(null) always classified that row's
/// sort key as blank. Excel's blank-last rule then pinned every spill-member row to the bottom of
/// the sortable block regardless of its actual value, silently mis-sorting any range whose sort key
/// column contains spilled (non-anchor) values. Fixed by having CaptureCellPayload also capture the
/// live spill value (via Sheet.GetValue, which already checks the _cells/_spillValues overlay in
/// the right order) into a new SortCellPayload.EffectiveValue field used for every sort-key
/// comparison, while leaving SortCellPayload.Cell (used for the actual write-back) untouched.
/// </summary>
public sealed class F1_SortCommandArraySpillMemberValueTests
{
    // ── F1 fix: a spill member's real value must be used as its sort key ──────────────────────

    [Fact]
    public void Sort_ByColumnContainingSpillMembers_UsesLiveSpillValueNotBlank()
    {
        var (workbook, sheet, ctx) = TestWorkbookFixture.CreateContext();

        // A1:A3 is a live dynamic-array spill: A1 is the anchor (real Cell, value 20), A2/A3 are
        // non-anchor spill members with no _cells entry of their own (values 10 and 30 live only
        // in Sheet's _spillValues overlay).
        var anchor = new CellAddress(sheet.Id, 1, 1); // A1
        sheet.SetFormula(anchor, "{20;10;30}");
        sheet.GetCell(anchor)!.Value = new NumberValue(20);
        sheet.SetSpillRange(anchor, new RangeValue(new ScalarValue[3, 1]
        {
            { new NumberValue(20) }, // row 0 (anchor slot) — SetSpillRange ignores this element
            { new NumberValue(10) }, // A2
            { new NumberValue(30) }, // A3
        }));

        // B1:B3 ride along as ordinary (non-spill) real cells, uniquely identifying each row.
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), Cell.FromValue(new TextValue("Twenty"))); // B1
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), Cell.FromValue(new TextValue("Ten")));     // B2
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), Cell.FromValue(new TextValue("Thirty")));  // B3

        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 2));
        var outcome = new SortCommand(sheet.Id, range, sortByColOffset: 0, ascending: true).Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);

        // Ascending by column A's real values (10, 20, 30): the row carrying 10 (a spill member)
        // must move to the top, the anchor row (20) to the middle, and the row carrying 30 (also a
        // spill member) stays last. Column B is plain real-cell data, so its post-sort position
        // unambiguously reveals which row the sort actually placed where — this is what the old
        // "spill members always read as blank" bug got wrong: it left the order completely
        // unchanged (Twenty, Ten, Thirty) because every spill member tied for "blank" and lost the
        // stable-sort tiebreak to their original position.
        sheet.GetCell(new CellAddress(sheet.Id, 1, 2))!.Value.Should().Be(new TextValue("Ten"));
        sheet.GetCell(new CellAddress(sheet.Id, 2, 2))!.Value.Should().Be(new TextValue("Twenty"));
        sheet.GetCell(new CellAddress(sheet.Id, 3, 2))!.Value.Should().Be(new TextValue("Thirty"));
    }

    // ── Sibling no-regression: an ordinary (non-spill) blank cell must still sort last ────────

    [Fact]
    public void Sort_WithGenuinelyBlankCell_StillSortsBlankLast()
    {
        var (workbook, sheet, ctx) = TestWorkbookFixture.CreateContext();

        // No spill anywhere on this sheet — A2 is a plain, genuinely empty cell (no _cells entry
        // and no _spillValues entry), which IsBlankOrError must still classify as blank after the
        // Cell? -> ScalarValue refactor.
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), Cell.FromValue(new NumberValue(5)));  // A1 = 5
        // A2 left empty on purpose.
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), Cell.FromValue(new NumberValue(1)));  // A3 = 1

        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), Cell.FromValue(new TextValue("Five")));  // B1
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), Cell.FromValue(new TextValue("Blank"))); // B2
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), Cell.FromValue(new TextValue("One")));   // B3

        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 2));
        var outcome = new SortCommand(sheet.Id, range, sortByColOffset: 0, ascending: true).Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);

        // Ascending by column A: 1, then 5, then the genuinely blank cell last (Excel's
        // blank-always-last rule, independent of direction).
        sheet.GetCell(new CellAddress(sheet.Id, 1, 2))!.Value.Should().Be(new TextValue("One"));
        sheet.GetCell(new CellAddress(sheet.Id, 2, 2))!.Value.Should().Be(new TextValue("Five"));
        sheet.GetCell(new CellAddress(sheet.Id, 3, 2))!.Value.Should().Be(new TextValue("Blank"));
    }
}
