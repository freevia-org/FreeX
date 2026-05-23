using System.IO.Compression;
using System.Xml.Linq;

namespace Freexcel.Core.IO;

internal static class XlsxSharedStringMetadataPreserver
{
    public static void PreserveRichTextAndPhonetics(ZipArchive sourceArchive, ZipArchive targetArchive)
    {
        var sourceEntry = sourceArchive.GetEntry("xl/sharedStrings.xml");
        var targetEntry = targetArchive.GetEntry("xl/sharedStrings.xml");
        if (sourceEntry is null || targetEntry is null)
            return;

        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var sourceXml = XlsxPackageXmlEditor.LoadXml(sourceEntry);
        var targetXml = XlsxPackageXmlEditor.LoadXml(targetEntry);
        var sourceRoot = sourceXml.Root;
        var targetRoot = targetXml.Root;
        if (sourceRoot is null || targetRoot is null)
            return;

        var sourceRichStringsByText = GetUniqueSharedStringsByPlainText(
            sourceRoot.Elements(workbookNs + "si")
                .Where(item => HasRichSharedStringMetadata(item, workbookNs)),
            workbookNs);
        if (sourceRichStringsByText.Count == 0)
            return;

        var targetStringsByText = GetUniqueSharedStringsByPlainText(
            targetRoot.Elements(workbookNs + "si"),
            workbookNs);

        var changed = false;
        foreach (var (plainText, sourceString) in sourceRichStringsByText)
        {
            if (!targetStringsByText.TryGetValue(plainText, out var targetString))
                continue;

            targetString.ReplaceWith(new XElement(sourceString));
            changed = true;
        }

        if (changed)
            XlsxPackageXmlEditor.ReplaceXml(targetArchive, "xl/sharedStrings.xml", targetXml);
    }

    private static Dictionary<string, XElement> GetUniqueSharedStringsByPlainText(
        IEnumerable<XElement> sharedStrings,
        XNamespace workbookNs)
    {
        return sharedStrings
            .Select(element => new
            {
                Text = ReadSharedStringPlainText(element, workbookNs),
                Element = element
            })
            .Where(item => !string.IsNullOrEmpty(item.Text))
            .GroupBy(item => item.Text, StringComparer.Ordinal)
            .Where(group => group.Count() == 1)
            .ToDictionary(
                group => group.Key,
                group => group.Single().Element,
                StringComparer.Ordinal);
    }

    private static bool HasRichSharedStringMetadata(XElement sharedString, XNamespace workbookNs) =>
        sharedString.Elements(workbookNs + "r").Any() ||
        sharedString.Element(workbookNs + "rPh") is not null ||
        sharedString.Element(workbookNs + "phoneticPr") is not null;

    private static string ReadSharedStringPlainText(XElement sharedString, XNamespace workbookNs)
    {
        var runs = sharedString.Elements(workbookNs + "r").ToList();
        if (runs.Count > 0)
            return string.Concat(runs.Select(run => run.Element(workbookNs + "t")?.Value ?? string.Empty));

        return sharedString.Element(workbookNs + "t")?.Value ?? string.Empty;
    }
}
