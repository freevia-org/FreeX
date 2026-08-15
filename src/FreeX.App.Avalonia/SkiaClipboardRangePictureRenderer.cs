using System.Globalization;
using FreeX.App.Presentation.Editing;
using SkiaSharp;

namespace FreeX.App.Avalonia;

internal static class SkiaClipboardRangePictureRenderer
{
    public static PlatformClipboardImage? TryRender(ClipboardRangePicturePlan? plan)
    {
        if (plan is null)
            return null;

        try
        {
            using var bitmap = new SKBitmap(
                plan.PixelWidth,
                plan.PixelHeight,
                SKColorType.Rgba8888,
                SKAlphaType.Premul);
            using var canvas = new SKCanvas(bitmap);
            canvas.Clear(ToSkColor(ClipboardRangePicturePlanner.BackgroundColor));

            using var gridPaint = new SKPaint
            {
                Color = ToSkColor(ClipboardRangePicturePlanner.GridlineColor),
                IsAntialias = false,
                StrokeWidth = 1,
                Style = SKPaintStyle.Stroke,
            };
            using var textPaint = new SKPaint
            {
                Color = ToSkColor(ClipboardRangePicturePlanner.TextColor),
                IsAntialias = true,
                Style = SKPaintStyle.Fill,
            };
            using var typeface = SKTypeface.FromFamilyName("Segoe UI");
            using var font = new SKFont(typeface ?? SKTypeface.Default, (float)ClipboardRangePicturePlanner.FontSize);
            var metrics = font.Metrics;
            var textHeight = metrics.Descent - metrics.Ascent;

            for (var row = 0; row < plan.RowCount; row++)
            {
                for (var column = 0; column < plan.ColumnCount; column++)
                {
                    var left = column * ClipboardRangePicturePlanner.CellWidth;
                    var top = row * ClipboardRangePicturePlanner.CellHeight;
                    var rect = new SKRect(
                        left,
                        top,
                        left + ClipboardRangePicturePlanner.CellWidth,
                        top + ClipboardRangePicturePlanner.CellHeight);
                    canvas.DrawRect(rect, gridPaint);

                    var text = plan.TextAt(row, column);
                    if (string.IsNullOrEmpty(text))
                        continue;

                    var availableWidth = ClipboardRangePicturePlanner.CellWidth
                        - (2 * ClipboardRangePicturePlanner.TextPaddingHorizontal);
                    var displayText = FitText(text, font, availableWidth);
                    if (displayText.Length == 0)
                        continue;

                    var restoreCount = canvas.Save();
                    canvas.ClipRect(new SKRect(
                        rect.Left + ClipboardRangePicturePlanner.TextPaddingHorizontal,
                        rect.Top + ClipboardRangePicturePlanner.TextPaddingVertical,
                        rect.Right - ClipboardRangePicturePlanner.TextPaddingHorizontal,
                        rect.Bottom - ClipboardRangePicturePlanner.TextPaddingVertical));
                    var baseline = rect.Top
                        + ((ClipboardRangePicturePlanner.CellHeight - textHeight) / 2f)
                        - metrics.Ascent;
                    canvas.DrawText(
                        displayText,
                        rect.Left + ClipboardRangePicturePlanner.TextPaddingHorizontal,
                        baseline,
                        font,
                        textPaint);
                    canvas.RestoreToCount(restoreCount);
                }
            }

            using var image = SKImage.FromBitmap(bitmap);
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            return data is null
                ? null
                : new PlatformClipboardImage(data.ToArray(), plan.PixelWidth, plan.PixelHeight);
        }
        catch
        {
            // The image is an optional interoperability flavor. Text/HTML/CSV copy must still succeed.
            return null;
        }
    }

    private static string FitText(string text, SKFont font, float availableWidth)
    {
        if (font.MeasureText(text) <= availableWidth)
            return text;

        const string ellipsis = "\u2026";
        var ellipsisWidth = font.MeasureText(ellipsis);
        if (ellipsisWidth > availableWidth)
            return string.Empty;

        var textElements = StringInfo.ParseCombiningCharacters(text);
        var low = 0;
        var high = textElements.Length;
        while (low < high)
        {
            var middle = (low + high + 1) / 2;
            var end = middle == textElements.Length ? text.Length : textElements[middle];
            if (font.MeasureText(text[..end]) + ellipsisWidth <= availableWidth)
                low = middle;
            else
                high = middle - 1;
        }

        if (low == 0)
            return ellipsis;

        var prefixEnd = low == textElements.Length ? text.Length : textElements[low];
        return text[..prefixEnd] + ellipsis;
    }

    private static SKColor ToSkColor(ClipboardRangePictureColor color) =>
        new(color.Red, color.Green, color.Blue);
}
