using FreeX.Core.Model;

namespace FreeX.Core.Calc;

public sealed partial class ViewportService
{
    private static IReadOnlyList<DrawingObjectBounds> BuildDrawingObjectBounds(
        Sheet sheet,
        WorkbookTheme theme,
        IReadOnlyList<RowMetric> rowMetrics,
        IReadOnlyList<ColMetric> colMetrics)
    {
        if (sheet.DrawingShapes.Count == 0 &&
            sheet.Pictures.Count == 0 &&
            sheet.TextBoxes.Count == 0)
        {
            return [];
        }

        var bounds = new List<DrawingObjectBounds>(
            sheet.DrawingShapes.Count + sheet.Pictures.Count + sheet.TextBoxes.Count);
        foreach (var entry in DrawingObjectZOrder.GetNormalizedOrder(sheet))
        {
            switch (entry.Kind)
            {
                case SelectionPaneObjectKind.Shape:
                    AddShapeBounds(sheet, theme, entry.Id, rowMetrics, colMetrics, bounds);
                    break;
                case SelectionPaneObjectKind.Picture:
                    AddPictureBounds(sheet, entry.Id, rowMetrics, colMetrics, bounds);
                    break;
                case SelectionPaneObjectKind.TextBox:
                    AddTextBoxBounds(sheet, theme, entry.Id, rowMetrics, colMetrics, bounds);
                    break;
            }
        }

        return bounds;
    }

    private static void AddShapeBounds(
        Sheet sheet,
        WorkbookTheme theme,
        Guid id,
        IReadOnlyList<RowMetric> rowMetrics,
        IReadOnlyList<ColMetric> colMetrics,
        List<DrawingObjectBounds> bounds)
    {
        if (!TryFindDrawingShape(sheet, id, out var shape) ||
            !shape.IsVisible ||
            !TryCreateAnchoredDrawingObjectBounds(
                rowMetrics,
                colMetrics,
                shape.Anchor,
                shape.Width,
                shape.Height,
                out var left,
                out var top,
                out var width,
                out var height,
                shape.AnchorOffsetX,
                shape.AnchorOffsetY))
        {
            return;
        }

        bounds.Add(new DrawingObjectBounds(
            SelectionPaneObjectKind.Shape,
            shape.Id,
            GetObjectDisplayName("Shape", shape.Name),
            shape.Anchor.Row,
            shape.Anchor.Col,
            left,
            top,
            width,
            height,
            shape.RotationDegrees,
            shape.FlipHorizontal,
            shape.FlipVertical,
            ShapeKind: shape.Kind,
            FillColor: ResolveShapeFillColor(shape, theme),
            GradientFillEndColor: shape.GradientFillEndColor,
            GradientFillDirection: shape.GetEffectiveGradientFillDirection(),
            OutlineColor: ResolveShapeOutlineColor(shape, theme),
            Effect: BuildShapeEffect(shape),
            OutlineWidthPoints: shape.OutlineWidthPoints,
            OutlineDash: shape.OutlineDash,
            OutlineHasNoFill: shape.OutlineHasNoFill,
            HeadArrowhead: shape.HeadArrowhead,
            TailArrowhead: shape.TailArrowhead,
            ShapeText: shape.ShapeText,
            ShapeTextFontSizePoints: shape.ShapeTextFontSizePoints,
            ShapeTextBold: shape.ShapeTextBold,
            ShapeTextItalic: shape.ShapeTextItalic,
            ShapeTextUnderline: shape.ShapeTextUnderline,
            ShapeTextColor: shape.ResolveShapeTextColor(theme),
            ShapeTextHAlign: shape.ShapeTextHAlign,
            ShapeTextVAnchor: shape.ShapeTextVAnchor,
            ShapeTextWrap: shape.ShapeTextWrap,
            IsWordArt: shape.IsWordArt,
            ShapeTextGradientEndColor: shape.ResolveShapeTextGradientEndColor(theme),
            ShapeTextOutlineColor: shape.ResolveShapeTextOutlineColor(theme),
            ShapeTextOutlineWidthPoints: shape.ShapeTextOutlineWidthPoints,
            ShapeTextHasNoFill: shape.ShapeTextHasNoFill));
    }

