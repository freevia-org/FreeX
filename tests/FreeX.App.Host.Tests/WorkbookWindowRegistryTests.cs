using FluentAssertions;
using FreeX.App.Host;

namespace FreeX.App.Host.Tests;

public sealed class WorkbookWindowRegistryTests
{
    [Fact]
    public void Register_FirstWindow_HasNoTitleSuffixAndReportsHasWindows()
    {
        var registry = new WorkbookWindowRegistry();
        var w1 = new TestWorkbookWindow();

        registry.HasWindows.Should().BeFalse();
        registry.Register(w1);

        registry.HasWindows.Should().BeTrue();
        registry.Count.Should().Be(1);
        w1.Suffix.Should().BeEmpty("a lone window is not numbered");
    }

    [Fact]
    public void Register_SecondWindow_NumbersBothWindowsExcelStyle()
    {
        var registry = new WorkbookWindowRegistry();
        var w1 = new TestWorkbookWindow();
        var w2 = new TestWorkbookWindow();

        registry.Register(w1);
        registry.Register(w2);

        registry.Count.Should().Be(2);
        w1.Suffix.Should().Be(":1");
        w2.Suffix.Should().Be(":2");
    }

    [Fact]
    public void Register_SameWindowTwice_IsIgnored()
    {
        var registry = new WorkbookWindowRegistry();
        var w1 = new TestWorkbookWindow();

        registry.Register(w1);
        registry.Register(w1);

        registry.Count.Should().Be(1);
    }

    [Fact]
    public void Unregister_Closing_RenumbersSurvivorsBackToLoneWindow()
    {
        var registry = new WorkbookWindowRegistry();
        var w1 = new TestWorkbookWindow();
        var w2 = new TestWorkbookWindow();
        var w3 = new TestWorkbookWindow();
        registry.Register(w1);
        registry.Register(w2);
        registry.Register(w3);

        registry.Unregister(w2);

        registry.Count.Should().Be(2);
        registry.Windows.Should().Equal(w1, w3);
        w1.Suffix.Should().Be(":1");
        w3.Suffix.Should().Be(":2", "the third window becomes window 2 after the middle one closes");

        registry.Unregister(w3);

        registry.Count.Should().Be(1);
        w1.Suffix.Should().BeEmpty("the last remaining window drops its number, like Excel");
    }

    [Fact]
    public void NextWindowTarget_CyclesForwardAndWraps()
    {
        var registry = new WorkbookWindowRegistry();
        var w1 = new TestWorkbookWindow();
        var w2 = new TestWorkbookWindow();
        var w3 = new TestWorkbookWindow();
        registry.Register(w1);
        registry.Register(w2);
        registry.Register(w3);

        registry.NextWindowTarget(w1).Should().BeSameAs(w2);
        registry.NextWindowTarget(w2).Should().BeSameAs(w3);
        registry.NextWindowTarget(w3).Should().BeSameAs(w1, "Switch Windows wraps back to the first window");
    }

    [Fact]
    public void NextWindowTarget_SkipsHiddenWindowsAndWrapsAcrossVisibleWindowsOnly()
    {
        var registry = new WorkbookWindowRegistry();
        var w1 = new TestWorkbookWindow();
        var w2 = new TestWorkbookWindow();
        var w3 = new TestWorkbookWindow();
        registry.Register(w1);
        registry.Register(w2);
        registry.Register(w3);
        registry.Hide(w2);

        registry.NextWindowTarget(w1).Should().BeSameAs(w3);
        registry.NextWindowTarget(w3).Should().BeSameAs(w1);
        registry.NextWindowTarget(w2).Should().BeNull("a hidden window is not an active switch-cycle origin");

        registry.SwitchToNextWindow(w1).Should().BeTrue();
        w3.ActivateCount.Should().Be(1);
        w2.ActivateCount.Should().Be(0, "Switch Windows must not re-show a hidden window");
    }

    [Fact]
    public void NextWindowTarget_SingleWindow_HasNoOtherWindow()
    {
        var registry = new WorkbookWindowRegistry();
        var w1 = new TestWorkbookWindow();
        registry.Register(w1);

        registry.NextWindowTarget(w1).Should().BeNull();
    }

    [Fact]
    public void NextWindowTarget_WithOnlyOneVisibleWindow_HasNoOtherWindow()
    {
        var registry = new WorkbookWindowRegistry();
        var w1 = new TestWorkbookWindow();
        var w2 = new TestWorkbookWindow();
        registry.Register(w1);
        registry.Register(w2);
        registry.Hide(w2);

        registry.NextWindowTarget(w1).Should().BeNull();
        registry.SwitchToNextWindow(w1).Should().BeFalse();
        w2.ActivateCount.Should().Be(0);
    }

