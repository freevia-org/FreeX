using FreeP.Core.Model;

namespace FreeP.App.Compositor;

/// <summary>
/// Shared typography defaults for the WPF and Avalonia in-canvas rich editors.
/// WPF's FlowDocument is the authority for inherited run metrics, so the Avalonia
/// surface must not derive its fallback from the hidden native TextBox theme.
/// </summary>
public static class InCanvasRichTextEditorDefaults
{
    public const string FallbackFontFamily = "Calibri";
    public const double ShapeFallbackFontSizePt = 14;
    public const double TableCellFallbackFontSizePt = 13;

    public static double ResolveFallbackFontSize(TextBody? body, double defaultFontSizePt)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(defaultFontSizePt);

        return body?.Paragraphs
                   .SelectMany(paragraph => paragraph.Runs)
                   .Select(run => run.FontSizePt)
                   .FirstOrDefault(size => size is > 0)
               ?? defaultFontSizePt;
    }
}
