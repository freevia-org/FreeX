using FreeX.App.Presentation.DrawingUI;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia;

public sealed partial class MainWindow
{
    private readonly DrawingObjectClipboardSession _drawingObjectClipboard = new();

    private bool TryCopySelectedDrawingObject(bool isCut = false)
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
            ShowEditIssue(outcome.ErrorMessage ?? "Paste failed.");
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
        RefreshShell($"Selected {FormatDrawingObjectKind(selection.Kind)}");
    }

    // Test seams drive the same copy/paste entry points used by Ctrl+C/Ctrl+V without depending on
    // a platform clipboard implementation, which is unavailable in Avalonia headless tests.
    internal void SelectDrawingObjectForTest(
        SelectionPaneObjectKind kind,
        Guid objectId,
        CellAddress anchor)
    {
        _session.SelectCell(anchor);
        _selectedDrawingObjectKind = kind;
        _selectedDrawingObjectId = objectId;
        _ribbonContextSource.OnDrawingObjectSelected(kind);
        RefreshTableContextualTab();
        RefreshPivotContextualTab();
    }

    internal void SelectCellForTest(CellAddress address) => SelectCell(address);
}