    [Fact]
    public void PreviousWindowTarget_CyclesBackwardAndWraps()
    {
        var registry = new WorkbookWindowRegistry();
        var w1 = new TestWorkbookWindow();
        var w2 = new TestWorkbookWindow();
        var w3 = new TestWorkbookWindow();
        registry.Register(w1);
        registry.Register(w2);
        registry.Register(w3);

        registry.PreviousWindowTarget(w1).Should().BeSameAs(w3, "reverse cycling wraps back to the last window");
        registry.PreviousWindowTarget(w2).Should().BeSameAs(w1);
        registry.PreviousWindowTarget(w3).Should().BeSameAs(w2);
    }

    [Fact]
    public void PreviousWindowTarget_SkipsHiddenWindowsAndWrapsAcrossVisibleWindowsOnly()
    {
        var registry = new WorkbookWindowRegistry();
        var w1 = new TestWorkbookWindow();
        var w2 = new TestWorkbookWindow();
        var w3 = new TestWorkbookWindow();
        registry.Register(w1);
        registry.Register(w2);
        registry.Register(w3);
        registry.Hide(w2);

        registry.PreviousWindowTarget(w1).Should().BeSameAs(w3);
        registry.PreviousWindowTarget(w3).Should().BeSameAs(w1);
        registry.PreviousWindowTarget(w2).Should().BeNull("a hidden window is not an active switch-cycle origin");

        registry.SwitchToPreviousWindow(w1).Should().BeTrue();
        w3.ActivateCount.Should().Be(1);
        w2.ActivateCount.Should().Be(0, "Switch Windows must not re-show a hidden window");
    }

    [Fact]
    public void PreviousWindowTarget_SingleWindow_HasNoOtherWindow()
    {
        var registry = new WorkbookWindowRegistry();
        var w1 = new TestWorkbookWindow();
        registry.Register(w1);

        registry.PreviousWindowTarget(w1).Should().BeNull();
    }

    [Fact]
    public void SwitchToNextWindow_ActivatesTheNextWindowOnly()
    {
        var registry = new WorkbookWindowRegistry();
        var w1 = new TestWorkbookWindow();
        var w2 = new TestWorkbookWindow();
        registry.Register(w1);
        registry.Register(w2);

        registry.SwitchToNextWindow(w1).Should().BeTrue();

        w2.ActivateCount.Should().Be(1);
        w1.ActivateCount.Should().Be(0);
    }

    [Fact]
    public void SwitchToPreviousWindow_ActivatesThePreviousWindowOnly()
    {
        var registry = new WorkbookWindowRegistry();
        var w1 = new TestWorkbookWindow();
        var w2 = new TestWorkbookWindow();
        registry.Register(w1);
        registry.Register(w2);

        registry.SwitchToPreviousWindow(w1).Should().BeTrue();

        w2.ActivateCount.Should().Be(1);
        w1.ActivateCount.Should().Be(0);
    }

    [Fact]
    public void SwitchToNextWindow_SingleWindow_DoesNothing()
    {
        var registry = new WorkbookWindowRegistry();
        var w1 = new TestWorkbookWindow();
        registry.Register(w1);

        registry.SwitchToNextWindow(w1).Should().BeFalse();
        w1.ActivateCount.Should().Be(0);
    }

    [Fact]
    public void SwitchToPreviousWindow_SingleWindow_DoesNothing()
    {
        var registry = new WorkbookWindowRegistry();
        var w1 = new TestWorkbookWindow();
        registry.Register(w1);

        registry.SwitchToPreviousWindow(w1).Should().BeFalse();
        w1.ActivateCount.Should().Be(0);
    }

    [Fact]
    public void NotifyWorkbookChanged_RefreshesEveryWindowExceptTheOrigin()
    {
        var registry = new WorkbookWindowRegistry();
        var origin = new TestWorkbookWindow();
        var other1 = new TestWorkbookWindow();
        var other2 = new TestWorkbookWindow();
        registry.Register(origin);
        registry.Register(other1);
        registry.Register(other2);

        registry.NotifyWorkbookChanged(origin);

        origin.RefreshCount.Should().Be(0, "the originating window already reflects its own change");
        other1.RefreshCount.Should().Be(1);
        other2.RefreshCount.Should().Be(1);
    }

    [Fact]
    public void NotifyWorkbookChanged_SingleWindow_RefreshesNobody()
    {
        var registry = new WorkbookWindowRegistry();
        var w1 = new TestWorkbookWindow();
        registry.Register(w1);

        registry.NotifyWorkbookChanged(w1);

        w1.RefreshCount.Should().Be(0);
    }

    [Fact]
    public void Windows_ReportsRegistrationOrder()
    {
        var registry = new WorkbookWindowRegistry();
        var w1 = new TestWorkbookWindow();
        var w2 = new TestWorkbookWindow();
        registry.Register(w1);
        registry.Register(w2);

        registry.Windows.Should().Equal(w1, w2);
        registry.IndexOf(w2).Should().Be(1);
    }
}
