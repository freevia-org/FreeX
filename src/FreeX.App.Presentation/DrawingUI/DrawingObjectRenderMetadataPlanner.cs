using FreeX.Core.Model;

namespace FreeX.App.Presentation.DrawingUI;

public static class DrawingObjectRenderMetadataPlanner
{
    private const double PointsToDip = 96.0 / 72.0;
    private const double DefaultShapeOutlineThicknessDip = 1.5;

    public static DrawingObjectLayerRenderMode PlanLayerRenderMode(DrawingObjectLayerDisplayMode displayMode) =>
        displayMode switch
        {
            DrawingObjectLayerDisplayMode.Placeholders => DrawingObjectLayerRenderMode.Placeholders,
            DrawingObjectLayerDisplayMode.Nothing => DrawingObjectLayerRenderMode.Hidden,
            _ => DrawingObjectLayerRenderMode.Objects
        };

    public static bool HasExplicitZOrder(IReadOnlyList<DrawingObjectZOrderEntry>? zOrder) =>
        zOrder is { Count: > 0 };

    public static IReadOnlyList<DrawingObjectZOrderEntry> NormalizeZOrder(
        IReadOnlyList<DrawingShapeModel>? shapes,
        IReadOnlyList<PictureModel>? pictures,
        IReadOnlyList<TextBoxModel>? textBoxes,
        IReadOnlyList<DrawingObjectZOrderEntry>? explicitOrder)
    {
        var normalized = new List<DrawingObjectZOrderEntry>(
            (shapes?.Count ?? 0) + (pictures?.Count ?? 0) + (textBoxes?.Count ?? 0));
        var seen = new HashSet<DrawingObjectZOrderEntry>();

        if (explicitOrder is not null)
        {
            foreach (var entry in explicitOrder)
            {
                if (!DrawingObjectZOrder.IsSupportedKind(entry.Kind) ||
                    !ContainsObject(entry, shapes, pictures, textBoxes) ||
                    !seen.Add(entry))
                {
                    continue;
                }

                normalized.Add(entry);
            }
        }

        AddMissing(shapes, SelectionPaneObjectKind.Shape, normalized, seen);
        AddMissing(pictures, SelectionPaneObjectKind.Picture, normalized, seen);
        AddMissing(textBoxes, SelectionPaneObjectKind.TextBox, normalized, seen);
        return normalized;
    }

    public static DrawingShapeRenderMetadata ResolveDrawingShapeRenderMetadata(
        DrawingShapeModel shape,
        WorkbookTheme theme)
    {
        ArgumentNullException.ThrowIfNull(shape);
        ArgumentNullException.ThrowIfNull(theme);

        var paint = DrawingObjectViewportPlanner.ResolveDrawingShapePaint(shape, theme);
        var isLineLike = DrawingShapeKindSupport.IsLineLike(shape.Kind);
        var gradient = shape.GradientFillEndColor is { } gradientEnd && !isLineLike
            ? new DrawingShapeFillGradientMetadata(gradientEnd, shape.GetEffectiveGradientFillDirection())
            : (DrawingShapeFillGradientMetadata?)null;

        return new DrawingShapeRenderMetadata(
            shape.Kind,
            paint,
            new DrawingObjectTransformMetadata(
                shape.RotationDegrees,
                shape.FlipHorizontal,
                shape.FlipVertical),
            gradient,
            new DrawingShapeOutlineRenderMetadata(
                !shape.OutlineHasNoFill,
                ResolveOutlineThicknessDip(shape.OutlineWidthPoints),
                shape.OutlineDash),
            shape.GetEffectiveEffectPreset(),
            shape.UsesThemeEffects,
            isLineLike,
            shape.HasShapeText,
            HasArrowheads(shape),
            shape.IsWordArt);
    }

    public static TextBoxRenderMetadata ResolveTextBoxRenderMetadata(
        TextBoxModel textBox,
        WorkbookTheme theme)
    {
        ArgumentNullException.ThrowIfNull(textBox);
        ArgumentNullException.ThrowIfNull(theme);

        return new TextBoxRenderMetadata(
            DrawingObjectViewportPlanner.ResolveTextBoxPaint(textBox, theme),
            new DrawingObjectTransformMetadata(
                textBox.RotationDegrees,
                textBox.FlipHorizontal,
                textBox.FlipVertical),
            !string.IsNullOrEmpty(textBox.Text));
    }

