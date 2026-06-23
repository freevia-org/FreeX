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
