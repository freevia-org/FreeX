using FluentAssertions;
using Free.Shared.Ribbon;
using FreeW.App.Host.Editing;
using FreeW.App.Presentation.Ribbon;

namespace FreeW.App.Host.Tests;

public sealed class FreeWRibbonHostExecutionBoundaryTests
{
    [StaFact]
    public void Wpf_registry_routes_shell_commands_through_the_shared_host_profile()
    {
        var opened = 0;
        var found = 0;
        var accepted = 0;
        var reviewingPaneVisible = false;
        var ports = FreeWRibbonHostExecutionPorts.Empty with
        {
            Open = () => opened++,
            OpenFindReplaceDialog = () => found++,
            AcceptThisChange = () => accepted++,
            ToggleReviewingPane = () => reviewingPaneVisible = !reviewingPaneVisible,
            IsReviewingPaneVisible = () => reviewingPaneVisible,
        };
        var registry = FreeWRibbonCommands.Build(
            new DocumentView(),
            new RibbonStateStore(),
            ports);

        Execute(registry, "freew.open");
        Execute(registry, "freew.find");
        Execute(registry, "freew.accept-this");
        Execute(registry, "freew.reviewing-pane");

        opened.Should().Be(1);
        found.Should().Be(1);
        accepted.Should().Be(1);
        reviewingPaneVisible.Should().BeTrue();
    }

    private static void Execute(RibbonCommandRegistry registry, string commandId)
    {
        registry.TryGet(commandId, out var command).Should().BeTrue();
        command!.Execute(RibbonCommandContext.Empty);
    }
}
