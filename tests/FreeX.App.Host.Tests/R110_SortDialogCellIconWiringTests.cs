using System.Linq;
using FluentAssertions;
using FreeX.App.Services;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

/// <summary>
/// R110-services-sort-cellicon follow-up: SortDialogPlanner fully supports "Sort On: Cell Icon"
/// (SortOnFromLabel, BuildSortKeys, BuildIconChoices -- see R110_SortDialogPlannerCellIconTests in
/// FreeX.App.Services.Tests), but the WPF SortDialog itself never surfaced it: the "Sort On"
/// DataGridComboBoxColumn only ever offered Cell Values / Cell Color / Font Color, and there was no
/// "Icon" column at all, so a user could never actually pick "Cell Icon" or a target icon swatch --
/// the fully-implemented sort-engine path was unreachable from the real dialog. Covers:
///  - The "Sort On" combo now includes "Cell Icon" (SortDialog.Types.cs SortOnChoices) and picking
///    it drives the same reactive wiring Cell Color/Font Color already use (AttachLevel's
///    PropertyChanged subscription), which scans the live workbook for the level's target column's
///    actual icon set and populates a real "Icon" combo's choices -- proven end-to-end through
///    SortDialog itself, not just the planner.
///  - Sibling no-regression: Cell Color/Font Color continue to populate ColorChoices reactively the
///    same way they did before this change.
/// </summary>
public sealed class R110_SortDialogCellIconWiringTests
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

    private static (Workbook Workbook, Sheet Sheet, GridRange SortRange) BuildIconWorkbook()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");

        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new Cell { Value = new TextValue("A") });
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new NumberValue(1));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new Cell { Value = new TextValue("B") });
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(5));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new Cell { Value = new TextValue("C") });
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(9));

        var iconRange = new GridRange(new CellAddress(sheet.Id, 1, 2), new CellAddress(sheet.Id, 3, 2));
        AddThreeArrowsIconSet(sheet, iconRange);

        var sortRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 2));
        return (workbook, sheet, sortRange);
    }

    /// <summary>
    /// Fails before the fix: the "Sort On" combo's ItemsSource (SortOnChoices) had no "Cell Icon"
    /// entry, and SortDialog never scanned the workbook for icon choices at all -- a level's
    /// IconChoices stayed the empty "(none)" placeholder forever, no matter what SortOn was set to,
    /// because AttachLevel only reacted to color sorts. After the fix, picking "Cell Icon" (exactly
    /// what a user selecting that combo entry produces) makes the dialog itself scan the real
    /// workbook and expose the actual icon-set swatch ("3Arrows:2") through IconChoices, and the
    /// resulting SortKey built from the dialog's own levels carries the chosen TargetIcon.
    /// </summary>
    [Fact]
    public void SortDialog_SortOnCellIcon_IsReachableThroughTheSortOnComboAndRealIconChoices()
    {
        StaTestRunner.Run(() =>
        {
            var (workbook, sheet, sortRange) = BuildIconWorkbook();

            var dialog = new SortDialog(
                levels: [new SortDialogLevel(1, true)],
                iconWorkbook: workbook,
                iconSheet: sheet,
                iconRange: sortRange);
            dialog.Show();
            try
            {
                var level = dialog.Levels[0];

                // Mirrors exactly what the "Sort On" DataGridComboBoxColumn's TwoWay binding does
                // when a user picks "Cell Icon" from the dropdown (SelectedValueBinding -> SortOn).
                level.SortOn = SortDialogPlannerText.Default.SortOnCellIcon;

                // Proves the host dialog itself reacted (not just SortDialogPlanner in isolation):
                // it rescanned the live workbook for the real 3-Arrows icon set on the level's
                // target column, exactly what the "Icon" combo's ItemsSource must show the user.
                level.IconChoices.Select(choice => choice.Label).Should().Contain("3Arrows:2",
                    "SortDialog must scan the real workbook for the level's column icon set once Cell Icon is picked");

                var greenArrowToken = level.IconChoices.Single(choice => choice.Label == "3Arrows:2").Label;

                // Mirrors what the "Icon" combo's TwoWay binding does when a user picks a swatch.
                level.TargetIcon = greenArrowToken;

                var keys = SortDialogPlanner.BuildSortKeys(dialog.Levels);
                keys.Should().ContainSingle(key =>
                    key.ColumnOffset == 1 &&
                    key.SortOn == SortOn.CellIcon &&
                    key.TargetIcon == new CfIconOverride("3Arrows", 2));
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    /// <summary>Sibling no-regression: Cell Color / Font Color still populate ColorChoices from the
    /// live dialog exactly as before this change once a level's SortOn is switched to them.</summary>
    [Fact]
    public void SortDialog_SortOnCellOrFontColor_StillPopulatesColorChoicesReactively_NoRegression()
    {
        StaTestRunner.Run(() =>
        {
            var workbook = new Workbook("test");
            var sheet = workbook.AddSheet("Sheet1");
            var red = workbook.RegisterStyle(new CellStyle { FillColor = new CellColor(255, 0, 0) });
            var redCell = Cell.FromValue(new TextValue("red"));
            redCell.StyleId = red;
            sheet.SetCell(new CellAddress(sheet.Id, 1, 1), redCell);
            var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 1));

            var dialog = new SortDialog(
                levels: [new SortDialogLevel(0, true)],
                cellColorChoices: SortDialogPlanner.BuildColorChoices(workbook, sheet, range, SortOn.CellColor),
                fontColorChoices: SortDialogPlanner.BuildColorChoices(workbook, sheet, range, SortOn.FontColor));
            dialog.Show();
            try
            {
                var level = dialog.Levels[0];
                level.SortOn = "Cell Color";

                level.ColorChoices.Select(choice => choice.Label).Should().Contain("#FF0000");

                level.TargetColor = "#FF0000";
                var keys = SortDialogPlanner.BuildSortKeys(dialog.Levels);
                keys.Should().ContainSingle(key =>
                    key.ColumnOffset == 0 &&
                    key.SortOn == SortOn.CellColor &&
                    key.TargetColor == new CellColor(255, 0, 0));
            }
            finally
            {
                dialog.Close();
            }
        });
    }
}
