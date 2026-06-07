using System.IO.Compression;
using System.Xml.Linq;

namespace FreeX.Core.IO;

internal static class XlsxRichTextFontNormalizer
{
    private static readonly XNamespace WorkbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    public static void NormalizePackage(ZipArchive archive)
    {
        NormalizeSharedStrings(archive);
        NormalizeWorksheetInlineStrings(archive);
    }

    public static void NormalizeSharedStrings(ZipArchive archive)
    {
        var sharedStringsEntry = archive.GetEntry("xl/sharedStrings.xml");
        if (sharedStringsEntry is null)
            return;

        var sharedStringsXml = XlsxPackageXmlEditor.LoadXml(sharedStringsEntry);
        var root = sharedStringsXml.Root;
        if (root is null)
            return;

        if (!SanitizeRunFontNames(root))
            return;

        XlsxPackageXmlEditor.ReplaceXml(archive, "xl/sharedStrings.xml", sharedStringsXml);
    }

    public static void NormalizeWorksheetInlineStrings(ZipArchive archive)
    {
        foreach (var worksheetEntry in archive.Entries.Where(IsWorksheetXmlEntry).ToList())
        {
            var worksheetXml = XlsxPackageXmlEditor.LoadXml(worksheetEntry);
            var root = worksheetXml.Root;
            if (root is null)
                continue;

            var changed = false;
            foreach (var inlineStringFont in root
                         .Descendants(WorkbookNs + "is")
                         .Descendants(WorkbookNs + "rFont"))
            {
                changed |= XlsxFontNameSanitizer.SanitizeValAttribute(inlineStringFont);
            }

            if (changed)
                XlsxPackageXmlEditor.ReplaceXml(archive, worksheetEntry.FullName, worksheetXml);
        }
    }

    public static bool SanitizeRunFontNames(XElement root)
    {
        var changed = false;
        foreach (var richTextFont in root.Descendants(WorkbookNs + "rFont"))
            changed |= XlsxFontNameSanitizer.SanitizeValAttribute(richTextFont);

        return changed;
    }

    private static bool IsWorksheetXmlEntry(ZipArchiveEntry entry)
    {
        var path = XlsxPackagePath.NormalizeZipPath(entry.FullName.Replace('\\', '/'));
        return path.StartsWith("xl/worksheets/", StringComparison.OrdinalIgnoreCase) &&
               path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) &&
               !path.Contains("/_rels/", StringComparison.OrdinalIgnoreCase);
    }
}
