using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.DrawingUI;

public sealed record DrawingObjectClipboardSnapshot(
    SheetId SourceSheetId,
    SelectionPaneObjectKind Kind,
    Guid ObjectId,
    bool IsCut);

public sealed class DrawingObjectClipboardSession
{
    public DrawingObjectClipboardSnapshot? Content { get; private set; }

    public bool HasContent => Content is not null;

    public bool TryCapture(
        SheetId sourceSheetId,
        SelectionPaneObjectKind? kind,
        Guid objectId,
        bool isCut = false)
    {
        if (kind is null || objectId == Guid.Empty || !IsSupportedKind(kind.Value))
            return false;

        Content = new DrawingObjectClipboardSnapshot(sourceSheetId, kind.Value, objectId, isCut);
        return true;
    }

    public bool TryCaptureExisting(
        Sheet sourceSheet,
        SelectionPaneObjectKind? kind,
        Guid objectId,
        bool isCut = false)
    {
        ArgumentNullException.ThrowIfNull(sourceSheet);

        return kind is { } resolvedKind &&
               ContainsObject(sourceSheet, resolvedKind, objectId) &&
               TryCapture(sourceSheet.Id, resolvedKind, objectId, isCut);
    }

    public void Clear() => Content = null;

    public void CompletePaste(DrawingObjectClipboardSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (snapshot.IsCut && Content == snapshot)
            Content = null;
    }

    public static DuplicateDrawingObjectCommand CreatePasteCommand(
        DrawingObjectClipboardSnapshot snapshot,
        SheetId destinationSheetId)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        return new DuplicateDrawingObjectCommand(
            snapshot.SourceSheetId,
            destinationSheetId,
            snapshot.Kind,
            snapshot.ObjectId,
            removeSource: snapshot.IsCut);
    }

    public static bool ContainsObject(
        Sheet sheet,
        SelectionPaneObjectKind kind,
        Guid objectId)
    {
        ArgumentNullException.ThrowIfNull(sheet);

        if (objectId == Guid.Empty)
            return false;

        return kind switch
        {
            SelectionPaneObjectKind.Chart => sheet.Charts.Any(chart => chart.Id == objectId),
            SelectionPaneObjectKind.Shape => sheet.DrawingShapes.Any(shape => shape.Id == objectId),
            SelectionPaneObjectKind.Picture => sheet.Pictures.Any(picture => picture.Id == objectId),
            SelectionPaneObjectKind.TextBox => sheet.TextBoxes.Any(textBox => textBox.Id == objectId),
            _ => false,
        };
    }

    public static CellAddress ResolveAnchor(
        Sheet? destinationSheet,
        SheetId destinationSheetId,
        SelectionPaneObjectKind kind,
        Guid objectId) =>
        kind switch
        {
            SelectionPaneObjectKind.Chart when destinationSheet?.Charts.Find(chart => chart.Id == objectId) is { } chart =>
                new CellAddress(destinationSheetId, chart.DataRange.Start.Row, chart.DataRange.Start.Col),
            SelectionPaneObjectKind.Shape when destinationSheet?.DrawingShapes.Find(shape => shape.Id == objectId) is { } shape =>
                shape.Anchor,
            SelectionPaneObjectKind.Picture when destinationSheet?.Pictures.Find(picture => picture.Id == objectId) is { } picture =>
                picture.Anchor,
            SelectionPaneObjectKind.TextBox when destinationSheet?.TextBoxes.Find(textBox => textBox.Id == objectId) is { } textBox =>
                textBox.Anchor,
            _ => new CellAddress(destinationSheetId, 1, 1),
        };

    private static bool IsSupportedKind(SelectionPaneObjectKind kind) => kind is
        SelectionPaneObjectKind.Chart or
        SelectionPaneObjectKind.Shape or
        SelectionPaneObjectKind.Picture or
        SelectionPaneObjectKind.TextBox;
}
