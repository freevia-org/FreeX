using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// R78-commands-sort-multikey-5-2: SortCommand had no "Cell Icon" Sort On option at all — a
/// column with a conditional-format icon set (e.g. the 3-arrows set) had no way to be sorted by
/// its displayed icon, unlike Excel's Data &gt; Sort "Sort On: Cell Icon". Covers:
///  - SortOn.CellIcon with a chosen target icon pulls matching-icon rows to the front, mirroring
///    Sort On: Cell Color's target-color behavior (GetEffectiveIcon/CompareTargetIcon).
///  - SortOn.CellIcon with NO target icon chosen is a no-op between differently-iconed rows
///    (original relative order preserved), mirroring the no-target-color no-op rule — sibling
///    no-regression case for the new icon plumbing.
/// </summary>
public sealed class R78_SortCommandCellIconTests
{
    private static ConditionalFormat AddThreeArrowsIconSet(Sheet sheet, GridRange range)
    {
        var cf = new ConditionalFormat
        {
            AppliesTo = range,
            RuleType = CfRuleType.IconSet,
            IconSetStyle = "3Arrows"
        };
        cf.IconSetThresholds.Add(new CfThresholdModel(CfThresholdType.Number, "4", GreaterThanOrEqual: true));
        cf.IconSetThresholds.Add(new CfThresholdModel(CfThresholdType.Number, "8", GreaterThanOrEqual: true));
        sheet.ConditionalFormats.Add(cf);
        return cf;
    }

    [Fact]
    public void SortByCellIcon_WithTargetIcon_PutsMatchingIconFirst()
    {
        // Column: 1 (red-down, bucket 0), 5 (yellow-flat, bucket 1), 9 (green-up, bucket 2).
        // Sorting "On Cell Icon" targeting the green-up icon (bucket 2) must pull row C to the
        // top, exactly like Sort On: Cell Color pulls a target color's rows to the top.
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);

        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new Cell { Value = new TextValue("A") });
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new NumberValue(1));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new Cell { Value = new TextValue("B") });
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(5));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new Cell { Value = new TextValue("C") });
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(9));

        var iconRange = new GridRange(new CellAddress(sheet.Id, 1, 2), new CellAddress(sheet.Id, 3, 2));
        AddThreeArrowsIconSet(sheet, iconRange);

        var sortRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 2));
        var command = new SortCommand(
            sheet.Id, sortRange,
            [new SortKey(1, true, SortOn.CellIcon, TargetIcon: new CfIconOverride("3Arrows", 2))]);

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.GetValue(1, 1).Should().Be(new TextValue("C"));
        sheet.GetValue(2, 1).Should().Be(new TextValue("A"));
        sheet.GetValue(3, 1).Should().Be(new TextValue("B"));

        command.Revert(ctx);

        sheet.GetValue(1, 1).Should().Be(new TextValue("A"));
        sheet.GetValue(2, 1).Should().Be(new TextValue("B"));
        sheet.GetValue(3, 1).Should().Be(new TextValue("C"));
    }

    [Fact]
    public void SortByCellIcon_NoTargetIcon_LeavesDifferentIconedRowsInOriginalOrder_NoRegression()
    {
        // Sibling no-regression case, mirroring SortByCellColor_NoTargetColor_...: with no
        // target icon chosen, rows showing three DIFFERENT icons must keep their original
        // relative order — not be reordered by icon bucket index (which would put A, B, C in
        // ascending-value order here, since the buckets already happen to match) — the no-op
        // rule must be driven purely by "no target chosen", not an inferred natural icon order.
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);

        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new Cell { Value = new TextValue("C") });
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new NumberValue(9));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new Cell { Value = new TextValue("A") });
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(1));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new Cell { Value = new TextValue("B") });
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(5));

        var iconRange = new GridRange(new CellAddress(sheet.Id, 1, 2), new CellAddress(sheet.Id, 3, 2));
        AddThreeArrowsIconSet(sheet, iconRange);

        var sortRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 2));
        var command = new SortCommand(
            sheet.Id, sortRange,
            [new SortKey(1, true, SortOn.CellIcon, TargetIcon: null)]);

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.GetValue(1, 1).Should().Be(new TextValue("C"));
        sheet.GetValue(2, 1).Should().Be(new TextValue("A"));
        sheet.GetValue(3, 1).Should().Be(new TextValue("B"));
    }
}
