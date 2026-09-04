using System.Globalization;
using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;

namespace FreeX.Core.IO;

/// <summary>
/// r366: drops a cell or row style index that points past the end of the stylesheet.
///
/// <para>A worksheet's <c>s</c> attribute is an index into <c>xl/styles.xml</c>'s
/// <c>cellXfs</c>. Nothing in the file format stops it naming an entry that does not exist, and
/// ClosedXML answers one with an <c>ArgumentOutOfRangeException</c> that aborts the entire LOAD -- so
/// a single bad index anywhere in a sheet cost the user the whole workbook. Excel repairs such a file
/// and opens it with the affected cells at the default format, which is what removing the attribute
/// achieves here.</para>
///
/// <para>Companion to the row-index guard added in r365, which had the same shape: a value that is
/// syntactically a perfectly good number but cannot refer to anything.</para>
///
/// <para>Self-gating, because it runs on every load: the stylesheet is counted once, then each
/// worksheet is scanned with an <see cref="XmlReader"/> and only materialized as an
/// <see cref="XDocument"/> if an out-of-range index is actually present. A well-formed workbook pays
/// one streaming pass and no allocation.</para>
/// </summary>
internal static class XlsxWorksheetCellStyleIndexNormalizer
{
    private static readonly XNamespace WorksheetNs =
        "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    /// <summary>True when any worksheet names a style entry the stylesheet does not have.</summary>
    internal static bool HasOutOfRangeStyleIndexes(ZipArchive archive)
    {
        var styleCount = ReadCellXfsCount(archive);
        if (styleCount <= 0)
            return false;

        return archive.Entries
            .Where(XlsxConditionalFormatRuleSupport.IsWorksheetEntry)
            .ToList()
            .Any(entry => HasOutOfRangeStyleIndex(entry, styleCount));
    }

    internal static void RemoveOutOfRangeStyleIndexes(ZipArchive archive)
    {
        var styleCount = ReadCellXfsCount(archive);

        // A workbook with no stylesheet at all cannot have a valid index, but it also cannot be
        // judged: leave it alone rather than stripping every s attribute in the package.
        if (styleCount <= 0)
            return;

        foreach (var entry in archive.Entries
                     .Where(XlsxConditionalFormatRuleSupport.IsWorksheetEntry)
                     .ToList())
        {
            if (!HasOutOfRangeStyleIndex(entry, styleCount))
                continue;

            var document = XlsxPackageXmlEditor.LoadXml(entry);
            if (document.Root is not { } root)
                continue;

            var changed = false;
            foreach (var element in root.Descendants())
            {
                if (element.Name != WorksheetNs + "c" && element.Name != WorksheetNs + "row")
                    continue;

                if (element.Attribute("s") is { } style && IsOutOfRange(style.Value, styleCount))
                {
                    style.Remove();
                    changed = true;
                }
            }

            if (changed)
                XlsxPackageXmlEditor.ReplaceXml(archive, entry.FullName, document);
        }
    }

    private static bool IsOutOfRange(string? value, int styleCount) =>
        int.TryParse(value?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var index) &&
        index >= styleCount;

    private static bool HasOutOfRangeStyleIndex(ZipArchiveEntry entry, int styleCount)
    {
        try
        {
            using var stream = entry.Open();
            using var reader = XmlReader.Create(stream, SecureXmlReaderSettings.Create());
            while (reader.Read())
            {
                if (reader.NodeType != XmlNodeType.Element ||
                    (reader.LocalName != "c" && reader.LocalName != "row"))
                {
                    continue;
                }

                if (IsOutOfRange(reader.GetAttribute("s"), styleCount))
                    return true;
            }
        }
        catch (XmlException)
        {
            // Malformed worksheet XML is another normalizer's problem; it must not turn into a
            // failure here.
            return false;
        }

        return false;
    }

    private static int ReadCellXfsCount(ZipArchive archive)
    {
        var entry = archive.GetEntry("xl/styles.xml");
        if (entry is null)
            return 0;

        try
        {
            using var stream = entry.Open();
            using var reader = XmlReader.Create(stream, SecureXmlReaderSettings.Create());

            // Counted rather than taken from cellXfs/@count: the attribute is what a writer CLAIMED,
            // and a file corrupt enough to carry a bad index is exactly the file whose count cannot
            // be trusted.
            var count = 0;
            var insideCellXfs = false;
            while (reader.Read())
            {
                if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "cellXfs")
                {
                    if (reader.IsEmptyElement)
                        return 0;

                    insideCellXfs = true;
                    continue;
                }

                if (!insideCellXfs)
                    continue;

                if (reader.NodeType == XmlNodeType.EndElement && reader.LocalName == "cellXfs")
                    break;

                if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "xf")
                    count++;
            }

            return count;
        }
        catch (XmlException)
        {
            return 0;
        }
    }
}
