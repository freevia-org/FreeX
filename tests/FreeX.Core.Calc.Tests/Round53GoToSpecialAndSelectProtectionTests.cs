using FreeX.Core.Commands;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Calc.Tests;

/// <summary>
/// R53-commands-goto-special-3-1/-2/-3/-4 and R53-io-protection-allowedit-3-2: Go To Special gaps
/// (blank-active-cell Current Region, missing "Same as active cell" sub-filter for Conditional
/// Formats/Data Validation, Objects ignoring form controls, no Current Array kind) plus a sheet
/// -protection selection-permission enforcement gap (SelectLockedCells/SelectUnlockedCells were
/// round-tripped but never consulted by any guard).
/// </summary>
public sealed class Round53GoToSpecialAndSelectProtectionTests
{
    // R53-commands-goto-special-3-1 (blank-active-cell Current Region -> 1x1 instead of null) was
    // REVERTED: it conflicts with the deliberately-authored SelectionRangeServiceTests
    // .GetCurrentRegion_ReturnsNullForBlankActiveCell, and Excel's exact blank-cell Current-Region
    // behavior needs COM verification. Deferred; the two tests that asserted the new behavior are removed.

    // ── R53-commands-goto-special-3-2 ──────────────────────────────────────────────────────────

    [Fact]
    public void GoToSpecial_ConditionalFormats_MatchActiveCellRuleOnly_SelectsOnlySameRule()
    {
        var (workbook, sheet) = TestWorkbookFixture.CreateWorkbook();
        var ruleA = new ConditionalFormat { AppliesTo = new GridRange(new CellAddress(sheet.Id, 2, 2), new CellAddress(sheet.Id, 5, 2)) }; // B2:B5
        var ruleB = new ConditionalFormat { AppliesTo = new GridRange(new CellAddress(sheet.Id, 2, 4), new CellAddress(sheet.Id, 5, 4)) }; // D2:D5
        sheet.ConditionalFormats.Add(ruleA);
        sheet.ConditionalFormats.Add(ruleB);

        var activeCell = new CellAddress(sheet.Id, 3, 2); // B3, governed only by ruleA
        var searchRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 10, 10));

        var matches = GoToSpecialService.Find(
            sheet,
            searchRange,
            GoToSpecialKind.ConditionalFormats,
            activeCell,
            new GoToSpecialOptions(MatchActiveCellRuleOnly: true));

        matches.Should().BeEquivalentTo(
            ruleA.AppliesTo.AllCells());
    }

    // Sibling/no-regression: the default ("All") behavior is unchanged -- it still selects every
    // rule intersecting the search range, not just the active cell's own rule.
    [Fact]
    public void GoToSpecial_ConditionalFormats_DefaultAllOption_StillSelectsEveryRule()
    {
        var (workbook, sheet) = TestWorkbookFixture.CreateWorkbook();
        var ruleA = new ConditionalFormat { AppliesTo = new GridRange(new CellAddress(sheet.Id, 2, 2), new CellAddress(sheet.Id, 5, 2)) }; // B2:B5
        var ruleB = new ConditionalFormat { AppliesTo = new GridRange(new CellAddress(sheet.Id, 2, 4), new CellAddress(sheet.Id, 5, 4)) }; // D2:D5
        sheet.ConditionalFormats.Add(ruleA);
        sheet.ConditionalFormats.Add(ruleB);

        var activeCell = new CellAddress(sheet.Id, 3, 2); // B3
        var searchRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 10, 10));

        var matches = GoToSpecialService.Find(sheet, searchRange, GoToSpecialKind.ConditionalFormats, activeCell);

        matches.Should().BeEquivalentTo(ruleA.AppliesTo.AllCells().Concat(ruleB.AppliesTo.AllCells()));
    }

    // ── R53-commands-goto-special-3-3 ──────────────────────────────────────────────────────────

    [Fact]
    public void GoToSpecial_Objects_IncludesFormControlAnchor()
    {
        var (workbook, sheet) = TestWorkbookFixture.CreateWorkbook();
        var anchorCell = new CellAddress(sheet.Id, 3, 3); // C3
        sheet.FormControls.Add(new FormControlModel
        {
            Kind = FormControlKind.CheckBox,
            Anchor = new GridRange(anchorCell, anchorCell),
        });

        var searchRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 10, 10));
        var matches = GoToSpecialService.Find(sheet, searchRange, GoToSpecialKind.Objects);

        matches.Should().ContainSingle().Which.Should().Be(anchorCell);
    }

    // Sibling/no-regression: Objects still finds an ordinary picture too (pre-existing behavior).
    [Fact]
    public void GoToSpecial_Objects_StillIncludesPictureAnchor()
    {
        var (workbook, sheet) = TestWorkbookFixture.CreateWorkbook();
        var anchorCell = new CellAddress(sheet.Id, 4, 4);
        sheet.Pictures.Add(new PictureModel { Anchor = anchorCell });

        var searchRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 10, 10));
        var matches = GoToSpecialService.Find(sheet, searchRange, GoToSpecialKind.Objects);

        matches.Should().ContainSingle().Which.Should().Be(anchorCell);
    }

    // ── R53-commands-goto-special-3-4 ──────────────────────────────────────────────────────────

    [Fact]
    public void GoToSpecial_CurrentArray_FromNonAnchorMember_SelectsWholeSpillExtent()
    {
        var (workbook, sheet) = TestWorkbookFixture.CreateWorkbook();
        var anchor = new CellAddress(sheet.Id, 2, 2); // B2
        sheet.SetFormula(anchor, "SEQUENCE(3,1)");
        sheet.GetCell(anchor)!.Value = new NumberValue(1);
        sheet.SetSpillRange(anchor, new RangeValue(new ScalarValue[3, 1]
        {
            { new NumberValue(1) },
            { new NumberValue(2) },
            { new NumberValue(3) },
        })); // spills B2:B4

        var nonAnchorMember = new CellAddress(sheet.Id, 3, 2); // B3, not the anchor
        var searchRange = new GridRange(nonAnchorMember, nonAnchorMember);

        var matches = GoToSpecialService.Find(sheet, searchRange, GoToSpecialKind.CurrentArray, nonAnchorMember);

        matches.Should().BeEquivalentTo(
        [
            new CellAddress(sheet.Id, 2, 2),
            new CellAddress(sheet.Id, 3, 2),
            new CellAddress(sheet.Id, 4, 2),
        ]);
    }

    // Sibling/no-regression: a cell with no array/spill membership at all yields no match.
    [Fact]
    public void GoToSpecial_CurrentArray_OnOrdinaryCell_ReturnsEmpty()
    {
        var (workbook, sheet) = TestWorkbookFixture.CreateWorkbook();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(42));
        var activeCell = new CellAddress(sheet.Id, 1, 1);

        var matches = GoToSpecialService.Find(
            sheet,
            new GridRange(activeCell, activeCell),
            GoToSpecialKind.CurrentArray,
            activeCell);

        matches.Should().BeEmpty();
    }

    // ── R53-io-protection-allowedit-3-2 ────────────────────────────────────────────────────────

    [Fact]
    public void CanSelectCell_LockedCellWithSelectLockedCellsDenied_ReturnsFalse()
    {
        var (workbook, sheet, _) = TestWorkbookFixture.CreateContext();
        var address = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(address, new NumberValue(1)); // default style: Locked = true
        sheet.IsProtected = true;
        sheet.ProtectionPermissions.Remove(SheetProtectionPermission.SelectLockedCells);

        CommandGuards.CanSelectCell(workbook, sheet, address).Should().BeFalse();
    }

    // Sibling/no-regression: with the default protection permissions (both Select* permissions
    // present, matching Excel's default-checked Protect Sheet dialog boxes), selecting a locked
    // cell is still allowed -- only editing it is blocked (unchanged CanEditCell behavior).
    [Fact]
    public void CanSelectCell_LockedCellWithDefaultPermissions_StillReturnsTrue()
    {
        var (workbook, sheet, _) = TestWorkbookFixture.CreateContext();
        var address = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(address, new NumberValue(1)); // default style: Locked = true
        sheet.IsProtected = true;

        CommandGuards.CanSelectCell(workbook, sheet, address).Should().BeTrue();
        CommandGuards.CanEditCell(workbook, sheet, address).Should().BeFalse();
    }
}
