using FreeX.Core.Calc;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Calc.Tests;

/// <summary>
/// Regression coverage for R20-conditional-format-eval-deep-1: StopIfTrue on a matched IconSet or
/// DataBar rule must suppress lower-priority conditional format rules for the same cell, exactly
/// like Excel. Before the fix, ViewportConditionalFormatEvaluator.MatchesRuleCondition hardcoded
/// `CfRuleType.IconSet => false` / `CfRuleType.DataBar => false`, so the StopIfTrue break in
/// Evaluate() (and IsSuppressedByHigherPriorityStopIfTrue used by the icon/data-bar evaluators)
/// could never fire for those two rule kinds.
/// </summary>
public sealed class R20_cf_stopiftrue_Tests
{
    private static (Workbook workbook, Sheet sheet) MakeWorkbook() =>
        TestWorkbookFixture.CreateWorkbook();

    private static ViewportModel GetViewport(Workbook wb, Sheet sheet)
    {
        var svc = new ViewportService();
        return svc.GetViewport(wb, sheet.Id, new ViewportRequest(1, 1, 500, 500));
    }

    private static DisplayCell GetCell(ViewportModel vp, uint row, uint col) =>
        vp.Cells.Single(c => c.Row == row && c.Col == col);

    [Fact]
    public void IconSet_WithStopIfTrue_SuppressesLowerPriorityFillRule()
    {
        var (wb, sheet) = MakeWorkbook();
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 10, 1));

        for (uint row = 1; row <= 10; row++)
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), Cell.FromValue(new NumberValue(row)));

        // Rule 1 (higher precedence): 3-icon traffic-light set, StopIfTrue set. Every numeric
        // cell in the range always resolves into some icon bucket, so this rule's condition is
        // met for every cell -- and with StopIfTrue, Excel suppresses all lower-priority rules.
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = range,
            Priority = 1,
            RuleType = CfRuleType.IconSet,
            IconSetStyle = "3TrafficLights1",
            StopIfTrue = true
        });

        // Rule 2 (lower precedence): plain cell-fill rule that would otherwise color values > 5 red.
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = range,
            Priority = 2,
            RuleType = CfRuleType.CellValue,
            Operator = CfOperator.GreaterThan,
            Value1 = "5",
            FormatIfTrue = new CellStyle { FillColor = new CellColor(255, 0, 0) }
        });

        var vp = GetViewport(wb, sheet);

        // The icon set itself must still render.
        GetCell(vp, 6, 1).ConditionalIcon.Should().NotBeNull("the higher-priority icon set rule matches every numeric cell");

        // But the lower-priority red-fill rule must be suppressed for A6:A10 (values > 5),
        // because the icon set's StopIfTrue rule already matched.
        for (uint row = 6; row <= 10; row++)
        {
            var cell = GetCell(vp, row, 1);
            (cell.Style?.FillColor).Should().NotBe(
                new CellColor(255, 0, 0),
                $"row {row}: StopIfTrue on the matched icon-set rule must suppress the lower-priority fill rule");
        }
    }

    [Fact]
    public void DataBar_WithStopIfTrue_SuppressesLowerPriorityFillRule()
    {
        var (wb, sheet) = MakeWorkbook();
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 10, 1));

        for (uint row = 1; row <= 10; row++)
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), Cell.FromValue(new NumberValue(row)));

        // Rule 1 (higher precedence): data bar covering the whole range, StopIfTrue set. Every
        // finite numeric cell in the range always renders (or would render) a bar, so this rule's
        // condition is met for every cell.
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = range,
            Priority = 1,
            RuleType = CfRuleType.DataBar,
            DataBarColor = new RgbColor(0, 120, 215),
            DataBarMinThresholdType = CfThresholdType.Min,
            DataBarMaxThresholdType = CfThresholdType.Max,
            StopIfTrue = true
        });

        // Rule 2 (lower precedence): plain cell-fill rule that would otherwise color values > 5 red.
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = range,
            Priority = 2,
            RuleType = CfRuleType.CellValue,
            Operator = CfOperator.GreaterThan,
            Value1 = "5",
            FormatIfTrue = new CellStyle { FillColor = new CellColor(255, 0, 0) }
        });

        var vp = GetViewport(wb, sheet);

        GetCell(vp, 6, 1).ConditionalDataBar.Should().NotBeNull("the higher-priority data bar rule matches every numeric cell");

        for (uint row = 6; row <= 10; row++)
        {
            var cell = GetCell(vp, row, 1);
            (cell.Style?.FillColor).Should().NotBe(
                new CellColor(255, 0, 0),
                $"row {row}: StopIfTrue on the matched data-bar rule must suppress the lower-priority fill rule");
        }
    }
}
