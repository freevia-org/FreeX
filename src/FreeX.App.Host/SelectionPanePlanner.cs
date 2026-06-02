using FreeX.Core.Model;

namespace FreeX.App.Host;

public sealed record SelectionPaneItem(
    SelectionPaneObjectKind Kind,
    Guid Id,
    string Name,
    bool IsVisible,
    bool CanMoveUp,
    bool CanMoveDown);

public static class SelectionPanePlanner
{
    public static IReadOnlyList<SelectionPaneItem> BuildItems(Sheet sheet)
    {
        var items = new List<SelectionPaneItem>();
        AddChartItems(sheet, items);
        AddDrawingObjectItems(sheet, items);
        items.Reverse();
        return items;
    }

    private static void AddChartItems(Sheet sheet, List<SelectionPaneItem> items)
    {
        for (var index = 0; index < sheet.Charts.Count; index++)
        {
            var chart = sheet.Charts[index];
            items.Add(new SelectionPaneItem(
                SelectionPaneObjectKind.Chart,
                chart.Id,
                DisplayName(chart.Name, UiText.Format("SelectionPane_DefaultChartName", index + 1)),
                chart.IsVisible,
                index < sheet.Charts.Count - 1,
                index > 0));
        }
    }

    private static void AddDrawingObjectItems(Sheet sheet, List<SelectionPaneItem> items)
    {
        var shapeIndexes = CreateIndexMap(sheet.DrawingShapes, shape => shape.Id);
        var pictureIndexes = CreateIndexMap(sheet.Pictures, picture => picture.Id);
        var textBoxIndexes = CreateIndexMap(sheet.TextBoxes, textBox => textBox.Id);
        var order = DrawingObjectZOrder.GetNormalizedOrder(sheet);
        for (var stackIndex = 0; stackIndex < order.Count; stackIndex++)
        {
            var entry = order[stackIndex];
            var canMoveUp = stackIndex < order.Count - 1;
            var canMoveDown = stackIndex > 0;
            switch (entry.Kind)
            {
                case SelectionPaneObjectKind.Shape:
                    AddShapeItem(sheet, items, entry.Id, shapeIndexes, canMoveUp, canMoveDown);
                    break;
                case SelectionPaneObjectKind.Picture:
                    AddPictureItem(sheet, items, entry.Id, pictureIndexes, canMoveUp, canMoveDown);
                    break;
                case SelectionPaneObjectKind.TextBox:
                    AddTextBoxItem(sheet, items, entry.Id, textBoxIndexes, canMoveUp, canMoveDown);
                    break;
            }
        }
    }

    private static void AddShapeItem(
        Sheet sheet,
        List<SelectionPaneItem> items,
        Guid id,
        IReadOnlyDictionary<Guid, int> shapeIndexes,
        bool canMoveUp,
        bool canMoveDown)
    {
        if (!shapeIndexes.TryGetValue(id, out var index))
            return;

        var shape = sheet.DrawingShapes[index];
        items.Add(new SelectionPaneItem(
            SelectionPaneObjectKind.Shape,
            shape.Id,
            DisplayName(shape.Name, UiText.Format("SelectionPane_DefaultShapeNameFormat", ShapeName(shape.Kind), index + 1)),
            shape.IsVisible,
            canMoveUp,
            canMoveDown));
    }

    private static void AddPictureItem(
        Sheet sheet,
        List<SelectionPaneItem> items,
        Guid id,
        IReadOnlyDictionary<Guid, int> pictureIndexes,
        bool canMoveUp,
        bool canMoveDown)
    {
        if (!pictureIndexes.TryGetValue(id, out var index))
            return;

        var picture = sheet.Pictures[index];
        items.Add(new SelectionPaneItem(
            SelectionPaneObjectKind.Picture,
            picture.Id,
            DisplayName(picture.Name, UiText.Format("SelectionPane_DefaultPictureName", index + 1)),
            picture.IsVisible,
            canMoveUp,
            canMoveDown));
    }

    private static void AddTextBoxItem(
        Sheet sheet,
        List<SelectionPaneItem> items,
        Guid id,
        IReadOnlyDictionary<Guid, int> textBoxIndexes,
        bool canMoveUp,
        bool canMoveDown)
    {
        if (!textBoxIndexes.TryGetValue(id, out var index))
            return;

        var textBox = sheet.TextBoxes[index];
        items.Add(new SelectionPaneItem(
            SelectionPaneObjectKind.TextBox,
            textBox.Id,
            DisplayName(textBox.Name, UiText.Format("SelectionPane_DefaultTextBoxName", index + 1)),
            textBox.IsVisible,
            canMoveUp,
            canMoveDown));
    }

    private static string ShapeName(DrawingShapeKind kind) =>
        kind switch
        {
            DrawingShapeKind.Ellipse => UiText.Get("SelectionPane_DefaultEllipseName"),
            DrawingShapeKind.Line => UiText.Get("SelectionPane_DefaultLineName"),
            _ => UiText.Get("SelectionPane_DefaultRectangleName")
        };

    private static string DisplayName(string? name, string fallback) =>
        string.IsNullOrWhiteSpace(name) ? fallback : name.Trim();

    private static IReadOnlyDictionary<Guid, int> CreateIndexMap<T>(
        IReadOnlyList<T> items,
        Func<T, Guid> getId)
    {
        var indexes = new Dictionary<Guid, int>(items.Count);
        for (var index = 0; index < items.Count; index++)
            indexes[getId(items[index])] = index;

        return indexes;
    }
}
