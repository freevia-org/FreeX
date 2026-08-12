using FreeW.App.Presentation.Dialogs;

namespace FreeW.App.Avalonia.Editing;

internal sealed class AvaloniaFindReplaceCommandHost(DocumentView editor) : IFindReplaceDialogCommandHost
{
    public bool FindNext(FindReplaceSearchRequest request) =>
        editor.FindNext(request.Term, request.Options);

    public bool ReplaceNext(FindReplaceReplaceRequest request) =>
        editor.ReplaceNext(request.Term, request.Replacement, request.Options);

    public FindReplaceAllExecutionResult ReplaceAll(FindReplaceReplaceRequest request) =>
        new(editor.ReplaceAll(request.Term, request.Replacement, request.Options));
}
