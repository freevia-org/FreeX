using Free.Shared.AppServices;
using FreeX.App.Presentation.DrawingUI;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia;

public sealed partial class MainWindow
{
    private readonly DrawingObjectClipboardSession _drawingObjectClipboard = new();

    /// <summary>
    /// R139-shared-clipboard-images (clipboard-drawing-object-no-os-clipboard-write): the internal
    /// <see cref="_drawingObjectClipboard"/> capture below only ever served FreeX-to-FreeX paste
    /// (<see cref="PasteClipboardObject"/>) -- it never touched the real OS clipboard, so Ctrl+C on a
    /// chart/shape/picture/text box followed by Alt-Tab to another app (or even a second FreeX
    /// window) and Ctrl+V pasted nothing at all. Once an object is genuinely captured, also render it
    /// to a PNG-backed <see cref="PlatformClipboardImage"/> (best effort -- never throws) and place
    /// that on the OS clipboard, matching the WPF host's identical fix and the plain cell-range copy
    /// below, which always offers a picture flavor. Async (instead of a synchronous
    /// GetAwaiter().GetResult() block, as the WPF host uses) because Avalonia's IPlatformClipboard
    /// write can genuinely go async, and the r139 "sweep-must-not-block" lens specifically flagged
    /// blocking the UI thread on that exact call shape elsewhere in this round -- this method's own
    /// callers (CutSelectedRangeToClipboardAsync/CopySelectedRangeToClipboardAsync) already await.
    /// The internal <see cref="_drawingObjectClipboard"/> capture/paste path is unaffected either way.
    /// </summary>
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

        if (DrawingObjectClipboardImageRenderer.TryRender(
                _session.ActiveSheet, _session.Workbook.Theme, kind, objectId) is { } clipboardImage)
            _ = await _platformClipboard.WriteAsync(new PlatformClipboardContent(Image: clipboardImage));

        SetClipboardMarquee(null, isCut);
        return true;
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
