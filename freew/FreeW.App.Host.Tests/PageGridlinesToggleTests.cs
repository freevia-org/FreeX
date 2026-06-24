using FreeW.App.Host.Editing;
using FreeW.App.Host;
using Xunit;

namespace FreeW.App.Host.Tests;

/// <summary>
/// STA tests for the page gridlines toggle on <see cref="DocumentView"/>. Verifies that
/// <see cref="DocumentView.TogglePageGridlines"/> flips the <see cref="DocumentView.ShowPageGridlines"/>
/// property and that the command registered as <c>freew.gridlines</c> is stateful.
/// </summary>
public sealed class PageGridlinesToggleTests
{
    [StaFact]
    public void TogglePageGridlines_StartsOff()
    {
        var view = new DocumentView();
        view.ShowPageGridlines.Should().BeFalse("gridlines are off by default");
    }

    [StaFact]
    public void TogglePageGridlines_TurnsOn()
    {
        var view = new DocumentView();
        var result = view.TogglePageGridlines();
        result.Should().BeTrue();
        view.ShowPageGridlines.Should().BeTrue();
    }

    [StaFact]
    public void TogglePageGridlines_TogglesBackOff()
    {
        var view = new DocumentView();
        view.TogglePageGridlines();
        var result = view.TogglePageGridlines();
        result.Should().BeFalse();
        view.ShowPageGridlines.Should().BeFalse();
    }

    [StaFact]
    public void GridlinesCommand_IsStateful()
    {
        var view = new DocumentView();
        var registry = FreeWRibbonCommands.Build(view, new Free.Shared.Ribbon.RibbonStateStore());

        registry.TryGet("freew.gridlines", out var command).Should().BeTrue();
        command.Should().BeAssignableTo<Free.Shared.Ribbon.IRibbonStatefulCommand>();
    }
}
