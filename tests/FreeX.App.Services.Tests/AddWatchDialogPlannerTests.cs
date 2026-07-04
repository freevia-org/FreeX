using FluentAssertions;
using FreeX.App.Services;

namespace FreeX.App.Services.Tests;

public sealed class AddWatchDialogPlannerTests
{
    [Fact]
    public void DialogContract_UsesStableWindowsSizedSurfaceAndAutomationIds()
    {
        AddWatchDialogPlanner.TitleKey.Should().Be("AddWatch_Title");
        AddWatchDialogPlanner.SelectedRangeLabelKey.Should().Be("AddWatch_SelectedRangeLabel");
        AddWatchDialogPlanner.BodyTextKey.Should().Be("AddWatch_BodyText");
        AddWatchDialogPlanner.Width.Should().Be(360);
        AddWatchDialogPlanner.Height.Should().Be(170);
        AddWatchDialogPlanner.ButtonWidth.Should().Be(76);
        AddWatchDialogPlanner.DialogAutomationId.Should().Be("AddWatchDialog");
        AddWatchDialogPlanner.SelectedRangeAutomationId.Should().Be("AddWatchSelectedRangeBox");
        AddWatchDialogPlanner.AddButtonAutomationId.Should().Be("AddWatchAddButton");
        AddWatchDialogPlanner.CancelButtonAutomationId.Should().Be("AddWatchCancelButton");
    }
}

public sealed class WatchWindowDialogPlannerTests
{
    [Fact]
    public void DialogContract_UsesStableWindowsSizedSurfaceAndColumns()
    {
        WatchWindowDialogPlanner.TitleKey.Should().Be("WatchWindow_WatchWindow");
        WatchWindowDialogPlanner.DialogAutomationId.Should().Be("WatchWindowDialog");
        WatchWindowDialogPlanner.BookColumnWidth.Should().Be(90);
        WatchWindowDialogPlanner.SheetColumnWidth.Should().Be(110);
        WatchWindowDialogPlanner.NameColumnWidth.Should().Be(80);
        WatchWindowDialogPlanner.CellColumnWidth.Should().Be(70);
        WatchWindowDialogPlanner.ValueColumnWidth.Should().Be(120);
        WatchWindowDialogPlanner.FormulaColumnWidth.Should().Be(170);
        WatchWindowDialogPlanner.ColumnsWidth.Should().Be(640);
        WatchWindowDialogPlanner.ChromeAndPaddingWidth.Should().Be(120);
        WatchWindowDialogPlanner.Width.Should().Be(760);
        WatchWindowDialogPlanner.Height.Should().Be(320);
        WatchWindowDialogPlanner.MinWidth.Should().Be(720);
        WatchWindowDialogPlanner.MinHeight.Should().Be(220);
    }
}
