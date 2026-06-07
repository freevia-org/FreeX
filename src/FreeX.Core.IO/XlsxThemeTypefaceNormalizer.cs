using System.IO.Compression;
using System.Xml.Linq;

namespace FreeX.Core.IO;

internal static class XlsxThemeTypefaceNormalizer
{
    public static void NormalizePackage(ZipArchive archive)
    {
        foreach (var themeEntry in archive.Entries.Where(IsThemeXmlEntry).ToList())
        {
            var themeXml = XlsxPackageXmlEditor.LoadXml(themeEntry);
            var root = themeXml.Root;
            if (root is null || !SanitizeNonEmptyTypefaceAttributes(root))
                continue;

            XlsxPackageXmlEditor.ReplaceXml(archive, themeEntry.FullName, themeXml);
        }
    }

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

    private static bool IsThemeXmlEntry(ZipArchiveEntry entry)
    {
        var path = XlsxPackagePath.NormalizeZipPath(entry.FullName.Replace('\\', '/'));
        return path.StartsWith("xl/theme/", StringComparison.OrdinalIgnoreCase) &&
               path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) &&
               !path.Contains("/_rels/", StringComparison.OrdinalIgnoreCase);
    }
}
