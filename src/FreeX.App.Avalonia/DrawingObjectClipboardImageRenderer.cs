using Free.Shared.AppServices;
using FreeX.App.Presentation.DrawingUI;
using FreeX.Core.Model;
using SkiaSharp;

namespace FreeX.App.Avalonia;

/// <summary>
/// R139-shared-clipboard-images: renders a selected chart/shape/picture/text box into a PNG-backed
/// <see cref="PlatformClipboardImage"/> for the OS clipboard, mirroring the WPF host's identical fix
/// (<c>MainWindow.ClipboardCommands.TryRenderDrawingObjectClipboardImage</c>). Picture reuses SkiaSharp's
/// own decoder for full fidelity, including whatever alpha channel the source bytes carry. Chart/Shape/
/// TextBox have no isolated off-screen renderer available in this shell without attaching a live control
/// to a compositor (AvaloniaChartRenderer/the shape-painting overlay are both tied to an on-screen visual
/// tree pass), so they get the same simple filled-rectangle-plus-text stand-in <see cref="SkiaClipboardRangePictureRenderer"/>
/// already established for the plain cell-range picture flavor. Returns null (never throws) when the
/// object can no longer be found or nothing sensible can be rendered.
/// </summary>
internal static class DrawingObjectClipboardImageRenderer
{
    public static PlatformClipboardImage? TryRender(
        Sheet? sheet, WorkbookTheme theme, SelectionPaneObjectKind kind, Guid objectId)
    {
        if (sheet is null)
            return null;

        try
        {
            return kind switch
            {
                SelectionPaneObjectKind.Picture => RenderPicture(sheet, objectId),
                SelectionPaneObjectKind.Chart => RenderChartStandIn(sheet, objectId),
                SelectionPaneObjectKind.Shape => RenderShapeStandIn(sheet, theme, objectId),
                SelectionPaneObjectKind.TextBox => RenderTextBoxStandIn(sheet, theme, objectId),
                _ => null,
            };
        }
        catch
        {
            // Best-effort extra clipboard flavor -- never let a rendering hiccup fail the copy itself.
            return null;
        }
    }

    private static PlatformClipboardImage? RenderPicture(Sheet sheet, Guid objectId)
    {
        var picture = sheet.Pictures.Find(p => p.Id == objectId);
        if (picture?.ImageBytes is not { Length: > 0 } imageBytes)
            return null;

        using var bitmap = SKBitmap.Decode(imageBytes);
        if (bitmap is null)
            return null;

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data is null ? null : new PlatformClipboardImage(data.ToArray(), bitmap.Width, bitmap.Height);
    }

    private static PlatformClipboardImage? RenderChartStandIn(Sheet sheet, Guid objectId)
    {
        var chart = sheet.Charts.Find(c => c.Id == objectId);
        if (chart is null)
            return null;
        return RenderStandIn(
            chart.Width,
            chart.Height,
            DrawingShapeModel.DefaultFillColor,
            DrawingShapeModel.DefaultOutlineColor,
            chart.Title);
    }

    private static PlatformClipboardImage? RenderShapeStandIn(Sheet sheet, WorkbookTheme theme, Guid objectId)
    {
        var shape = sheet.DrawingShapes.Find(s => s.Id == objectId);
        if (shape is null)
            return null;
        var fill = shape.ResolveFillColor(theme, DrawingShapeModel.DefaultFillColor);
        var outline = shape.OutlineHasNoFill
            ? null
            : (CellColor?)shape.GetEffectiveOutlineColor(theme, DrawingShapeModel.DefaultOutlineColor);
        return RenderStandIn(shape.Width, shape.Height, fill, outline, shape.ShapeText);
    }

    private static PlatformClipboardImage? RenderTextBoxStandIn(Sheet sheet, WorkbookTheme theme, Guid objectId)
    {
        var textBox = TextBoxModel.FindById(sheet.TextBoxes, objectId);
        if (textBox is null)
            return null;
        var fill = textBox.ResolveFillColor(theme, new CellColor(255, 255, 255));
        var outline = textBox.OutlineHasNoFill
            ? null
            : (CellColor?)textBox.GetEffectiveOutlineColor(theme, new CellColor(0, 0, 0));
        return RenderStandIn(textBox.Width, textBox.Height, fill, outline, textBox.Text);
    }

    /// <summary>
    /// Smallest correct stand-in for an object's OS-clipboard picture flavor: a fully transparent
    /// canvas painted only where <paramref name="fill"/>/<paramref name="outline"/> actually apply,
    /// so an object authored with no fill (e.g. a text box's default "No Fill, No Line") pastes as a
    /// transparent picture rather than an opaque box.
    /// </summary>
    private static PlatformClipboardImage? RenderStandIn(
        double widthDip, double heightDip, CellColor? fill, CellColor? outline, string? text)
    {
        var width = Math.Max(1, (int)Math.Round(widthDip));
        var height = Math.Max(1, (int)Math.Round(heightDip));

        using var bitmap = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);

        if (fill is { } f)
        {
            using var fillPaint = new SKPaint { Color = new SKColor(f.R, f.G, f.B), Style = SKPaintStyle.Fill };
            canvas.DrawRect(new SKRect(0, 0, width, height), fillPaint);
        }

        if (outline is { } o)
        {
            using var outlinePaint = new SKPaint
            {
                Color = new SKColor(o.R, o.G, o.B),
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 1.5f,
            };
            canvas.DrawRect(new SKRect(0.75f, 0.75f, width - 0.75f, height - 0.75f), outlinePaint);
        }

        if (!string.IsNullOrEmpty(text))
        {
            using var typeface = SKTypeface.FromFamilyName("Segoe UI");
            using var font = new SKFont(typeface ?? SKTypeface.Default, 12f);
            using var textPaint = new SKPaint { Color = SKColors.Black, IsAntialias = true };
            var metrics = font.Metrics;
            canvas.Save();
            canvas.ClipRect(new SKRect(4, 4, Math.Max(4, width - 4), Math.Max(4, height - 4)));
            canvas.DrawText(text, 4, 4 - metrics.Ascent, font, textPaint);
            canvas.Restore();
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data is null ? null : new PlatformClipboardImage(data.ToArray(), width, height);
    }
}
