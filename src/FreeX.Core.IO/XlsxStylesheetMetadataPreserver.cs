using System.Globalization;
using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;

namespace FreeX.Core.IO;

internal static class XlsxStylesheetMetadataPreserver
{
    public static void Preserve(ZipArchive sourceArchive, ZipArchive targetArchive)
    {
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        var sourceStylesEntry = sourceArchive.GetEntry("xl/styles.xml");
        var targetStylesEntry = targetArchive.GetEntry("xl/styles.xml");
        if (sourceStylesEntry is null || targetStylesEntry is null)
            return;
        if (!HasPreservableStylesheetMetadata(sourceStylesEntry))
            return;

        var sourceStylesXml = XlsxPackageXmlEditor.LoadXml(sourceStylesEntry);
        var targetStylesXml = XlsxPackageXmlEditor.LoadXml(targetStylesEntry);
        var targetRoot = targetStylesXml.Root;
        if (targetRoot is null)
            return;

        var changed = false;
        if (MergeStylesheetColors(sourceStylesXml.Root?.Element(workbookNs + "colors"), targetRoot, workbookNs))
            changed = true;
        if (MergeStylesheetGradientFills(sourceStylesXml.Root, targetRoot, workbookNs))
            changed = true;
        if (MergeStylesheetDifferentialStyles(sourceStylesXml.Root?.Element(workbookNs + "dxfs"), targetRoot, workbookNs))
            changed = true;
        if (MergeStylesheetTableStyles(sourceStylesXml.Root?.Element(workbookNs + "tableStyles"), targetRoot, workbookNs))
            changed = true;
        if (MergeStylesheetNamedCellStyles(sourceStylesXml.Root, targetRoot, workbookNs))
            changed = true;
        if (XlsxNativeXmlMerger.MergeExtensionList(sourceStylesXml.Root?.Element(workbookNs + "extLst"), targetRoot, workbookNs))
            changed = true;

        if (changed)
            XlsxPackageXmlEditor.ReplaceXml(targetArchive, "xl/styles.xml", targetStylesXml);
    }

