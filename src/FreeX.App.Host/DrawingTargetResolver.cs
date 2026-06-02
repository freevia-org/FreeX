using FreeX.Core.Model;

namespace FreeX.App.Host;

public static class DrawingTargetResolver
{
    public static PictureModel? GetTargetPicture(Sheet? sheet, CellAddress? selectedAnchor)
    {
        if (sheet is null || sheet.Pictures.Count == 0)
            return null;

        return GetSelectedOrLast(
            sheet.Pictures,
            selectedAnchor,
            picture => picture.Anchor,
            picture => picture.IsVisible);
    }

    public static DrawingShapeModel? GetTargetDrawingShape(Sheet? sheet, CellAddress? selectedAnchor)
    {
        if (sheet is null || sheet.DrawingShapes.Count == 0)
            return null;

        return GetSelectedOrLast(
            sheet.DrawingShapes,
            selectedAnchor,
            shape => shape.Anchor,
            shape => shape.IsVisible);
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

    public static DrawingObjectZOrderTarget? GetTargetDrawingZOrderObject(
        Sheet? sheet,
        CellAddress? selectedAnchor,
        SelectionPaneObjectKind? preferredKind = null)
    {
        if (sheet is null)
            return null;

        var order = DrawingObjectZOrder.GetNormalizedOrder(sheet);
        DrawingObjectZOrderTarget? fallback = null;
        for (var index = order.Count - 1; index >= 0; index--)
        {
            var entry = order[index];
            if (preferredKind is not null && entry.Kind != preferredKind.Value)
                continue;

            var target = ResolveDrawingZOrderTarget(sheet, entry, out var isVisible);
            if (target is null || !isVisible)
                continue;

            fallback ??= target;
            if (selectedAnchor is not { } selected)
                return target;

            if (target.Anchor.Row == selected.Row && target.Anchor.Col == selected.Col)
                return target;
        }

        return fallback;
    }

    private static TextBoxModel? GetTargetTextBox(Sheet sheet, CellAddress? selectedAnchor)
    {
        if (sheet.TextBoxes.Count == 0)
            return null;

        return GetSelectedOrLast(
            sheet.TextBoxes,
            selectedAnchor,
            textBox => textBox.Anchor,
            textBox => textBox.IsVisible);
    }

    private static T? GetSelectedOrLast<T>(
        IReadOnlyList<T> items,
        CellAddress? selectedAnchor,
        Func<T, CellAddress> getAnchor,
        Func<T, bool> isVisible)
        where T : class
    {
        T? lastVisible = null;
        if (selectedAnchor is { } selected)
        {
            for (var index = items.Count - 1; index >= 0; index--)
            {
                var item = items[index];
                if (!isVisible(item))
                    continue;

                var anchor = getAnchor(item);
                if (anchor.Row == selected.Row && anchor.Col == selected.Col)
                    return item;

                lastVisible ??= item;
            }
        }
        else
        {
            for (var index = items.Count - 1; index >= 0; index--)
            {
                var item = items[index];
                if (isVisible(item))
                    return item;
            }
        }

        return lastVisible;
    }

    private static DrawingObjectZOrderTarget? ResolveDrawingZOrderTarget(
        Sheet sheet,
        DrawingObjectZOrderEntry entry,
        out bool isVisible)
    {
        isVisible = false;
        switch (entry.Kind)
        {
            case SelectionPaneObjectKind.Shape:
                foreach (var shape in sheet.DrawingShapes)
                {
                    if (shape.Id != entry.Id)
                        continue;

                    isVisible = shape.IsVisible;
                    return new DrawingObjectZOrderTarget(entry.Kind, shape.Id, shape.Anchor);
                }

                return null;
            case SelectionPaneObjectKind.Picture:
                foreach (var picture in sheet.Pictures)
                {
                    if (picture.Id != entry.Id)
                        continue;

                    isVisible = picture.IsVisible;
                    return new DrawingObjectZOrderTarget(entry.Kind, picture.Id, picture.Anchor);
                }

                return null;
            case SelectionPaneObjectKind.TextBox:
                foreach (var textBox in sheet.TextBoxes)
                {
                    if (textBox.Id != entry.Id)
                        continue;

                    isVisible = textBox.IsVisible;
                    return new DrawingObjectZOrderTarget(entry.Kind, textBox.Id, textBox.Anchor);
                }

                return null;
            default:
                return null;
        }
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

public sealed record DrawingObjectZOrderTarget(
    SelectionPaneObjectKind Kind,
    Guid Id,
    CellAddress Anchor);
