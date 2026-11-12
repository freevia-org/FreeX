namespace FreeW.Core.Model;

/// <summary>
/// The theme-font tokens of one <c>w:rFonts</c> (<c>w:asciiTheme</c>, <c>w:hAnsiTheme</c>,
/// <c>w:eastAsiaTheme</c>, <c>w:cstheme</c>), e.g. <c>"majorBidi"</c> or <c>"minorHAnsi"</c>.
/// </summary>
public sealed record RunThemeFontTokens(
    string? Ascii = null,
    string? HAnsi = null,
    string? EastAsia = null,
    string? ComplexScript = null)
{
    public bool IsEmpty =>
        string.IsNullOrEmpty(Ascii) && string.IsNullOrEmpty(HAnsi)
        && string.IsNullOrEmpty(EastAsia) && string.IsNullOrEmpty(ComplexScript);
}

/// <summary>
/// The package theme's non-Latin font slots, captured on open for render-time resolution of theme-font
/// tokens (the Latin major/minor fonts come from <see cref="TextDocument.Theme"/>, so a theme change
/// re-fonts token runs). <c>Major/MinorBidiScript</c> is the theme's per-script font for the document's
/// theme bidi language (<c>w:themeFontLang/@w:bidi</c>), which Word uses when <c>a:cs</c> is empty.
/// Render-only: never written.
/// </summary>
public sealed record ThemeScriptFonts(
    string? MajorEastAsia = null,
    string? MinorEastAsia = null,
    string? MajorComplexScript = null,
    string? MinorComplexScript = null,
    string? MajorBidiScript = null,
    string? MinorBidiScript = null);

/// <summary>Resolves <c>w:rFonts</c> theme-font tokens to font names at render time.</summary>
public static class ThemeFontTokenResolver
{
    /// <summary>
    /// The font a token names: its suffix selects the slot (<c>…EastAsia</c>, <c>…Bidi</c>, Latin
    /// otherwise), <c>major</c>/<c>minor</c> the heading/body font. Null for an unknown token or an
    /// unspecified (empty) theme slot.
    /// </summary>
    public static string? Resolve(string? token, TextDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (string.IsNullOrEmpty(token) || token.Length < 5)
            return null;

        var isMajor = token.StartsWith("major", StringComparison.OrdinalIgnoreCase);
        if (!isMajor && !token.StartsWith("minor", StringComparison.OrdinalIgnoreCase))
            return null;

        var suffix = token[5..];
        var scripts = document.ThemeScriptFonts;
        string? typeface;
        if (suffix.Equals("EastAsia", StringComparison.OrdinalIgnoreCase))
        {
            typeface = isMajor ? scripts?.MajorEastAsia : scripts?.MinorEastAsia;
        }
        else if (suffix.Equals("Bidi", StringComparison.OrdinalIgnoreCase))
        {
            typeface = isMajor ? scripts?.MajorComplexScript : scripts?.MinorComplexScript;
            if (string.IsNullOrWhiteSpace(typeface))
                typeface = isMajor ? scripts?.MajorBidiScript : scripts?.MinorBidiScript;
        }
        else
        {
            typeface = isMajor ? document.Theme.HeadingFont : document.Theme.BodyFont;
        }

        return string.IsNullOrWhiteSpace(typeface) ? null : typeface;
    }

    /// <summary>
    /// <paramref name="formatting"/> with its theme tokens resolved into any font slot it leaves unset
    /// (a literal font on the run wins), and its symbol font applied. Returns the input when nothing applies.
    /// </summary>
    public static RunFormatting ApplyForRendering(RunFormatting formatting, TextDocument document)
    {
        ArgumentNullException.ThrowIfNull(formatting);
        ArgumentNullException.ThrowIfNull(document);

        var result = formatting;
        if (formatting.ThemeFonts is { IsEmpty: false } tokens)
        {
            var ascii = formatting.FontFamily
                ?? Resolve(tokens.Ascii, document)
                ?? Resolve(tokens.HAnsi, document);
            var eastAsia = formatting.EastAsiaFontFamily ?? Resolve(tokens.EastAsia, document);
            var complexScript = formatting.ComplexScriptFontFamily ?? Resolve(tokens.ComplexScript, document);
            if (ascii != formatting.FontFamily
                || eastAsia != formatting.EastAsiaFontFamily
                || complexScript != formatting.ComplexScriptFontFamily)
            {
                result = result with
                {
                    FontFamily = ascii,
                    EastAsiaFontFamily = eastAsia,
                    ComplexScriptFontFamily = complexScript,
                };
            }
        }

        if (formatting.SymbolFont is { Length: > 0 } symbolFont)
            result = result with { FontFamily = symbolFont };
        return result;
    }
}
