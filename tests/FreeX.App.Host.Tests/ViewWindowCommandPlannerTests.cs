using FluentAssertions;
using FreeX.App.Host;

namespace FreeX.App.Host.Tests;

public sealed class ViewWindowCommandPlannerTests
{
    [Fact]
    public void SingleVisibleWorkbook_DisablesUnsafeDependentWindowCommands()
    {
        var state = ViewWorkbookWindowState.SingleVisibleWorkbook;

        var plans = Enum.GetValues<ViewWindowCommandKind>()
            .ToDictionary(command => command, command => ViewWindowCommandPlanner.CreatePlan(command, state));

        plans[ViewWindowCommandKind.NewWindow].IsEnabled.Should().BeTrue();
        plans[ViewWindowCommandKind.NewWindow].Availability.Should().Be(ViewWindowCommandAvailability.DeferredMultiWindowHosting);
        plans[ViewWindowCommandKind.NewWindow].TooltipDescriptionResourceKey
            .Should().Be("MainWindow_TooltipDescription_DeferredRequiresMultipleLiveWindowsOverTheSameWorkbookSession");

        plans[ViewWindowCommandKind.Hide].IsEnabled.Should().BeFalse();
        plans[ViewWindowCommandKind.Hide].Availability.Should().Be(ViewWindowCommandAvailability.CannotHideOnlyVisibleWindow);
        plans[ViewWindowCommandKind.Hide].TooltipDescriptionResourceKey
            .Should().Be("MainWindow_TooltipDescription_UnavailableCannotHideOnlyVisibleWorkbookWindow");

        plans[ViewWindowCommandKind.Unhide].IsEnabled.Should().BeFalse();
        plans[ViewWindowCommandKind.Unhide].Availability.Should().Be(ViewWindowCommandAvailability.NoHiddenWorkbookWindows);

        plans[ViewWindowCommandKind.ViewSideBySide].IsEnabled.Should().BeFalse();
        plans[ViewWindowCommandKind.ViewSideBySide].IsChecked.Should().BeFalse();
        plans[ViewWindowCommandKind.ViewSideBySide].Availability.Should().Be(ViewWindowCommandAvailability.RequiresSecondVisibleWindow);

        plans[ViewWindowCommandKind.SynchronousScrolling].IsEnabled.Should().BeFalse();
        plans[ViewWindowCommandKind.SynchronousScrolling].IsChecked.Should().BeFalse();
        plans[ViewWindowCommandKind.SynchronousScrolling].Availability.Should().Be(ViewWindowCommandAvailability.RequiresSideBySidePair);

        plans[ViewWindowCommandKind.ResetWindowPosition].IsEnabled.Should().BeFalse();
        plans[ViewWindowCommandKind.ResetWindowPosition].Availability.Should().Be(ViewWindowCommandAvailability.RequiresSideBySidePair);

        plans[ViewWindowCommandKind.SwitchWindows].IsEnabled.Should().BeFalse();
        plans[ViewWindowCommandKind.SwitchWindows].Availability.Should().Be(ViewWindowCommandAvailability.RequiresSecondVisibleWindow);
    }

    [Fact]
    public void MultiWindowState_EnablesVisibleWindowAndPairCommands()
    {
        var state = new ViewWorkbookWindowState(
            visibleWindowCount: 2,
            hiddenWindowCount: 1,
            isSideBySideActive: true,
            isSynchronousScrollingEnabled: true);

        ViewWindowCommandPlanner.CreatePlan(ViewWindowCommandKind.Hide, state).IsEnabled.Should().BeTrue();
        ViewWindowCommandPlanner.CreatePlan(ViewWindowCommandKind.Unhide, state).IsEnabled.Should().BeTrue();
        ViewWindowCommandPlanner.CreatePlan(ViewWindowCommandKind.SwitchWindows, state).IsEnabled.Should().BeTrue();

        var sideBySide = ViewWindowCommandPlanner.CreatePlan(ViewWindowCommandKind.ViewSideBySide, state);
        sideBySide.IsEnabled.Should().BeTrue();
        sideBySide.IsChecked.Should().BeTrue();

        var sync = ViewWindowCommandPlanner.CreatePlan(ViewWindowCommandKind.SynchronousScrolling, state);
        sync.IsEnabled.Should().BeTrue();
        sync.IsChecked.Should().BeTrue();

        ViewWindowCommandPlanner.CreatePlan(ViewWindowCommandKind.ResetWindowPosition, state)
            .IsEnabled.Should().BeTrue();
    }

    [Fact]
    public void State_NormalizesImpossibleCountsAndPairFlags()
    {
        var negative = new ViewWorkbookWindowState(-1, -2, true, true);

        negative.VisibleWindowCount.Should().Be(0);
        negative.HiddenWindowCount.Should().Be(0);
        negative.IsSideBySideActive.Should().BeFalse();
        negative.IsSynchronousScrollingEnabled.Should().BeFalse();

        var singleWithPairFlags = new ViewWorkbookWindowState(1, 0, true, true);

        singleWithPairFlags.IsSideBySideActive.Should().BeFalse();
        singleWithPairFlags.IsSynchronousScrollingEnabled.Should().BeFalse();
    }

    [Theory]
    [InlineData("New Window", ViewWindowCommandKind.NewWindow)]
    [InlineData("Hide", ViewWindowCommandKind.Hide)]
    [InlineData("Unhide", ViewWindowCommandKind.Unhide)]
    [InlineData("View Side by Side", ViewWindowCommandKind.ViewSideBySide)]
    [InlineData("Synchronous Scrolling", ViewWindowCommandKind.SynchronousScrolling)]
    [InlineData("Reset Window Position", ViewWindowCommandKind.ResetWindowPosition)]
    [InlineData("Switch Windows", ViewWindowCommandKind.SwitchWindows)]
    public void TryParseCommandName_MapsRibbonCommandNames(
        string commandName,
        ViewWindowCommandKind expected)
    {
        ViewWindowCommandPlanner.TryParseCommandName(commandName, out var command).Should().BeTrue();
        command.Should().Be(expected);
    }

    [Fact]
    public void CreateMessage_ExplainsPlannerBackedNewWindowBoundary()
    {
        var plan = ViewWindowCommandPlanner.CreatePlan(
            ViewWindowCommandKind.NewWindow,
            ViewWorkbookWindowState.SingleVisibleWorkbook);

        var message = ViewWindowCommandPlanner.CreateMessage("New Window", plan);

        message.Title.Should().Be("New Window");
        message.Body.Should().Contain("deferred");
        message.Body.Should().Contain("additional live workbook windows");
        message.Body.Should().Contain("single-window state");
    }
}
