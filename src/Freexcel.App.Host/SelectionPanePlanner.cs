using Freexcel.Core.Model;

namespace Freexcel.App.Host;

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
                $"Chart {index + 1}",
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
                $"{ShapeName(shape.Kind)} {index + 1}",
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
                $"Picture {index + 1}",
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
                $"Text Box {index + 1}",
                textBox.IsVisible,
                index < sheet.TextBoxes.Count - 1,
                index > 0));
        }
    }

    private static string ShapeName(DrawingShapeKind kind) =>
        kind switch
        {
            DrawingShapeKind.Ellipse => "Ellipse",
            DrawingShapeKind.Line => "Line",
            _ => "Rectangle"
        };
}
