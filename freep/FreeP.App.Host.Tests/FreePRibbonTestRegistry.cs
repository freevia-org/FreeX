using Free.Shared.Ribbon;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Host.Tests;

internal static class FreePRibbonTestRegistry
{
    public static RibbonCommandRegistry Compose(
        EditingSession editor,
        FreePRibbonHostPorts? ports = null,
        RibbonStateStore? stateStore = null) =>
        FreePRibbonHostRegistryComposer.Build(
            editor,
            stateStore ?? new RibbonStateStore(),
            FreePRibbonHostProfileFactory.Create(ports ?? CreateModelPorts(editor))).Registry;

    private static FreePRibbonHostPorts CreateModelPorts(EditingSession editor) => new()
    {
        ActionEndpoints = CreateModelActionEndpoints(editor),
    };

    public static FreePRibbonHostActionEndpoints CreateModelActionEndpoints(
        EditingSession editor) => new()
    {
        Copy = () => PresentationClipboardWorkflow.CommitCopy(
            PresentationClipboardWorkflow.PrepareInternalWrite(editor)),
        Cut = () => PresentationClipboardWorkflow.CommitCut(
            PresentationClipboardWorkflow.PrepareInternalWrite(editor)),
        Paste = editor.Paste,
        MergeTableCells = () => editor.TryMergeActiveTableCell(),
        SplitTableCell = () => editor.TrySplitActiveTableCell(),
    };
}