    private static bool HasPreservableStylesheetMetadata(ZipArchiveEntry sourceStylesEntry)
    {
        try
        {
            using var stream = sourceStylesEntry.Open();
            using var reader = XmlReader.Create(stream, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                IgnoreComments = true,
                IgnoreProcessingInstructions = true,
                IgnoreWhitespace = true
            });

            var stylesheetDepth = -1;
            while (reader.Read())
            {
                if (reader.NodeType != XmlNodeType.Element)
                    continue;

                if (stylesheetDepth < 0)
                {
                    if (reader.LocalName == "styleSheet" &&
                        reader.NamespaceURI == "http://schemas.openxmlformats.org/spreadsheetml/2006/main")
                    {
                        stylesheetDepth = reader.Depth;
                    }

                    continue;
                }

                if (reader.Depth != stylesheetDepth + 1 ||
                    reader.NamespaceURI != "http://schemas.openxmlformats.org/spreadsheetml/2006/main")
                {
                    continue;
                }

                switch (reader.LocalName)
                {
                    case "colors":
                    case "extLst":
                        return true;
                    case "fills":
                        if (HasGradientFill(reader))
                            return true;
                        break;
                    case "dxfs":
                        if (HasPreservableDifferentialStyles(reader))
                            return true;
                        break;
                    case "tableStyles":
                        if (HasPreservableTableStyles(reader))
                            return true;
                        break;
                    case "cellStyles":
                        if (HasPreservableNamedCellStyles(reader))
                            return true;
                        break;
                }
            }

            return false;
        }
        catch
        {
            return true;
        }
    }

    private static bool HasGradientFill(XmlReader reader)
    {
        if (reader.IsEmptyElement)
            return false;

        using var subtree = reader.ReadSubtree();
        while (subtree.Read())
        {
            if (subtree.NodeType == XmlNodeType.Element &&
                subtree.LocalName == "gradientFill" &&
                subtree.NamespaceURI == "http://schemas.openxmlformats.org/spreadsheetml/2006/main")
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasPreservableDifferentialStyles(XmlReader reader)
    {
        if (HasNativeOnlyAttributes(reader, "count"))
            return true;
        if (reader.IsEmptyElement)
            return false;

        using var subtree = reader.ReadSubtree();
        while (subtree.Read())
        {
            if (subtree.NodeType == XmlNodeType.Element && subtree.Depth > 0)
                return true;
        }

        return false;
    }

    private static bool HasPreservableTableStyles(XmlReader reader)
    {
        if (reader.HasAttributes)
        {
            for (var i = 0; i < reader.AttributeCount; i++)
            {
                reader.MoveToAttribute(i);
                if (IsNamespaceDeclaration(reader))
                    continue;

                if (reader.LocalName == "count")
                {
                    if (!string.Equals(reader.Value, "0", StringComparison.Ordinal))
                    {
                        reader.MoveToElement();
                        return true;
                    }

                    continue;
                }

                if (reader.LocalName == "defaultTableStyle")
                {
                    if (!string.Equals(reader.Value, "TableStyleMedium2", StringComparison.Ordinal))
                    {
                        reader.MoveToElement();
                        return true;
                    }

                    continue;
                }

                if (reader.LocalName == "defaultPivotStyle")
                {
                    if (!string.Equals(reader.Value, "PivotStyleLight16", StringComparison.Ordinal))
                    {
                        reader.MoveToElement();
                        return true;
                    }

                    continue;
                }

                reader.MoveToElement();
                return true;
            }

            reader.MoveToElement();
        }

        if (reader.IsEmptyElement)
            return false;

        using var subtree = reader.ReadSubtree();
        while (subtree.Read())
        {
            if (subtree.NodeType == XmlNodeType.Element && subtree.Depth > 0)
                return true;
        }

        return false;
    }

    // ClosedXML always emits exactly one cellStyles entry: the built-in "Normal" style (xfId="0",
    // builtinId="0") pointing at the sole default cellStyleXfs record it produces. Any additional
    // <cellStyle> entry, or a first entry that isn't that default "Normal" definition, means the
    // source workbook had custom/Excel-authored named cell styles that would otherwise be dropped.
    private static bool HasPreservableNamedCellStyles(XmlReader reader)
    {
        if (reader.IsEmptyElement)
            return false;

        using var subtree = reader.ReadSubtree();
        var sawCellStyle = false;
        while (subtree.Read())
        {
            if (subtree.NodeType != XmlNodeType.Element || subtree.Depth != 1)
                continue;

            if (subtree.LocalName != "cellStyle")
                return true;

            if (sawCellStyle)
                return true;
            sawCellStyle = true;

            if (!IsDefaultNormalCellStyle(subtree))
                return true;
        }

        return false;
    }

    private static bool IsDefaultNormalCellStyle(XmlReader reader)
    {
        if (!reader.HasAttributes)
            return false;

        var name = reader.GetAttribute("name");
        var xfId = reader.GetAttribute("xfId");
        var builtinId = reader.GetAttribute("builtinId");
        return string.Equals(name, "Normal", StringComparison.Ordinal) &&
               string.Equals(xfId, "0", StringComparison.Ordinal) &&
               string.Equals(builtinId, "0", StringComparison.Ordinal);
    }

    private static bool HasNativeOnlyAttributes(XmlReader reader, params string[] modeledLocalNames)
    {
        if (!reader.HasAttributes)
            return false;

        for (var i = 0; i < reader.AttributeCount; i++)
        {
            reader.MoveToAttribute(i);
            if (!IsNamespaceDeclaration(reader) &&
                !modeledLocalNames.Contains(reader.LocalName, StringComparer.Ordinal))
            {
                reader.MoveToElement();
                return true;
            }
        }

        reader.MoveToElement();
        return false;
    }

    private static bool IsNamespaceDeclaration(XmlReader reader) =>
        reader.Prefix == "xmlns" ||
        (reader.Prefix.Length == 0 && reader.LocalName == "xmlns");

    /// <summary>
    /// Copies gradient fill entries from the source styles.xml fills section into the target.
    /// ClosedXML does not know about gradient fills; when a loaded workbook is saved, ClosedXML
    /// replaces all gradient fill entries with a default patternFill:None. This method detects
    /// that case and restores the gradient fill at the correct fill index by matching fillId
    /// references in the cellXfs sections.
    /// </summary>
    private static bool MergeStylesheetGradientFills(XElement? sourceRoot, XElement targetRoot, XNamespace workbookNs)
    {
        var sourceFills = sourceRoot?.Element(workbookNs + "fills");
        if (sourceFills is null)
            return false;

        var sourceGradientFills = sourceFills
            .Elements(workbookNs + "fill")
            .Select((fill, idx) => (Fill: fill, Index: idx))
            .Where(pair => pair.Fill.Element(workbookNs + "gradientFill") is not null)
            .ToList();

        if (sourceGradientFills.Count == 0)
            return false;

        var targetFills = targetRoot.Element(workbookNs + "fills");
        if (targetFills is null)
            return false;

        var targetFillList = targetFills.Elements(workbookNs + "fill").ToList();
        var changed = false;

        foreach (var (sourceFill, fillIndex) in sourceGradientFills)
        {
            if (fillIndex >= targetFillList.Count)
                continue;

            var targetFill = targetFillList[fillIndex];
            // Only replace if target has a patternFill (not already a gradientFill)
            if (targetFill.Element(workbookNs + "gradientFill") is not null)
                continue; // already a gradient — no change needed

            // Replace the target fill's content with the source gradient fill content
            targetFill.ReplaceNodes(sourceFill.Nodes().Select(n => new XElement((XElement)n)));
            changed = true;
        }

        return changed;
    }

    private static bool MergeStylesheetColors(XElement? sourceColors, XElement targetRoot, XNamespace workbookNs)
    {
        if (sourceColors is null)
            return false;

        var targetColors = targetRoot.Element(workbookNs + "colors");
        if (targetColors is null)
        {
            targetRoot.Add(new XElement(sourceColors));
            return true;
        }

        return XlsxNativeXmlMerger.MergeElementNativeAttributesAndChildren(sourceColors, targetColors);
    }

    private static bool MergeStylesheetDifferentialStyles(XElement? sourceDifferentialStyles, XElement targetRoot, XNamespace workbookNs)
    {
        if (sourceDifferentialStyles is null)
            return false;

        var targetDifferentialStyles = targetRoot.Element(workbookNs + "dxfs");
        if (targetDifferentialStyles is null)
        {
            targetRoot.Add(new XElement(sourceDifferentialStyles));
            return true;
        }

        var changed = MergeDifferentialStyleContainerAttributes(sourceDifferentialStyles, targetDifferentialStyles);
        var targetStyles = targetDifferentialStyles.Elements(workbookNs + "dxf").ToList();
        foreach (var (sourceStyle, index) in sourceDifferentialStyles.Elements(workbookNs + "dxf").Select((style, index) => (style, index)))
        {
            if (index >= targetStyles.Count)
            {
                targetDifferentialStyles.Add(new XElement(sourceStyle));
                targetStyles.Add(targetDifferentialStyles.Elements(workbookNs + "dxf").Last());
                XlsxAdvancedConditionalFormatWriter.NormalizeDifferentialStyleOrder(targetStyles[^1], workbookNs);
                changed = true;
                continue;
            }

            // Source and target dxf lists are aligned by raw index, but a rebuild reorders dxfs (ClosedXML's
            // dxfs plus FreeX's appended advanced-conditional-format dxfs land in a different order than the
            // source). Merging native font/fill/border content into a dxf that renders a different style would
            // corrupt it (e.g. inject a red font into an unrelated green-fill rule), so only merge when the two
            // dxfs render the same visible style. Genuinely corresponding dxfs still recover their native
            // metadata; for the rest, FreeX's model has already re-emitted that metadata into the rebuilt dxf.
            if (!RendersEquivalentDifferentialStyle(sourceStyle, targetStyles[index], workbookNs))
                continue;

            if (XlsxNativeXmlMerger.MergeElementNativeAttributesAndChildren(sourceStyle, targetStyles[index]))
                changed = true;
            if (XlsxAdvancedConditionalFormatWriter.NormalizeDifferentialStyleOrder(targetStyles[index], workbookNs))
                changed = true;
        }

        targetDifferentialStyles.SetAttributeValue(
            "count",
            targetDifferentialStyles.Elements(workbookNs + "dxf").Count().ToString(CultureInfo.InvariantCulture));
        return changed;
    }

    // Two dxfs "render the same style" when their modeled font/fill/border/number-format produce an equal
    // CellStyle. Native (unmodeled) metadata is intentionally ignored here — that is precisely what the merge
    // recovers — so it is cleared before comparing.
    private static bool RendersEquivalentDifferentialStyle(XElement sourceDxf, XElement targetDxf, XNamespace workbookNs)
    {
        var sourceStyle = XlsxDifferentialStyleReader.ReadDifferentialStyle(sourceDxf, workbookNs);
        var targetStyle = XlsxDifferentialStyleReader.ReadDifferentialStyle(targetDxf, workbookNs);
        ClearNativeDifferentialMetadata(sourceStyle);
        ClearNativeDifferentialMetadata(targetStyle);
        return sourceStyle.Equals(targetStyle);
    }

    private static void ClearNativeDifferentialMetadata(FreeX.Core.Model.CellStyle style)
    {
        style.NativeDifferentialAttributes = null;
        style.NativeDifferentialChildXmls = null;
        style.NativeDifferentialElementXmls = null;
    }

    private static bool MergeDifferentialStyleContainerAttributes(XElement sourceDifferentialStyles, XElement targetDifferentialStyles)
    {
        var changed = false;
        foreach (var attribute in sourceDifferentialStyles.Attributes())
        {
            if (attribute.IsNamespaceDeclaration ||
                string.Equals(attribute.Name.LocalName, "count", StringComparison.Ordinal) ||
                string.Equals(targetDifferentialStyles.Attribute(attribute.Name)?.Value, attribute.Value, StringComparison.Ordinal))
            {
                continue;
            }

            targetDifferentialStyles.SetAttributeValue(attribute.Name, attribute.Value);
            changed = true;
        }

        return changed;
    }

    private static bool MergeStylesheetTableStyles(XElement? sourceTableStyles, XElement targetRoot, XNamespace workbookNs)
    {
        if (sourceTableStyles is null)
            return false;

        var targetTableStyles = targetRoot.Element(workbookNs + "tableStyles");
        if (targetTableStyles is null)
        {
            targetRoot.Add(new XElement(sourceTableStyles));
            return true;
        }

        var changed = false;
        foreach (var attribute in sourceTableStyles.Attributes())
        {
            if (targetTableStyles.Attribute(attribute.Name)?.Value == attribute.Value)
                continue;

            targetTableStyles.SetAttributeValue(attribute.Name, attribute.Value);
            changed = true;
        }

        var targetStylesByName = targetTableStyles
            .Elements(workbookNs + "tableStyle")
            .Select(element => (Name: element.Attribute("name")?.Value, Element: element))
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Name))
            .ToDictionary(pair => pair.Name!, pair => pair.Element, StringComparer.OrdinalIgnoreCase);
        foreach (var sourceStyle in sourceTableStyles.Elements(workbookNs + "tableStyle"))
        {
            var name = sourceStyle.Attribute("name")?.Value;
            if (string.IsNullOrWhiteSpace(name) || !targetStylesByName.TryGetValue(name, out var targetStyle))
            {
                targetTableStyles.Add(new XElement(sourceStyle));
                if (!string.IsNullOrWhiteSpace(name))
                    targetStylesByName[name] = targetTableStyles.Elements(workbookNs + "tableStyle").Last();
                changed = true;
                continue;
            }

            if (XlsxNativeXmlMerger.MergeElementNativeAttributesAndChildren(sourceStyle, targetStyle))
                changed = true;
        }

        if (MergeTableStylesNativeChildren(sourceTableStyles, targetTableStyles, workbookNs))
            changed = true;

        targetTableStyles.SetAttributeValue(
            "count",
            targetTableStyles.Elements(workbookNs + "tableStyle").Count().ToString(CultureInfo.InvariantCulture));
        return changed;
    }

    private static bool MergeTableStylesNativeChildren(
        XElement sourceTableStyles,
        XElement targetTableStyles,
        XNamespace workbookNs)
    {
        var targetChildrenByKey = targetTableStyles
            .Elements()
            .Where(child => child.Name != workbookNs + "tableStyle")
            .GroupBy(NativeChildKey, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        var changed = false;
        foreach (var sourceChild in sourceTableStyles.Elements().Where(child => child.Name != workbookNs + "tableStyle"))
        {
            var key = NativeChildKey(sourceChild);
            if (targetChildrenByKey.TryGetValue(key, out var targetChild))
            {
                if (XlsxNativeXmlMerger.MergeElementNativeAttributesAndChildren(sourceChild, targetChild))
                    changed = true;
                continue;
            }

            targetTableStyles.Add(new XElement(sourceChild));
            targetChildrenByKey[key] = targetTableStyles.Elements().Last();
            changed = true;
        }

        return changed;
    }

    private static string NativeChildKey(XElement element)
    {
        var identity = element.Attribute("name")?.Value
            ?? element.Attribute("id")?.Value
            ?? element.Attribute("uid")?.Value
            ?? element.Attribute("uri")?.Value
            ?? string.Empty;
        return $"{element.Name}\u001f{identity}";
    }

    /// <summary>
    /// Recovers named cell style definitions (cellStyleXfs/cellStyles) that ClosedXML drops on
    /// rebuild. ClosedXML has no notion of a named cell style, so a saved workbook's styles.xml
    /// always ends up with just the single built-in "Normal" entry, silently deleting any
    /// custom/Excel-authored named styles the source file defined. This merge appends the source's
    /// additional cellStyleXfs format records (remapping their xfId references) and cellStyles
    /// name bindings that are missing from the rebuilt target, so the style gallery entries and
    /// their name bindings survive a save even though FreeX's own per-cell model still only tracks
    /// direct formatting (a cell already linked to a custom style by xfId in the source keeps that
    /// binding only if ClosedXML preserved the referencing cellXfs entry's xfId; the style
    /// definitions themselves are no longer silently discarded).
    /// </summary>
    private static bool MergeStylesheetNamedCellStyles(XElement? sourceRoot, XElement targetRoot, XNamespace workbookNs)
    {
        var sourceCellStyles = sourceRoot?.Element(workbookNs + "cellStyles");
        var sourceCellStyleXfs = sourceRoot?.Element(workbookNs + "cellStyleXfs");
        if (sourceCellStyles is null || sourceCellStyleXfs is null)
            return false;

        var sourceStyleList = sourceCellStyles.Elements(workbookNs + "cellStyle").ToList();
        if (sourceStyleList.Count == 0)
            return false;

        var targetCellStyles = targetRoot.Element(workbookNs + "cellStyles");
        var targetCellStyleXfs = targetRoot.Element(workbookNs + "cellStyleXfs");
        if (targetCellStyles is null || targetCellStyleXfs is null)
            return false;

        var targetStyleNames = targetCellStyles
            .Elements(workbookNs + "cellStyle")
            .Select(style => style.Attribute("name")?.Value)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var sourceStyleXfList = sourceCellStyleXfs.Elements(workbookNs + "xf").ToList();
        var targetStyleXfCountBeforeMerge = targetCellStyleXfs.Elements(workbookNs + "xf").Count();
        var appendedXfIndexBySourceIndex = new Dictionary<int, int>();
        var changed = false;

        foreach (var sourceStyle in sourceStyleList)
        {
            var name = sourceStyle.Attribute("name")?.Value;
            if (string.IsNullOrWhiteSpace(name) || targetStyleNames.Contains(name))
                continue;

            var sourceXfId = XlsxXmlAttributeReader.ReadIntAttribute(sourceStyle, "xfId") ?? 0;
            if (!appendedXfIndexBySourceIndex.TryGetValue(sourceXfId, out var newXfIndex))
            {
                var sourceStyleXf = sourceXfId >= 0 && sourceXfId < sourceStyleXfList.Count
                    ? sourceStyleXfList[sourceXfId]
                    : null;
                if (sourceStyleXf is null)
                    continue;

                targetCellStyleXfs.Add(new XElement(sourceStyleXf));
                newXfIndex = targetStyleXfCountBeforeMerge + appendedXfIndexBySourceIndex.Count;
                appendedXfIndexBySourceIndex[sourceXfId] = newXfIndex;
            }

            var newStyle = new XElement(sourceStyle);
            newStyle.SetAttributeValue("xfId", newXfIndex.ToString(CultureInfo.InvariantCulture));
            targetCellStyles.Add(newStyle);
            targetStyleNames.Add(name);
            changed = true;
        }

        if (!changed)
            return false;

        targetCellStyleXfs.SetAttributeValue(
            "count",
            targetCellStyleXfs.Elements(workbookNs + "xf").Count().ToString(CultureInfo.InvariantCulture));
        targetCellStyles.SetAttributeValue(
            "count",
            targetCellStyles.Elements(workbookNs + "cellStyle").Count().ToString(CultureInfo.InvariantCulture));
        return true;
    }
}
