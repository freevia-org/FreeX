using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// R150 spill-overlay-root F8/F9/F10: Go To Special's Blanks / Row Differences / Column
/// Differences all used to read a cell's value via <c>sheet.GetCell(address)?.Value</c>, which
/// returns null for a non-anchor dynamic-array spill member (its value lives only in the
/// separate spill overlay, never in Sheet's private _cells dictionary -- see
/// R150_SheetGetCellGetValueSpillOverlayDivergenceTests). That null then got treated as
/// BlankValue.Instance, so every visibly-populated spill member other than the formula's own
/// anchor cell was silently corrupted into "blank" for these three Go To Special modes. The fix
/// is to read the effective value with <see cref="Sheet.GetValue(CellAddress)"/> instead, which
/// falls back to the spill overlay.
/// </summary>
public sealed class R150_GoToSpecialSpillOverlayTests
{
    private static Sheet SpillSheet(out CellAddress anchor, out CellAddress member1, out CellAddress member2)
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");

        // A1 anchors a formula that spills {10;20;30} down to A1:A3.
        anchor = new CellAddress(sheet.Id, 1, 1); // A1
        member1 = new CellAddress(sheet.Id, 2, 1); // A2
        member2 = new CellAddress(sheet.Id, 3, 1); // A3

        sheet.SetFormula(anchor, "{10;20;30}");
        sheet.GetCell(anchor)!.Value = new NumberValue(10);
        sheet.SetSpillRange(anchor, new RangeValue(new ScalarValue[3, 1]
        {
            { new NumberValue(10) }, // anchor slot -- SetSpillRange ignores this element
            { new NumberValue(20) }, // A2 -- non-anchor spill member
            { new NumberValue(30) }, // A3 -- non-anchor spill member
        }));

