using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class StatusBarFocusNavigationSourceGuardTests
{
    [Fact]
    public void StatusBarKeyboardFocus_DelegatesSequencingToServicesPlanner()
    {
        var hostSource = DialogSourceTestSupport.ReadHostSources("MainWindow.KeyboardFocus.cs");
        var plannerSource = DialogSourceTestSupport.ReadAppServicesSource("StatusBarFocusNavigationPlanner.cs");

        plannerSource.Should().Contain("public static class StatusBarFocusNavigationPlanner");
        plannerSource.Should().Contain("StatusBarFocusTarget.ZoomOutButton");
        plannerSource.Should().Contain("StatusBarFocusTarget.PageBreakPreviewButton");
        plannerSource.Should().NotContain("System.Windows");
        plannerSource.Should().NotContain("System.Windows.Input");

        hostSource.Should().Contain("StatusBarFocusNavigationPlanner.BuildKeyboardNavigationPlan(");
        hostSource.Should().Contain("FocusStatusMode()");
        hostSource.Should().Contain("TryFocusElement(StatusModeFocusTarget)");
        hostSource.Should().Contain("Keyboard.Focus(control)");
        hostSource.Should().Contain("FocusManager.SetFocusedElement");
        hostSource.Should().NotContain("StatusBarFocusNavigationPlanner.BuildInitialFocusOrder(");
        hostSource.Should().NotContain("GetStatusBarFocusOrder");
        hostSource.Should().NotContain("TryMoveStatusBarFocus");
        hostSource.Should().NotContain("FindStatusBarFocusIndex");
    }
}
