using FreeX.Core.Model;

namespace FreeX.App.Presentation.FormulaBar;

/// <summary>The kinds of target that can be selected from the Name Box list.</summary>
public enum NameBoxNavigationItemKind
{
    DefinedName,
    Table,
    Object,
}

/// <summary>
/// Renderer-neutral Name Box list entry. Range targets are selected through the host's normal range
/// navigation route; object targets carry the same kind/id pair used by the worksheet object-selection
/// surface. The projection deliberately contains no WPF or Avalonia types.
/// </summary>
public sealed record NameBoxNavigationItem(
    string Name,
    NameBoxNavigationItemKind Kind,
    SheetId SheetId,
    GridRange? Range = null,
    SelectionPaneObjectKind? ObjectKind = null,
    Guid? ObjectId = null,
    CellAddress? Anchor = null)
{
    public string AccessibleDescription => Kind switch
    {
        NameBoxNavigationItemKind.DefinedName => $"Defined name {Name}",
        NameBoxNavigationItemKind.Table => $"Table {Name}",
        NameBoxNavigationItemKind.Object => $"Object {Name}",
        _ => Name,
    };

    public override string ToString() => Name;
}

/// <summary>Builds the shared, deterministic Name Box navigation projection.</summary>
public static class NameBoxDropdownPlanner
{
    public static IReadOnlyList<NameBoxNavigationItem> Build(
        Workbook workbook,
        SheetId activeSheetId)
    {
        ArgumentNullException.ThrowIfNull(workbook);

        var items = new List<NameBoxNavigationItem>();

        foreach (var (name, range) in workbook.NamedRanges)
        {
            if (!string.IsNullOrWhiteSpace(name))
                items.Add(new(name, NameBoxNavigationItemKind.DefinedName, range.Start.Sheet, Range: range));
        }

        foreach (var ((name, sheetId), range) in workbook.ScopedNamedRanges)
        {
            if (sheetId.Equals(activeSheetId) && !string.IsNullOrWhiteSpace(name))
                items.Add(new(name, NameBoxNavigationItemKind.DefinedName, sheetId, Range: range));
        }

        foreach (var sheet in workbook.Sheets)
        {
            foreach (var table in sheet.StructuredTables)
            {
                var name = string.IsNullOrWhiteSpace(table.Name) ? table.DisplayName : table.Name;
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                items.Add(new(
                    name,
                    NameBoxNavigationItemKind.Table,
                    sheet.Id,
                    Range: GetTableDataBodyRange(table)));
            }

            AddObjects(items, sheet);
        }

        return items
            .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Kind)
            .ThenBy(item => item.SheetId.Value)
            .ThenBy(item => item.ObjectId)
            .ToArray();
    }

    public static GridRange GetTableDataBodyRange(StructuredTableModel table)
    {
        var rowCount = checked((int)table.Range.RowCount);
        var headerRows = (uint)Math.Clamp(table.HeaderRowCount ?? 1, 0, rowCount);
        var startRow = table.Range.Start.Row + headerRows;
        var endRow = table.TotalsRowShown ? table.Range.End.Row - 1 : table.Range.End.Row;
        return startRow > endRow
            ? table.Range
            : new GridRange(
                new CellAddress(table.Range.Start.Sheet, startRow, table.Range.Start.Col),
                new CellAddress(table.Range.Start.Sheet, endRow, table.Range.End.Col));
    }

    private static void AddObjects(List<NameBoxNavigationItem> items, Sheet sheet)
    {
        foreach (var shape in sheet.DrawingShapes.Where(shape => shape.IsVisible && !string.IsNullOrWhiteSpace(shape.Name)))
            items.Add(new(shape.Name!, NameBoxNavigationItemKind.Object, sheet.Id,
                ObjectKind: SelectionPaneObjectKind.Shape, ObjectId: shape.Id, Anchor: shape.Anchor));

        foreach (var picture in sheet.Pictures.Where(picture => picture.IsVisible && !string.IsNullOrWhiteSpace(picture.Name)))
            items.Add(new(picture.Name!, NameBoxNavigationItemKind.Object, sheet.Id,
                ObjectKind: SelectionPaneObjectKind.Picture, ObjectId: picture.Id, Anchor: picture.Anchor));

        foreach (var textBox in sheet.TextBoxes.Where(textBox => textBox.IsVisible && !string.IsNullOrWhiteSpace(textBox.Name)))
            items.Add(new(textBox.Name!, NameBoxNavigationItemKind.Object, sheet.Id,
                ObjectKind: SelectionPaneObjectKind.TextBox, ObjectId: textBox.Id, Anchor: textBox.Anchor));

        foreach (var chart in sheet.Charts.Where(chart => chart.IsVisible && !string.IsNullOrWhiteSpace(chart.Name)))
            items.Add(new(chart.Name!, NameBoxNavigationItemKind.Object, sheet.Id,
                ObjectKind: SelectionPaneObjectKind.Chart, ObjectId: chart.Id, Anchor: chart.DataRange.Start));
    }
}
