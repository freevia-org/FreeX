using Avalonia.Media;
using Avalonia.Platform;
using Free.Shared.Pdf;
using FreeW.App.Avalonia.Editing;

namespace FreeW.App.Avalonia.Pdf;

/// <summary>Supplies FreeW's bundled document fallback only when Avalonia actually selected it.</summary>
internal static class FreeWAvaloniaPdfFontSource
{
    internal static Stream? Open(string? authoredFamily, PdfFontFace face) =>
        OpenCore(authoredFamily, face, () => { _ = FontManager.Current; }, typeface => typeface.GlyphTypeface.FamilyName);

    internal static Stream? OpenCore(string? authoredFamily, PdfFontFace face,
        Action acquireFontServices, Func<Typeface, string?> selectedFamily)
    {
        if (!IsCalibri(authoredFamily))
            return null;
        // Model-only exports can run without Avalonia's font services. Limit this fallback
        // to service acquisition: real face resolution and asset failures must remain visible.
        try { acquireFontServices(); }
        catch (InvalidOperationException) { return null; }
        var style = face is PdfFontFace.Italic or PdfFontFace.BoldItalic ? FontStyle.Italic : FontStyle.Normal;
        var weight = face is PdfFontFace.Bold or PdfFontFace.BoldItalic ? FontWeight.Bold : FontWeight.Normal;
        var typeface = new Typeface(DocumentFontFamilyResolver.Resolve(authoredFamily), style, weight);
        // GlyphTypeface reads the actual font's OpenType family name. The platform family
        // can instead retain an alias. This cached Avalonia object is borrowed, never disposed here.
        return OpenResolvedFamily(authoredFamily, face, selectedFamily(typeface));
    }

    internal static Stream? OpenResolvedFamily(string? authoredFamily, PdfFontFace face, string? selectedFamily)
    {
        if (!IsCalibri(authoredFamily) || !string.Equals(selectedFamily, "Carlito", StringComparison.OrdinalIgnoreCase))
            return null;
        var variant = face switch
        {
            PdfFontFace.Bold => "Bold",
            PdfFontFace.Italic => "Italic",
            PdfFontFace.BoldItalic => "BoldItalic",
            _ => "Regular",
        };
        return AssetLoader.Open(new Uri($"avares://FreeW/Assets/Fonts/Carlito/Carlito-{variant}.ttf"));
    }

    private static bool IsCalibri(string? family) =>
        string.Equals(family?.Trim(), "Calibri", StringComparison.OrdinalIgnoreCase);
}
