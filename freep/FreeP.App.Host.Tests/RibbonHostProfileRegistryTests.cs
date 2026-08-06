using Free.Shared.Ribbon;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Host.Tests;

public sealed class RibbonHostProfileRegistryTests
{
    [Fact]
    public void WpfRegistryMatchesPortableInventoryAndOwnsOnlyOleNativeCommands()
    {
        var editor = MakeEditor();
        var expectedCommon = FreePRibbonCommandWorkflow.Build(
            editor,
            new RibbonStateStore()).CommonCommandIds;
        var registry = FreePRibbonCommands.Build(new RibbonStateStore(), editor);

        foreach (var commandId in expectedCommon)
            registry.TryGet(commandId, out _).Should().BeTrue($"{commandId} is portable");
        foreach (var commandId in FreePRibbonHostRegistryComposer.OleCommandIds)
            registry.TryGet(commandId, out _).Should().BeTrue($"{commandId} is WPF-native OLE");
        foreach (var commandId in FreePRibbonHostRegistryComposer.FileCommandIds)
            registry.TryGet(commandId, out _).Should().BeFalse($"{commandId} stays in WPF Backstage");
    }

    private static EditingSession MakeEditor()
    {
        var presentation = Presentation.CreateEmpty();
        return new EditingSession(presentation, new PresentationCommandBus(presentation));
    }
}
