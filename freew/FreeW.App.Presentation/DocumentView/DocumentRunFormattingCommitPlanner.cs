using FreeW.Core.Model;

namespace FreeW.App.Presentation.DocumentView;

/// <summary>
/// Preserves direct run formatting when a native renderer commits its effective presentation.
/// Renderers commonly materialize inherited style/default values as local native properties; an
/// unchanged rendered value must therefore round-trip to the original direct value instead of
/// flattening the formatting cascade into every run.
/// </summary>
public static class DocumentRunFormattingCommitPlanner
{
    public static RunFormatting Resolve(
        RunFormatting direct,
        RunFormatting rendered,
        RunFormatting observed,
        bool isVisuallyHidden)
    {
        ArgumentNullException.ThrowIfNull(direct);
        ArgumentNullException.ThrowIfNull(rendered);
        ArgumentNullException.ThrowIfNull(observed);

        if (isVisuallyHidden)
            return direct;

        return direct with
        {
            Bold = Changed(rendered.Bold, observed.Bold) ? observed.Bold : direct.Bold,
            Italic = Changed(rendered.Italic, observed.Italic) ? observed.Italic : direct.Italic,
            Underline = Changed(rendered.Underline, observed.Underline) ? observed.Underline : direct.Underline,
            Strikethrough = Changed(rendered.Strikethrough, observed.Strikethrough)
                ? observed.Strikethrough
                : direct.Strikethrough,
            SmallCaps = Changed(rendered.SmallCaps, observed.SmallCaps) ? observed.SmallCaps : direct.SmallCaps,
            AllCaps = Changed(rendered.AllCaps, observed.AllCaps) ? observed.AllCaps : direct.AllCaps,
            VerticalAlign = Changed(rendered.VerticalAlign, observed.VerticalAlign)
                ? observed.VerticalAlign
                : direct.VerticalAlign,
            Rtl = Changed(rendered.Rtl, observed.Rtl) ? observed.Rtl : direct.Rtl,
            FontFamily = ChangedText(rendered.FontFamily, observed.FontFamily)
                ? observed.FontFamily
                : direct.FontFamily,
            FontSizePt = ChangedNumber(rendered.FontSizePt, observed.FontSizePt)
                ? observed.FontSizePt
                : direct.FontSizePt,
            ColorHex = ChangedText(rendered.ColorHex, observed.ColorHex)
                ? observed.ColorHex
                : direct.ColorHex,
            HighlightColorHex = ChangedText(rendered.HighlightColorHex, observed.HighlightColorHex)
                ? observed.HighlightColorHex
                : direct.HighlightColorHex,
        };
    }

    private static bool Changed<T>(T rendered, T observed) where T : struct =>
        !EqualityComparer<T>.Default.Equals(rendered, observed);

    private static bool ChangedText(string? rendered, string? observed) =>
        !string.Equals(rendered, observed, StringComparison.OrdinalIgnoreCase);

    private static bool ChangedNumber(double? rendered, double? observed) =>
        rendered is null || observed is null
            ? rendered != observed
            : Math.Abs(rendered.Value - observed.Value) > 0.0001;
}
