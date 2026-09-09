using System.Globalization;
using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;
using Free.Shared.Opc;

namespace FreeX.Core.IO;

/// <summary>
/// r584: drops an attribute whose value cannot be parsed as the type the LOADER will demand of it.
///
/// <para>Companion to r583, and found by widening the same probe. r583 mutated every integer
/// attribute to one value -- an integer too large for <c>uint</c> -- and fixed the nineteen loads
/// that aborted. Feeding the same attributes four more malformed values (<c>-1</c>, <c>yes</c>, the
/// empty string, <c>1.5</c>) produced NINETY-EIGHT aborted loads across 320 mutants: the same
/// twenty-odd attributes reject anything they cannot parse, not merely a value that is too large,
/// and every throw is again DocumentFormat.OpenXml's, reached through ClosedXML.</para>
///
/// <para>r583 could be one rule on the VALUE because no xlsx attribute may hold a decimal integer
/// above <c>uint.MaxValue</c>. This one cannot: whether <c>"true"</c> is legal in an attribute
/// depends entirely on which attribute it is. So it carries a table -- and the table is EVIDENCE
/// rather than a reading of the schema. Each entry is an attribute the probe demonstrated will abort
/// a load, and its type was established by a discriminating mutation: feeding <c>"true"</c> to every
/// one of them separates the boolean-typed (which accept it) from the numeric and colour ones (which
/// throw <c>FormatException</c>).</para>
///
/// <para>A table can rot as new attribute reads are added, so it is not left to rot: the r584
/// tripwire mutates the WHOLE surface, not this list, and fails the day a newly-read attribute
/// starts aborting loads. The table is the fix; the tripwire is what keeps it honest.</para>
///
/// <para>Dropping rather than repairing follows r365, r366, r369 and r583: Excel repairs such a file
/// and opens it with the affected item at its default, which is what removing the attribute
/// achieves.</para>
/// </summary>
internal static class XlsxMalformedTypedAttributeNormalizer
{
    private enum AttributeValueKind
    {
        /// <summary>xsd:boolean -- the loader accepts only 0, 1, true or false.</summary>
        Boolean,

        /// <summary>xsd:unsignedInt -- the common case; a negative value throws just as a word does.</summary>
        UnsignedInteger,

        /// <summary>xsd:int -- the rarer signed case, where a leading minus is legitimate.</summary>
        SignedInteger,

        /// <summary>A double-shaped attribute (row height, column width and the like).</summary>
        Double,

        /// <summary>An RGB or ARGB hex colour, which the loader requires to be 6 or 8 hex digits.</summary>
        HexColor,
    }

    /// <summary>
    /// Keyed by (element local name, attribute local name), because the same attribute name means
    /// the same thing wherever that element appears, and the part it sits in does not change its
    /// type. Every entry was demonstrated by the r584 probe.
    /// </summary>
    private static readonly Dictionary<(string Element, string Attribute), AttributeValueKind> Typed = new()
    {
        // xl/styles.xml -- numeric
        [("numFmt", "numFmtId")] = AttributeValueKind.UnsignedInteger,
        [("sz", "val")] = AttributeValueKind.Double,
        [("family", "val")] = AttributeValueKind.UnsignedInteger,
        [("alignment", "textRotation")] = AttributeValueKind.UnsignedInteger,
        [("alignment", "indent")] = AttributeValueKind.UnsignedInteger,
        [("alignment", "relativeIndent")] = AttributeValueKind.SignedInteger,
        [("alignment", "readingOrder")] = AttributeValueKind.UnsignedInteger,
        [("cellStyle", "xfId")] = AttributeValueKind.UnsignedInteger,
        [("cellStyle", "builtinId")] = AttributeValueKind.UnsignedInteger,

        // xl/styles.xml -- boolean
        [("alignment", "wrapText")] = AttributeValueKind.Boolean,
        [("alignment", "justifyLastLine")] = AttributeValueKind.Boolean,
        [("alignment", "shrinkToFit")] = AttributeValueKind.Boolean,
        [("border", "diagonalUp")] = AttributeValueKind.Boolean,
        [("border", "diagonalDown")] = AttributeValueKind.Boolean,

        // worksheet
        [("sheetFormatPr", "defaultRowHeight")] = AttributeValueKind.Double,
        [("dataValidation", "allowBlank")] = AttributeValueKind.Boolean,
        [("dataValidation", "showDropDown")] = AttributeValueKind.Boolean,
        [("dataValidation", "showInputMessage")] = AttributeValueKind.Boolean,
        [("dataValidation", "showErrorMessage")] = AttributeValueKind.Boolean,

        // workbook -- a required identity attribute. Removing it cannot make the file loadable, but it
        // turns an unhandled library exception into FreeX's own typed WorkbookInvalidException, which
        // is the same trade r583 recorded for it.
        [("sheet", "sheetId")] = AttributeValueKind.UnsignedInteger,

        // theme
        [("srgbClr", "val")] = AttributeValueKind.HexColor,
    };

