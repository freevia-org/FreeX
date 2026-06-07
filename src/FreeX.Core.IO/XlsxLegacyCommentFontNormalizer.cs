using System.IO.Compression;
using System.Xml.Linq;

namespace FreeX.Core.IO;

internal static class XlsxLegacyCommentFontNormalizer
{
    public static void NormalizePackage(ZipArchive archive)
    {
        foreach (var commentsEntry in archive.Entries.Where(IsLegacyCommentXmlEntry).ToList())
        {
            var commentsXml = XlsxPackageXmlEditor.LoadXml(commentsEntry);
            if (!SanitizeRunFontNames(commentsXml))
                continue;

            XlsxPackageXmlEditor.ReplaceXml(archive, commentsEntry.FullName, commentsXml);
        }
    }

    public static bool SanitizeRunFontNames(XDocument commentsXml)
    {
        var root = commentsXml.Root;
        if (root is null)
            return false;

        var changed = false;
        var workbookNs = root.Name.Namespace;
        foreach (var richTextFont in root.Descendants(workbookNs + "rFont"))
            changed |= XlsxFontNameSanitizer.SanitizeValAttribute(richTextFont);

        return changed;
    }

    private static bool IsLegacyCommentXmlEntry(ZipArchiveEntry entry)
    {
        var path = XlsxPackagePath.NormalizeZipPath(entry.FullName.Replace('\\', '/'));
        return path.StartsWith("xl/comments", StringComparison.OrdinalIgnoreCase) &&
               path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) &&
               !path.Contains("/_rels/", StringComparison.OrdinalIgnoreCase);
    }
}