        return sheet;
    }

    // ── F8: Blanks must not select spill members ──────────────────────────────────────────────

    [Fact]
    public void FindBlanks_SpillMembers_AreNotSelected()
    {
        var sheet = SpillSheet(out var anchor, out var member1, out var member2);
        var range = new GridRange(anchor, member2); // A1:A3

        var result = GoToSpecialService.Find(sheet, range, GoToSpecialKind.Blanks);

        result.Should().BeEmpty(
            "A2/A3 visibly display 20/30 from the spill overlay and must not be treated as blank");
    }

    [Fact]
    public void FindBlanks_GenuinelyEmptyCellsInSameRange_AreStillSelected()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 2));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(1));

        var result = GoToSpecialService.Find(sheet, range, GoToSpecialKind.Blanks);

        // Sibling no-regression: a real empty cell (no spill, no _cells entry at all) must still
        // be picked up, exactly as FindBlanks_ReturnsBlankAddressesInRange already asserts.
        result.Should().Equal(new CellAddress(sheet.Id, 1, 2));
    }

    // ── F9: Row Differences must compare the real spilled value, not a false blank ────────────

    [Fact]
    public void FindRowDifferences_BaseCellIsSpillMember_ComparesRealValue()
    {
        var sheet = SpillSheet(out var anchor, out var member1, out var member2);
        // Row 2: A2 (spill member, value 20) is the base column; B2 also holds 20 -> should match
        // (no difference); C2 holds 99 -> should be flagged as different.
        var b2 = new CellAddress(sheet.Id, 2, 2);
        var c2 = new CellAddress(sheet.Id, 2, 3);
        sheet.SetCell(b2, new NumberValue(20));
        sheet.SetCell(c2, new NumberValue(99));

        var range = new GridRange(new CellAddress(sheet.Id, 2, 1), new CellAddress(sheet.Id, 2, 3));
        var activeCell = member1; // A2 is the active cell -> base column = A2's column

        var result = GoToSpecialService.Find(sheet, range, GoToSpecialKind.RowDifferences, activeCell);

        // Before the fix, the base value read back as BlankValue, so B2 (a real 20, matching A2's
        // real 20) would be wrongly flagged as different too.
        result.Should().Equal(c2);
    }

    [Fact]
    public void FindRowDifferences_ComparedCellIsSpillMember_ComparesRealValue()
    {
        var sheet = SpillSheet(out var anchor, out var member1, out var member2);
        // Row 3: base column B3 holds 30, matching A3's spilled value of 30 -> no difference.
        var b3 = new CellAddress(sheet.Id, 3, 2);
        sheet.SetCell(b3, new NumberValue(30));

        var range = new GridRange(new CellAddress(sheet.Id, 3, 1), new CellAddress(sheet.Id, 3, 2));
        var activeCell = b3; // base column = B

        var result = GoToSpecialService.Find(sheet, range, GoToSpecialKind.RowDifferences, activeCell);

        // Before the fix, A3 (the spill member) read back as BlankValue while B3 is 30, so A3 was
        // wrongly flagged as different from the base.
        result.Should().BeEmpty();
    }

    // ── F10: Column Differences has the identical bug for columns instead of rows ─────────────

    [Fact]
    public void FindColumnDifferences_BaseCellIsSpillMember_ComparesRealValue()
    {
        var sheet = SpillSheet(out var anchor, out var member1, out var member2);
        // Column A, row 2 (A2, spill member, value 20) is the base row for this column.
        // Add a second column B with B2=20 (matches) and B3=99 (differs) using the SAME base row.
        var b2 = new CellAddress(sheet.Id, 2, 2);
        var b3 = new CellAddress(sheet.Id, 3, 2);
        sheet.SetCell(b2, new NumberValue(20));
        sheet.SetCell(b3, new NumberValue(99));

        // Compare column A (base row 2 = spill member A2) against nothing else in column A itself
        // matters; test column B against base row 2's value taken from column B, so instead
        // structure the range to span both columns with base row = 2.
        var range = new GridRange(new CellAddress(sheet.Id, 2, 1), new CellAddress(sheet.Id, 3, 2));
        var activeCell = member1; // A2 -> base row = row 2

        var result = GoToSpecialService.Find(sheet, range, GoToSpecialKind.ColumnDifferences, activeCell);

        // Column A: only row 2 (the base) is compared against itself, skipped; row 3 (A3, value 30)
        // is compared to the column's own base row-2 value A2=20 -> different.
        // Column B: base row 2 for column B is B2=20; row 3 is B3=99 -> different.
        // Neither A2 nor B2 (the base cells) appear in the result.
        result.Should().Contain(new CellAddress(sheet.Id, 3, 1)); // A3 differs from A2 (30 vs 20)
        result.Should().Contain(b3); // B3 differs from B2 (99 vs 20)
        result.Should().NotContain(anchor);
        result.Should().NotContain(member1);
        result.Should().NotContain(b2);
    }

    [Fact]
    public void FindColumnDifferences_ComparedCellIsSpillMember_ComparesRealValue()
    {
        var sheet = SpillSheet(out var anchor, out var member1, out var member2);
        // Column A: base row 1 (anchor, value 10). Row 3 (A3) is a spill member with value 30,
        // which genuinely differs from the base and must still be flagged.
        var range = new GridRange(anchor, member2); // A1:A3
        var activeCell = anchor; // base row = row 1

        var result = GoToSpecialService.Find(sheet, range, GoToSpecialKind.ColumnDifferences, activeCell);

        result.Should().Contain(member1); // A2 = 20, differs from base 10
        result.Should().Contain(member2); // A3 = 30, differs from base 10

        // Sibling no-regression: if instead the spill member's value matched the base, it must
        // NOT be flagged (proves we're comparing real values, not just "always different").
    }

    [Fact]
    public void FindColumnDifferences_SpillMemberMatchingBaseValue_IsNotFlagged()
    {
        var sheet = SpillSheet(out var anchor, out var member1, out var member2);
        // Overwrite the anchor's formula-driven value to equal A2's spilled value (20), so a
        // correct implementation must NOT flag A2 as different.
        sheet.GetCell(anchor)!.Value = new NumberValue(20);

        var range = new GridRange(anchor, member1); // A1:A2 only
        var activeCell = anchor;

        var result = GoToSpecialService.Find(sheet, range, GoToSpecialKind.ColumnDifferences, activeCell);

        result.Should().BeEmpty("A2's real spilled value (20) matches the base value (20)");
    }
}
