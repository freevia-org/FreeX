using System.Xml.Linq;

namespace FreeX.Core.IO;

internal static class XlsxThemeTypefaceNormalizer
{
    public static bool SanitizeNonEmptyTypefaceAttributes(XElement element)
    {
        var changed = false;
        foreach (var typeface in element.DescendantsAndSelf().Attributes("typeface"))
        {
            if (typeface.Value.Length == 0)
                continue;

            var normalized = XlsxFontNameSanitizer.NormalizeFontName(typeface.Value);
            if (string.Equals(typeface.Value, normalized, StringComparison.Ordinal))
                continue;

            typeface.Value = normalized;
            changed = true;
        }

        return changed;
    }
}
