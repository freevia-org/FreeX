using FreeW.App.Presentation.Dialogs;

namespace FreeW.App.Avalonia.Editing;

internal sealed class AvaloniaFindReplaceCommandHost(DocumentView editor, FindReplaceDialogText? text = null)
    : IFindReplaceDialogCommandHost
{
    private readonly FindReplaceDialogText _text = text ?? new FindReplaceDialogText(UiText.Get);

    public bool FindNext(FindReplaceSearchRequest request) =>
        editor.FindNext(request);

    public bool ReplaceNext(FindReplaceReplaceRequest request) =>
        editor.ReplaceNext(request);

    public FindReplaceAllExecutionResult ReplaceAll(FindReplaceReplaceRequest request) =>
        editor.ReplaceAll(request);

    // A match in a table nested in a table cell is not selected on this surface (a nested table holds no
    // caret here): the status line says where it is, exactly as the WPF host reports it.
    public string? LastMatchStatus =>
        editor.FindNestedMatch is { } nested
            ? FindReplaceBodyPlanner.DescribeNested(nested, _text)
            : null;
}
