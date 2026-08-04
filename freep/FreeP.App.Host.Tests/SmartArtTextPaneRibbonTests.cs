using Free.Shared.Ribbon;
using FreeP.App.Compositor;

namespace FreeP.App.Host.Tests;

public sealed class SmartArtTextPaneRibbonTests
{
    [StaFact]
    public void SmartArtTextPaneRibbonCommand_InvokesHostCallback()
    {
        var presentation = Presentation.CreateEmpty();
        var editor = new EditingSession(presentation, new PresentationCommandBus(presentation));
        var invoked = false;

        var registry = FreePRibbonCommands.Build(
            new RibbonStateStore(),
            editor,
            onOpenSmartArtTextPane: () => invoked = true);

        registry.TryGet(SmartArtEditingPlanner.OpenTextPaneCommandId, out var command)
            .Should().BeTrue();

        command!.Execute(RibbonCommandContext.Empty);
        invoked.Should().BeTrue();
    }

    [StaFact]
    public void SmartArtConvertToShapesRibbonCommand_InvokesHostCallback()
    {
        var presentation = Presentation.CreateEmpty();
        var editor = new EditingSession(presentation, new PresentationCommandBus(presentation));
        var invoked = false;

        var registry = FreePRibbonCommands.Build(
            new RibbonStateStore(),
            editor,
            onConvertSmartArtToShapes: () => invoked = true);

        registry.TryGet(SmartArtAuthoringPlanner.ConvertToShapesCommandId, out var command)
            .Should().BeTrue();

        command!.Execute(RibbonCommandContext.Empty);
        invoked.Should().BeTrue();
    }
}
