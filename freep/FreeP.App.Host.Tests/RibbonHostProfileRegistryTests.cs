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
        var result = FreePRibbonHostRegistryComposer.Build(
            editor,
            new RibbonStateStore(),
            FreePRibbonHostProfileFactory.Create(new FreePRibbonHostPorts
            {
                OleCommands = new FreePRibbonOleCommandEndpoints
                {
                    InsertEmbeddedObject = () => { },
                    TryOpenInlineEmbeddedObject = () => false,
                },
            }));

        foreach (var commandId in expectedCommon)
            result.Registry.TryGet(commandId, out _).Should().BeTrue($"{commandId} is portable");
        result.NativeCommandIds.Should().Equal(FreePRibbonHostRegistryComposer.OleCommandIds);
        foreach (var commandId in FreePRibbonHostRegistryComposer.FileCommandIds)
            result.Registry.TryGet(commandId, out _).Should().BeFalse($"{commandId} stays in WPF Backstage");
    }

    private static EditingSession MakeEditor()
    {
        var presentation = Presentation.CreateEmpty();
        return new EditingSession(presentation, new PresentationCommandBus(presentation));
    }
}
