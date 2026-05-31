using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace FreeX.Core.IO;

internal static class XlsxSharedStringMetadataPreserver
{
    public static void PreserveRichTextAndPhonetics(ZipArchive sourceArchive, ZipArchive targetArchive)
    {
        var sourceEntry = sourceArchive.GetEntry("xl/sharedStrings.xml");
        var targetEntry = targetArchive.GetEntry("xl/sharedStrings.xml");
        if (sourceEntry is null || targetEntry is null)
            return;

        if (!ContainsRichSharedStringMetadata(sourceEntry))
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

    private static bool ContainsRichSharedStringMetadata(ZipArchiveEntry sharedStringsEntry)
    {
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            IgnoreComments = true,
            IgnoreProcessingInstructions = true
        };

        using var stream = sharedStringsEntry.Open();
        using var reader = XmlReader.Create(stream, settings);
        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.Element &&
                reader.NamespaceURI == "http://schemas.openxmlformats.org/spreadsheetml/2006/main" &&
                reader.LocalName is "r" or "rPh" or "phoneticPr")
            {
                return true;
            }
        }

        return false;
    }

    private static Dictionary<string, XElement> GetUniqueSharedStringsByPlainText(
        IEnumerable<XElement> sharedStrings,
        XNamespace workbookNs)
    {
        var unique = new Dictionary<string, XElement>(StringComparer.Ordinal);
        HashSet<string>? duplicates = null;
        foreach (var element in sharedStrings)
        {
            var text = ReadSharedStringPlainText(element, workbookNs);
            if (string.IsNullOrEmpty(text) || duplicates?.Contains(text) == true)
                continue;

            if (unique.ContainsKey(text))
            {
                unique.Remove(text);
                duplicates ??= new HashSet<string>(StringComparer.Ordinal);
                duplicates.Add(text);
                continue;
            }

            unique.Add(text, element);
        }

        return unique;
    }

    private static bool HasRichSharedStringMetadata(XElement sharedString, XNamespace workbookNs) =>
        sharedString.Elements(workbookNs + "r").Any() ||
        sharedString.Element(workbookNs + "rPh") is not null ||
        sharedString.Element(workbookNs + "phoneticPr") is not null;

    private static string ReadSharedStringPlainText(XElement sharedString, XNamespace workbookNs)
    {
        var textName = workbookNs + "t";
        string? singleRunText = null;
        StringBuilder? builder = null;
        var hasRuns = false;
        foreach (var run in sharedString.Elements(workbookNs + "r"))
        {
            hasRuns = true;
            var text = run.Element(textName)?.Value ?? string.Empty;
            if (builder is not null)
            {
                builder.Append(text);
            }
            else if (singleRunText is null)
            {
                singleRunText = text;
            }
            else
            {
                builder = new StringBuilder(singleRunText.Length + text.Length);
                builder.Append(singleRunText);
                builder.Append(text);
            }
        }

        if (hasRuns)
            return builder?.ToString() ?? singleRunText ?? string.Empty;

        return sharedString.Element(textName)?.Value ?? string.Empty;
    }
}
