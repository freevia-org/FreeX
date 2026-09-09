using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;
using Free.Shared.Opc;

namespace FreeX.Core.IO;

/// <summary>
/// r583: drops an attribute whose value is a decimal integer too large for any xlsx attribute type.
///
/// <para>Found by a behavioural probe rather than a scan: a valid workbook was saved, then each
/// distinct (part, element, attribute) triple whose value was a plain integer was set in turn to
/// 4294967296 -- one past <c>uint.MaxValue</c> -- and reloaded. Nineteen of sixty-four mutants
/// aborted the ENTIRE load with an unhandled exception, every one thrown inside
/// DocumentFormat.OpenXml (<c>UInt32.Parse</c>, <c>Int32.Parse</c>,
/// <c>DocumentFormat.OpenXml.BooleanValue.Parse</c>) reached through ClosedXML. Among them:
/// <c>numFmt/@numFmtId</c>, <c>alignment/@textRotation</c>, <c>alignment/@indent</c>,
/// <c>cellStyle/@xfId</c>, <c>font family/@val</c>, <c>sheet/@sheetId</c>, and the
/// <c>dataValidation</c> booleans.</para>
///
/// <para>This is the fourth instance of the shape behind r365 (row index), r366 (style index) and
/// r369 (malformed reference): one bad attribute anywhere costs the user the whole workbook, and
/// Excel repairs such a file rather than refusing it. Removing the attribute lets the schema default
/// apply, which is what Excel's repair does.</para>
///
/// <para>The rule is deliberately one rule rather than a table of attribute types: NO attribute in
/// the xlsx schemas can legitimately be a decimal integer above <c>uint.MaxValue</c>. The numeric
/// attribute types are all int/uint-shaped, and the xsd:double ones (column width, row height, page
/// margins) are bounded far below that by Excel itself -- a column width of four billion is not a
/// value any writer produces. So the test can be made on the VALUE alone, with no per-attribute
/// knowledge to get wrong or to fall out of date.</para>
///
/// <para>What it deliberately does NOT cover: a non-numeric value in a typed attribute, such as
/// <c>wrapText="yes"</c>. That needs per-attribute type knowledge, and the probe did not
/// demonstrate it. Recorded rather than guessed.</para>
///
/// <para>Self-gating, like its siblings: every part is scanned with an <see cref="XmlReader"/> and
/// only materialized as an <see cref="XDocument"/> when an offending value is actually present, so a
/// well-formed workbook pays one streaming pass and no allocation.</para>
/// </summary>
internal static class XlsxOutOfRangeIntegerAttributeNormalizer
{
    /// <summary>True when any XML part carries a decimal-integer attribute above uint.MaxValue.</summary>
    internal static bool HasOutOfRangeIntegerAttributes(ZipArchive archive)
    {
        foreach (var entry in archive.Entries.ToList())
        {
            if (!IsXmlPart(entry.FullName))
                continue;

            if (EntryHasOutOfRangeAttribute(entry))
                return true;
        }

        return false;
    }

    /// <summary>Removes every such attribute, leaving the schema default to apply.</summary>
    internal static void RemoveOutOfRangeIntegerAttributes(ZipArchive archive)
    {
        foreach (var entry in archive.Entries.ToList())
        {
            if (!IsXmlPart(entry.FullName) || !EntryHasOutOfRangeAttribute(entry))
                continue;

            XDocument document;
            try
            {
                using var stream = entry.Open();
                document = XDocument.Load(stream, LoadOptions.PreserveWhitespace);
            }
            catch (XmlException)
            {
                // A part that is not well-formed XML is some other normalizer's problem, and is not
                // made worse by leaving it alone.
                continue;
            }

            var removed = false;
            foreach (var element in document.Descendants().ToList())
            foreach (var attribute in element.Attributes().ToList())
            {
                if (attribute.IsNamespaceDeclaration || !IsOutOfRange(attribute.Value))
                    continue;

                attribute.Remove();
                removed = true;
            }

            if (!removed)
                continue;

            var fullName = entry.FullName;
            entry.Delete();
            var replacement = archive.CreateEntry(fullName);
            using var output = replacement.Open();
            document.Save(output, SaveOptions.DisableFormatting);
        }
    }

    private static bool IsXmlPart(string fullName) =>
        fullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) ||
        fullName.EndsWith(".rels", StringComparison.OrdinalIgnoreCase);

    private static bool EntryHasOutOfRangeAttribute(ZipArchiveEntry entry)
    {
        try
        {
            using var stream = entry.Open();
            // r276: the package layer's readers must carry the character cap as well as the DTD
            // prohibition -- WorkbookOpenSizeGuard validates only the zip's DECLARED lengths, so a
            // part with a tiny compressed size and an enormous real one is unbounded at the point of
            // parse, and streaming does not help when one attribute value is the colossal thing.
            using var reader = XmlReader.Create(stream, SecureXmlReaderSettings.Create());
            while (reader.Read())
            {
                if (reader.NodeType != XmlNodeType.Element || !reader.HasAttributes)
                    continue;

                while (reader.MoveToNextAttribute())
                {
                    if (IsOutOfRange(reader.Value))
                        return true;
                }
            }
        }
        catch (XmlException)
        {
            return false;
        }

        return false;
    }

    /// <summary>
    /// A value made only of decimal digits that does not fit in <see cref="uint"/>. The digit-only
    /// test is what keeps this from touching a GUID, a date, a reference like "A1", or a decimal
    /// such as "8.43" -- none of which can reach here.
    /// </summary>
    private static bool IsOutOfRange(string value)
    {
        if (value.Length < 10 || value.Length > 40)
            return false;

        foreach (var c in value)
        {
            if (!char.IsAsciiDigit(c))
                return false;
        }

        return !uint.TryParse(value, out _);
    }
}