    // Projects the authored shape effect preset into the render plan using the same parameter
    // values the WPF grid uses (shadow offset/alpha, glow color/radius, soft-edge radius). Kept here
    // so every shell renders the same approximation without re-reading the source model.
    private static DrawingObjectEffect? BuildShapeEffect(DrawingShapeModel shape)
    {
        var preset = shape.GetEffectiveEffectPreset();
        return preset switch
        {
            // Outer drop shadow: 3,3 offset, ~23% black (alpha 58/255).
            DrawingShapeEffectPreset.Shadow => new DrawingObjectEffect(
                preset, OffsetX: 3, OffsetY: 3, BlurRadius: 4, Opacity: 58d / 255d, Color: new CellColor(0, 0, 0)),
            // Inner shadow approximated as a tight, low-offset dark shadow.
            DrawingShapeEffectPreset.InnerShadow => new DrawingObjectEffect(
                preset, OffsetX: 1, OffsetY: 1, BlurRadius: 3, Opacity: 70d / 255d, Color: new CellColor(0, 0, 0)),
            // Glow: accent-blue halo, no offset, larger blur (alpha 96/255, radius ~6).
            DrawingShapeEffectPreset.Glow => new DrawingObjectEffect(
                preset, BlurRadius: 6, Opacity: 96d / 255d, Color: new CellColor(91, 155, 213)),
            // Soft edges: small grey feather (alpha 54/255, radius ~8).
            DrawingShapeEffectPreset.SoftEdges => new DrawingObjectEffect(
                preset, BlurRadius: 8, Opacity: 54d / 255d, Color: new CellColor(128, 128, 128)),
            // Bevel / Reflection / 3-D rotation: carry the preset through so the shell can apply a
            // light shadow approximation; faithful geometry is out of scope for the render plan.
            DrawingShapeEffectPreset.Bevel or
            DrawingShapeEffectPreset.Reflection or
            DrawingShapeEffectPreset.ThreeDRotation => new DrawingObjectEffect(
                preset, OffsetX: 2, OffsetY: 2, BlurRadius: 3, Opacity: 45d / 255d, Color: new CellColor(0, 0, 0)),
            _ => null,
        };
    }

    private static void AddPictureBounds(
        Sheet sheet,
        Guid id,
        IReadOnlyList<RowMetric> rowMetrics,
        IReadOnlyList<ColMetric> colMetrics,
        List<DrawingObjectBounds> bounds)
    {
        if (!TryFindPicture(sheet, id, out var picture) ||
            !picture.IsVisible ||
            !TryCreateAnchoredDrawingObjectBounds(
                rowMetrics,
                colMetrics,
                picture.Anchor,
                picture.Width,
                picture.Height,
                out var left,
                out var top,
                out var width,
                out var height,
                picture.AnchorOffsetX,
                picture.AnchorOffsetY))
        {
            return;
        }

        bounds.Add(new DrawingObjectBounds(
            SelectionPaneObjectKind.Picture,
            picture.Id,
            GetObjectDisplayName("Picture", picture.Name),
            picture.Anchor.Row,
            picture.Anchor.Col,
            left,
            top,
            width,
            height,
            picture.RotationDegrees,
            picture.FlipHorizontal,
            picture.FlipVertical,
            PictureKind: picture.Kind,
            ImageBytes: picture.Kind == PictureKind.Image && picture.ImageBytes is { Length: > 0 } imageBytes
                ? imageBytes.ToArray()
                : null,
            ImageContentType: picture.ContentType,
            CropLeft: picture.CropLeft,
            CropTop: picture.CropTop,
            CropRight: picture.CropRight,
            CropBottom: picture.CropBottom,
            SourceRowCount: picture.SourceRowCount,
            SourceColumnCount: picture.SourceColumnCount,
            PictureCells: picture.Kind == PictureKind.CellRangeSnapshot
                ? picture.Cells.ToArray()
                : []));
    }

    private static void AddTextBoxBounds(
        Sheet sheet,
        WorkbookTheme theme,
        Guid id,
        IReadOnlyList<RowMetric> rowMetrics,
        IReadOnlyList<ColMetric> colMetrics,
        List<DrawingObjectBounds> bounds)
    {
        if (!TryFindTextBox(sheet, id, out var textBox) ||
            !textBox.IsVisible ||
            !TryCreateAnchoredDrawingObjectBounds(
                rowMetrics,
                colMetrics,
                textBox.Anchor,
                textBox.Width,
                textBox.Height,
                out var left,
                out var top,
                out var width,
                out var height,
                textBox.AnchorOffsetX,
                textBox.AnchorOffsetY))
        {
            return;
        }

        bounds.Add(new DrawingObjectBounds(
            SelectionPaneObjectKind.TextBox,
            textBox.Id,
            GetObjectDisplayName("Text Box", textBox.Name),
            textBox.Anchor.Row,
            textBox.Anchor.Col,
            left,
            top,
            width,
            height,
            textBox.RotationDegrees,
            textBox.FlipHorizontal,
            textBox.FlipVertical,
            Text: textBox.Text,
            FillColor: ResolveTextBoxFillColor(textBox, theme),
            OutlineColor: ResolveTextBoxOutlineColor(textBox, theme)));
    }

