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

        // Shared strings are keyed by plain text because ClosedXML fully regenerates
        // sharedStrings.xml (dedup/reorder), so source and target indices don't align.
        // Plain text that is unique in a document is matched directly. Plain text that
        // collides (rich vs. plain, or two differently-formatted runs rendering the same
        // text) must NOT be silently dropped -- those are matched positionally instead
        // (first source occurrence with that text to the first target occurrence with
        // that text, second to second, etc.), since both documents enumerate shared
        // strings in the same first-use cell order.
        var sourceRichStrings = sourceRoot.Elements(workbookNs + "si")
            .Where(item => HasRichSharedStringMetadata(item, workbookNs))
            .ToList();
        if (sourceRichStrings.Count == 0)
            return;

        var sourceUniqueByText = GetUniqueSharedStringsByPlainText(sourceRichStrings, workbookNs, out var sourceDuplicateTexts);
        var targetElements = targetRoot.Elements(workbookNs + "si").ToList();
        var targetUniqueByText = GetUniqueSharedStringsByPlainText(targetElements, workbookNs, out var targetDuplicateTexts);

        var changed = false;
        foreach (var (plainText, sourceString) in sourceUniqueByText)
        {
            if (!targetUniqueByText.TryGetValue(plainText, out var targetString))
                continue;

            ReplaceSharedString(targetString, sourceString, workbookNs);
            changed = true;
        }

        if (sourceDuplicateTexts is not null || targetDuplicateTexts is not null)
        {
            IEnumerable<string> textsToMatch = sourceDuplicateTexts is null
                ? targetDuplicateTexts!
                : targetDuplicateTexts is null
                    ? sourceDuplicateTexts
                    : sourceDuplicateTexts.Concat(targetDuplicateTexts);

            var handledTexts = new HashSet<string>(StringComparer.Ordinal);
            foreach (var text in textsToMatch)
            {
                if (!handledTexts.Add(text))
                    continue;

                var sourceMatches = sourceRichStrings.Where(item => ReadSharedStringPlainText(item, workbookNs) == text).ToList();
                if (sourceMatches.Count == 0)
                    continue;

                // P70: pair rich source occurrences only against target occurrences that are
                // ALSO rich (same-text plain si entries in targetElements must never receive a
                // rich replacement positionally — a plain first-use si sharing the text with a
                // later rich si would otherwise get clobbered, silently promoting every plain
                // cell using that string to rich formatting/phonetics it never had).
                var targetMatches = targetElements
                    .Where(item => HasRichSharedStringMetadata(item, workbookNs) && ReadSharedStringPlainText(item, workbookNs) == text)
                    .ToList();
                var count = Math.Min(sourceMatches.Count, targetMatches.Count);
                for (var i = 0; i < count; i++)
                {
                    ReplaceSharedString(targetMatches[i], sourceMatches[i], workbookNs);
                    changed = true;
                }
            }
        }

        if (changed)
            XlsxPackageXmlEditor.ReplaceXml(targetArchive, "xl/sharedStrings.xml", targetXml);
    }

    private static void ReplaceSharedString(XElement targetString, XElement sourceString, XNamespace workbookNs)
    {
        var replacement = new XElement(sourceString);
        SanitizeRichSharedStringFontNames(replacement, workbookNs);
        targetString.ReplaceWith(replacement);
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
        XNamespace workbookNs,
        out HashSet<string>? duplicates)
    {
        var unique = new Dictionary<string, XElement>(StringComparer.Ordinal);
        duplicates = null;
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

    private static void SanitizeRichSharedStringFontNames(XElement sharedString, XNamespace workbookNs)
    {
        foreach (var richTextFont in sharedString.Descendants(workbookNs + "rFont"))
            XlsxFontNameSanitizer.SanitizeValAttribute(richTextFont);
    }

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
