using FreeX.Core.Model;

namespace FreeX.App.Host;

public partial class MainWindow
{
    private bool _parityCaptureWorkbookPrepared;
    private CommentListWindow? _reviewNotesWindow;

    partial void AdjustExternalInitialWorkbookCreation(ref bool shouldCreate)
    {
        if (_parityCaptureWorkbookPrepared)
            shouldCreate = false;
    }

    partial void StartExternalLoadedWorkflows()
    {
        TryStartScreenshotTour();
        TryStartSheetTabVisualTour();
        TryStartSheetTabWorkflowsTour();
        TryStartAccentBarVisualTour();
    }

    partial void RefreshExternalReviewWindows(Sheet sheet) =>
        _reviewNotesWindow?.Refresh(CommentListWindow.CreateNoteItems(sheet.Comments));

    internal void AdoptWorkbookForParityCapture(Workbook workbook)
    {
        ArgumentNullException.ThrowIfNull(workbook);
        if (workbook.Sheets.Count == 0)
            throw new ArgumentException("Parity capture requires a workbook with a worksheet.", nameof(workbook));
        if (workbook.Sheets.Any(sheet => sheet.Id.Value == Guid.Empty))
            throw new ArgumentException("Parity capture requires non-empty sheet identities.", nameof(workbook));

        _parityCaptureWorkbookPrepared = true;
        CloseFindReplaceDialogIfOpen();
        AdoptWorkbookAsInitial(workbook);
    }

    private void ShowNotesListForParityCapture()
    {
        var sheet = _workbook.GetSheet(_currentSheetId)
            ?? throw new InvalidOperationException("Parity capture requires an active worksheet.");
        _reviewNotesWindow = ShowOrRefreshCommentListWindow(
            _reviewNotesWindow,
            UiText.Get("MainWindow_Text_Notes"),
            CommentListWindow.CreateNoteItems(sheet.Comments),
            window => _reviewNotesWindow = window);
    }
}
