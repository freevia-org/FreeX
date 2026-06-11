using FreeX.Core.Model;

namespace FreeX.App.Host;

public static class DrawingTargetResolver
{
    public static PictureModel? GetTargetPicture(
        Sheet? sheet,
        CellAddress? selectedAnchor,
        bool allowFallback = true)
    {
        if (sheet is null || sheet.Pictures.Count == 0)
            return null;

        return GetSelectedOrLast(
            sheet.Pictures,
            selectedAnchor,
            picture => picture.Anchor,
            picture => picture.IsVisible,
            allowFallback);
    }

    public static DrawingShapeModel? GetTargetDrawingShape(
        Sheet? sheet,
        CellAddress? selectedAnchor,
        bool allowFallback = true)
    {
        if (sheet is null || sheet.DrawingShapes.Count == 0)
            return null;

        return GetSelectedOrLast(
            sheet.DrawingShapes,
            selectedAnchor,
            shape => shape.Anchor,
            shape => shape.IsVisible,
            allowFallback);
    }

    public static DrawingObjectTarget? GetTargetDrawingObject(
        Sheet? sheet,
        CellAddress? selectedAnchor,
        DrawingObjectTargetKind? preferredKind = null,
        Guid selectedObjectId = default,
        bool includePictures = false,
        bool allowFallback = true)
    {
        if (sheet is null)
            return null;

        if (preferredKind is not null && selectedObjectId != Guid.Empty)
        {
            var selectedTarget = GetTargetById(sheet, preferredKind.Value, selectedObjectId, includePictures);
            if (selectedTarget is not null)
                return selectedTarget;

            if (!allowFallback || (preferredKind.Value == DrawingObjectTargetKind.Picture && !includePictures))
                return null;
        }

        if (preferredKind is null or DrawingObjectTargetKind.Shape &&
            GetTargetDrawingShape(sheet, selectedAnchor, allowFallback) is { } shape)
        {
            return DrawingObjectTarget.FromShape(shape);
        }

        if (includePictures &&
            (preferredKind is null or DrawingObjectTargetKind.Picture) &&
            GetTargetPicture(sheet, selectedAnchor, allowFallback) is { } picture)
        {
            return DrawingObjectTarget.FromPicture(picture);
        }

        if (preferredKind is null or DrawingObjectTargetKind.TextBox &&
            GetTargetTextBox(sheet, selectedAnchor, allowFallback) is { } textBox)
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

    private static DrawingObjectTarget? GetTargetById(
        Sheet sheet,
        DrawingObjectTargetKind kind,
        Guid selectedObjectId,
        bool includePictures)
    {
        switch (kind)
        {
            case DrawingObjectTargetKind.Picture when includePictures:
                foreach (var picture in sheet.Pictures)
                {
                    if (picture.Id == selectedObjectId && picture.IsVisible)
                        return DrawingObjectTarget.FromPicture(picture);
                }

                return null;
            case DrawingObjectTargetKind.Shape:
                foreach (var shape in sheet.DrawingShapes)
                {
                    if (shape.Id == selectedObjectId && shape.IsVisible)
                        return DrawingObjectTarget.FromShape(shape);
                }

                return null;
            case DrawingObjectTargetKind.TextBox:
                foreach (var textBox in sheet.TextBoxes)
                {
                    if (textBox.Id == selectedObjectId && textBox.IsVisible)
                        return DrawingObjectTarget.FromTextBox(textBox);
                }

                return null;
            default:
                return null;
        }
    }

    private static TextBoxModel? GetTargetTextBox(
        Sheet sheet,
        CellAddress? selectedAnchor,
        bool allowFallback)
    {
        if (sheet.TextBoxes.Count == 0)
            return null;

        return GetSelectedOrLast(
            sheet.TextBoxes,
            selectedAnchor,
            textBox => textBox.Anchor,
            textBox => textBox.IsVisible,
            allowFallback);
    }

    private static T? GetSelectedOrLast<T>(
        IReadOnlyList<T> items,
        CellAddress? selectedAnchor,
        Func<T, CellAddress> getAnchor,
        Func<T, bool> isVisible,
        bool allowFallback)
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
        else if (allowFallback)
        {
            for (var index = items.Count - 1; index >= 0; index--)
            {
                var item = items[index];
                if (isVisible(item))
                    return item;
            }
        }

        return allowFallback ? lastVisible : null;
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
    Picture,
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
    public static DrawingObjectTarget FromPicture(PictureModel picture) =>
        new(
            DrawingObjectTargetKind.Picture,
            picture.Id,
            picture.Anchor,
            picture.Width,
            picture.Height,
            picture.RotationDegrees,
            null,
            null);

    public static DrawingObjectTarget FromShape(DrawingShapeModel shape) =>
        new(
            DrawingObjectTargetKind.Shape,
            shape.Id,
            shape.Anchor,
            shape.Width,
            shape.Height,
            shape.RotationDegrees,
            shape.FillColor,
            shape.OutlineColor)
        {
            FillThemeColor = shape.FillThemeColor,
            OutlineThemeColor = shape.OutlineThemeColor
        };

    public static DrawingObjectTarget FromTextBox(TextBoxModel textBox) =>
        new(
            DrawingObjectTargetKind.TextBox,
            textBox.Id,
            textBox.Anchor,
            textBox.Width,
            textBox.Height,
            textBox.RotationDegrees,
            textBox.FillColor,
            textBox.OutlineColor)
        {
            FillThemeColor = textBox.FillThemeColor,
            OutlineThemeColor = textBox.OutlineThemeColor
        };

    public WorkbookThemeColorReference? FillThemeColor { get; init; }
    public WorkbookThemeColorReference? OutlineThemeColor { get; init; }
}

public sealed record DrawingObjectZOrderTarget(
    SelectionPaneObjectKind Kind,
    Guid Id,
    CellAddress Anchor);
