using FreeW.App.Presentation.Dialogs;

namespace FreeW.App.Avalonia.Editing;

internal sealed class AvaloniaFindReplaceCommandHost(DocumentView editor) : IFindReplaceDialogCommandHost
{
    public bool FindNext(FindReplaceSearchRequest request) =>
        editor.FindNext(request);

    public bool ReplaceNext(FindReplaceReplaceRequest request) =>
        editor.ReplaceNext(request);

    public FindReplaceAllExecutionResult ReplaceAll(FindReplaceReplaceRequest request) =>
        editor.ReplaceAll(request);
}