    public static DrawingObjectBoundsShapeRenderMetadata ResolveBoundsShapeRenderMetadata(
        DrawingObjectBounds drawingObject)
    {
        var fill = drawingObject.FillColor ??
            (drawingObject.IsWordArt ? (CellColor?)null : DrawingShapeModel.DefaultFillColor);
        var outline = drawingObject.OutlineHasNoFill
            ? (CellColor?)null
            : drawingObject.OutlineColor ?? DrawingShapeModel.DefaultOutlineColor;
        var isLineLike = drawingObject.ShapeKind is { } kind &&
            DrawingShapeKindSupport.IsLineLike(kind);
        var gradient = drawingObject.GradientFillEndColor is { } gradientEnd && !isLineLike
            ? new DrawingShapeFillGradientMetadata(gradientEnd, drawingObject.GradientFillDirection)
            : (DrawingShapeFillGradientMetadata?)null;

        return new DrawingObjectBoundsShapeRenderMetadata(
            fill,
            outline,
            gradient,
            ResolveOutlineThicknessDip(drawingObject.OutlineWidthPoints),
            drawingObject.OutlineDash,
            isLineLike,
            drawingObject.HeadArrowhead?.IsPresent == true ||
                drawingObject.TailArrowhead?.IsPresent == true,
            !string.IsNullOrEmpty(drawingObject.ShapeText));
    }

    public static DrawingObjectPlaceholderMetadata CreatePlaceholderMetadata(
        string objectType,
        string? objectName,
        int index) =>
        new(objectType, index, DrawingObjectViewportPlanner.CreateObjectPlaceholderLabel(objectType, objectName, index));

    public static int CalculateDrawingShapeRenderStamp(IReadOnlyList<DrawingShapeModel>? shapes)
    {
        if (shapes is null || shapes.Count == 0)
            return 0;

        var hash = new HashCode();
        foreach (var shape in shapes)
        {
            hash.Add(shape.Id);
            hash.Add(shape.Anchor);
            hash.Add(shape.AnchorOffsetX);
            hash.Add(shape.AnchorOffsetY);
            hash.Add(shape.Kind);
            hash.Add(shape.Width);
            hash.Add(shape.Height);
            hash.Add(shape.RotationDegrees);
            hash.Add(shape.FlipHorizontal);
            hash.Add(shape.FlipVertical);
            hash.Add(shape.IsVisible);
            hash.Add(shape.HasFill);
            hash.Add(shape.FillColor);
            hash.Add(shape.OutlineColor);
            hash.Add(shape.GradientFillEndColor);
            hash.Add(shape.GradientFillDirection);
            hash.Add(shape.FillThemeColor);
            hash.Add(shape.OutlineThemeColor);
            hash.Add(shape.HasShadowEffect);
            hash.Add(shape.EffectPreset);
            hash.Add(shape.UsesThemeEffects);
            hash.Add(shape.OutlineWidthPoints);
            hash.Add(shape.OutlineHasNoFill);
            hash.Add(shape.OutlineDash);
            AddArrowhead(ref hash, shape.HeadArrowhead);
            AddArrowhead(ref hash, shape.TailArrowhead);
            hash.Add(shape.ShapeText);
            hash.Add(shape.ShapeTextFontSizePoints);
            hash.Add(shape.ShapeTextBold);
            hash.Add(shape.ShapeTextItalic);
            hash.Add(shape.ShapeTextUnderline);
            hash.Add(shape.ShapeTextColor);
            hash.Add(shape.ShapeTextThemeColor);
            hash.Add(shape.ShapeTextHAlign);
            hash.Add(shape.ShapeTextVAnchor);
            hash.Add(shape.ShapeTextWrap);
            hash.Add(shape.IsWordArt);
            hash.Add(shape.ShapeTextGradientEndColor);
            hash.Add(shape.ShapeTextGradientEndThemeColor);
            hash.Add(shape.ShapeTextOutlineColor);
            hash.Add(shape.ShapeTextOutlineThemeColor);
            hash.Add(shape.ShapeTextOutlineWidthPoints);
        }

        return hash.ToHashCode();
    }

    public static int CalculatePictureRenderStamp(IReadOnlyList<PictureModel>? pictures)
    {
        if (pictures is null || pictures.Count == 0)
            return 0;

        var hash = new HashCode();
        foreach (var picture in pictures)
        {
            hash.Add(picture.Id);
            hash.Add(picture.Anchor);
            hash.Add(picture.AnchorOffsetX);
            hash.Add(picture.AnchorOffsetY);
            hash.Add(picture.Kind);
            hash.Add(picture.Width);
            hash.Add(picture.Height);
            hash.Add(picture.RotationDegrees);
            hash.Add(picture.FlipHorizontal);
            hash.Add(picture.FlipVertical);
            hash.Add(picture.IsVisible);
            hash.Add(picture.CropLeft);
            hash.Add(picture.CropTop);
            hash.Add(picture.CropRight);
            hash.Add(picture.CropBottom);
            hash.Add(picture.ImageBytes?.Length ?? 0);
            hash.Add(picture.ContentType);
            hash.Add(picture.SourceRowCount);
            hash.Add(picture.SourceColumnCount);
            hash.Add(picture.Cells.Count);
            foreach (var cell in picture.Cells)
            {
                hash.Add(cell.RowOffset);
                hash.Add(cell.ColumnOffset);
                hash.Add(cell.Text);
                hash.Add(cell.IsNumericOrDate);
                hash.Add(cell.Style);
            }
        }

        return hash.ToHashCode();
    }

