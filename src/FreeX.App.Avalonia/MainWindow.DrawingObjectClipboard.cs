using Free.Shared.AppServices;
using FreeX.App.Presentation.DrawingUI;
using FreeX.App.Presentation.Editing;
using FreeX.App.Services;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia;

public sealed partial class MainWindow
{
    private readonly DrawingObjectClipboardSession _drawingObjectClipboard = new();
    private Task? _drawingClipboardWrite;

    private async Task<bool> TryCopySelectedDrawingObjectAsync(bool isCut = false)
    {
        if (_selectedDrawingObjectKind is not { } kind ||
            _selectedDrawingObjectId is not { } objectId ||
            !_drawingObjectClipboard.TryCaptureExisting(
                _session.ActiveSheet,
                kind,
                objectId,
                isCut))
        {
            return false;
        }

        _session.DiscardCellClipboard();
        // Publish the marker even when the drawing cannot be rendered for other applications.
        var marker = _drawingObjectClipboard.Marker;
        var clipboardImage = DrawingObjectClipboardImageRenderer.TryRender(
            _session.ActiveSheet, _session.Workbook, kind, objectId);
        var write = WriteDrawingClipboardAsync(clipboardImage, marker, isCut);
        _drawingClipboardWrite = write;
        try
        {
            await write;
        }
        finally
        {
            if (ReferenceEquals(_drawingClipboardWrite, write))
                _drawingClipboardWrite = null;
        }
        return true;
    }

    private async Task WriteDrawingClipboardAsync(PlatformClipboardImage? clipboardImage, string? marker, bool isCut)
    {
        var write = await _platformClipboard.WriteAsync(WorkbookClipboardSession.AttachMarker(
            new PlatformClipboardContent(Image: clipboardImage), marker));
        if (!_drawingObjectClipboard.MatchesMarker(marker))
            return;

        if (!write.IsSuccess)
        {
            _drawingObjectClipboard.Clear();
            ShowEditIssue(write.Status == PlatformClipboardWriteStatus.Unavailable
                ? UiText.Get("Clipboard_UnavailableOnPlatform")
                : write.ErrorMessage ?? UiText.Get(isCut ? "MainLoc_CutFailed" : "MainLoc_CopyFailed"));
            return;
        }

        SetClipboardMarquee(null, isCut);
    }

    private bool TryHandleDrawingObjectPaste(WorkbookClipboardReadObservation clipboard)
    {
        if (_drawingObjectClipboard.Content is not { } drawing)
            return false;

        if (clipboard.ReadFailed)
        {
            ShowEditIssue(ClipboardFeedbackPlanner.ReadFailed.Resolve(UiText.Get));
            return true;
        }

        if (_drawingObjectClipboard.MatchesMarker(clipboard.Marker))
        {
            PasteClipboardObject(drawing);
            return true;
        }

        _drawingObjectClipboard.Clear();
        return false;
    }

    private void PasteClipboardObject(DrawingObjectClipboardSnapshot objectClip)
    {
        var destinationSheetId = _session.ActiveSheet.Id;
        var command = DrawingObjectClipboardSession.CreatePasteCommand(objectClip, destinationSheetId);
        var outcome = _session.ExecuteReviewCommand(command, _session.ActiveCell);
        if (!outcome.Success)
        {
            ShowEditIssue(outcome.ErrorMessage ?? UiText.Get("MainLoc_PasteFailed"));
            return;
        }

        if (objectClip.IsCut)
        {
            _drawingObjectClipboard.CompletePaste(objectClip);
            SetClipboardMarquee(null, isCut: false);
        }

        if (command.NewObjectId is { } newObjectId)
            SelectPastedDrawingObject(destinationSheetId, objectClip.Kind, newObjectId);
    }

    private void SelectPastedDrawingObject(
        SheetId destinationSheetId,
        SelectionPaneObjectKind kind,
        Guid objectId)
    {
        var sheet = _session.Workbook.GetSheet(destinationSheetId);
        var selection = DrawingObjectClipboardSession.CreatePasteSelectionPlan(
            sheet,
            destinationSheetId,
            kind,
            objectId);

        _session.SelectCell(selection.Anchor);
        _selectedDrawingObjectKind = selection.Kind;
        _selectedDrawingObjectId = selection.ObjectId;
        _ribbonContextSource.OnDrawingObjectSelected(selection.Kind);
        RefreshTableContextualTab();
        RefreshPivotContextualTab();
        RefreshShell(UiText.Format("MainLoc_SelectedX", FormatDrawingObjectKind(selection.Kind)));
    }
}
