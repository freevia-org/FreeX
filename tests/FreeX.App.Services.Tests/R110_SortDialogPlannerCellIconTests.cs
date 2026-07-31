using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

/// <summary>
/// R110-services-sort-cellicon: FreeX.Core.Commands.SortCommand fully implements a fourth "Sort On"
/// mode, SortOn.CellIcon (icon-set bucket resolution, target-icon comparator, "no icon always goes
/// last" rule, sortBy="icon" persistence — see R78_SortCommandCellIconTests), but the only two paths
/// that build a SortKey from user-facing input, SortDialogPlanner.SortOnFromLabel and
/// SortDialogPlannerText, could only ever resolve to CellValues/CellColor/FontColor. There was no
/// SortOnCellIcon label, no icon-choice scan analogous to BuildColorChoices, and no way for
/// BuildSortKeys to populate SortKey.TargetIcon, so the already-implemented engine path was
/// unreachable through the planner every host dialog (WPF SortDialog, Avalonia MainWindow) is built
/// on. Covers:
///  - BuildSortKeys resolving the "Cell Icon" label end-to-end through the REAL SortCommand entry
///    point (not a hand-built SortKey/model), proving a user picking "Cell Icon" + a target icon in
///    a Sort On combo actually reorders rows exactly like Sort On: Cell Color does for a target
///    color (mirrors R78_SortCommandCellIconTests' scenario, but driven by planner labels/tokens).
///  - BuildOrderChoices/SortOnFromLabel/BuildIconChoices(ForSortOn) — the sibling planner surface a
///    host dialog needs to wire the combo — behave analogously to the existing Cell Color/Font Color
///    paths (no-regression coverage for the neighbouring color-sort behavior these mirror).
/// </summary>
public sealed class R110_SortDialogPlannerCellIconTests
{
    private sealed class TestCommandContext(Workbook workbook) : ICommandContext
    {
        public Workbook Workbook { get; } = workbook;

        public Sheet GetSheet(SheetId sheetId) =>
            Workbook.GetSheet(sheetId) ?? throw new KeyNotFoundException($"Sheet {sheetId} not found");
    }

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

