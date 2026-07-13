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
        if (MergeStylesheetGradientFills(sourceStylesXml, targetRoot, sourceArchive, workbookNs))
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
    /// Restores gradient fill content that ClosedXML drops when it rebuilds styles.xml from its own
    /// style cache (ClosedXML has no gradient-fill model). To stop a gradient-only cell from
    /// collapsing into the shared default style (which would delete its cell element and its
    /// restorable fill slot outright), <see cref="XlsxClosedXmlCellMapper"/>.ApplyStyle stamps a
    /// solid placeholder fill whose foreground colour is the gradient's first (lowest-position) stop
    /// colour. This method performs the exact inverse: it resolves each source cellXf's gradient with
    /// the same reader and workbook theme the loader used (so the first-stop colour matches the
    /// stamped placeholder byte for byte), finds the rebuilt target's solid placeholder fill of that
    /// colour, and swaps the real source gradient content back in.
    /// </summary>
    /// <remarks>
    /// Correlating by placeholder colour rather than by cellXf style signature is deliberate. A full
    /// rebuild renumbers and reshapes every index space: fontId/borderId/numFmtId references are
    /// renumbered, and ClosedXML re-emits every apply* flag plus a fully expanded alignment/protection
    /// child even for otherwise-default xfs. A minimally-authored source xf (as real Excel files emit)
    /// and its rebuilt counterpart therefore share almost no raw attribute text, so a raw-attribute
    /// signature match never fires for real files. The placeholder colour is the one signal ApplyStyle
    /// deliberately carries across the rebuild.
    /// </remarks>
    private static bool MergeStylesheetGradientFills(
        XDocument sourceStylesXml,
        XElement targetRoot,
        ZipArchive sourceArchive,
        XNamespace workbookNs)
    {
        var sourceRoot = sourceStylesXml.Root;
        var sourceFills = sourceRoot?.Element(workbookNs + "fills");
        if (sourceFills is null)
            return false;

        var sourceFillList = sourceFills.Elements(workbookNs + "fill").ToList();
        if (!sourceFillList.Any(fill => fill.Element(workbookNs + "gradientFill") is not null))
            return false;

        var sourceCellXfs = sourceRoot?.Element(workbookNs + "cellXfs")?.Elements(workbookNs + "xf").ToList();
        if (sourceCellXfs is not { Count: > 0 })
            return false;

        var targetFillList = targetRoot.Element(workbookNs + "fills")?.Elements(workbookNs + "fill").ToList();
        if (targetFillList is not { Count: > 0 })
            return false;

        // Resolve every source cellXf's gradient (theme/indexed/sRGB stop colours all resolved to
        // concrete RGB) with the SAME reader + workbook theme the loader used, so the first-stop
        // colour we compute equals the solid placeholder ApplyStyle stamped during the rebuild.
        var sourceTheme = XlsxWorkbookThemeReader.Load(sourceArchive);
        var sourceIndexedColors = XlsxIndexedColorPaletteMapper.Load(sourceStylesXml);
        var resolvedGradients = XlsxCellGradientFillReader.Read(sourceStylesXml, sourceTheme, sourceIndexedColors);
        if (!resolvedGradients.HasAny)
            return false;

        // Bucket the rebuilt target's solid fills by their foreground RGB. Several placeholders can
        // share a colour (ClosedXML deduplicates identical fills), so each colour maps to a queue and
        // every target fill is consumed at most once.
        var targetSolidFillsByRgb = new Dictionary<int, Queue<XElement>>();
        foreach (var targetFill in targetFillList)
        {
            if (TryGetSolidFillRgbKey(targetFill, workbookNs, out var rgbKey))
            {
                if (!targetSolidFillsByRgb.TryGetValue(rgbKey, out var queue))
                    targetSolidFillsByRgb[rgbKey] = queue = new Queue<XElement>();
                queue.Enqueue(targetFill);
            }
        }

        if (targetSolidFillsByRgb.Count == 0)
            return false;

        // A genuine (non-gradient) source cellXf whose own solid fill colour happens to equal some
        // gradient's first-stop colour is a landmine: XlsxClosedXmlCellMapper.ApplyStyle stamps the
        // gradient cell's placeholder as PatternType=Solid + that exact colour, which is byte-for-byte
        // the same ClosedXML Fill value as the genuine solid cell. ClosedXML's style cache legitimately
        // dedups equal Fill values into ONE rebuilt <fill>, so that colour's target fill ends up shared
        // between the gradient placeholder and the unrelated genuine cell. Track every such colour so
        // the restore below can refuse to touch it rather than risk overwriting the genuine cell's fill.
        var genuineSolidRgbs = new HashSet<int>();
        for (var i = 0; i < sourceCellXfs.Count; i++)
        {
            if (resolvedGradients.TryGet(i, out var placeholderGradient) &&
                placeholderGradient is { Stops.Count: > 0 })
            {
                continue; // this cellXf is itself a gradient placeholder, not a genuine solid fill.
            }

            if (!TryGetIntAttribute(sourceCellXfs[i], "fillId", out var xfFillId) ||
                xfFillId < 0 || xfFillId >= sourceFillList.Count)
            {
                continue;
            }

            if (TryGetSolidFillRgbKey(sourceFillList[xfFillId], workbookNs, out var solidRgb, sourceTheme, sourceIndexedColors))
                genuineSolidRgbs.Add(solidRgb);
        }

        var changed = false;
        var restoredSourceFillIndexes = new HashSet<int>();
        for (var sourceXfIndex = 0; sourceXfIndex < sourceCellXfs.Count; sourceXfIndex++)
        {
            if (!resolvedGradients.TryGet(sourceXfIndex, out var gradient) ||
                gradient is not { Stops.Count: > 0 })
            {
                continue;
            }

            // Restore each source gradient fill at most once: ClosedXML kept a single placeholder for
            // it, however many cellXfs referenced it (their placeholders deduplicate to one fill).
            if (!TryGetIntAttribute(sourceCellXfs[sourceXfIndex], "fillId", out var sourceFillId) ||
                sourceFillId < 0 || sourceFillId >= sourceFillList.Count ||
                !restoredSourceFillIndexes.Add(sourceFillId))
            {
                continue;
            }

            var sourceGradientFill = sourceFillList[sourceFillId].Element(workbookNs + "gradientFill");
            if (sourceGradientFill is null)
                continue;

            var placeholderRgb = RgbKey(gradient.Stops[0].Color);

            // This colour is also a genuine solid cell's fill colour elsewhere in the workbook — the
            // rebuilt target fill for it may be shared between that cell and this gradient's
            // placeholder. Don't risk overwriting the genuine cell; leave this gradient as its solid
            // placeholder rather than corrupt unrelated content.
            if (genuineSolidRgbs.Contains(placeholderRgb))
                continue;

            if (!targetSolidFillsByRgb.TryGetValue(placeholderRgb, out var candidates) || candidates.Count == 0)
                continue;

            // Overwrite the solid placeholder with a clone of the real source gradient. Cloning the
            // gradientFill element directly (rather than the source fill's child nodes) skips any
            // insignificant whitespace text node sitting between the fill and gradientFill tags.
            candidates.Dequeue().ReplaceNodes(new XElement(sourceGradientFill));
            changed = true;
        }

        return changed;
    }

    // A rebuilt solid placeholder fill (stamped by ApplyStyle from a gradient's first-stop colour) is
    // a <patternFill patternType="solid"> whose <fgColor> carries the explicit sRGB. Returns its
    // 24-bit RGB key, or false for any non-solid pattern or a foreground without a readable sRGB.
    // When a theme/indexed palette is supplied (source-side lookups), theme and indexed foreground
    // colours are resolved to concrete RGB too, not just literal sRGB attributes.
    private static bool TryGetSolidFillRgbKey(
        XElement fill,
        XNamespace workbookNs,
        out int rgbKey,
        FreeX.Core.Model.WorkbookTheme? theme = null,
        FreeX.Core.Model.WorkbookIndexedColorPalette? indexedColors = null)
    {
        rgbKey = 0;
        var patternFill = fill.Element(workbookNs + "patternFill");
        if (patternFill is null ||
            !string.Equals(patternFill.Attribute("patternType")?.Value, "solid", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var fgColor = patternFill.Element(workbookNs + "fgColor");
        var resolved = theme is not null && indexedColors is not null
            ? XlsxColorReader.TryReadCellColor(fgColor, theme, indexedColors, out var color)
            : XlsxColorReader.TryReadCellColor(fgColor, out color);
        if (!resolved)
            return false;

        rgbKey = RgbKey(color);
        return true;
    }

    private static int RgbKey(FreeX.Core.Model.CellColor color) =>
        (color.R << 16) | (color.G << 8) | color.B;

    private static bool TryGetIntAttribute(XElement element, string attributeName, out int value)
    {
        var raw = element.Attribute(attributeName)?.Value;
        return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
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

                var clonedStyleXf = new XElement(sourceStyleXf);
                RemapNamedStyleXfChildIndices(clonedStyleXf, sourceRoot, targetRoot, workbookNs);
                targetCellStyleXfs.Add(clonedStyleXf);
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

        // The recovered named-style records above are useless if no cell actually references them:
        // reconnect each rebuilt cellXfs <xf> that corresponds to a source xf bound to one of these
        // named styles, so the style gallery membership (not just its definition) survives the rebuild.
        ReconnectCellXfNamedStyleLinks(sourceRoot, targetRoot, workbookNs, appendedXfIndexBySourceIndex);
        return true;
    }

    /// <summary>
    /// ClosedXML always emits xfId="0" (or omits it, same default) for every rebuilt cellXfs &lt;xf&gt;
    /// — it has no per-cell named-style concept — so a source cell bound to a named style (e.g. the
    /// built-in "Good" cell style) loses that binding even though <see cref="MergeStylesheetNamedCellStyles"/>
    /// keeps the style's definition alive. There is no reliable index correspondence between source and
    /// rebuilt cellXfs (a full rebuild renumbers/reorders them), so cellXfs are correlated by their
    /// rendered style (the dereferenced font/fill/border/numFmt content) instead — the same technique
    /// <see cref="RendersEquivalentDifferentialStyle"/> uses for dxfs. A named-style cell's baked direct
    /// formatting (stamped by <see cref="XlsxClosedXmlCellMapper"/>.ApplyStyle from the resolved
    /// CellStyle) reliably reproduces that signature in the rebuilt xf.
    /// </summary>
    private static void ReconnectCellXfNamedStyleLinks(
        XElement? sourceRoot,
        XElement targetRoot,
        XNamespace workbookNs,
        IReadOnlyDictionary<int, int> sourceXfIdToTargetXfId)
    {
        if (sourceXfIdToTargetXfId.Count == 0)
            return;

        var sourceCellXfs = sourceRoot?.Element(workbookNs + "cellXfs")?.Elements(workbookNs + "xf").ToList();
        var targetCellXfsContainer = targetRoot.Element(workbookNs + "cellXfs");
        if (sourceCellXfs is not { Count: > 0 } || targetCellXfsContainer is null)
            return;

        var targetCellXfs = targetCellXfsContainer.Elements(workbookNs + "xf").ToList();
        if (targetCellXfs.Count == 0)
            return;

        var sourceFonts = sourceRoot?.Element(workbookNs + "fonts");
        var sourceFills = sourceRoot?.Element(workbookNs + "fills");
        var sourceBorders = sourceRoot?.Element(workbookNs + "borders");
        var sourceNumFmts = sourceRoot?.Element(workbookNs + "numFmts");
        var targetFonts = targetRoot.Element(workbookNs + "fonts");
        var targetFills = targetRoot.Element(workbookNs + "fills");
        var targetBorders = targetRoot.Element(workbookNs + "borders");
        var targetNumFmts = targetRoot.Element(workbookNs + "numFmts");

        // Bucket unclaimed target cellXfs (xfId still absent/"0" — the only value ClosedXML ever
        // emits) by rendered-style signature. Several source cellXfs can dedupe to one rebuilt xf, so
        // each signature maps to a queue and every candidate is claimed at most once, mirroring the
        // gradient-fill restore's dequeue pattern above.
        var targetXfsBySignature = new Dictionary<string, Queue<XElement>>(StringComparer.Ordinal);
        foreach (var targetXf in targetCellXfs)
        {
            var currentXfId = targetXf.Attribute("xfId")?.Value;
            if (currentXfId is not (null or "0"))
                continue;

            var signature = BuildXfStyleSignature(targetXf, targetFonts, targetFills, targetBorders, targetNumFmts, workbookNs);
            if (!targetXfsBySignature.TryGetValue(signature, out var queue))
                targetXfsBySignature[signature] = queue = new Queue<XElement>();
            queue.Enqueue(targetXf);
        }

        if (targetXfsBySignature.Count == 0)
            return;

        foreach (var sourceXf in sourceCellXfs)
        {
            if (!TryGetIntAttribute(sourceXf, "xfId", out var sourceXfId) ||
                sourceXfId == 0 ||
                !sourceXfIdToTargetXfId.TryGetValue(sourceXfId, out var targetNamedStyleXfId))
            {
                continue;
            }

            var signature = BuildXfStyleSignature(sourceXf, sourceFonts, sourceFills, sourceBorders, sourceNumFmts, workbookNs);
            if (!targetXfsBySignature.TryGetValue(signature, out var candidates) || candidates.Count == 0)
                continue;

            candidates.Dequeue().SetAttributeValue("xfId", targetNamedStyleXfId.ToString(CultureInfo.InvariantCulture));
        }
    }

    // A signature of the font/fill/border records an xf's fontId/fillId/borderId indices dereference,
    // plus its numFmtId's resolved format code (or the built-in id verbatim, universal below 164).
    // Two xfs with equal signatures render identically, which is the same notion of equivalence
    // <see cref="RendersEquivalentDifferentialStyle"/> uses for dxfs — just computed from indexed
    // records instead of a dxf's inline font/fill/border children.
    private const char SignatureSeparator = (char)0x1F;

    private static string BuildXfStyleSignature(
        XElement xf,
        XElement? fontsList,
        XElement? fillsList,
        XElement? bordersList,
        XElement? numFmtsList,
        XNamespace workbookNs)
    {
        var fontXml = ResolveIndexedRecordXml(xf, "fontId", fontsList, workbookNs + "font");
        var fillXml = ResolveIndexedRecordXml(xf, "fillId", fillsList, workbookNs + "fill");
        var borderXml = ResolveIndexedRecordXml(xf, "borderId", bordersList, workbookNs + "border");
        var numFmtKey = ResolveNumFmtSignatureKey(xf, numFmtsList, workbookNs);
        return string.Join(SignatureSeparator, fontXml, fillXml, borderXml, numFmtKey);
    }

    private static string ResolveIndexedRecordXml(XElement xf, string attributeName, XElement? list, XName itemName)
    {
        if (list is null || !TryGetIntAttribute(xf, attributeName, out var index))
            return string.Empty;

        var items = list.Elements(itemName).ToList();
        if (index < 0 || index >= items.Count)
            return string.Empty;

        return items[index].ToString(SaveOptions.DisableFormatting);
    }

    private static string ResolveNumFmtSignatureKey(XElement xf, XElement? numFmtsList, XNamespace workbookNs)
    {
        if (!TryGetIntAttribute(xf, "numFmtId", out var numFmtId))
            return string.Empty;
        if (numFmtId < 164)
            return numFmtId.ToString(CultureInfo.InvariantCulture);

        var formatCode = numFmtsList?
            .Elements(workbookNs + "numFmt")
            .FirstOrDefault(element => TryGetIntAttribute(element, "numFmtId", out var id) && id == numFmtId)?
            .Attribute("formatCode")?.Value;
        return formatCode ?? numFmtId.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// A recovered named-style <c>cellStyleXfs</c> entry is copied verbatim from the source, but its
    /// numFmtId/fontId/fillId/borderId children reference the SOURCE stylesheet's fonts/fills/borders/numFmts
    /// index space. ClosedXML rebuilds those lists from scratch with its own ordering and size, so the copied
    /// indices generally point at unrelated (or out-of-range) records in the target. Remap each child reference
    /// to the equivalent record in the target's lists, appending it there first if no equivalent exists yet.
    /// </summary>
    private static void RemapNamedStyleXfChildIndices(
        XElement xf,
        XElement? sourceRoot,
        XElement targetRoot,
        XNamespace workbookNs)
    {
        RemapIndexedRecordReference(xf, sourceRoot, targetRoot, workbookNs, "fontId", "fonts", "font");
        RemapIndexedRecordReference(xf, sourceRoot, targetRoot, workbookNs, "fillId", "fills", "fill");
        RemapIndexedRecordReference(xf, sourceRoot, targetRoot, workbookNs, "borderId", "borders", "border");
        RemapNumFmtIdReference(xf, sourceRoot, targetRoot, workbookNs);
    }

    private static void RemapIndexedRecordReference(
        XElement xf,
        XElement? sourceRoot,
        XElement targetRoot,
        XNamespace workbookNs,
        string attributeName,
        string listElementName,
        string itemElementName)
    {
        if (!TryGetIntAttribute(xf, attributeName, out var sourceIndex))
            return;

        var sourceItems = sourceRoot?.Element(workbookNs + listElementName)?.Elements(workbookNs + itemElementName).ToList();
        if (sourceItems is not { Count: > 0 } || sourceIndex < 0 || sourceIndex >= sourceItems.Count)
            return;

        var sourceItem = sourceItems[sourceIndex];
        var sourceItemXml = sourceItem.ToString(SaveOptions.DisableFormatting);

        var targetList = targetRoot.Element(workbookNs + listElementName);
        if (targetList is null)
            return; // fonts/fills/borders always exist in a rebuilt stylesheet; defensively skip if not

        var targetItems = targetList.Elements(workbookNs + itemElementName).ToList();
        var targetIndex = -1;
        for (var i = 0; i < targetItems.Count; i++)
        {
            if (string.Equals(targetItems[i].ToString(SaveOptions.DisableFormatting), sourceItemXml, StringComparison.Ordinal))
            {
                targetIndex = i;
                break;
            }
        }

        if (targetIndex < 0)
        {
            targetList.Add(new XElement(sourceItem));
            targetIndex = targetItems.Count;
            targetList.SetAttributeValue(
                "count",
                targetList.Elements(workbookNs + itemElementName).Count().ToString(CultureInfo.InvariantCulture));
        }

        xf.SetAttributeValue(attributeName, targetIndex.ToString(CultureInfo.InvariantCulture));
    }

    private static void RemapNumFmtIdReference(
        XElement xf,
        XElement? sourceRoot,
        XElement targetRoot,
        XNamespace workbookNs)
    {
        if (!TryGetIntAttribute(xf, "numFmtId", out var sourceNumFmtId) || sourceNumFmtId < 164)
            return; // built-in number format ids are universal — no remap needed

        var sourceFormatCode = sourceRoot?
            .Element(workbookNs + "numFmts")?
            .Elements(workbookNs + "numFmt")
            .FirstOrDefault(element => TryGetIntAttribute(element, "numFmtId", out var id) && id == sourceNumFmtId)?
            .Attribute("formatCode")?.Value;
        if (string.IsNullOrEmpty(sourceFormatCode))
            return;

        var targetNumFmts = targetRoot.Element(workbookNs + "numFmts");
        if (targetNumFmts is null)
        {
            targetNumFmts = new XElement(workbookNs + "numFmts");
            var firstFormatPeer = targetRoot.Elements().FirstOrDefault(element =>
                element.Name == workbookNs + "fonts" ||
                element.Name == workbookNs + "fills" ||
                element.Name == workbookNs + "borders" ||
                element.Name == workbookNs + "cellStyleXfs" ||
                element.Name == workbookNs + "cellXfs");
            if (firstFormatPeer is null)
                targetRoot.AddFirst(targetNumFmts);
            else
                firstFormatPeer.AddBeforeSelf(targetNumFmts);
        }

        var existingEntries = targetNumFmts.Elements(workbookNs + "numFmt").ToList();
        var existingMatch = existingEntries.FirstOrDefault(element =>
            string.Equals(element.Attribute("formatCode")?.Value, sourceFormatCode, StringComparison.Ordinal));

        int targetNumFmtId;
        if (existingMatch is not null && TryGetIntAttribute(existingMatch, "numFmtId", out var matchedId))
        {
            targetNumFmtId = matchedId;
        }
        else
        {
            var usedIds = existingEntries
                .Select(element => TryGetIntAttribute(element, "numFmtId", out var id) ? id : (int?)null)
                .Where(id => id is not null)
                .Select(id => id!.Value)
                .ToHashSet();
            var nextId = Math.Max(164, usedIds.Count == 0 ? 164 : usedIds.Max() + 1);
            while (usedIds.Contains(nextId))
                nextId++;

            targetNumFmtId = nextId;
            targetNumFmts.Add(new XElement(
                workbookNs + "numFmt",
                new XAttribute("numFmtId", targetNumFmtId.ToString(CultureInfo.InvariantCulture)),
                new XAttribute("formatCode", sourceFormatCode)));
            targetNumFmts.SetAttributeValue(
                "count",
                targetNumFmts.Elements(workbookNs + "numFmt").Count().ToString(CultureInfo.InvariantCulture));
        }

        xf.SetAttributeValue("numFmtId", targetNumFmtId.ToString(CultureInfo.InvariantCulture));
    }
}
