using System.IO.Compression;
using System.Xml.Linq;

namespace FreeX.Core.IO;

/// <summary>
/// r369: drops worksheet elements whose cell reference does not parse.
///
/// <para>A <c>mergeCell/@ref</c> or <c>dataValidation/@sqref</c> that is not a cell range refers to
/// nothing, and ClosedXML does not ignore it -- a malformed <c>mergeCell</c> ref surfaces as a
/// <c>NullReferenceException</c> raised inside <c>XLWorkbook.LoadSpreadsheetDocument</c>, and a
/// malformed <c>sqref</c> as an <c>ArgumentNullException</c>. Either aborts the load, so one bad
/// attribute costs the user the whole workbook. Excel repairs both and opens the file.</para>
///
/// <para>Third instance of the shape behind r365 (row index) and r366 (style index): a value that is
/// syntactically well-formed but names nothing. The normalizers that already validate these refs --
/// <see cref="XlsxWorksheetMergeCellsNormalizer"/> and the data-validation one -- run only on the
/// save/source-preservation pass, never on the ClosedXML LOAD path, which is why the load still
/// saw them.</para>
/// </summary>
internal static class XlsxWorksheetMalformedReferenceNormalizer
{
    private static readonly XNamespace WorksheetNs =
        "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    internal static bool HasMalformedReferences(ZipArchive archive)
    {
        foreach (var entry in archive.Entries
                     .Where(XlsxConditionalFormatRuleSupport.IsWorksheetEntry)
                     .ToList())
        {
            if (TryFindMalformed(entry, out _))
                return true;
        }

        return false;
    }

    internal static void RemoveMalformedReferences(ZipArchive archive)
    {
        foreach (var entry in archive.Entries
                     .Where(XlsxConditionalFormatRuleSupport.IsWorksheetEntry)
                     .ToList())
        {
            if (!TryFindMalformed(entry, out var document) || document?.Root is not { } root)
                continue;

            var changed = false;
            foreach (var element in root.Descendants().ToList())
            {
                if (IsMalformed(element))
                {
                    element.Remove();
                    changed = true;
                }
            }

            // A mergeCells or dataValidations wrapper left with no children is itself invalid.
            foreach (var wrapper in root.Descendants().ToList())
            {
                if ((wrapper.Name == WorksheetNs + "mergeCells" || wrapper.Name == WorksheetNs + "dataValidations") &&
                    !wrapper.Elements().Any())
                {
                    wrapper.Remove();
                    changed = true;
                }
            }

            if (changed)
                XlsxPackageXmlEditor.ReplaceXml(archive, entry.FullName, document);
        }
    }

    private static bool IsMalformed(XElement element)
    {
        if (element.Name == WorksheetNs + "mergeCell")
            return XlsxSqrefParser.NormalizeCellRangeList(element.Attribute("ref")?.Value) is null;

        if (element.Name == WorksheetNs + "dataValidation")
            return XlsxSqrefParser.NormalizeCellRangeList(element.Attribute("sqref")?.Value) is null;

        return false;
    }

    private static bool TryFindMalformed(ZipArchiveEntry entry, out XDocument? document)
    {
        document = null;
        try
        {
            var loaded = XlsxPackageXmlEditor.LoadXml(entry);
            if (loaded.Root is not { } root)
                return false;

            if (!root.Descendants().Any(IsMalformed))
                return false;

            document = loaded;
            return true;
        }
        catch (System.Xml.XmlException)
        {
            // Malformed worksheet XML belongs to another normalizer; it must not fail here.
            return false;
        }
    }
}