    /// <summary>True when any XML part carries a typed attribute the loader cannot parse.</summary>
    internal static bool HasMalformedTypedAttributes(ZipArchive archive)
    {
        foreach (var entry in archive.Entries.ToList())
        {
            if (IsXmlPart(entry.FullName) && EntryHasMalformedTypedAttribute(entry))
                return true;
        }

        return false;
    }

    /// <summary>Removes every such attribute, leaving the schema default to apply.</summary>
    internal static void RemoveMalformedTypedAttributes(ZipArchive archive)
    {
        foreach (var entry in archive.Entries.ToList())
        {
            if (!IsXmlPart(entry.FullName) || !EntryHasMalformedTypedAttribute(entry))
                continue;

            XDocument document;
            try
            {
                using var stream = entry.Open();
                document = XDocument.Load(stream, LoadOptions.PreserveWhitespace);
            }
            catch (XmlException)
            {
                // A part that is not well-formed XML is another normalizer's problem and is not made
                // worse by leaving it alone.
                continue;
            }

            var removed = false;
            foreach (var element in document.Descendants().ToList())
            foreach (var attribute in element.Attributes().ToList())
            {
                if (attribute.IsNamespaceDeclaration ||
                    !IsMalformed(element.Name.LocalName, attribute.Name.LocalName, attribute.Value))
                {
                    continue;
                }

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
        fullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase);

    private static bool EntryHasMalformedTypedAttribute(ZipArchiveEntry entry)
    {
        try
        {
            using var stream = entry.Open();
            // r276: the character cap as well as the DTD prohibition -- WorkbookOpenSizeGuard
            // validates only the zip's DECLARED entry lengths, so a part whose real size is enormous
            // is unbounded at the point of parse, and one colossal attribute value is materialized
            // as a single string even by a pull reader.
            using var reader = XmlReader.Create(stream, SecureXmlReaderSettings.Create());
            while (reader.Read())
            {
                if (reader.NodeType != XmlNodeType.Element || !reader.HasAttributes)
                    continue;

                var elementName = reader.LocalName;
                while (reader.MoveToNextAttribute())
                {
                    if (IsMalformed(elementName, reader.LocalName, reader.Value))
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

    private static bool IsMalformed(string elementName, string attributeName, string value) =>
        Typed.TryGetValue((elementName, attributeName), out var kind) && !IsValid(kind, value);

    private static bool IsValid(AttributeValueKind kind, string value) => kind switch
    {
        // The four spellings DocumentFormat.OpenXml's BooleanValue accepts.
        AttributeValueKind.Boolean =>
            value is "0" or "1" or "true" or "false",

        // The unsigned case must REJECT a leading minus: the probe showed "-1" aborting the load for
        // every one of these, exactly as "yes" does. An earlier draft of this table accepted the
        // whole int range for both kinds, and the probe caught it -- seven of them still failed.
        AttributeValueKind.UnsignedInteger =>
            ulong.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var unsigned) &&
            unsigned <= uint.MaxValue,

        AttributeValueKind.SignedInteger =>
            long.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var signed) &&
            signed is >= int.MinValue and <= int.MaxValue,

        AttributeValueKind.Double =>
            double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) &&
            double.IsFinite(number),

        AttributeValueKind.HexColor =>
            (value.Length == 6 || value.Length == 8) && value.All(char.IsAsciiHexDigit),

        _ => true,
    };
}
