using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.DrawingUI;

public sealed record DrawingObjectClipboardSnapshot(
    SheetId SourceSheetId,
    SelectionPaneObjectKind Kind,
    Guid ObjectId,
    bool IsCut);

public sealed record DrawingObjectPasteSelectionPlan(
    SelectionPaneObjectKind Kind,
    Guid ObjectId,
    CellAddress Anchor);

public sealed class DrawingObjectClipboardSession
{
    public DrawingObjectClipboardSnapshot? Content { get; private set; }

    public bool HasContent => Content is not null;

    /// <summary>
    /// shared-clipboard-formats-F1: an opaque token generated fresh every time <see
    /// cref="TryCapture"/> succeeds, alongside <see cref="Content"/>. The host shell writes this
    /// same value onto the real OS clipboard's marker custom format (see
    /// <c>WorkbookClipboardSession.AttachMarker</c>/<c>MarkerFormat</c>, reused here rather than
    /// inventing a second format name) every time it captures a drawing object, so a later Paste
    /// can re-read the OS clipboard and call <see cref="MatchesMarker"/> before trusting <see
    /// cref="Content"/>. Without this, nothing ever detected that some OTHER application (or even
    /// a plain-text copy typed into a different window) replaced the real OS clipboard since this
    /// object was captured -- Paste kept silently repasting the stale chart/shape/picture/text box
    /// forever. Always null exactly when <see cref="Content"/> is null.
    /// </summary>
    public string? Marker { get; private set; }

    public bool TryCapture(
        SheetId sourceSheetId,
        SelectionPaneObjectKind? kind,
        Guid objectId,
        bool isCut = false)
    {
        if (kind is null || objectId == Guid.Empty || !IsSupportedKind(kind.Value))
            return false;

        Content = new DrawingObjectClipboardSnapshot(sourceSheetId, kind.Value, objectId, isCut);
        Marker = Guid.NewGuid().ToString("N");
        return true;
    }

    /// <summary>
    /// True when <see cref="Content"/> is present AND <paramref name="observedMarker"/> (read back
    /// from the OS clipboard at Paste time) is the exact value this session wrote at Capture time --
    /// see <see cref="Marker"/>. A null/different observed marker means some other application (or
    /// window) replaced the OS clipboard since this Capture, so <see cref="Content"/> must be
    /// treated as stale rather than pasted.
    /// </summary>
    public bool MatchesMarker(string? observedMarker) =>
        Content is not null &&
        Marker is not null &&
        string.Equals(Marker, observedMarker, StringComparison.Ordinal);

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

    public void Clear()
    {
        Content = null;
        Marker = null;
    }

    public void CompletePaste(DrawingObjectClipboardSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (snapshot.IsCut && Content == snapshot)
        {
            Content = null;
            Marker = null;
        }
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

    public static DrawingObjectPasteSelectionPlan CreatePasteSelectionPlan(
        Sheet? destinationSheet,
        SheetId destinationSheetId,
        SelectionPaneObjectKind kind,
        Guid objectId) =>
        new(
            kind,
            objectId,
            ResolveAnchor(destinationSheet, destinationSheetId, kind, objectId));

    private static bool IsSupportedKind(SelectionPaneObjectKind kind) => kind is
        SelectionPaneObjectKind.Chart or
        SelectionPaneObjectKind.Shape or
        SelectionPaneObjectKind.Picture or
        SelectionPaneObjectKind.TextBox;
}