    /// <summary>
    /// Fails before the fix: SortOnFromLabel had no branch for "Cell Icon" so it fell through to
    /// SortOn.CellValues, and BuildSortKeys never populated TargetIcon at all — the resulting
    /// SortCommand would sort by the raw numeric cell values (1, 5, 9 ascending: A, B, C) instead of
    /// pulling the green-up-arrow (bucket 2) row to the top, exactly as picking "Cell Icon" from a
    /// real Sort dialog combo and choosing the green arrow swatch must do in Excel.
    /// </summary>
    [Fact]
    public void BuildSortKeys_ResolvesCellIconLabel_AndRealSortCommandReordersByTargetIcon()
    {
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

        // Exactly what a host "Sort On" combo + icon-swatch picker produces: the localized/default
        // "Cell Icon" label plus an opaque icon token from BuildIconChoices (column offset 1 = the
        // numeric column carrying the icon set), never a hand-built SortKey or CfIconOverride.
        var iconChoices = SortDialogPlanner.BuildIconChoices(workbook, sheet, sortRange, columnOffset: 1, hasHeaders: false);
        var greenArrowToken = iconChoices.Should().ContainSingle(c => c.Label.EndsWith(":2", StringComparison.Ordinal)).Which.Label;

        var levels = new[]
        {
            new SortDialogLevel(1, true) { SortOn = SortDialogPlannerText.Default.SortOnCellIcon, TargetIcon = greenArrowToken }
        };

        var keys = SortDialogPlanner.BuildSortKeys(levels);

        keys.Should().Equal(new SortKey(1, true, SortOn.CellIcon, TargetIcon: new CfIconOverride("3Arrows", 2)));

        var command = new SortCommand(sheet.Id, sortRange, keys);
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
    public void SortOnFromLabel_ResolvesCellIcon_AndLeavesOtherLabelsUnchanged_NoRegression()
    {
        SortDialogPlanner.SortOnFromLabel("Cell Icon").Should().Be(SortOn.CellIcon);
        SortDialogPlanner.SortOnFromLabel("Cell Values").Should().Be(SortOn.CellValues);
        SortDialogPlanner.SortOnFromLabel("Cell Color").Should().Be(SortOn.CellColor);
        SortDialogPlanner.SortOnFromLabel("Font Color").Should().Be(SortOn.FontColor);
        SortDialogPlanner.SortOnFromLabel("Unknown").Should().Be(SortOn.CellValues);
    }

    [Fact]
    public void BuildOrderChoices_UsesExcelColorSortLabelsForCellIconSort_LikeCellAndFontColor()
    {
        SortDialogPlanner.BuildOrderChoices("Cell Icon").Should().Equal(
            new SortDirectionChoice("On Top", true),
            new SortDirectionChoice("On Bottom", false));

        // Sibling no-regression: existing color-sort order choices are unaffected.
        SortDialogPlanner.BuildOrderChoices("Cell Color").Should().Equal(
            new SortDirectionChoice("On Top", true),
            new SortDirectionChoice("On Bottom", false));
        SortDialogPlanner.BuildOrderChoices("Cell Values").Should().Equal(
            new SortDirectionChoice("A to Z", true),
            new SortDirectionChoice("Z to A", false));
    }

    [Fact]
    public void BuildSortKeys_NoTargetIconChosen_LeavesTargetIconNull_NoRegressionMirrorsNoTargetColor()
    {
        var levels = new[]
        {
            new SortDialogLevel(0, true) { SortOn = "Cell Icon" },
            new SortDialogLevel(1, true) { SortOn = "Cell Color" }
        };

        var keys = SortDialogPlanner.BuildSortKeys(levels);

        keys.Should().Equal(
            new SortKey(0, true, SortOn.CellIcon, TargetIcon: null),
            new SortKey(1, true, SortOn.CellColor, TargetColor: null));
    }

    [Fact]
    public void BuildIconChoices_ListsDistinctEffectiveIconsFromTargetedColumn_ExcludingHeaderRow()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");

        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new NumberValue(0)); // header row, excluded
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(1)); // bucket 0
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(9)); // bucket 2
        sheet.SetCell(new CellAddress(sheet.Id, 4, 2), new NumberValue(9)); // bucket 2 (duplicate, deduped)

        var iconRange = new GridRange(new CellAddress(sheet.Id, 1, 2), new CellAddress(sheet.Id, 4, 2));
        AddThreeArrowsIconSet(sheet, iconRange);

        var fullRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 2));

        SortDialogPlanner.BuildIconChoices(workbook, sheet, fullRange, columnOffset: 1, hasHeaders: true)
            .Should()
            .Equal(new SortIconChoice(""), new SortIconChoice("3Arrows:0"), new SortIconChoice("3Arrows:2"));
    }

    [Fact]
    public void BuildIconChoicesForSortOn_ScopesToCellIconSortOnly_NoRegressionForOtherSortOns()
    {
        IReadOnlyList<SortIconChoice> iconChoices = [new SortIconChoice(""), new SortIconChoice("3Arrows:1")];

        SortDialogPlanner.BuildIconChoicesForSortOn("Cell Icon", iconChoices).Should().Equal(iconChoices);
        SortDialogPlanner.BuildIconChoicesForSortOn("Cell Values", iconChoices).Should().Equal(new SortIconChoice(""));
        SortDialogPlanner.BuildIconChoicesForSortOn("Cell Color", iconChoices).Should().Equal(new SortIconChoice(""));
    }

    [Fact]
    public void SortDialogPlannerText_DefaultSortOnCellIconLabel_IsCellIcon()
    {
        SortDialogPlannerText.Default.SortOnCellIcon.Should().Be("Cell Icon");
    }
}
