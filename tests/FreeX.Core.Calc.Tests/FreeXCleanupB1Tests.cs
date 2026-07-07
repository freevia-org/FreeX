using FluentAssertions;
using FreeX.Core.Calc;
using FreeX.Core.Model;

namespace FreeX.Core.Calc.Tests;

/// <summary>
/// Regression tests for FreeX cleanup batch B1 HIGH findings.
///
/// P51: a higher-priority Stop If True rule must suppress lower-priority icon-set and data-bar
/// conditional formats exactly as it already suppresses lower-priority style rules (Excel's
/// standard "stop if true hides icons/bars for selected cells" idiom).
/// </summary>
public class FreeXCleanupB1Tests
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
    public void StopIfTrue_OnHigherPriorityFormulaRule_SuppressesLowerPriorityIconSet()
    {
        var (wb, sheet) = MakeWorkbook();
        // A1 = "skip" -> rule 1 (=B1="skip") fires and stops; A1 must show no icon.
        // A2 = "keep" -> rule 1 does not fire; A2 must show its icon as normal.
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), Cell.FromValue(new TextValue("skip")));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), Cell.FromValue(new TextValue("keep")));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), Cell.FromValue(new NumberValue(10)));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), Cell.FromValue(new NumberValue(90)));

        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 1));

        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = range,
            Priority = 1,
            RuleType = CfRuleType.Formula,
            FormulaText = "$B1=\"skip\"",
            StopIfTrue = true
            // No FormatIfTrue: this rule exists purely to gate/stop, mirroring the documented
            // Excel idiom for hiding icon sets/data bars on selected cells.
        });
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = range,
            Priority = 2,
            RuleType = CfRuleType.IconSet,
            IconSetStyle = "3TrafficLights1"
        });

        var vp = GetViewport(wb, sheet);

        GetCell(vp, 1, 1).ConditionalIcon.Should().BeNull("Stop If True on the higher-priority rule must hide the icon set");
        GetCell(vp, 2, 1).ConditionalIcon.Should().NotBeNull("rows where the stopping rule does not match must still show the icon");
    }

    [Fact]
    public void StopIfTrue_OnHigherPriorityFormulaRule_SuppressesLowerPriorityDataBar()
    {
        var (wb, sheet) = MakeWorkbook();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), Cell.FromValue(new TextValue("skip")));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), Cell.FromValue(new TextValue("keep")));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), Cell.FromValue(new NumberValue(0)));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), Cell.FromValue(new NumberValue(100)));

        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 1));

        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = range,
            Priority = 1,
            RuleType = CfRuleType.Formula,
            FormulaText = "$B1=\"skip\"",
            StopIfTrue = true
        });
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = range,
            Priority = 2,
            RuleType = CfRuleType.DataBar,
            DataBarColor = new RgbColor(99, 142, 198)
        });

        var vp = GetViewport(wb, sheet);

        GetCell(vp, 1, 1).ConditionalDataBar.Should().BeNull("Stop If True on the higher-priority rule must hide the data bar");
        GetCell(vp, 2, 1).ConditionalDataBar.Should().NotBeNull("rows where the stopping rule does not match must still show the data bar");
    }
}
