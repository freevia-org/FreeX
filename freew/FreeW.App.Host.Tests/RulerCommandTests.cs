using Free.Shared.Ribbon;
using FreeW.App.Host.Editing;
using FreeW.Core.Model;

namespace FreeW.App.Host.Tests;

public sealed class RulerCommandTests
{
    [StaFact]
    public void RulerCommand_TogglesHostVisibilityState()
    {
        var visible = true;
        var registry = BuildRegistry(() => visible = !visible, () => visible);

        registry.TryGet("freew.ruler", out var command).Should().BeTrue();
        var stateful = command.Should().BeAssignableTo<IRibbonStatefulCommand>().Subject;
        stateful.GetState().IsChecked.Should().BeTrue();

        command!.Execute(RibbonCommandContext.Empty);

        visible.Should().BeFalse();
        stateful.GetState().IsChecked.Should().BeFalse();
    }

    [StaFact]
    public void RulerCommand_IsAbsent_WhenHostDoesNotSupplyVisibilityCallbacks()
    {
        var editor = new DocumentView();
        editor.LoadModel(TextDocument.CreateEmpty());

        var registry = FreeWRibbonCommands.Build(editor, new RibbonStateStore());

        registry.TryGet("freew.ruler", out _).Should().BeFalse();
    }

    private static RibbonCommandRegistry BuildRegistry(Action toggle, Func<bool> isVisible)
    {
        var editor = new DocumentView();
        editor.LoadModel(TextDocument.CreateEmpty());

        return FreeWRibbonCommands.Build(
            editor,
            new RibbonStateStore(),
            onPrintPreview: null,
            onToggleNavPane: null,
            isNavPaneVisible: null,
            onToggleReadMode: null,
            isReadModeActive: null,
            onTogglePrintLayout: null,
            isPrintLayoutActive: null,
            onToggleOutlineView: null,
            isOutlineViewActive: null,
            onZoomDialog: null,
            onToggleRuler: toggle,
            isRulerVisible: isVisible);
    }
}
