using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Free.Shared.AppServices;
using FreeX.App.Avalonia.Charts;
using FreeX.App.Presentation.Charts;
using FreeX.App.Presentation.DrawingUI;
using FreeX.Core.Calc;
using FreeX.Core.Model;
using SkiaSharp;

namespace FreeX.App.Avalonia;

/// <summary>
/// R139-shared-clipboard-images: renders a selected chart/shape/picture/text box into a PNG-backed
/// <see cref="PlatformClipboardImage"/> for the OS clipboard, mirroring the WPF host's identical fix
/// (<c>MainWindow.ClipboardCommands.TryRenderDrawingObjectClipboardImage</c>). Picture reuses SkiaSharp's
/// own decoder for full fidelity, including whatever alpha channel the source bytes carry. Charts use
/// the production chart layout and renderer with workbook data, independently of the visible viewport.
/// Shape/TextBox retain their simple geometry with bounded, wrapped text. Returns null (never throws) when the
/// object can no longer be found or nothing sensible can be rendered.
/// </summary>
internal static class DrawingObjectClipboardImageRenderer
{
    public static PlatformClipboardImage? TryRender(
        Sheet? sheet, Workbook workbook, SelectionPaneObjectKind kind, Guid objectId)
    {
        if (sheet is null)
            return null;

        try
        {
            return kind switch
            {
                SelectionPaneObjectKind.Picture => RenderPicture(sheet, objectId),
                SelectionPaneObjectKind.Chart => RenderChart(sheet, workbook, objectId),
                SelectionPaneObjectKind.Shape => RenderShapeStandIn(sheet, workbook.Theme, objectId),
                SelectionPaneObjectKind.TextBox => RenderTextBoxStandIn(sheet, workbook.Theme, objectId),
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

    private static PlatformClipboardImage? RenderChart(Sheet sheet, Workbook workbook, Guid objectId)
    {
        var chart = sheet.Charts.Find(c => c.Id == objectId);
        if (chart is null || CreateChartVisual(workbook, chart) is not { } visual)
            return null;

        var size = new Size(visual.Width, visual.Height);
        visual.Measure(size);
        visual.Arrange(new Rect(size));
        var pixels = new PixelSize((int)Math.Ceiling(size.Width), (int)Math.Ceiling(size.Height));
        using var target = new RenderTargetBitmap(pixels, new Vector(96, 96));
        target.Render(visual);
        return EncodeBitmap(target, pixels.Width, pixels.Height);
    }

    // A fresh visual excludes selection handles and other on-sheet interaction chrome. Read the
    // workbook directly: a chart may reference another sheet or cells outside the viewport sample.
    internal static Canvas? CreateChartVisual(Workbook workbook, ChartModel chart)
    {
        if (!ChartLayoutEngine.IsSupported(chart.Type))
            return null;

        var readCell = ViewportService.CreateChartDataCellReader(workbook, chart);
        bool ReadCell(uint row, uint col, out ScalarValue? rawValue, out double value, out string text)
        {
            (rawValue, text) = readCell(row, col);
            return ChartRenderPolicyPlanner.TryGetNumericValue(rawValue, text, out value);
        }

        var width = Math.Max(1, chart.Width);
        var height = Math.Max(1, chart.Height);
        var inset = Math.Min(28, Math.Min(width, height) / 4);
        var plotArea = new PlotRect(inset, inset, Math.Max(1, width - (2 * inset)), Math.Max(1, height - (2 * inset)));
        var request = ChartLayoutRequestBuilder.TryBuild(chart, plotArea, ReadCell, new AvaloniaTextMeasurer());
        if (request is null)
            return null;

        var layout = ChartLayoutEngine.Layout(request.WithChartSize(width, height));
        return new AvaloniaChartRenderer(chart, workbook.Theme).Render(layout, width, height);
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

        using var target = new RenderTargetBitmap(new PixelSize(width, height), new Vector(96, 96));
        using (var context = target.CreateDrawingContext())
        {
            if (fill is { } f)
                context.FillRectangle(new SolidColorBrush(Color.FromRgb(f.R, f.G, f.B)), new Rect(0, 0, width, height));

            if (outline is { } o)
            {
                var pen = new Pen(new SolidColorBrush(Color.FromRgb(o.R, o.G, o.B)), 1.5);
                context.DrawRectangle(null, pen, new Rect(0.75, 0.75, Math.Max(0, width - 1.5), Math.Max(0, height - 1.5)));
            }

            if (!string.IsNullOrEmpty(text))
                context.DrawText(CreateTextLayout(text, width, height), new Point(4, 4));
        }
        return EncodeBitmap(target, width, height);
    }

    internal static FormattedText CreateTextLayout(string text, double width, double height) => new(
        text,
        System.Globalization.CultureInfo.CurrentCulture,
        FlowDirection.LeftToRight,
        new Typeface("Segoe UI"),
        12,
        Brushes.Black)
    {
        MaxTextWidth = Math.Max(1, width - 8),
        MaxTextHeight = Math.Max(1, height - 8),
        Trimming = TextTrimming.CharacterEllipsis,
    };

    private static PlatformClipboardImage? EncodeBitmap(RenderTargetBitmap target, int width, int height)
    {
        using var stream = new MemoryStream();
        target.Save(stream);
        return stream.Length == 0 ? null : new PlatformClipboardImage(stream.ToArray(), width, height);
    }
}
