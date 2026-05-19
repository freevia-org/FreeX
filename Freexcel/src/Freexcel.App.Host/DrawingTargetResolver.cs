using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public static class DrawingTargetResolver
{
    public static PictureModel? GetTargetPicture(Sheet? sheet, CellAddress? selectedAnchor)
    {
        if (sheet is null || sheet.Pictures.Count == 0)
            return null;

        return GetSelectedOrLast(sheet.Pictures, selectedAnchor, picture => picture.Anchor);
    }

    public static DrawingShapeModel? GetTargetDrawingShape(Sheet? sheet, CellAddress? selectedAnchor)
    {
        if (sheet is null || sheet.DrawingShapes.Count == 0)
            return null;

        return GetSelectedOrLast(sheet.DrawingShapes, selectedAnchor, shape => shape.Anchor);
    }

    public static DrawingObjectTarget? GetTargetDrawingObject(
        Sheet? sheet,
        CellAddress? selectedAnchor,
        DrawingObjectTargetKind? preferredKind = null)
    {
        if (sheet is null)
            return null;

        if (preferredKind is null or DrawingObjectTargetKind.Shape &&
            GetTargetDrawingShape(sheet, selectedAnchor) is { } shape)
        {
            return DrawingObjectTarget.FromShape(shape);
        }

        if (preferredKind is null or DrawingObjectTargetKind.TextBox &&
            GetTargetTextBox(sheet, selectedAnchor) is { } textBox)
        {
            return DrawingObjectTarget.FromTextBox(textBox);
        }

        return null;
    }

    private static TextBoxModel? GetTargetTextBox(Sheet sheet, CellAddress? selectedAnchor)
    {
        if (sheet.TextBoxes.Count == 0)
            return null;

        return GetSelectedOrLast(sheet.TextBoxes, selectedAnchor, textBox => textBox.Anchor);
    }

    private static T? GetSelectedOrLast<T>(
        IReadOnlyList<T> items,
        CellAddress? selectedAnchor,
        Func<T, CellAddress> getAnchor)
        where T : class
    {
        if (selectedAnchor is { } selected)
        {
            var anchored = items.LastOrDefault(item =>
            {
                var anchor = getAnchor(item);
                return anchor.Row == selected.Row &&
                       anchor.Col == selected.Col;
            });

            if (anchored is not null)
                return anchored;
        }

        return items[^1];
    }
}

public enum DrawingObjectTargetKind
{
    Shape,
    TextBox
}

public sealed record DrawingObjectTarget(
    DrawingObjectTargetKind Kind,
    Guid Id,
    CellAddress Anchor,
    double Width,
    double Height,
    double RotationDegrees,
    CellColor? FillColor,
    CellColor? OutlineColor)
{
    public static DrawingObjectTarget FromShape(DrawingShapeModel shape) =>
        new(
            DrawingObjectTargetKind.Shape,
            shape.Id,
            shape.Anchor,
            shape.Width,
            shape.Height,
            shape.RotationDegrees,
            shape.FillColor,
            shape.OutlineColor);

    public static DrawingObjectTarget FromTextBox(TextBoxModel textBox) =>
        new(
            DrawingObjectTargetKind.TextBox,
            textBox.Id,
            textBox.Anchor,
            textBox.Width,
            textBox.Height,
            textBox.RotationDegrees,
            textBox.FillColor,
            textBox.OutlineColor);
}
