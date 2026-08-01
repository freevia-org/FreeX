using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia;

public sealed partial class MainWindow
{
    // WPF keeps object copies separate from the cell-range clipboard because a selected drawing
    // object can sit over an unrelated active cell. Keep the same distinction in the Avalonia host.
    private sealed record InternalObjectClipboard(
        SheetId SourceSheetId,
        SelectionPaneObjectKind Kind,
        Guid ObjectId);

    private InternalObjectClipboard? _internalObjectClipboard;

    private bool TryCopySelectedDrawingObject()
    {
        if (_selectedDrawingObjectKind is not { } kind ||
            _selectedDrawingObjectId is not { } objectId ||
            objectId == Guid.Empty ||
            !ContainsDrawingObject(_session.ActiveSheet, kind, objectId))
        {
            return false;
        }

        _internalObjectClipboard = new InternalObjectClipboard(
            _session.ActiveSheet.Id,
            kind,
            objectId);
        SetClipboardMarquee(null, isCut: false);
        return true;
    }

    private static bool ContainsDrawingObject(Sheet sheet, SelectionPaneObjectKind kind, Guid objectId) => kind switch
    {
        SelectionPaneObjectKind.Chart => sheet.Charts.Any(chart => chart.Id == objectId),
        SelectionPaneObjectKind.Shape => sheet.DrawingShapes.Any(shape => shape.Id == objectId),
        SelectionPaneObjectKind.Picture => sheet.Pictures.Any(picture => picture.Id == objectId),
        SelectionPaneObjectKind.TextBox => sheet.TextBoxes.Any(textBox => textBox.Id == objectId),
        _ => false,
    };

    private void PasteClipboardObject(InternalObjectClipboard objectClip)
    {
        var destinationSheetId = _session.ActiveSheet.Id;
        var command = new DuplicateDrawingObjectCommand(
            objectClip.SourceSheetId,
            destinationSheetId,
            objectClip.Kind,
            objectClip.ObjectId);
        var outcome = _session.ExecuteReviewCommand(command, _session.ActiveCell);
        if (!outcome.Success)
        {
            ShowEditIssue(outcome.ErrorMessage ?? "Paste failed.");
            return;
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
        var anchor = kind switch
        {
            SelectionPaneObjectKind.Chart when sheet?.Charts.Find(chart => chart.Id == objectId) is { } chart =>
                new CellAddress(destinationSheetId, chart.DataRange.Start.Row, chart.DataRange.Start.Col),
            SelectionPaneObjectKind.Shape when sheet?.DrawingShapes.Find(shape => shape.Id == objectId) is { } shape =>
                shape.Anchor,
            SelectionPaneObjectKind.Picture when sheet?.Pictures.Find(picture => picture.Id == objectId) is { } picture =>
                picture.Anchor,
            SelectionPaneObjectKind.TextBox when sheet?.TextBoxes.Find(textBox => textBox.Id == objectId) is { } textBox =>
                textBox.Anchor,
            _ => new CellAddress(destinationSheetId, 1, 1),
        };

        _session.SelectCell(anchor);
        _selectedDrawingObjectKind = kind;
        _selectedDrawingObjectId = objectId;
        _ribbonContextSource.OnDrawingObjectSelected(kind);
        RefreshTableContextualTab();
        RefreshPivotContextualTab();
        RefreshShell($"Selected {FormatDrawingObjectKind(kind)}");
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
