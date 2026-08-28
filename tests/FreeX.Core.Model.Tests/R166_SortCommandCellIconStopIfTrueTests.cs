using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// freex-conditional-format-edit F1: SortCommand.GetEffectiveIcon looped
/// sheet.ConditionalFormats in priority order but immediately `continue`d past any rule whose
/// RuleType was not IconSet, before ever checking whether that higher-priority rule matched the
/// cell and had StopIfTrue set. The real renderer
/// (ViewportConditionalFormatEvaluator.IsSuppressedByHigherPriorityStopIfTrue) suppresses a
/// lower-priority icon-set's icon when ANY higher-priority rule matches with StopIfTrue=true, so
/// the cell is actually painted with NO icon. GetEffectiveIcon disagreed: it skipped straight to
/// evaluating the icon-set rule and returned a real icon bucket for a cell that Excel (and
/// FreeX's own grid) renders with no icon at all -- so Sort On: Cell Icon put that row in the
/// wrong place.
/// </summary>
public sealed class R166_SortCommandCellIconStopIfTrueTests
{
    private static ConditionalFormat AddStopIfTrueGreaterThanZero(Sheet sheet, GridRange range, int priority) => new()
    {
        AppliesTo = range,
        Priority = priority,
        RuleType = CfRuleType.CellValue,
        Operator = CfOperator.GreaterThan,
        Value1 = "0",
        StopIfTrue = true
    };

    private static ConditionalFormat AddThreeTrafficLightsIconSet(GridRange range, int priority)
    {
        var cf = new ConditionalFormat
        {
            AppliesTo = range,
            Priority = priority,
            RuleType = CfRuleType.IconSet,
            IconSetStyle = "3TrafficLights1"
        };
        // Percent thresholds spanning the whole set so every numeric cell resolves to a bucket.
        cf.IconSetThresholds.Add(new CfThresholdModel(CfThresholdType.Percent, "33", GreaterThanOrEqual: true));
        cf.IconSetThresholds.Add(new CfThresholdModel(CfThresholdType.Percent, "67", GreaterThanOrEqual: true));
        return cf;
    }

    [Fact]
    public void SortByCellIcon_CellSuppressedByHigherPriorityStopIfTrue_SortsAsNoIcon()
    {
        // Rule A (Priority 1, higher precedence): CellValue > 0, StopIfTrue=true -- matches B2
        // (value 5) and B3 (value 9), so on screen those cells show NO icon (suppressed).
        // Rule B (Priority 2): 3TrafficLights icon set over B2:B4.
        // B4 = -1 does not match Rule A, so Rule B's icon set governs it (green/top bucket, since
        // -1 is the minimum of the range and percent thresholds are range-relative... instead use
        // a value that clearly lands in the top bucket without ambiguity from range percentiles).
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);

        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new Cell { Value = new TextValue("Row1") });
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new NumberValue(5));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new Cell { Value = new TextValue("Row2") });
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(-1));

        var iconRange = new GridRange(new CellAddress(sheet.Id, 1, 2), new CellAddress(sheet.Id, 2, 2));
        sheet.ConditionalFormats.Add(AddStopIfTrueGreaterThanZero(sheet, iconRange, priority: 1));
        sheet.ConditionalFormats.Add(AddThreeTrafficLightsIconSet(iconRange, priority: 2));

        // Sanity: GetEffectiveIcon for the suppressed cell (Row1, value 5, matches the
        // StopIfTrue rule) must resolve to "no icon" (null) -- exactly what the grid renders --
        // not the icon-set's own bucket for 5.
        var suppressedIcon = SortCommand.GetEffectiveIcon(
            workbook, sheet, new CellAddress(sheet.Id, 1, 2), sheet.GetCell(1, 2));
        suppressedIcon.Should().BeNull("Rule A (StopIfTrue) matched this cell at higher priority, so the icon-set rule never actually paints an icon here");

        // The cell NOT matched by the StopIfTrue rule must still resolve its real icon-set bucket.
        var unsuppressedIcon = SortCommand.GetEffectiveIcon(
            workbook, sheet, new CellAddress(sheet.Id, 2, 2), sheet.GetCell(2, 2));
        unsuppressedIcon.Should().NotBeNull("Row2's value does not match the higher-priority StopIfTrue rule, so the icon-set rule still governs it");

        // End-to-end: sort On Cell Icon targeting the icon Row2 actually shows. Since Row1 is
        // suppressed (no icon), it must sort like any other no-icon cell -- to the back -- exactly
        // like the aNoIcon-sorts-last rule the command already applies for genuinely icon-less
        // cells, and must NOT be pulled to the front by the icon-set's raw-value bucket for 5.
        var sortRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 2));
        var command = new SortCommand(
            sheet.Id, sortRange,
            [new SortKey(1, true, SortOn.CellIcon, TargetIcon: unsuppressedIcon)]);

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.GetValue(1, 1).Should().Be(new TextValue("Row2"), "Row2 actually shows the target icon on screen, so it sorts to the top");
        sheet.GetValue(2, 1).Should().Be(new TextValue("Row1"), "Row1 is suppressed to no-icon on screen, so it sorts to the back like any no-icon cell");
    }

    [Fact]
    public void SortByCellIcon_HigherPriorityRuleMatchesButNotStopIfTrue_DoesNotSuppress_NoRegression()
    {
        // Sibling no-regression case: a higher-priority rule that MATCHES the cell but does NOT
        // have StopIfTrue set must NOT suppress the lower-priority icon set -- only StopIfTrue
        // rules suppress lower-priority rules in Excel (and in the real renderer).
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");

        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new Cell { Value = new TextValue("Row1") });
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new NumberValue(5));

        var iconRange = new GridRange(new CellAddress(sheet.Id, 1, 2), new CellAddress(sheet.Id, 1, 2));

        var nonStopRule = AddStopIfTrueGreaterThanZero(sheet, iconRange, priority: 1);
        nonStopRule.StopIfTrue = false;
        sheet.ConditionalFormats.Add(nonStopRule);
        sheet.ConditionalFormats.Add(AddThreeTrafficLightsIconSet(iconRange, priority: 2));

        var icon = SortCommand.GetEffectiveIcon(
            workbook, sheet, new CellAddress(sheet.Id, 1, 2), sheet.GetCell(1, 2));

        icon.Should().NotBeNull("the higher-priority rule matched but did not have StopIfTrue set, so it must not suppress the lower-priority icon-set rule");
    }
}