    private static bool TryCreateAnchoredDrawingObjectBounds(
        IReadOnlyList<RowMetric> rowMetrics,
        IReadOnlyList<ColMetric> colMetrics,
        CellAddress anchor,
        double width,
        double height,
        out double left,
        out double top,
        out double normalizedWidth,
        out double normalizedHeight,
        double anchorOffsetX = 0,
        double anchorOffsetY = 0)
    {
        left = 0;
        top = 0;
        normalizedWidth = 0;
        normalizedHeight = 0;
        if (!TryFindRowMetric(rowMetrics, anchor.Row, out var row) ||
            !TryFindColumnMetric(colMetrics, anchor.Col, out var column))
        {
            return false;
        }

        // Add the from-cell sub-cell EMU offsets (already converted to DIP pixels on load, EMU/9525)
        // so side-by-side objects authored within one column keep their distinct positions instead of
        // snapping to the whole-cell left/top edge.
        left = column.LeftOffset + anchorOffsetX;
        top = row.TopOffset + anchorOffsetY;
        normalizedWidth = NormalizeObjectExtent(width);
        normalizedHeight = NormalizeObjectExtent(height);
        return true;
    }

    private static bool TryFindRowMetric(IReadOnlyList<RowMetric> rowMetrics, uint row, out RowMetric metric)
    {
        for (var i = 0; i < rowMetrics.Count; i++)
        {
            var candidate = rowMetrics[i];
            if (candidate.Row > row)
                break;
            if (candidate.Row == row)
            {
                metric = candidate;
                return true;
            }
        }

        metric = null!;
        return false;
    }

    private static bool TryFindColumnMetric(IReadOnlyList<ColMetric> colMetrics, uint column, out ColMetric metric)
    {
        for (var i = 0; i < colMetrics.Count; i++)
        {
            var candidate = colMetrics[i];
            if (candidate.Col > column)
                break;
            if (candidate.Col == column)
            {
                metric = candidate;
                return true;
            }
        }

        metric = null!;
        return false;
    }

    private static bool TryFindDrawingShape(Sheet sheet, Guid id, out DrawingShapeModel shape)
    {
        for (var i = 0; i < sheet.DrawingShapes.Count; i++)
        {
            if (sheet.DrawingShapes[i].Id == id)
            {
                shape = sheet.DrawingShapes[i];
                return true;
            }
        }

        shape = null!;
        return false;
    }

    private static bool TryFindPicture(Sheet sheet, Guid id, out PictureModel picture)
    {
        for (var i = 0; i < sheet.Pictures.Count; i++)
        {
            if (sheet.Pictures[i].Id == id)
            {
                picture = sheet.Pictures[i];
                return true;
            }
        }

        picture = null!;
        return false;
    }

    private static bool TryFindTextBox(Sheet sheet, Guid id, out TextBoxModel textBox)
    {
        for (var i = 0; i < sheet.TextBoxes.Count; i++)
        {
            if (sheet.TextBoxes[i].Id == id)
            {
                textBox = sheet.TextBoxes[i];
                return true;
            }
        }

        textBox = null!;
        return false;
    }

    private static double NormalizeObjectExtent(double extent) =>
        double.IsFinite(extent) && extent > 0 ? extent : 1;

    private static CellColor? ResolveShapeFillColor(DrawingShapeModel shape, WorkbookTheme theme) =>
        shape.ResolveFillColor(theme, DrawingShapeModel.ResolveDefaultFillColor(theme));

    private static CellColor? ResolveShapeOutlineColor(DrawingShapeModel shape, WorkbookTheme theme) =>
        shape.GetEffectiveOutlineColor(theme, DrawingShapeModel.ResolveDefaultOutlineColor(theme));

    private static CellColor? ResolveTextBoxFillColor(TextBoxModel textBox, WorkbookTheme theme) =>
        textBox.ResolveFillColor(theme, CellColor.White);

    private static CellColor? ResolveTextBoxOutlineColor(TextBoxModel textBox, WorkbookTheme theme) =>
        textBox.OutlineThemeColor?.Resolve(theme) ?? textBox.OutlineColor;

    private static string GetObjectDisplayName(string fallback, string? name) =>
        string.IsNullOrWhiteSpace(name) ? fallback : name.Trim();
}
