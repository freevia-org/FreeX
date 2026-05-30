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
        AddShapeItems(sheet, items);
        AddPictureItems(sheet, items);
        AddTextBoxItems(sheet, items);
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

    private static void AddShapeItems(Sheet sheet, List<SelectionPaneItem> items)
    {
        for (var index = 0; index < sheet.DrawingShapes.Count; index++)
        {
            var shape = sheet.DrawingShapes[index];
            items.Add(new SelectionPaneItem(
                SelectionPaneObjectKind.Shape,
                shape.Id,
                DisplayName(shape.Name, UiText.Format("SelectionPane_DefaultShapeNameFormat", ShapeName(shape.Kind), index + 1)),
                shape.IsVisible,
                index < sheet.DrawingShapes.Count - 1,
                index > 0));
        }
    }

    private static void AddPictureItems(Sheet sheet, List<SelectionPaneItem> items)
    {
        for (var index = 0; index < sheet.Pictures.Count; index++)
        {
            var picture = sheet.Pictures[index];
            items.Add(new SelectionPaneItem(
                SelectionPaneObjectKind.Picture,
                picture.Id,
                DisplayName(picture.Name, UiText.Format("SelectionPane_DefaultPictureName", index + 1)),
                picture.IsVisible,
                index < sheet.Pictures.Count - 1,
                index > 0));
        }
    }

    private static void AddTextBoxItems(Sheet sheet, List<SelectionPaneItem> items)
    {
        for (var index = 0; index < sheet.TextBoxes.Count; index++)
        {
            var textBox = sheet.TextBoxes[index];
            items.Add(new SelectionPaneItem(
                SelectionPaneObjectKind.TextBox,
                textBox.Id,
                DisplayName(textBox.Name, UiText.Format("SelectionPane_DefaultTextBoxName", index + 1)),
                textBox.IsVisible,
                index < sheet.TextBoxes.Count - 1,
                index > 0));
        }
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
}
