using System.Globalization;
using System.IO.Compression;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using static Free.Shared.Opc.XlsxXmlNormalizationHelpers;

namespace FreeX.Core.IO;

internal static class XlsxWorksheetGridXmlNormalizer
{
    private static readonly XNamespace WorksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace MarkupCompatNs = "http://schemas.openxmlformats.org/markup-compatibility/2006";

    private static readonly IReadOnlySet<string> EmptyAttributes = new HashSet<string>(StringComparer.Ordinal);
    private static readonly IReadOnlySet<string> ColumnAttributes =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "min",
            "max",
            "width",
            "style",
            "hidden",
            "bestFit",
            "customWidth",
            "phonetic",
            "outlineLevel",
            "collapsed"
        };

    private static readonly IReadOnlySet<string> RowAttributes =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "r",
            "spans",
            "s",
            "customFormat",
            "ht",
            "hidden",
            "customHeight",
            "outlineLevel",
            "collapsed",
            "thickTop",
            "thickBot",
            "ph"
        };

    private static readonly IReadOnlySet<string> CellAttributes =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "r",
            "s",
            "t",
            "cm",
            "vm",
            "ph"
        };

    private static readonly IReadOnlySet<string> CellTypeValues =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "b",
            "n",
            "e",
            "s",
            "str",
            "inlineStr",
            "d"
        };

    private static readonly IReadOnlySet<string> FormulaAttributes =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "t",
            "aca",
            "ref",
            "dt2D",
            "dtr",
            "del1",
            "del2",
            "r1",
            "r2",
            "ca",
            "si",
            "bx"
        };

    private static readonly IReadOnlySet<string> FormulaTypeValues =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "normal",
            "array",
            "dataTable",
            "shared"
        };

    private static readonly Regex CellReferencePattern = new(
        "^[A-Z]{1,3}[1-9][0-9]{0,6}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex CellRangePattern = new(
        "^[A-Z]{1,3}[1-9][0-9]{0,6}(:[A-Z]{1,3}[1-9][0-9]{0,6})?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static bool NormalizeWorksheetRoot(XElement worksheetRoot)
        => NormalizeWorksheetRoot(worksheetRoot, cellMetadataCount: 0, valueMetadataCount: 0);

    internal static (uint CellMetadataCount, uint ValueMetadataCount) ReadMetadataCountsForSinglePass(ZipArchive archive)
        => ReadMetadataCounts(archive);

    internal static bool NormalizeWorksheetRoot(
        XElement worksheetRoot,
        uint cellMetadataCount,
        uint valueMetadataCount)
    {
        var changed = false;

        if (worksheetRoot.Element(WorksheetNs + "cols") is { } columns)
            changed |= NormalizeColumnsElement(columns);
        if (worksheetRoot.Element(WorksheetNs + "sheetData") is { } sheetData)
            changed |= NormalizeSheetDataElement(sheetData, cellMetadataCount, valueMetadataCount);

        return changed;
    }

    public static void NormalizeWorksheets(ZipArchive archive)
    {
        var (cellMetadataCount, valueMetadataCount) = ReadMetadataCounts(archive);
        foreach (var worksheetEntry in archive.Entries.Where(XlsxPackagePath.IsWorksheetXmlEntry).ToList())
        {
            // The grid (cols + sheetData cells) is already canonical for virtually every input
            // (Excel-authored files and packages FreeX itself wrote).  Detect that with a streaming
            // scan that never materializes the multi-hundred-megabyte cell tree, and only fall back
            // to the authoritative full load + normalize when the scan finds something that
            // NormalizeWorksheetRoot would actually rewrite.
            if (IsWorksheetGridCanonical(worksheetEntry, cellMetadataCount, valueMetadataCount))
                continue;

            var worksheetXml = XlsxPackageXmlEditor.LoadXml(worksheetEntry);
            var root = worksheetXml.Root;
            if (root is null)
                continue;

            if (NormalizeWorksheetRoot(root, cellMetadataCount, valueMetadataCount))
                XlsxPackageXmlEditor.ReplaceXml(archive, worksheetEntry.FullName, worksheetXml);
        }
    }

    /// <summary>
    /// Streaming equivalent of "would <see cref="NormalizeWorksheets"/> rewrite any worksheet grid?".
    /// Used by the load sanitizer (which historically loaded every worksheet's full cell XDocument
    /// just to answer this) to decide whether grid normalization is required.  Reuses the canonical
    /// pre-scan, so no worksheet's cell tree is materialized; conservative (a worksheet whose grid the
    /// scan cannot prove canonical is reported as having issues, which is always safe).
    /// </summary>
    internal static bool HasGridXmlSchemaIssues(ZipArchive archive)
    {
        var (cellMetadataCount, valueMetadataCount) = ReadMetadataCounts(archive);
        foreach (var worksheetEntry in archive.Entries.Where(XlsxPackagePath.IsWorksheetXmlEntry).ToList())
        {
            if (!IsWorksheetGridCanonical(worksheetEntry, cellMetadataCount, valueMetadataCount))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Streaming check that returns <see langword="true"/> only when the worksheet's grid (the
    /// <c>cols</c> element and the <c>sheetData</c> cells) is already in the exact form
    /// <see cref="NormalizeWorksheetRoot(XElement, uint, uint)"/> would produce, so the caller can
    /// skip a full per-cell <see cref="XDocument"/> load.
    ///
    /// <para>The check is deliberately CONSERVATIVE.  A <see langword="false"/> result is always safe
    /// (the authoritative full normalizer runs); only a false POSITIVE would corrupt output, so every
    /// branch that is not certain the content is already canonical — foreign-namespace attributes,
    /// cell/row extension lists, metadata indices, values that re-serialize differently — returns
    /// <see langword="false"/>.  Scalar attribute checks reuse the very same predicate functions the
    /// normalizer applies, so they cannot drift.</para>
    /// </summary>
    private static bool IsWorksheetGridCanonical(
        ZipArchiveEntry worksheetEntry,
        uint cellMetadataCount,
        uint valueMetadataCount)
    {
        try
        {
            using var stream = worksheetEntry.Open();
            using var reader = XmlReader.Create(stream, SecureXmlReaderSettings.Create());
            reader.MoveToContent();
            if (reader.NodeType != XmlNodeType.Element ||
                reader.LocalName != "worksheet" ||
                !string.Equals(reader.NamespaceURI, WorksheetNs.NamespaceName, StringComparison.Ordinal))
            {
                return false;
            }

            if (reader.IsEmptyElement)
                return true;

            var worksheetDepth = reader.Depth;
            var readNext = true;
            while (true)
            {
                if (readNext && !reader.Read())
                    break;
                readNext = true;

                if (reader.NodeType == XmlNodeType.EndElement && reader.Depth == worksheetDepth)
                    break;
                if (reader.NodeType != XmlNodeType.Element || reader.Depth != worksheetDepth + 1)
                    continue;

                var isWorksheetNs = string.Equals(
                    reader.NamespaceURI, WorksheetNs.NamespaceName, StringComparison.Ordinal);

                if (isWorksheetNs && reader.LocalName == "cols")
                {
                    // cols is tiny; materialize it and run the real normalizer for an exact verdict.
                    if (XNode.ReadFrom(reader) is XElement colsElement)
                    {
                        if (NormalizeColumnsElement(new XElement(colsElement)))
                            return false;
                        readNext = false; // ReadFrom already advanced past cols.
                    }

                    continue;
                }

                if (isWorksheetNs && reader.LocalName == "sheetData")
                {
                    if (reader.HasAttributes && HasNonNamespaceAttribute(reader))
                        return false; // sheetData carries no attributes once normalized.
                    if (reader.IsEmptyElement)
                        continue;
                    if (!IsSheetDataCanonical(reader, cellMetadataCount, valueMetadataCount))
                        return false;

                    continue;
                }

                // Elements outside cols/sheetData are not touched by the grid normalizer.
                reader.Skip();
                readNext = false;
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsSheetDataCanonical(XmlReader reader, uint cellMetadataCount, uint valueMetadataCount)
    {
        var sheetDataDepth = reader.Depth;
        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.EndElement && reader.Depth == sheetDataDepth)
                return true;
            if (reader.NodeType != XmlNodeType.Element || reader.Depth != sheetDataDepth + 1)
                continue;

            // Only <row> elements survive directly under sheetData.
            if (!(string.Equals(reader.NamespaceURI, WorksheetNs.NamespaceName, StringComparison.Ordinal) &&
                  reader.LocalName == "row"))
            {
                return false;
            }

            if (!IsRowCanonical(reader, cellMetadataCount, valueMetadataCount))
                return false;
        }

        return true;
    }

    private static bool IsRowCanonical(XmlReader reader, uint cellMetadataCount, uint valueMetadataCount)
    {
        if (!AreAttributesCanonical(reader, RowAttributes, static (name, value) => name switch
        {
            "r" => IsCanonical(NormalizeUnsignedIntOrNull, value),
            "spans" => IsCanonical(NormalizeCellSpans, value),
            "s" => IsCanonical(NormalizeUnsignedIntOrNull, value),
            "ht" => IsCanonical(NormalizeNonNegativeDouble, value),
            "outlineLevel" => IsCanonical(NormalizeOutlineLevel, value),
            _ => IsCanonical(NormalizeBoolean, value),
        }))
        {
            return false;
        }

        if (reader.IsEmptyElement)
            return true;

        var rowDepth = reader.Depth;
        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.EndElement && reader.Depth == rowDepth)
                return true;
            if (reader.NodeType != XmlNodeType.Element || reader.Depth != rowDepth + 1)
                continue;

            // Anything other than a worksheet-namespace <c> (e.g. a row-level extLst, which the
            // normalizer rewrites) is treated as non-canonical.
            if (!(string.Equals(reader.NamespaceURI, WorksheetNs.NamespaceName, StringComparison.Ordinal) &&
                  reader.LocalName == "c"))
            {
                return false;
            }

            if (!IsCellCanonical(reader, cellMetadataCount, valueMetadataCount))
                return false;
        }

        return true;
    }

    private static bool IsCellCanonical(XmlReader reader, uint cellMetadataCount, uint valueMetadataCount)
    {
        if (!AreAttributesCanonical(reader, CellAttributes, (name, value) => name switch
        {
            "r" => IsCanonical(NormalizeCellReference, value),
            "s" => IsCanonical(NormalizeUnsignedIntOrNull, value),
            "t" => IsCanonical(v => NormalizeToken(v, CellTypeValues), value),
            "cm" => IsCanonical(v => NormalizeMetadataIndex(v, cellMetadataCount), value),
            "vm" => IsCanonical(v => NormalizeMetadataIndex(v, valueMetadataCount), value),
            _ => IsCanonical(NormalizeBoolean, value),
        }))
        {
            return false;
        }

        if (reader.IsEmptyElement)
            return true;

        var cellDepth = reader.Depth;
        var seenFormula = false;
        var seenValue = false;
        var seenInlineString = false;
        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.EndElement && reader.Depth == cellDepth)
                return true;
            if (reader.NodeType != XmlNodeType.Element || reader.Depth != cellDepth + 1)
                continue;

            if (!string.Equals(reader.NamespaceURI, WorksheetNs.NamespaceName, StringComparison.Ordinal))
                return false;

            switch (reader.LocalName)
            {
                case "f":
                    if (seenFormula || !IsFormulaCanonical(reader))
                        return false;
                    seenFormula = true;
                    break;
                case "v":
                    if (seenValue || !IsValueChildCanonical(reader))
                        return false;
                    seenValue = true;
                    break;
                case "is":
                    if (seenInlineString)
                        return false;
                    seenInlineString = true;
                    // Inline strings are preserved verbatim by the normalizer.
                    ConsumeToEndElement(reader);
                    break;
                default:
                    // extLst or any other child triggers normalization.
                    return false;
            }
        }

        return true;
    }

    private static bool IsFormulaCanonical(XmlReader reader)
    {
        if (!AreAttributesCanonical(reader, FormulaAttributes, static (name, value) => name switch
        {
            "t" => IsCanonical(v => NormalizeToken(v, FormulaTypeValues), value),
            "ref" => IsCanonical(NormalizeCellRange, value),
            "r1" => IsCanonical(NormalizeCellReference, value),
            "r2" => IsCanonical(NormalizeCellReference, value),
            "si" => IsCanonical(NormalizeUnsignedIntOrNull, value),
            _ => IsCanonical(NormalizeBoolean, value),
        }))
        {
            return false;
        }

        // A formula must have no child elements (the normalizer strips them).
        return HasNoChildElements(reader);
    }

    private static bool IsValueChildCanonical(XmlReader reader)
    {
        // <v> is canonical only with no attributes and no child elements.
        if (reader.HasAttributes && HasNonNamespaceAttribute(reader))
            return false;

        return HasNoChildElements(reader);
    }

    private static bool HasNoChildElements(XmlReader reader)
    {
        if (reader.IsEmptyElement)
            return true;

        var depth = reader.Depth;
        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.EndElement && reader.Depth == depth)
                return true;
            if (reader.NodeType == XmlNodeType.Element)
                return false;
        }

        return true;
    }

    private static void ConsumeToEndElement(XmlReader reader)
    {
        if (reader.IsEmptyElement)
            return;

        var depth = reader.Depth;
        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.EndElement && reader.Depth == depth)
                return;
        }
    }

    /// <summary>
    /// Iterates the current element's attributes and returns <see langword="false"/> as soon as one
    /// would be removed or rewritten by the normalizer: a foreign-namespace attribute, an attribute
    /// outside <paramref name="allowed"/>, or one whose value is not already canonical per
    /// <paramref name="isValueCanonical"/>.  Namespace declarations and markup-compatibility
    /// attributes are preserved (matching the normalizer).  Leaves the reader on the element.
    /// </summary>
    private static bool AreAttributesCanonical(
        XmlReader reader,
        IReadOnlySet<string> allowed,
        Func<string, string, bool> isValueCanonical)
    {
        if (!reader.HasAttributes)
            return true;

        var canonical = true;
        for (var i = 0; i < reader.AttributeCount; i++)
        {
            reader.MoveToAttribute(i);
            if (IsNamespaceDeclaration(reader) ||
                string.Equals(reader.NamespaceURI, MarkupCompatNs.NamespaceName, StringComparison.Ordinal))
            {
                continue;
            }

            if (reader.NamespaceURI.Length != 0 ||
                !allowed.Contains(reader.LocalName) ||
                !isValueCanonical(reader.LocalName, reader.Value))
            {
                canonical = false;
                break;
            }
        }

        reader.MoveToElement();
        return canonical;
    }

    private static bool HasNonNamespaceAttribute(XmlReader reader)
    {
        var hasNonNamespace = false;
        for (var i = 0; i < reader.AttributeCount; i++)
        {
            reader.MoveToAttribute(i);
            if (!IsNamespaceDeclaration(reader))
            {
                hasNonNamespace = true;
                break;
            }
        }

        reader.MoveToElement();
        return hasNonNamespace;
    }

    private static bool IsNamespaceDeclaration(XmlReader reader) =>
        string.Equals(reader.NamespaceURI, "http://www.w3.org/2000/xmlns/", StringComparison.Ordinal);

    private static bool IsCanonical(Func<string?, string?> normalize, string value) =>
        string.Equals(normalize(value), value, StringComparison.Ordinal);

    /// <summary>
    /// Streaming check used by the cell-patch save pre-flight: returns <see langword="true"/> if any
    /// <c>row</c> in the worksheet lacks an <c>r</c> (row-index) attribute.  Avoids loading the full
    /// worksheet XDocument just to inspect row indices.  Returns <see langword="true"/> on any parse
    /// failure so the caller conservatively rejects the patch (matching the prior full-parse guard).
    /// </summary>
    internal static bool AnyRowMissingRowIndex(ZipArchiveEntry worksheetEntry)
    {
        try
        {
            using var stream = worksheetEntry.Open();
            using var reader = XmlReader.Create(stream, SecureXmlReaderSettings.Create());
            reader.MoveToContent();
            if (reader.NodeType != XmlNodeType.Element ||
                reader.LocalName != "worksheet" ||
                !string.Equals(reader.NamespaceURI, WorksheetNs.NamespaceName, StringComparison.Ordinal) ||
                reader.IsEmptyElement)
            {
                return false;
            }

            var readNext = true;
            while (true)
            {
                if (readNext && !reader.Read())
                    break;
                readNext = true;

                if (reader.NodeType != XmlNodeType.Element ||
                    reader.LocalName != "row" ||
                    !string.Equals(reader.NamespaceURI, WorksheetNs.NamespaceName, StringComparison.Ordinal))
                {
                    continue;
                }

                if (reader.GetAttribute("r") is null)
                    return true;

                // Skip the row's cell subtree, then process the node Skip lands on without re-reading.
                reader.Skip();
                readNext = false;
            }

            return false;
        }
        catch
        {
            return true;
        }
    }

    public static bool NormalizeColumnsElement(XElement columns)
    {
        var changed = false;
        changed |= RemoveUnknownAttributes(columns, EmptyAttributes);
        changed |= RemoveChildElementsExcept(columns, WorksheetNs + "col");

        foreach (var column in columns.Elements(WorksheetNs + "col").ToList())
            changed |= NormalizeColumnElement(column);

        return changed;
    }

    public static bool NormalizeSheetDataElement(XElement sheetData)
        => NormalizeSheetDataElement(sheetData, cellMetadataCount: 0, valueMetadataCount: 0);

    private static bool NormalizeSheetDataElement(
        XElement sheetData,
        uint cellMetadataCount,
        uint valueMetadataCount)
    {
        var changed = false;
        changed |= RemoveUnknownAttributes(sheetData, EmptyAttributes);
        changed |= RemoveChildElementsExcept(sheetData, WorksheetNs + "row");

        foreach (var row in sheetData.Elements(WorksheetNs + "row").ToList())
            changed |= NormalizeRowElement(row, cellMetadataCount, valueMetadataCount);

        return changed;
    }

    private static bool NormalizeColumnElement(XElement column)
    {
        var normalizedMin = NormalizeUnsignedIntOrNull(column.Attribute("min")?.Value);
        var normalizedMax = NormalizeUnsignedIntOrNull(column.Attribute("max")?.Value);
        if (normalizedMin is null || normalizedMax is null)
        {
            column.Remove();
            return true;
        }

        var changed = false;
        changed |= RemoveUnknownAttributes(column, ColumnAttributes);
        changed |= XlsxXmlNormalizationHelpers.SetAttributeIfChanged(column, "min", normalizedMin);
        changed |= XlsxXmlNormalizationHelpers.SetAttributeIfChanged(column, "max", normalizedMax);
        changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(column, "width", NormalizeNonNegativeDouble);
        changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(column, "style", NormalizeUnsignedIntOrNull);
        changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(column, "outlineLevel", NormalizeOutlineLevel);
        foreach (var attributeName in new[] { "hidden", "bestFit", "customWidth", "phonetic", "collapsed" })
            changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(column, attributeName, NormalizeBoolean);
        changed |= XlsxXmlNormalizationHelpers.RemoveChildElements(column);
        return changed;
    }

    private static bool NormalizeRowElement(
        XElement row,
        uint cellMetadataCount,
        uint valueMetadataCount)
    {
        var changed = false;
        changed |= RemoveUnknownAttributes(row, RowAttributes);
        changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(row, "r", NormalizeUnsignedIntOrNull);
        changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(row, "spans", NormalizeCellSpans);
        changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(row, "s", NormalizeUnsignedIntOrNull);
        changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(row, "ht", NormalizeNonNegativeDouble);
        changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(row, "outlineLevel", NormalizeOutlineLevel);
        foreach (var attributeName in new[] { "customFormat", "hidden", "customHeight", "collapsed", "thickTop", "thickBot", "ph" })
            changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(row, attributeName, NormalizeBoolean);

        changed |= NormalizeRowChildren(row);
        foreach (var cell in row.Elements(WorksheetNs + "c").ToList())
            changed |= NormalizeCellElement(cell, cellMetadataCount, valueMetadataCount);

        return changed;
    }

    private static bool NormalizeCellElement(
        XElement cell,
        uint cellMetadataCount,
        uint valueMetadataCount)
    {
        var changed = false;
        changed |= RemoveUnknownAttributes(cell, CellAttributes);
        changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(cell, "r", NormalizeCellReference);
        changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(cell, "s", NormalizeUnsignedIntOrNull);
        changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(cell, "t", value => NormalizeToken(value, CellTypeValues));
        changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(cell, "cm", value => NormalizeMetadataIndex(value, cellMetadataCount));
        changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(cell, "vm", value => NormalizeMetadataIndex(value, valueMetadataCount));
        changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(cell, "ph", NormalizeBoolean);

        changed |= NormalizeCellChildren(cell);
        foreach (var formula in cell.Elements(WorksheetNs + "f").ToList())
            changed |= NormalizeFormulaElement(formula);

        return changed;
    }

    private static bool NormalizeFormulaElement(XElement formula)
    {
        var changed = false;
        changed |= RemoveUnknownAttributes(formula, FormulaAttributes);
        changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(formula, "t", value => NormalizeToken(value, FormulaTypeValues));
        foreach (var attributeName in new[] { "aca", "dt2D", "dtr", "del1", "del2", "ca", "bx" })
            changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(formula, attributeName, NormalizeBoolean);
        changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(formula, "ref", NormalizeCellRange);
        changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(formula, "r1", NormalizeCellReference);
        changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(formula, "r2", NormalizeCellReference);
        changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(formula, "si", NormalizeUnsignedIntOrNull);
        changed |= XlsxXmlNormalizationHelpers.RemoveChildElements(formula);
        return changed;
    }

    private static bool NormalizeRowChildren(XElement row)
    {
        var changed = false;
        var keptExtensionList = false;
        foreach (var child in row.Elements().ToList())
        {
            if (child.Name == WorksheetNs + "c")
                continue;

            if (child.Name == WorksheetNs + "extLst")
            {
                changed |= XlsxWorksheetExtensionListNormalizer.NormalizeChild(child, ref keptExtensionList);
                continue;
            }

            child.Remove();
            changed = true;
        }

        return changed;
    }

    private static bool NormalizeCellChildren(XElement cell)
    {
        var changed = false;
        var seenFormula = false;
        var seenValue = false;
        var seenInlineString = false;
        var keptExtensionList = false;

        foreach (var child in cell.Elements().ToList())
        {
            if (child.Name == WorksheetNs + "f" && !seenFormula)
            {
                seenFormula = true;
                continue;
            }

            if (child.Name == WorksheetNs + "v" && !seenValue)
            {
                seenValue = true;
                changed |= RemoveUnknownAttributes(child, EmptyAttributes);
                changed |= XlsxXmlNormalizationHelpers.RemoveChildElements(child);
                continue;
            }

            if (child.Name == WorksheetNs + "is" && !seenInlineString)
            {
                seenInlineString = true;
                continue;
            }

            if (child.Name == WorksheetNs + "extLst")
            {
                changed |= XlsxWorksheetExtensionListNormalizer.NormalizeChild(child, ref keptExtensionList);
                continue;
            }

            child.Remove();
            changed = true;
        }

        return changed;
    }

    private static bool RemoveUnknownAttributes(XElement element, IReadOnlySet<string> allowedNames)
    {
        var changed = false;
        foreach (var attribute in element.Attributes().ToList())
        {
            if (attribute.IsNamespaceDeclaration ||
                attribute.Name.NamespaceName == MarkupCompatNs.NamespaceName ||
                (attribute.Name.NamespaceName.Length == 0 && allowedNames.Contains(attribute.Name.LocalName)))
            {
                continue;
            }

            attribute.Remove();
            changed = true;
        }

        return changed;
    }

    private static string? NormalizeMetadataIndex(string? value, uint metadataCount)
    {
        var normalized = NormalizeUnsignedIntOrNull(value);
        if (normalized is null)
            return null;

        return metadataCount > 0 &&
            uint.TryParse(normalized, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) &&
            parsed <= metadataCount
            ? normalized
            : null;
    }

    private static string? NormalizeOutlineLevel(string? value)
    {
        var trimmed = value?.Trim();
        return uint.TryParse(trimmed, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) && parsed <= 7
            ? parsed.ToString(CultureInfo.InvariantCulture)
            : null;
    }

    private static string? NormalizeNonNegativeDouble(string? value)
    {
        var trimmed = value?.Trim();
        return double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) &&
            double.IsFinite(parsed) &&
            parsed >= 0
            ? parsed.ToString(CultureInfo.InvariantCulture)
            : null;
    }

    private static string? NormalizeCellReference(string? value)
    {
        var trimmed = value?.Trim().ToUpperInvariant();
        return trimmed is not null && CellReferencePattern.IsMatch(trimmed) ? trimmed : null;
    }

    private static string? NormalizeCellRange(string? value)
    {
        var trimmed = value?.Trim().ToUpperInvariant();
        return trimmed is not null && CellRangePattern.IsMatch(trimmed) ? trimmed : null;
    }

    private static string? NormalizeCellSpans(string? value)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
            return null;

        var spans = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        foreach (var span in spans)
        {
            var parts = span.Split(':');
            if (parts.Length != 2 ||
                !uint.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var start) ||
                !uint.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var end) ||
                start == 0 ||
                end == 0 ||
                start > end)
            {
                return null;
            }
        }

        return string.Join(" ", spans);
    }


    private static (uint CellMetadataCount, uint ValueMetadataCount) ReadMetadataCounts(ZipArchive archive)
    {
        var metadataEntry = archive.GetEntry("xl/metadata.xml");
        if (metadataEntry is null)
            return (0, 0);

        try
        {
            var metadataXml = XlsxPackageXmlEditor.LoadXml(metadataEntry);
            var root = metadataXml.Root;
            return (
                ReadMetadataCount(root, WorksheetNs + "cellMetadata"),
                ReadMetadataCount(root, WorksheetNs + "valueMetadata"));
        }
        catch
        {
            return (0, 0);
        }
    }

    private static uint ReadMetadataCount(XElement? root, XName elementName)
    {
        var metadataElement = root?.Element(elementName);
        if (metadataElement is null)
            return 0;

        var countText = metadataElement.Attribute("count")?.Value?.Trim();
        if (uint.TryParse(countText, NumberStyles.None, CultureInfo.InvariantCulture, out var count))
            return count;

        return (uint)metadataElement.Elements(WorksheetNs + "bk").Count();
    }
}
