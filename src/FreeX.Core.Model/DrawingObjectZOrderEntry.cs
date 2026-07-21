namespace FreeX.Core.Model;

public readonly record struct DrawingObjectZOrderEntry(
    SelectionPaneObjectKind Kind,
    Guid Id);

public static class DrawingObjectZOrder
{
    public static bool IsSupportedKind(SelectionPaneObjectKind kind) =>
        kind is SelectionPaneObjectKind.Shape or
            SelectionPaneObjectKind.Picture or
            SelectionPaneObjectKind.TextBox or
            SelectionPaneObjectKind.Chart;

    public static IReadOnlyList<DrawingObjectZOrderEntry> GetNormalizedOrder(Sheet sheet)
    {
        var normalized = new List<DrawingObjectZOrderEntry>(SupportedObjectCount(sheet));
        AddNormalizedEntries(sheet, normalized);
        return normalized;
    }

    public static IReadOnlyList<DrawingObjectZOrderEntry> EnsureNormalizedOrder(Sheet sheet)
    {
        var normalized = GetNormalizedOrder(sheet);
        sheet.DrawingObjectZOrder.Clear();
        sheet.DrawingObjectZOrder.AddRange(normalized);
        return sheet.DrawingObjectZOrder;
    }

    public static bool ContainsObject(Sheet sheet, DrawingObjectZOrderEntry entry) =>
        entry.Kind switch
        {
            SelectionPaneObjectKind.Shape => sheet.DrawingShapes.Any(item => item.Id == entry.Id),
            SelectionPaneObjectKind.Picture => sheet.Pictures.Any(item => item.Id == entry.Id),
            SelectionPaneObjectKind.TextBox => sheet.TextBoxes.Any(item => item.Id == entry.Id),
            // R62-meta-1: Chart IS a supported kind (see IsSupportedKind above) -- without this case
            // every Chart entry was reported as "not present" and stripped by every normalization
            // entry point (AddNormalizedEntries below), permanently discarding a chart's recorded
            // z-order slot and forcing it to always render/hit-test as topmost.
            SelectionPaneObjectKind.Chart => sheet.Charts.Any(item => item.Id == entry.Id),
            _ => false
        };

    private static void AddNormalizedEntries(Sheet sheet, List<DrawingObjectZOrderEntry> normalized)
    {
        var seen = new HashSet<DrawingObjectZOrderEntry>();
        foreach (var entry in sheet.DrawingObjectZOrder)
        {
            if (!IsSupportedKind(entry.Kind) ||
                !ContainsObject(sheet, entry) ||
                !seen.Add(entry))
            {
                continue;
            }

            normalized.Add(entry);
        }

        AddMissingShapes(sheet, normalized, seen);
        AddMissingPictures(sheet, normalized, seen);
        AddMissingTextBoxes(sheet, normalized, seen);
        AddMissingCharts(sheet, normalized, seen);
    }

    private static void AddMissingShapes(
        Sheet sheet,
        List<DrawingObjectZOrderEntry> normalized,
        HashSet<DrawingObjectZOrderEntry> seen)
    {
        foreach (var shape in sheet.DrawingShapes)
            AddMissing(new DrawingObjectZOrderEntry(SelectionPaneObjectKind.Shape, shape.Id), normalized, seen);
    }

    private static void AddMissingPictures(
        Sheet sheet,
        List<DrawingObjectZOrderEntry> normalized,
        HashSet<DrawingObjectZOrderEntry> seen)
    {
        foreach (var picture in sheet.Pictures)
            AddMissing(new DrawingObjectZOrderEntry(SelectionPaneObjectKind.Picture, picture.Id), normalized, seen);
    }

    private static void AddMissingTextBoxes(
        Sheet sheet,
        List<DrawingObjectZOrderEntry> normalized,
        HashSet<DrawingObjectZOrderEntry> seen)
    {
        foreach (var textBox in sheet.TextBoxes)
            AddMissing(new DrawingObjectZOrderEntry(SelectionPaneObjectKind.TextBox, textBox.Id), normalized, seen);
    }

    private static void AddMissingCharts(
        Sheet sheet,
        List<DrawingObjectZOrderEntry> normalized,
        HashSet<DrawingObjectZOrderEntry> seen)
    {
        foreach (var chart in sheet.Charts)
            AddMissing(new DrawingObjectZOrderEntry(SelectionPaneObjectKind.Chart, chart.Id), normalized, seen);
    }

    private static void AddMissing(
        DrawingObjectZOrderEntry entry,
        List<DrawingObjectZOrderEntry> normalized,
        HashSet<DrawingObjectZOrderEntry> seen)
    {
        if (seen.Add(entry))
            normalized.Add(entry);
    }

    private static int SupportedObjectCount(Sheet sheet) =>
        sheet.DrawingShapes.Count + sheet.Pictures.Count + sheet.TextBoxes.Count + sheet.Charts.Count;
}