    public static int CalculateTextBoxRenderStamp(IReadOnlyList<TextBoxModel>? textBoxes)
    {
        if (textBoxes is null || textBoxes.Count == 0)
            return 0;

        var hash = new HashCode();
        foreach (var textBox in textBoxes)
        {
            hash.Add(textBox.Id);
            hash.Add(textBox.Anchor);
            hash.Add(textBox.AnchorOffsetX);
            hash.Add(textBox.AnchorOffsetY);
            hash.Add(textBox.Text);
            hash.Add(textBox.Width);
            hash.Add(textBox.Height);
            hash.Add(textBox.RotationDegrees);
            hash.Add(textBox.FlipHorizontal);
            hash.Add(textBox.FlipVertical);
            hash.Add(textBox.IsVisible);
            hash.Add(textBox.HasFill);
            hash.Add(textBox.FillColor);
            hash.Add(textBox.OutlineColor);
            hash.Add(textBox.FillThemeColor);
            hash.Add(textBox.OutlineThemeColor);
        }

        return hash.ToHashCode();
    }

    private static double ResolveOutlineThicknessDip(double outlineWidthPoints) =>
        outlineWidthPoints > 0
            ? outlineWidthPoints * PointsToDip
            : DefaultShapeOutlineThicknessDip;

    private static bool ContainsObject(
        DrawingObjectZOrderEntry entry,
        IReadOnlyList<DrawingShapeModel>? shapes,
        IReadOnlyList<PictureModel>? pictures,
        IReadOnlyList<TextBoxModel>? textBoxes) =>
        entry.Kind switch
        {
            SelectionPaneObjectKind.Shape => ContainsId(shapes, entry.Id),
            SelectionPaneObjectKind.Picture => ContainsId(pictures, entry.Id),
            SelectionPaneObjectKind.TextBox => ContainsId(textBoxes, entry.Id),
            _ => false
        };

    private static bool ContainsId<T>(IReadOnlyList<T>? items, Guid id)
    {
        if (items is null)
            return false;

        foreach (var item in items)
        {
            if (GetId(item) == id)
                return true;
        }

        return false;
    }

    private static void AddMissing<T>(
        IReadOnlyList<T>? items,
        SelectionPaneObjectKind kind,
        List<DrawingObjectZOrderEntry> normalized,
        HashSet<DrawingObjectZOrderEntry> seen)
    {
        if (items is null)
            return;

        foreach (var item in items)
        {
            var entry = new DrawingObjectZOrderEntry(kind, GetId(item));
            if (seen.Add(entry))
                normalized.Add(entry);
        }
    }

    private static Guid GetId<T>(T item) =>
        item switch
        {
            DrawingShapeModel shape => shape.Id,
            PictureModel picture => picture.Id,
            TextBoxModel textBox => textBox.Id,
            _ => Guid.Empty
        };

    private static bool HasArrowheads(DrawingShapeModel shape) =>
        shape.HeadArrowhead?.IsPresent == true ||
        shape.TailArrowhead?.IsPresent == true;

    private static void AddArrowhead(ref HashCode hash, DrawingArrowhead? arrowhead)
    {
        if (arrowhead is null)
        {
            hash.Add(DrawingArrowheadType.None);
            return;
        }

        hash.Add(arrowhead.Type);
        hash.Add(arrowhead.Width);
        hash.Add(arrowhead.Length);
    }
}

public enum DrawingObjectLayerDisplayMode
{
    All,
    Placeholders,
    Nothing
}

public enum DrawingObjectLayerRenderMode
{
    Objects,
    Placeholders,
    Hidden
}

public readonly record struct DrawingObjectTransformMetadata(
    double RotationDegrees,
    bool FlipHorizontal,
    bool FlipVertical);

public readonly record struct DrawingShapeFillGradientMetadata(
    CellColor EndColor,
    DrawingShapeGradientDirection Direction);

public readonly record struct DrawingShapeOutlineRenderMetadata(
    bool HasOutline,
    double ThicknessDip,
    DrawingShapeOutlineDash Dash);

public readonly record struct DrawingShapeRenderMetadata(
    DrawingShapeKind Kind,
    DrawingObjectPaintMetadata Paint,
    DrawingObjectTransformMetadata Transform,
    DrawingShapeFillGradientMetadata? FillGradient,
    DrawingShapeOutlineRenderMetadata Outline,
    DrawingShapeEffectPreset AuthoredEffect,
    bool UsesThemeEffects,
    bool IsLineLike,
    bool HasShapeText,
    bool HasArrowheads,
    bool IsWordArt);

public readonly record struct TextBoxRenderMetadata(
    DrawingObjectPaintMetadata Paint,
    DrawingObjectTransformMetadata Transform,
    bool HasText);

public readonly record struct DrawingObjectBoundsShapeRenderMetadata(
    CellColor? FillColor,
    CellColor? OutlineColor,
    DrawingShapeFillGradientMetadata? FillGradient,
    double OutlineThicknessDip,
    DrawingShapeOutlineDash OutlineDash,
    bool IsLineLike,
    bool HasArrowheads,
    bool HasShapeText);

public readonly record struct DrawingObjectPlaceholderMetadata(
    string ObjectType,
    int Index,
    string Label);
