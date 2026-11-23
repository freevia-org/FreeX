using System.IO;
using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;
using Free.Shared.Opc;

namespace FreeX.Core.IO;

/// <summary>
/// Strips schema-invalid <c>horizontalDpi</c>/<c>verticalDpi</c> attributes from worksheet
/// <c>&lt;pageSetup&gt;</c> elements. Excel itself emits <c>horizontalDpi="0" verticalDpi="0"</c>
/// when a worksheet references a printerSettings part (via <c>r:id</c>); those values violate the
/// SpreadsheetML schema facet (the DPI attributes are <c>unsignedInt</c> with MinInclusive=1), so the
/// strict OpenXML validator rejects the file. Excel re-derives the DPI when none is present, so dropping
/// a non-positive value is lossless. This runs once at load against the source-package snapshot bytes so
/// every save path (verbatim source-copy, cell patch-save, and full ClosedXML save) emits valid pageSetup.
/// </summary>
internal static class XlsxWorksheetPageSetupDpiSanitizer
{
    private static readonly XNamespace WorksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    private const long MaxStreamedWorksheetCharacters = 4L * 1024L * 1024L * 1024L;

    private static readonly string[] DpiAttributeNames = ["horizontalDpi", "verticalDpi"];

    /// <summary>
    /// Returns true when at least one worksheet pageSetup carried a non-positive DPI attribute that was
    /// removed (i.e. the package bytes were rewritten in place).
    /// </summary>
    public static bool Sanitize(MemoryStream packageStream)
    {
        if (!HasInvalidDpi(packageStream))
            return false;

        packageStream.Position = 0;
        using var archive = new ZipArchive(packageStream, ZipArchiveMode.Update, leaveOpen: true);
        var changedAny = false;
        foreach (var entry in archive.Entries.Where(XlsxPackagePath.IsWorksheetXmlEntry).ToList())
        {
            // Streaming pre-check per part so the DOM load below only ever runs for the rare part that needs it.
            if (!EntryHasInvalidDpi(entry))
                continue;

            var xml = XlsxPackageXmlEditor.LoadXml(entry);
            var root = xml.Root;
            if (root is null)
                continue;

            if (root.Element(WorksheetNs + "pageSetup") is not { } pageSetup)
                continue;

            if (RemoveInvalidDpiAttributes(pageSetup))
            {
                XlsxPackageXmlEditor.ReplaceXml(archive, entry.FullName, xml);
                changedAny = true;
            }
        }

        return changedAny;
    }

    // Cheap pre-scan: only pay the unzip/parse/rezip cost for the rare files that actually carry DPI="0".
    private static bool HasInvalidDpi(MemoryStream packageStream)
    {
        packageStream.Position = 0;
        using var archive = new ZipArchive(packageStream, ZipArchiveMode.Read, leaveOpen: true);
        foreach (var entry in archive.Entries.Where(XlsxPackagePath.IsWorksheetXmlEntry))
        {
            if (EntryHasInvalidDpi(entry))
                return true;
        }

        return false;
    }

    // STREAMING scan of one worksheet part: skips sheetData without building a DOM, so memory is bounded for
    // very large worksheets (a 130-200 MB part otherwise tripped the DOM character cap and made the whole
    // workbook fail to open as "not a valid .xlsx package").
    private static bool EntryHasInvalidDpi(ZipArchiveEntry entry)
    {
        var settings = SecureXmlReaderSettings.Create(MaxStreamedWorksheetCharacters);
        settings.IgnoreComments = true;
        settings.IgnoreProcessingInstructions = true;
        using var stream = entry.Open();
        using var reader = XmlReader.Create(stream, settings);
        if (reader.MoveToContent() != XmlNodeType.Element || reader.IsEmptyElement)
            return false;

        var depth = reader.Depth;
        reader.Read();
        while (!reader.EOF && reader.Depth > depth)
        {
            if (reader.NodeType != XmlNodeType.Element)
            {
                reader.Read();
                continue;
            }

            if (reader.LocalName == "pageSetup" && reader.NamespaceURI == WorksheetNs.NamespaceName)
            {
                foreach (var name in DpiAttributeNames)
                {
                    var value = reader.GetAttribute(name);
                    if (value is not null && !IsPositiveInt(value))
                        return true;
                }

                return false;
            }

            reader.Skip();
        }

        return false;
    }

    private static bool HasInvalidDpiAttribute(XElement pageSetup)
    {
        foreach (var name in DpiAttributeNames)
        {
            if (pageSetup.Attribute(name) is { } attribute && !IsPositiveInt(attribute.Value))
                return true;
        }

        return false;
    }

    private static bool RemoveInvalidDpiAttributes(XElement pageSetup)
    {
        var changed = false;
        foreach (var name in DpiAttributeNames)
        {
            if (pageSetup.Attribute(name) is { } attribute && !IsPositiveInt(attribute.Value))
            {
                attribute.Remove();
                changed = true;
            }
        }

        return changed;
    }

    private static bool IsPositiveInt(string? value) =>
        int.TryParse(value?.Trim(), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var parsed) &&
        parsed >= 1;
}
