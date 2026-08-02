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

        // Captured BEFORE any merge below mutates <tableStyles> -- used only to compute which
        // trailing source dxfs are still "live" (see ComputeLiveTrailingDifferentialStyleIndexes).
        var targetTableStylesBeforeMerge = targetRoot.Element(workbookNs + "tableStyles");

        var changed = false;
        if (MergeStylesheetColors(sourceStylesXml.Root?.Element(workbookNs + "colors"), targetRoot, workbookNs))
            changed = true;
        if (MergeStylesheetGradientFills(sourceStylesXml, targetRoot, sourceArchive, workbookNs))
            changed = true;
        var liveTrailingIndexes = ComputeLiveTrailingDifferentialStyleIndexes(
            sourceArchive,
            targetArchive,
            sourceStylesXml.Root?.Element(workbookNs + "tableStyles"),
            targetTableStylesBeforeMerge,
            workbookNs);
        if (MergeStylesheetDifferentialStyles(sourceStylesXml.Root?.Element(workbookNs + "dxfs"), targetRoot, workbookNs, liveTrailingIndexes))
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

            // Must match XlsxClosedXmlCellMapper.ApplyStyle's placeholder colour exactly, including
            // the low-bit perturbation ComputeGradientPlaceholderColor derives from the gradient's
            // FULL content — not just its first stop — so two distinct gradients sharing a first
            // stop colour resolve to two distinct target fills instead of colliding on one.
            var placeholderRgb = RgbKey(XlsxClosedXmlCellMapper.ComputeGradientPlaceholderColor(gradient));

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

    private static bool MergeStylesheetDifferentialStyles(
        XElement? sourceDifferentialStyles,
        XElement targetRoot,
        XNamespace workbookNs,
        IReadOnlyCollection<int> liveTrailingIndexes)
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
                // A rebuild sizes the target's <dxfs> to exactly the model's CURRENT live rules, so
                // any source dxf past that count belonged to something the rebuild no longer emits.
                // Most commonly that is a conditional-format rule the user just deleted -- appending
                // it unconditionally would resurrect a dxf nothing references, and because the
                // just-saved package is rebased to become the next save's "source" (see
                // XlsxFileAdapter.SavePostProcessing.cs), the zombie would never get pruned. Only
                // keep it when something that will actually survive into the final package still
                // addresses this exact source-side index (an "unsupported" CF rule preserved
                // verbatim, a native tableStyle not tracked by the model, or a raw dxfId passthrough
                // already written into the rebuilt worksheet) -- see
                // ComputeLiveTrailingDifferentialStyleIndexes for the full enumeration.
                if (!liveTrailingIndexes.Contains(index))
                    continue;

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

    // Computes which trailing (index >= the just-rebuilt target's dxf count) SOURCE dxf indices are
    // still "live" -- i.e. still addressed by something that will actually survive into the final
    // saved package -- as opposed to belonging to a conditional-format rule the user just deleted.
    // The rebuild sizes <dxfs> to exactly the model's CURRENT rules (SaveDifferentialStyles), so a
    // deleted rule's old dxf always lands past that count with nothing left pointing at it; blindly
    // re-appending it (the historical bug) resurrects a zombie entry that -- because the just-saved
    // package becomes the next save's "source" -- never gets pruned.
    //
    // Three kinds of surviving content still address a dxf by its ORIGINAL source-side index rather
    // than a freshly-rebuilt one, and must keep that dxf entry alive:
    //   1. An "unsupported" CF rule type FreeX doesn't model at all: XlsxUnsupportedConditionalFormattingPreserver
    //      (called later in the same save, from PreserveSourcePackageParts) re-inserts that rule's
    //      whole <conditionalFormatting> block verbatim from the SOURCE worksheet, dxfId untouched.
    //   2. A <tableStyle> present in the source but not in the just-rebuilt target (by name) --
    //      MergeStylesheetTableStyles (below, in this same Preserve pass) raw-clones the whole
    //      <tableStyle> element, including its <tableStyleElement dxfId="..."/> children, unchanged.
    //      (A tableStyle the model DOES track -- e.g. via XlsxStructuredTableStyleMetadataWriter or
    //      XlsxSlicerTimelineWriter.SavePivotTableStyles, both of which run before this preserver --
    //      already exists in the target by name and remaps its own dxfId against the rebuilt <dxfs>
    //      itself; see R22_TableStyleDxfIdRemapTests.)
    //   3. A raw dxfId passthrough already written into the just-rebuilt TARGET worksheet XML for an
    //      AutoFilter/structured-table color filter that carried a native dxfId at load time (see
    //      XlsxWorksheetAutoFilterXmlMapper/XlsxStructuredTableWriter's DifferentialFormatIdRaw
    //      passthrough) -- that raw string is the SOURCE package's original index, written into the
    //      target during the initial model-to-XML pass, well before this preserver ever runs.
    //
    // Any source dxf beyond the rebuilt count referenced by NONE of the above has no live consumer
    // left and must be dropped rather than resurrected.
    private static HashSet<int> ComputeLiveTrailingDifferentialStyleIndexes(
        ZipArchive sourceArchive,
        ZipArchive targetArchive,
        XElement? sourceTableStyles,
        XElement? targetTableStylesBeforeMerge,
        XNamespace workbookNs)
    {
        var live = new HashSet<int>();

        // (3) any dxfId already written into the rebuilt target's worksheets.
        foreach (var worksheetEntry in targetArchive.Entries.Where(XlsxConditionalFormatRuleSupport.IsWorksheetEntry))
        {
            XDocument worksheetXml;
            try
            {
                worksheetXml = XlsxPackageXmlEditor.LoadXml(worksheetEntry);
            }
            catch
            {
                continue;
            }

            foreach (var attribute in worksheetXml.Descendants().Attributes("dxfId"))
            {
                if (TryParseInt(attribute.Value, out var dxfId))
                    live.Add(dxfId);
            }
        }

        // (1) any dxfId referenced from an "unsupported" CF rule in the SOURCE worksheets -- that
        // whole block gets pasted back verbatim later in this same save.
        foreach (var worksheetEntry in sourceArchive.Entries.Where(XlsxConditionalFormatRuleSupport.IsWorksheetEntry))
        {
            XDocument worksheetXml;
            try
            {
                worksheetXml = XlsxPackageXmlEditor.LoadXml(worksheetEntry);
            }
            catch
            {
                continue;
            }

            foreach (var block in worksheetXml.Root?.Elements(workbookNs + "conditionalFormatting") ?? Enumerable.Empty<XElement>())
            {
                if (!XlsxConditionalFormatRuleSupport.ConditionalFormattingHasUnsupportedRule(
                        block, workbookNs, allowBlankType: true, comparison: StringComparison.Ordinal))
                {
                    continue;
                }

                foreach (var rule in block.Elements(workbookNs + "cfRule"))
                {
                    if (TryGetIntAttribute(rule, "dxfId", out var dxfId))
                        live.Add(dxfId);
                }
            }
        }

        // (2) any dxfId used by a source <tableStyle> not present (by name) in the just-rebuilt target.
        if (sourceTableStyles is not null)
        {
            var targetNames = (targetTableStylesBeforeMerge?.Elements(workbookNs + "tableStyle") ?? Enumerable.Empty<XElement>())
                .Select(element => element.Attribute("name")?.Value)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var sourceStyle in sourceTableStyles.Elements(workbookNs + "tableStyle"))
            {
                var name = sourceStyle.Attribute("name")?.Value;
                if (!string.IsNullOrWhiteSpace(name) && targetNames.Contains(name))
                    continue; // already tracked by the model; its own writer remaps dxfId itself (R22)

                foreach (var element in sourceStyle.Elements(workbookNs + "tableStyleElement"))
                {
                    if (TryGetIntAttribute(element, "dxfId", out var dxfId))
                        live.Add(dxfId);
                }
            }
        }

        return live;
    }

    private static bool TryParseInt(string? raw, out int value) =>
        int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);

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

        // Dxf*/tri-state fields record "was this toggle explicitly authored vs never mentioned" for
        // in-memory CF-stacking logic (see CellStyle.cs), not how the style renders: Bold=false with
        // DxfBold=null (never mentioned) and Bold=false with DxfBold=false (explicit <b val="0"/>) are
        // visually identical. None of FreeX's dxf writers ever re-emit an explicit off-toggle (they only
        // check e.g. `style.Bold != def.Bold`, which is only true when Bold==true), so a rebuilt dxf for
        // an explicit-off source always reads back with the tri-state field null even when the source had
        // it set to false. Leaving these fields in the comparison would make an otherwise render-identical
        // pair compare unequal and silently drop the source's native XML from the merge. Clear them here so
        // the comparison stays scoped to modeled font/fill/border/number-format, as documented above.
        style.DxfBold = null;
        style.DxfItalic = null;
        style.DxfUnderline = null;
        style.DxfStrikethrough = null;
        style.DxfFontColor = null;
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

        // Provenance guard: a rebuilt cellXfs <xf> record can be shared by ClosedXML across several
        // source cells that render identically even though only some of them were bound to a named
        // style in the source (e.g. a "Good"-styled cell and an unrelated plain-formatted cell that
        // happens to look pixel-identical). Reconnecting such a shared target xf would silently pull
        // the plain cell into the named style's gallery membership too. Since this pass only sees
        // styles.xml (no per-cell xf usage), the best available signal is: does ANY source cellXfs
        // record with that same rendered signature lack a named-style link (xfId 0/absent)? If so,
        // the shared target xf cannot be safely attributed to the named style alone, so skip it.
        var plainSourceSignatures = new HashSet<string>(StringComparer.Ordinal);
        foreach (var sourceXf in sourceCellXfs)
        {
            if (TryGetIntAttribute(sourceXf, "xfId", out var xfId) && xfId != 0)
                continue;

            plainSourceSignatures.Add(
                BuildXfStyleSignature(sourceXf, sourceFonts, sourceFills, sourceBorders, sourceNumFmts, workbookNs));
        }

        // Cross-style ambiguity guard: two DIFFERENT named styles (distinct source xfIds, therefore
        // distinct recovered target named-style xfIds) can also resolve to byte-identical rendered
        // formatting -- e.g. one custom style duplicated under a new name. When that happens, ClosedXML's
        // rebuild style cache collapses every cell bound to either style onto the SAME shared target
        // <xf>, so there is only one candidate to claim for a signature that in the source legitimately
        // belongs to more than one named style. Blindly letting the first-processed source xf claim it
        // (the prior dequeue-only behavior) would silently mislabel every cell bound to the other style
        // with the first style's name. Since a single shared target record cannot correctly represent two
        // different named styles at once, treat such signatures as unresolvable and leave them alone
        // (same "don't guess" outcome as the plain-collision guard above), rather than reconnect them to
        // whichever named style happened to be processed first.
        var namedStyleTargetIdsBySignature = new Dictionary<string, HashSet<int>>(StringComparer.Ordinal);
        foreach (var sourceXf in sourceCellXfs)
        {
            if (!TryGetIntAttribute(sourceXf, "xfId", out var namedXfId) ||
                namedXfId == 0 ||
                !sourceXfIdToTargetXfId.TryGetValue(namedXfId, out var candidateTargetXfId))
            {
                continue;
            }

            var namedSignature = BuildXfStyleSignature(sourceXf, sourceFonts, sourceFills, sourceBorders, sourceNumFmts, workbookNs);
            if (!namedStyleTargetIdsBySignature.TryGetValue(namedSignature, out var targetIds))
                namedStyleTargetIdsBySignature[namedSignature] = targetIds = new HashSet<int>();
            targetIds.Add(candidateTargetXfId);
        }

        var ambiguousNamedStyleSignatures = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (signatureKey, targetIds) in namedStyleTargetIdsBySignature)
        {
            if (targetIds.Count > 1)
                ambiguousNamedStyleSignatures.Add(signatureKey);
        }

        foreach (var sourceXf in sourceCellXfs)
        {
            if (!TryGetIntAttribute(sourceXf, "xfId", out var sourceXfId) ||
                sourceXfId == 0 ||
                !sourceXfIdToTargetXfId.TryGetValue(sourceXfId, out var targetNamedStyleXfId))
            {
                continue;
            }

            var signature = BuildXfStyleSignature(sourceXf, sourceFonts, sourceFills, sourceBorders, sourceNumFmts, workbookNs);
            if (plainSourceSignatures.Contains(signature) || ambiguousNamedStyleSignatures.Contains(signature))
                continue;

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
        // R94-named-style-xfId: font/fill get the same default-normalization treatment as
        // border/alignment/protection below, via BuildXfFontSignatureXml/BuildXfFillSignatureXml
        // rather than a raw indexed-record XML comparison. ClosedXML's own rebuilt font ALWAYS
        // writes an explicit <vertAlign val="baseline"/> and a fixed self-closing <b/>/<i/>/...
        // toggle form, in a fixed element order that is NOT the ECMA-376 schema sequence, while a
        // real Excel-authored (or third-party) source file typically omits vertAlign entirely when
        // it is the default, may write toggle attributes as val="1"/"true" instead of a bare
        // element, and orders/omits <scheme>/<charset> (which ClosedXML's rebuild never carries
        // through at all) differently. Comparing that raw XML directly made the signature mismatch
        // for virtually every real font, for the same reason the R93 alignment/protection/border
        // gap did -- see BuildXfFontSignatureXml for the exact default-filling applied.
        var fontXml = BuildXfFontSignatureXml(xf, fontsList, workbookNs);
        var fillXml = BuildXfFillSignatureXml(xf, fillsList, workbookNs);
        var borderXml = BuildXfBorderSignatureXml(xf, bordersList, workbookNs);
        var numFmtKey = ResolveNumFmtSignatureKey(xf, numFmtsList, workbookNs);

        // R53-io-cellstyle-named-3-2: include alignment/protection so two named cell styles that
        // differ only by those (e.g. two report-header styles distinguished purely by horizontal
        // alignment) don't collapse onto the same signature and get treated as an unresolvable
        // cross-style collision (see ambiguousNamedStyleSignatures below) even though the rebuild
        // legitimately produced two distinguishable target xfs for them.
        //
        // R93-named-style-xfId: a source xf that never declares <alignment>/<protection> at all
        // (the overwhelming common case -- only a cell with actual custom alignment/protection
        // ever writes one) means "every attribute at its ECMA-376 default", while ClosedXML's own
        // rebuilt xf ALWAYS bakes an explicit, fully-attributed <alignment>/<protection> even for
        // a cell that never asked for one. Comparing the raw XML directly (absent vs. fully
        // spelled-out defaults) made this signature mismatch for virtually every real cellXfs
        // entry, so a named style's cellXfs link almost never actually reconnected after a real
        // full-rebuild save. Normalizing both sides to the same explicit-defaults form (below)
        // fixes that without weakening the disambiguation R53 added this pair for in the first
        // place (a cell that DOES declare a non-default alignment/protection still compares by
        // its real values, same as before).
        var alignmentXml = BuildNamespaceAgnosticXml(NormalizeAlignmentForSignature(xf.Element(workbookNs + "alignment"), workbookNs + "alignment"));
        var protectionXml = BuildNamespaceAgnosticXml(NormalizeProtectionForSignature(xf.Element(workbookNs + "protection"), workbookNs + "protection"));
        return string.Join(SignatureSeparator, fontXml, fillXml, borderXml, numFmtKey, alignmentXml, protectionXml);
    }

    // ECMA-376 CT_CellAlignment attribute defaults (Part 1, §18.8.1) -- an <alignment> element
    // that omits one of these means that attribute takes exactly this value, identical in effect
    // to an xf that omits <alignment> entirely (every attribute at its default).
    private static readonly (string Name, string Default)[] AlignmentAttributeDefaults =
    [
        ("horizontal", "general"),
        ("vertical", "bottom"),
        ("textRotation", "0"),
        ("wrapText", "0"),
        ("indent", "0"),
        ("relativeIndent", "0"),
        ("justifyLastLine", "0"),
        ("shrinkToFit", "0"),
        ("readingOrder", "0"),
    ];

    // ECMA-376 CT_CellProtection attribute defaults (Part 1, §18.8.33): locked defaults to true.
    private static readonly (string Name, string Default)[] ProtectionAttributeDefaults =
    [
        ("locked", "1"),
        ("hidden", "0"),
    ];

    private static XElement NormalizeAlignmentForSignature(XElement? alignment, XName elementName)
    {
        var normalized = new XElement(elementName);
        foreach (var (name, defaultValue) in AlignmentAttributeDefaults)
        {
            normalized.SetAttributeValue(
                name,
                NormalizeOoxmlBooleanLikeValue(alignment?.Attribute(name)?.Value) ?? defaultValue);
        }

        return normalized;
    }

    private static XElement NormalizeProtectionForSignature(XElement? protection, XName elementName)
    {
        var normalized = new XElement(elementName);
        foreach (var (name, defaultValue) in ProtectionAttributeDefaults)
        {
            normalized.SetAttributeValue(
                name,
                NormalizeOoxmlBooleanLikeValue(protection?.Attribute(name)?.Value) ?? defaultValue);
        }

        return normalized;
    }

    // OOXML's xsd:boolean permits both "0"/"1" and "false"/"true" for the same value; ClosedXML
    // and a hand/third-party-authored source file are not guaranteed to agree on which spelling
    // they use, so canonicalize to "0"/"1" before comparing. Non-boolean attribute values (e.g.
    // "general" for horizontal) pass through unchanged.
    private static string? NormalizeOoxmlBooleanLikeValue(string? raw) => raw switch
    {
        null => null,
        "1" or "true" => "1",
        "0" or "false" => "0",
        _ => raw
    };

    // CT_Border side elements (left/right/top/bottom/diagonal/vertical/horizontal) default their
    // own "style" attribute to "none" (ECMA-376 Part 1, §18.8.4) when omitted -- and a side with
    // style="none" renders no border regardless of what (if anything) its <color> child says, so
    // that color is immaterial to two borders rendering identically and must not be compared when
    // style is (or defaults to) "none". <border>'s own diagonalUp/diagonalDown attributes default
    // to "0" per the same clause.
    private static readonly string[] BorderSideNames = ["left", "right", "top", "bottom", "diagonal", "vertical", "horizontal"];

    private static string BuildXfBorderSignatureXml(XElement xf, XElement? bordersList, XNamespace workbookNs)
    {
        if (bordersList is null || !TryGetIntAttribute(xf, "borderId", out var index))
            return string.Empty;

        var items = bordersList.Elements(workbookNs + "border").ToList();
        if (index < 0 || index >= items.Count)
            return string.Empty;

        var source = items[index];
        var normalized = new XElement(
            workbookNs + "border",
            new XAttribute("diagonalUp", NormalizeOoxmlBooleanLikeValue(source.Attribute("diagonalUp")?.Value) ?? "0"),
            new XAttribute("diagonalDown", NormalizeOoxmlBooleanLikeValue(source.Attribute("diagonalDown")?.Value) ?? "0"));

        foreach (var sideName in BorderSideNames)
        {
            var side = source.Element(workbookNs + sideName);
            var style = side?.Attribute("style")?.Value;
            style = string.IsNullOrEmpty(style) ? "none" : style;

            var normalizedSide = new XElement(workbookNs + sideName, new XAttribute("style", style));
            if (!string.Equals(style, "none", StringComparison.OrdinalIgnoreCase))
            {
                var color = side?.Element(workbookNs + "color");
                if (color is not null)
                    normalizedSide.Add(new XElement(color));
            }

            normalized.Add(normalizedSide);
        }

        return BuildNamespaceAgnosticXml(normalized);
    }

    // R94-named-style-xfId: CT_BooleanProperty toggle children (ECMA-376 Part 1, §18.8.2/§18.8.20/
    // §18.8.24/§18.8.36/§18.8.44/§18.8.13/§18.8.14 for extend/condense/outline/shadow/strike/b/i
    // respectively) all share the same semantics -- the element's mere presence with no "val" means
    // "on" (default true), an explicit val="0"/"false" means "off", and OMITTING the element
    // entirely also means "off". So a bare source `<b/>` and ClosedXML's own rebuilt `<b/>` already
    // agree, but a source `<b val="1"/>` (equally schema-legal) would not raw-string-match it. Both
    // the "off via omission" and "off via explicit val=0" forms collapse to the SAME omitted-element
    // normalized shape, since they render identically.
    private static readonly string[] FontToggleElementNames = ["b", "condense", "extend", "i", "outline", "shadow", "strike"];

    private static XElement NormalizeFontForSignature(XElement font, XNamespace workbookNs)
    {
        var normalized = new XElement(workbookNs + "font");

        foreach (var toggleName in FontToggleElementNames)
        {
            var element = font.Element(workbookNs + toggleName);
            if (element is null)
                continue;

            var isOn = NormalizeOoxmlBooleanLikeValue(element.Attribute("val")?.Value) ?? "1";
            if (isOn == "1")
                normalized.Add(new XElement(workbookNs + toggleName));
        }

        // CT_UnderlineProperty (§18.8.3): val is an enum (single/double/singleAccounting/
        // doubleAccounting/none), not a plain boolean, and defaults to "single" when the element is
        // present without val. val="none" renders no underline, same as the element being absent
        // entirely, so both normalize to "no <u> element" rather than comparing "none" against absence.
        var underline = font.Element(workbookNs + "u");
        if (underline is not null)
        {
            var underlineVal = underline.Attribute("val")?.Value;
            underlineVal = string.IsNullOrEmpty(underlineVal) ? "single" : underlineVal;
            if (!string.Equals(underlineVal, "none", StringComparison.OrdinalIgnoreCase))
                normalized.Add(new XElement(workbookNs + "u", new XAttribute("val", underlineVal)));
        }

        // CT_VerticalAlignFontProperty (§18.8.42): the ENTIRE <vertAlign> element is optional and its
        // absence means "baseline" (neither super- nor subscript) -- but ClosedXML's own rebuild
        // ALWAYS emits an explicit <vertAlign val="baseline"/> even for a plain font that never asked
        // for one, exactly the same "absent vs. fully-spelled-out default" mismatch R93 fixed for
        // <alignment>/<protection>.
        var vertAlign = font.Element(workbookNs + "vertAlign")?.Attribute("val")?.Value;
        normalized.Add(new XElement(workbookNs + "vertAlign",
            new XAttribute("val", string.IsNullOrEmpty(vertAlign) ? "baseline" : vertAlign)));

        // sz (§18.8.38) has no ECMA-376 default -- two fonts with different (or missing) sz are NOT
        // renders-equivalent, so it is carried through verbatim (present or absent) rather than defaulted.
        var sz = font.Element(workbookNs + "sz")?.Attribute("val")?.Value;
        if (!string.IsNullOrEmpty(sz))
            normalized.Add(new XElement(workbookNs + "sz", new XAttribute("val", sz)));

        // color (§18.8.3 CT_Color) has no single schema default that lets an absent color compare
        // equal to some concrete rgb/theme/indexed value without resolving the theme palette, which
        // this signature has no access to -- so it is compared structurally as-is (present-and-equal,
        // or both-absent), same residual scope as R93 left for numFmt/alignment colour-adjacent cases.
        var color = font.Element(workbookNs + "color");
        if (color is not null)
            normalized.Add(new XElement(color));

        // name (§18.8.29) has no default and must match exactly -- two different font families are
        // never renders-equivalent.
        var name = font.Element(workbookNs + "name")?.Attribute("val")?.Value;
        if (!string.IsNullOrEmpty(name))
            normalized.Add(new XElement(workbookNs + "name", new XAttribute("val", name)));

        // family (§18.8.18 CT_IntProperty): 0 ("Not applicable") is the conventional value when a
        // font's family classification is unknown/unset, which is exactly what an absent <family>
        // element also means -- default both to "0" so one representation doesn't spuriously differ
        // from the other.
        var family = font.Element(workbookNs + "family")?.Attribute("val")?.Value;
        normalized.Add(new XElement(workbookNs + "family",
            new XAttribute("val", string.IsNullOrEmpty(family) ? "0" : family)));

        // charset (§18.8.5) and scheme (§18.8.35) are intentionally OMITTED from the signature:
        // ClosedXML's own rebuilt font never carries either through (confirmed empirically -- its
        // rebuild output for every font, regardless of source, is exactly <vertAlign>/<sz>/<color>/
        // <name>/<family>, no <charset> or <scheme>), so comparing them would never let a source font
        // that legitimately uses one (e.g. Excel's own default-font <scheme val="minor"/>) match its
        // rebuilt counterpart -- the opposite of what this normalization exists to fix.
        return normalized;
    }

    private static string BuildXfFontSignatureXml(XElement xf, XElement? fontsList, XNamespace workbookNs)
    {
        if (fontsList is null || !TryGetIntAttribute(xf, "fontId", out var index))
            return string.Empty;

        var items = fontsList.Elements(workbookNs + "font").ToList();
        if (index < 0 || index >= items.Count)
            return string.Empty;

        return BuildNamespaceAgnosticXml(NormalizeFontForSignature(items[index], workbookNs));
    }

    // CT_PatternFill (§18.8.32): patternType defaults to "none" when omitted, and a "none" pattern
    // renders no fill colour at all regardless of what (if anything) fgColor/bgColor say -- the same
    // "colour immaterial when the pattern/style defaults away" rule BuildXfBorderSignatureXml already
    // applies to border sides.
    private static XElement NormalizeFillForSignature(XElement fill, XNamespace workbookNs)
    {
        var normalized = new XElement(workbookNs + "fill");

        var patternFill = fill.Element(workbookNs + "patternFill");
        if (patternFill is null)
        {
            // Not a pattern fill (e.g. a gradientFill) -- out of scope for this normalization; carry
            // the child through as-is so gradient fills still compare (in)equal exactly as before.
            var gradientFill = fill.Element(workbookNs + "gradientFill");
            if (gradientFill is not null)
                normalized.Add(new XElement(gradientFill));
            return normalized;
        }

        var patternType = patternFill.Attribute("patternType")?.Value;
        patternType = string.IsNullOrEmpty(patternType) ? "none" : patternType;

        var normalizedPattern = new XElement(workbookNs + "patternFill", new XAttribute("patternType", patternType));
        if (!string.Equals(patternType, "none", StringComparison.OrdinalIgnoreCase))
        {
            var fgColor = patternFill.Element(workbookNs + "fgColor");
            if (fgColor is not null)
                normalizedPattern.Add(new XElement(fgColor));

            var bgColor = patternFill.Element(workbookNs + "bgColor");
            if (bgColor is not null)
                normalizedPattern.Add(new XElement(bgColor));
        }

        normalized.Add(normalizedPattern);
        return normalized;
    }

    private static string BuildXfFillSignatureXml(XElement xf, XElement? fillsList, XNamespace workbookNs)
    {
        if (fillsList is null || !TryGetIntAttribute(xf, "fillId", out var index))
            return string.Empty;

        var items = fillsList.Elements(workbookNs + "fill").ToList();
        if (index < 0 || index >= items.Count)
            return string.Empty;

        return BuildNamespaceAgnosticXml(NormalizeFillForSignature(items[index], workbookNs));
    }

    // Real Excel-authored styles.xml uses the default (unprefixed) spreadsheetml namespace, while
    // ClosedXML's own SaveAs() output always uses an explicit "x:" prefix on every element (e.g.
    // <x:styleSheet xmlns:x="..."><x:fonts><x:font>...). XElement.ToString() reproduces whichever
    // prefix was in scope when the element was parsed, so a raw string compare between a source
    // element and its rebuilt ClosedXML counterpart never matches even when they render identically.
    // Strip every element/attribute down to its local name (dropping namespace URIs and prefixes
    // entirely, and any xmlns declarations) before serializing, so the comparison is namespace-agnostic
    // and depends only on element/attribute names, values, and structure.
    private static string BuildNamespaceAgnosticXml(XElement? element) =>
        element is null ? string.Empty : NormalizeElementForSignature(element).ToString(SaveOptions.DisableFormatting);

    private static XElement NormalizeElementForSignature(XElement element)
    {
        var normalized = new XElement(element.Name.LocalName);
        foreach (var attribute in element.Attributes())
        {
            if (attribute.IsNamespaceDeclaration)
                continue;
            normalized.SetAttributeValue(attribute.Name.LocalName, attribute.Value);
        }

        foreach (var child in element.Elements())
            normalized.Add(NormalizeElementForSignature(child));

        return normalized;
    }

    private static string ResolveNumFmtSignatureKey(XElement xf, XElement? numFmtsList, XNamespace workbookNs)
    {
        if (!TryGetIntAttribute(xf, "numFmtId", out var numFmtId))
            return string.Empty;

        // R53-io-cellstyle-named-3-1: resolve BOTH builtin (<164) and custom (>=164) numFmtIds to
        // their format-code text, instead of returning the raw builtin id for one side and the
        // resolved code for the other. FreeX's own save path re-canonicalizes any format string that
        // matches a builtin catalog entry to its builtin numFmtId (XlsxClosedXmlCellMapper.ApplyStyle),
        // so a source file's custom numFmtId (>=164) for a code like "0%" and the rebuilt target's
        // canonicalized builtin numFmtId (9) for the identical code must compare equal here, or a
        // named cell style bound to that format silently loses its reconnect on save.
        if (numFmtId < 164)
        {
            return FreeX.Core.Model.BuiltInNumberFormatCatalog.TryResolveFormatCode(numFmtId, out var builtinFormatCode)
                ? builtinFormatCode
                : numFmtId.ToString(CultureInfo.InvariantCulture);
        }

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
        var sourceItemXml = BuildNamespaceAgnosticXml(sourceItem);

        var targetList = targetRoot.Element(workbookNs + listElementName);
        if (targetList is null)
            return; // fonts/fills/borders always exist in a rebuilt stylesheet; defensively skip if not

        var targetItems = targetList.Elements(workbookNs + itemElementName).ToList();
        var targetIndex = -1;
        for (var i = 0; i < targetItems.Count; i++)
        {
            if (string.Equals(BuildNamespaceAgnosticXml(targetItems[i]), sourceItemXml, StringComparison.Ordinal))
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
