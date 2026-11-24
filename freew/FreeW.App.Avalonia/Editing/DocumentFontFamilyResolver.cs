using Avalonia.Media;

namespace FreeW.App.Avalonia.Editing;

/// <summary>Resolves document faces without changing the font names stored in a document.</summary>
public static class DocumentFontFamilyResolver
{
    // A composite family tries installed Calibri first. The resource candidate works in the app,
    // exports and rendering tools without installing a font or changing the global UI font.
    private static readonly FontFamily Calibri = new(
        "Calibri, avares://FreeW/Assets/Fonts/Carlito#Carlito");

    public static FontFamily Resolve(string? authoredFamily)
    {
        if (string.IsNullOrWhiteSpace(authoredFamily))
            return FontFamily.Default;
        return string.Equals(authoredFamily.Trim(), "Calibri", StringComparison.OrdinalIgnoreCase)
            ? Calibri
            : new FontFamily(authoredFamily);
    }
}
