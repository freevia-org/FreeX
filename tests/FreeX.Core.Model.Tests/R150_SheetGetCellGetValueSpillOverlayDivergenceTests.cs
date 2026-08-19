using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// R150 spill-overlay-root F1: pins the DELIBERATE divergence between Sheet.GetCell and
/// Sheet.GetValue at a non-anchor dynamic-array spill member.
///
/// GetCell(row,col) only ever looks in the private _cells dictionary. A non-anchor spill member
/// (e.g. row 2 of a formula anchored at row 1 that spilled via SetSpillRange) has no _cells entry
/// of its own -- its value lives only in the separate _spillValues overlay -- so GetCell returns
/// null for it even though the grid visibly shows a value there and GetValue correctly returns it.
///
/// Per the round-150 scope directive for this file, GetCell's behaviour is intentionally NOT
/// changed here (fourteen other findings this round fix the affected call sites individually;
/// changing what GetCell returns would move all of them, plus save/write, clear, and used-range
/// computation, at once). This test instead asserts the two methods' documented contracts by
/// execution: GetValue must see the spill overlay, GetCell must not. See the XML doc remarks on
/// Sheet.GetCell / Sheet.GetValue for the caller-facing rule ("value questions must use GetValue").
/// </summary>
public sealed class R150_SheetGetCellGetValueSpillOverlayDivergenceTests
{
    // ── Pins the divergence: GetValue sees the spill overlay, GetCell does not ────────────────

    [Fact]
    public void NonAnchorSpillMember_GetValueSeesSpillOverlay_GetCellReturnsNull()
    {
        var sheet = new Sheet(SheetId.New(), "Test");

        // A1 is the anchor of a spilled formula (real Cell + real value).
        var anchor = new CellAddress(sheet.Id, 1, 1); // A1
        sheet.SetFormula(anchor, "{10;20;30}");
        sheet.GetCell(anchor)!.Value = new NumberValue(10);

        // Spill A1:A3 -- A2/A3 get entries only in the _spillValues overlay, never in _cells.
        sheet.SetSpillRange(anchor, new RangeValue(new ScalarValue[3, 1]
        {
            { new NumberValue(10) }, // row 0 (anchor slot) -- SetSpillRange ignores this element
            { new NumberValue(20) }, // A2 -- non-anchor spill member
            { new NumberValue(30) }, // A3 -- non-anchor spill member
        }));

        var member = new CellAddress(sheet.Id, 2, 1); // A2

        // GetValue is the documented "value question" API: it must see the spilled value.
        sheet.GetValue(member).Should().Be(new NumberValue(20),
            "GetValue explicitly falls back to the _spillValues overlay when _cells has no entry");

        // GetCell is NOT changed by this fix -- it must still return null for a spill member,
        // because it only ever consults _cells. This is the divergence the finding identified;
        // pinning it here (rather than only documenting it) means a future accidental change to
        // GetCell's storage-only contract fails a test instead of silently rippling into the
        // fourteen call sites this round is fixing independently.
        sheet.GetCell(member).Should().BeNull(
            "GetCell only ever looks in _cells and must not be changed to consult the spill overlay " +
            "(see the round-150 scope directive: that is a separate, unbounded-blast-radius decision)");
    }

    // ── Sibling no-regression: the spill ANCHOR is a real cell and both methods agree on it ────

    [Fact]
    public void SpillAnchorCell_GetCellAndGetValueAgree()
    {
        var sheet = new Sheet(SheetId.New(), "Test");

        var anchor = new CellAddress(sheet.Id, 1, 1); // A1
        sheet.SetFormula(anchor, "{10;20;30}");
        sheet.GetCell(anchor)!.Value = new NumberValue(10);
        sheet.SetSpillRange(anchor, new RangeValue(new ScalarValue[3, 1]
        {
            { new NumberValue(10) },
            { new NumberValue(20) },
            { new NumberValue(30) },
        }));

        // The anchor itself has a genuine _cells entry (it's where the formula lives), so unlike
        // a non-anchor member, GetCell and GetValue must agree on it -- the divergence is strictly
        // about non-anchor spill members, not the anchor.
        sheet.GetCell(anchor).Should().NotBeNull();
        sheet.GetCell(anchor)!.Value.Should().Be(new NumberValue(10));
        sheet.GetValue(anchor).Should().Be(new NumberValue(10));
    }

    // ── Sibling no-regression: a genuinely empty cell (no spill at all) still reads as blank ───

    [Fact]
    public void GenuinelyEmptyCell_NoSpillAnywhere_GetCellNullAndGetValueBlank()
    {
        var sheet = new Sheet(SheetId.New(), "Test");
        var empty = new CellAddress(sheet.Id, 5, 5);

        sheet.GetCell(empty).Should().BeNull();
        sheet.GetValue(empty).Should().BeOfType<BlankValue>();
    }
}
