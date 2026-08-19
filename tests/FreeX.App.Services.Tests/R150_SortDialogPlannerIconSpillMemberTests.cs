using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

/// <summary>
/// R150-sort-dialog-icon-spill (spill-overlay-root F7): BuildIconChoices called
/// SortCommand.GetEffectiveIcon(workbook, sheet, address, sheet.GetCell(address)) without an
/// explicit effectiveValue. A non-anchor member of a live dynamic-array spill has no entry in
/// Sheet's _cells dictionary (GetCell returns null for it) -- its value lives only in the
/// separate spill overlay -- so GetEffectiveIcon fell back to evaluating the icon-set rule
/// against BlankValue.Instance instead of the real spilled number, and the icon-choice scan the
/// Sort dialog's "Cell Icon" picker is built from silently dropped any icon that only appears on
/// a spill member. Mirrors the fix already applied to SortCommand's own comparator (see
/// R149Remediation_SortCommandColorIconSpillTests), applied here to the dialog-choice scan that
/// feeds the picker, not the comparator itself.
/// </summary>
public sealed class R150_SortDialogPlannerIconSpillMemberTests
{
    private static (Workbook workbook, Sheet sheet) BuildSpillWithThreeArrowsIconSet()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");

        // A1:A3 is a live dynamic-array spill: A1 is the anchor (real Cell, value 1 -> bucket 0).
        // A2/A3 are non-anchor spill members with no _cells entry of their own (values live only
        // in the _spillValues overlay). A2 = 9 resolves to bucket 2 (the top icon) -- an icon
        // that ONLY appears on a spill member, never on the real anchor cell.
        var anchor = new CellAddress(sheet.Id, 1, 1); // A1
        sheet.SetFormula(anchor, "{1;9;1}");
        sheet.GetCell(anchor)!.Value = new NumberValue(1);
        sheet.SetSpillRange(anchor, new RangeValue(new ScalarValue[3, 1]
        {
            { new NumberValue(1) },  // row 0 (anchor slot) -- SetSpillRange ignores this element
            { new NumberValue(9) },  // A2 -- bucket 2 (spill-only icon)
            { new NumberValue(1) },  // A3 -- bucket 0 (duplicate of anchor)
        }));

        var cf = new ConditionalFormat
        {
            AppliesTo = new GridRange(anchor, new CellAddress(sheet.Id, 3, 1)),
            RuleType = CfRuleType.IconSet,
            IconSetStyle = "3Arrows"
        };
        cf.IconSetThresholds.Add(new CfThresholdModel(CfThresholdType.Number, "4", GreaterThanOrEqual: true));
        cf.IconSetThresholds.Add(new CfThresholdModel(CfThresholdType.Number, "8", GreaterThanOrEqual: true));
        sheet.ConditionalFormats.Add(cf);

        return (workbook, sheet);
    }

    /// <summary>
    /// Fails before the fix: BuildIconChoices evaluated the spill member A2 against BlankValue
    /// (because sheet.GetCell(A2) is null), so "3Arrows:2" -- the icon A2 visibly shows -- was
    /// missing from the choice list, leaving the user unable to sort by it. Only "3Arrows:0"
    /// (from the real anchor cell A1/A3) would appear.
    /// </summary>
    [Fact]
    public void BuildIconChoices_IncludesIconThatOnlyAppearsOnASpillMember()
    {
        var (workbook, sheet) = BuildSpillWithThreeArrowsIconSet();
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 1));

        var choices = SortDialogPlanner.BuildIconChoices(workbook, sheet, range, columnOffset: 0, hasHeaders: false);

        choices.Should().Equal(
            new SortIconChoice(""),
            new SortIconChoice("3Arrows:0"),
            new SortIconChoice("3Arrows:2"));
    }

    /// <summary>
    /// Sibling no-regression: an ordinary, non-spill column (every address has a real Cell) must
    /// keep resolving exactly as before -- the fix only changes how the effective value is
    /// sourced for cells GetCell returns null for; a real Cell's own Value is still used.
    /// </summary>
    [Fact]
    public void BuildIconChoices_OrdinaryNonSpillColumn_StillResolvesEachRealCellsIcon_NoRegression()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");

        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new Cell { Value = new NumberValue(1) }); // bucket 0
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new Cell { Value = new NumberValue(9) }); // bucket 2
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new Cell { Value = new NumberValue(1) }); // bucket 0 (dup)

        var cf = new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 1)),
            RuleType = CfRuleType.IconSet,
            IconSetStyle = "3Arrows"
        };
        cf.IconSetThresholds.Add(new CfThresholdModel(CfThresholdType.Number, "4", GreaterThanOrEqual: true));
        cf.IconSetThresholds.Add(new CfThresholdModel(CfThresholdType.Number, "8", GreaterThanOrEqual: true));
        sheet.ConditionalFormats.Add(cf);

        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 1));

        var choices = SortDialogPlanner.BuildIconChoices(workbook, sheet, range, columnOffset: 0, hasHeaders: false);

        choices.Should().Equal(
            new SortIconChoice(""),
            new SortIconChoice("3Arrows:0"),
            new SortIconChoice("3Arrows:2"));
    }
}
