using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

public sealed partial class PivotTableCommandTests
{
    [Fact]
    public void ConfigurePivotTableOptionsCommand_UpdatesShowExpandCollapseButtonsAndUndoRestores()
    {
        var workbook = new Workbook("PivotShowDrillOptionsCommandTest");
        var sheet = workbook.AddSheet("Data");
        SeedData(sheet);
        var ctx = new TestCommandContext(workbook);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "B3"),
            TargetRange = Range(sheet, "D3", "F8"),
            ShowFieldHeaders = true,
            ShowExpandCollapseButtons = true,
            ShowContextualTooltips = true,
            ShowPropertiesInTooltips = true,
            ShowClassicLayout = false,
            MergeAndCenterLabels = false,
            PageOverThenDown = false,
            PageWrap = 0,
            PrintExpandCollapseButtons = true
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(1, "Sum of Amount", "sum"));
        sheet.PivotTables.Add(pivot);

        var command = new ConfigurePivotTableOptionsCommand(
            sheet.Id,
            "PivotTable1",
            showRowGrandTotals: true,
            showColumnGrandTotals: true,
            showSubtotals: false,
            subtotalPlacement: PivotSubtotalPlacement.Bottom,
            repeatItemLabels: true,
            blankLineAfterItems: false,
            styleName: "PivotStyleLight16",
            mergeAndCenterLabels: true,
            showExpandCollapseButtons: false,
            showContextualTooltips: false,
            showPropertiesInTooltips: false,
            showClassicLayout: true,
            pageOverThenDown: true,
            pageWrap: 4,
            printExpandCollapseButtons: false,
            showFieldHeaders: false);

        command.Apply(ctx).Success.Should().BeTrue();

        pivot.MergeAndCenterLabels.Should().BeTrue();
        pivot.ShowExpandCollapseButtons.Should().BeFalse();
        pivot.ShowContextualTooltips.Should().BeFalse();
        pivot.ShowPropertiesInTooltips.Should().BeFalse();
        pivot.ShowClassicLayout.Should().BeTrue();
        pivot.PageOverThenDown.Should().BeTrue();
        pivot.PageWrap.Should().Be(4);
        pivot.PrintExpandCollapseButtons.Should().BeFalse();
        pivot.ShowFieldHeaders.Should().BeFalse();

        command.Revert(ctx);

        pivot.MergeAndCenterLabels.Should().BeFalse();
        pivot.ShowExpandCollapseButtons.Should().BeTrue();
        pivot.ShowContextualTooltips.Should().BeTrue();
        pivot.ShowPropertiesInTooltips.Should().BeTrue();
        pivot.ShowClassicLayout.Should().BeFalse();
        pivot.PageOverThenDown.Should().BeFalse();
        pivot.PageWrap.Should().Be(0);
        pivot.PrintExpandCollapseButtons.Should().BeTrue();
        pivot.ShowFieldHeaders.Should().BeTrue();
    }

    [Fact]
    public void ConfigurePivotTableOptionsCommand_UpdatesShowItemsWithNoDataAndUndoRestores()
    {
        var workbook = new Workbook("PivotShowItemsWithNoDataCommandTest");
        var sheet = workbook.AddSheet("Data");
        SeedData(sheet);
        var ctx = new TestCommandContext(workbook);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "B3"),
            TargetRange = Range(sheet, "D3", "F8"),
            ShowItemsWithNoDataOnRows = false,
            ShowItemsWithNoDataOnColumns = false
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(1, "Sum of Amount", "sum"));
        sheet.PivotTables.Add(pivot);

        var command = new ConfigurePivotTableOptionsCommand(
            sheet.Id,
            "PivotTable1",
            showRowGrandTotals: true,
            showColumnGrandTotals: true,
            showSubtotals: false,
            subtotalPlacement: PivotSubtotalPlacement.Bottom,
            repeatItemLabels: true,
            blankLineAfterItems: false,
            styleName: "PivotStyleLight16",
            showItemsWithNoDataOnRows: true,
            showItemsWithNoDataOnColumns: true);

        command.Apply(ctx).Success.Should().BeTrue();

        pivot.ShowItemsWithNoDataOnRows.Should().BeTrue();
        pivot.ShowItemsWithNoDataOnColumns.Should().BeTrue();

        command.Revert(ctx);

        pivot.ShowItemsWithNoDataOnRows.Should().BeFalse();
        pivot.ShowItemsWithNoDataOnColumns.Should().BeFalse();
    }
}
