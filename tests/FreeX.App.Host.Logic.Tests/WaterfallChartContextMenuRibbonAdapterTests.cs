using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed class WaterfallChartContextMenuRibbonAdapterTests
{
    [Fact]
    public void MapsCheckableSetAsTotal_CarriesHeaderCheckedEnabledAndToggleCommandId()
    {
        var chart = CreateWaterfallChart();
        chart.WaterfallTotalPointIndices = [1];
        var commands = WaterfallChartContextMenuPlanner.BuildCommands(chart, 1);

        var menu = WaterfallChartContextMenuRibbonAdapter.ToRibbonMenu(commands);

        var item = menu.Items.Should().ContainSingle().Subject;
        item.Header.Should().Be("_Set as Total"); // access mnemonic carried verbatim
        item.IsChecked.Should().BeTrue();
        item.IsEnabled.Should().BeTrue();
        item.CommandId.Should().Be(WaterfallChartContextMenuRibbonAdapter.ToggleTotalCommandId);
        item.Kind.Should().Be(Free.Shared.Ribbon.RibbonMenuItemKind.Command);
    }

    [Fact]
    public void MapsUncheckedPoint_IsCheckedFalseNotNull()
    {
        var chart = CreateWaterfallChart();
        chart.WaterfallTotalPointIndices = [1];

        var menu = WaterfallChartContextMenuRibbonAdapter.ToRibbonMenu(
            WaterfallChartContextMenuPlanner.BuildCommands(chart, 3));

        menu.Items.Should().ContainSingle().Which.IsChecked.Should().BeFalse();
    }

    [Fact]
    public void MapsInvalidPoint_DisabledButStillCheckable()
    {
        var chart = CreateWaterfallChart();

        var menu = WaterfallChartContextMenuRibbonAdapter.ToRibbonMenu(
            WaterfallChartContextMenuPlanner.BuildCommands(chart, 99));

        var item = menu.Items.Should().ContainSingle().Subject;
        item.IsEnabled.Should().BeFalse();
        item.IsChecked.Should().BeFalse(); // non-null → renders as a checkable (unchecked) item
    }

    private static ChartModel CreateWaterfallChart()
    {
        var sheetId = SheetId.New();
        return new ChartModel
        {
            Type = ChartType.Waterfall,
            FirstRowIsHeader = true,
            FirstColIsCategories = true,
            DataRange = new GridRange(
                new CellAddress(sheetId, 1, 1),
                new CellAddress(sheetId, 5, 2))
        };
    }
}
