using System.IO;
using System.IO.Compression;
using System.Xml.Linq;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

public sealed partial class XlsxFileAdapter
{
    private static void ApplyPackagePostProcessing(
        Workbook workbook,
        Stream packageStream,
        string? currentModelFingerprint = null,
        bool removeSourceCalcChain = false)
    {
        var featurePlan = XlsxPostProcessingFeaturePlan.Create(workbook);
        XlsxWorkbookWorksheetPathMap? worksheetPathMap = null;
        XlsxWorkbookWorksheetPathMap? GetWorksheetPathMap()
        {
            if (worksheetPathMap is not null)
                return worksheetPathMap;

            packageStream.Position = 0;
            using var archive = new ZipArchive(packageStream, ZipArchiveMode.Read, leaveOpen: true);
            worksheetPathMap = XlsxWorkbookWorksheetPathMap.TryCreate(archive);
            return worksheetPathMap;
        }

        if (featurePlan.HasWorkbookPostProcessingMetadata)
        {
            packageStream.Position = 0;
            XlsxWorkbookMetadataWriter.SavePostProcessingMetadata(packageStream, workbook);
        }

        if (workbook.NamedRanges.Count > 0 ||
            workbook.NamedFormulas.Count > 0 ||
            workbook.ScopedNamedRanges.Count > 0 ||
            workbook.ScopedNamedFormulas.Count > 0)
        {
            packageStream.Position = 0;
            XlsxNamedRangeMapper.SaveToPackage(workbook, packageStream);
        }

        if (featurePlan.HasNonDefaultDimensions)
        {
            packageStream.Position = 0;
            XlsxWorksheetDimensionDefaultsWriter.Save(packageStream, workbook, GetWorksheetPathMap());
        }

        // ClosedXML's Column.Width setter inflates the stored width (e.g. 2.0 -> 2.71) on save; rewrite
        // each worksheet's <cols> with the modelled exact widths so column widths round-trip.
        if (featurePlan.HasColumnWidths)
        {
            packageStream.Position = 0;
            XlsxWorksheetColumnWidthWriter.Save(packageStream, workbook);
        }

        if (featurePlan.HasStyleOnlyCells)
        {
            packageStream.Position = 0;
            XlsxStyleOnlyCellWriter.Save(packageStream, workbook, GetWorksheetPathMap());
        }

        // BX1: ClosedXML emits <color rgb="00000000"/> (transparent black) as a sentinel for
        // CellRunColorKind.Auto, which it cannot express as <color auto="1"/>.  Rewrite the
        // shared-strings part so every rgb="00000000" becomes auto="1", restoring correct
        // round-trip semantics.  Transparent black (alpha=0) is never written by Excel for a
        // real color, so this substitution is safe and unambiguous.
        if (featurePlan.HasRichAutoColorRuns)
        {
            packageStream.Position = 0;
            FixRichAutoColorRunsInSharedStrings(packageStream);
        }

        if (featurePlan.HasFullCalculationOnLoad)
        {
            packageStream.Position = 0;
            XlsxWorksheetCalculationPropertyMapper.Save(packageStream, workbook);
        }

        if (featurePlan.HasModeledPrinterAttributes)
        {
            packageStream.Position = 0;
            XlsxWorksheetPageSetupMetadataWriter.Save(packageStream, workbook, GetWorksheetPathMap());
        }

        if (featurePlan.HasPhoneticProperties)
        {
            packageStream.Position = 0;
            XlsxWorksheetPhoneticPropertyMapper.Save(packageStream, workbook);
        }

        if (featurePlan.HasAllowEditRanges)
        {
            packageStream.Position = 0;
            XlsxAllowEditRangeMapper.Save(packageStream, workbook);
        }

        if (featurePlan.HasAdvancedConditionalFormats)
        {
            packageStream.Position = 0;
            XlsxAdvancedConditionalFormatWriter.Save(packageStream, workbook, GetWorksheetPathMap());
        }

        if (featurePlan.HasX14DataValidations)
        {
            packageStream.Position = 0;
            XlsxX14DataValidationWriter.Save(packageStream, workbook);
        }

        if (featurePlan.HasSparklines)
        {
            packageStream.Position = 0;
            XlsxSparklineMapper.Save(packageStream, workbook);
        }

        if (featurePlan.HasThreadedComments)
        {
            packageStream.Position = 0;
            XlsxWorksheetThreadedCommentMapper.Save(packageStream, workbook, GetWorksheetPathMap());
        }

        if (featurePlan.HasBackgroundImages)
        {
            packageStream.Position = 0;
            XlsxWorksheetBackgroundReaderWriter.Save(packageStream, workbook);
        }

        if (featurePlan.HasHeaderFooterPictures)
        {
            IReadOnlySet<string>? sheetsToPreserve = null;
            if (SourcePackages.TryGetValue(workbook, out var headerFooterSourcePackage))
            {
                using var sourceStream = headerFooterSourcePackage.OpenRead();
                sheetsToPreserve = XlsxHeaderFooterPictureReaderWriter.FindSheetsWithUnchangedSourcePictures(
                    sourceStream,
                    workbook);
            }

            packageStream.Position = 0;
            XlsxHeaderFooterPictureReaderWriter.Save(packageStream, workbook, sheetsToPreserve);
        }

        if (featurePlan.HasPersistableViewState)
        {
            packageStream.Position = 0;
            XlsxWorksheetViewWriter.Save(packageStream, workbook, GetWorksheetPathMap());
        }

        if (featurePlan.HasCodeNames)
        {
            packageStream.Position = 0;
            XlsxWorksheetCodeNameWriter.Save(packageStream, workbook);
        }

        if (featurePlan.HasIgnoredFormulaErrors)
        {
            packageStream.Position = 0;
            XlsxWorksheetDiagnosticsMapper.SaveIgnoredErrors(packageStream, workbook, GetWorksheetPathMap());
        }

        // Persist cached values onto formula cells so FreeX's own ClosedXML reload never recomputes
        // them. ClosedXML's calc engine cannot evaluate modern dynamic-array functions (throws
        // "Array formulas not implemented") and produces spurious cycle errors on incomplete caches —
        // see XlsxWorksheetFormulaCachedValueWriter. (This call handles the no-source-package /
        // ClosedXML-authored path; the source-package path re-applies after part preservation.)
        if (featurePlan.HasCellFormulas)
        {
            packageStream.Position = 0;
            XlsxWorksheetFormulaCachedValueWriter.Save(packageStream, workbook, GetWorksheetPathMap());
        }

        if (workbook.WatchedCells.Count > 0)
        {
            packageStream.Position = 0;
            XlsxWorksheetDiagnosticsMapper.SaveCellWatches(packageStream, workbook);
        }

        if (workbook.Scenarios.Count > 0)
        {
            packageStream.Position = 0;
            XlsxWorksheetScenarioMapper.Save(packageStream, workbook);
        }

        if (workbook.CustomViews.Count > 0)
        {
            packageStream.Position = 0;
            XlsxCustomViewMapper.Save(packageStream, workbook);
        }

        if (featurePlan.HasCustomProperties)
        {
            packageStream.Position = 0;
            XlsxWorksheetCustomPropertyMapper.Save(packageStream, workbook);
        }

        if (featurePlan.HasWorksheetElementMetadata)
        {
            packageStream.Position = 0;
            XlsxWorksheetPostProcessingMetadataBatchWriter.SaveWorksheetElementMetadata(
                packageStream,
                workbook,
                GetWorksheetPathMap());
        }

        packageStream.Position = 0;
        XlsxWorkbookThemeWriter.Save(packageStream, workbook.Theme);

        if (workbook.IndexedColors.Colors.Count > 0)
        {
            packageStream.Position = 0;
            XlsxIndexedColorPaletteMapper.Save(packageStream, workbook);
        }

        if (featurePlan.HasSupportedCharts)
        {
            packageStream.Position = 0;
            XlsxWorksheetChartWriter.Save(
                packageStream,
                workbook,
                XlsxChartXmlWriter.IsSupportedXlsxChart,
                XlsxChartXmlWriter.ToChartXml,
                XlsxChartXmlWriter.GetContentType,
                XlsxChartXmlWriter.GetRelationshipType,
                GetSourceDrawingPathsBySheet(workbook));
        }

        if (featurePlan.HasSupportedDrawingObjects)
        {
            packageStream.Position = 0;
            XlsxWorksheetDrawingObjectWriter.Save(
                packageStream,
                workbook,
                GetSourceDrawingPathsBySheet(workbook),
                startPictureIndex: GetSourceMaxPictureIndex(workbook) + 1);
        }

        if (featurePlan.HasStructuredTables)
        {
            packageStream.Position = 0;
            XlsxStructuredTableWriter.Save(packageStream, workbook);
        }

        if (workbook.PivotTableStyles.Count > 0)
        {
            packageStream.Position = 0;
            XlsxSlicerTimelineWriter.SavePivotTableStyles(packageStream, workbook);
        }

        if (workbook.StructuredTableStyles.Count > 0)
        {
            packageStream.Position = 0;
            XlsxStructuredTableStyleMetadataWriter.Save(packageStream, workbook);
        }

        IReadOnlyDictionary<int, int> numberFormatIdMap = new Dictionary<int, int>();
        if (workbook.NumberFormatCatalog.Count > 0 ||
            featurePlan.HasPivotCustomNumberFormats)
        {
            packageStream.Position = 0;
            numberFormatIdMap = XlsxNumberFormatCatalogWriter.Save(packageStream, workbook);
        }

        var hasSourcePackage = SourcePackages.TryGetValue(workbook, out var sourcePackage);
        if (!hasSourcePackage &&
            workbook.PivotCaches.Count > 0 &&
            featurePlan.HasPivotTables)
        {
            packageStream.Position = 0;
            XlsxPivotTableWriter.Save(packageStream, workbook, numberFormatIdMap);
        }

        if (!hasSourcePackage &&
            (workbook.Slicers.Count > 0 || workbook.Timelines.Count > 0))
        {
            packageStream.Position = 0;
            XlsxSlicerTimelineWriter.SaveSlicerTimelines(packageStream, workbook);
        }

        // Normalize VML <x:Visible/> for fresh (no source package) workbooks so that
        // ClosedXML's generated VML correctly reflects the ShownComments model state.
        // Must run before the early-return path below; has no effect when hasSourcePackage is true
        // because the Preserve() path handles that case via ApplyVisibleFlag.
        if (!hasSourcePackage && featurePlan.HasLegacyNotes)
        {
            packageStream.Position = 0;
            XlsxLegacyCommentVisibilityNormalizer.NormalizePackage(packageStream, workbook);
        }

        if (!hasSourcePackage)
        {
            SaveSourcePackageIndependentPostProcessingMetadata();
            NormalizeStylesheetForSchema();
            NormalizeDocumentPropertiesPackageGraph();
            NormalizeWorkbookForSchema();
            return;
        }

        packageStream.Position = 0;
        var sourceParts = PreserveSourcePackageParts(workbook, packageStream);

        // Re-apply x14 data validations after source-part preservation. The source package
        // restores the original worksheet XML (which may carry an x14 DV extLst block); we
        // overwrite it with the current model state so edits to x14 DV rules survive save.
        if (featurePlan.HasX14DataValidations)
        {
            packageStream.Position = 0;
            XlsxX14DataValidationWriter.Save(packageStream, workbook);
        }

        // Re-apply after source-part preservation: PreserveSourcePackageParts restores Excel's
        // original worksheet XML, which can carry formulas (notably dynamic arrays stored as
        // <f t="array" ca="1">) WITHOUT a cached <v>. FreeX's own ClosedXML reload would then
        // recompute and throw. Inject the cached value the model holds so the reload never recomputes.
        // (The earlier pre-preservation call handles the no-source-package / ClosedXML-authored path.)
        if (featurePlan.HasCellFormulas)
        {
            packageStream.Position = 0;
            XlsxWorksheetFormulaCachedValueWriter.Save(packageStream, workbook, GetWorksheetPathMap());
        }

        // F15: source-loaded drawing objects (pictures/shapes/text boxes originally loaded from the
        // .xlsx) are never emitted by XlsxWorksheetDrawingObjectWriter — it gates every object behind
        // !IsSourceLoaded — so their drawing part above was just PRESERVED verbatim (copied from the
        // source package), replaying the ORIGINAL anchor geometry. Rewrite that copied part's anchors
        // in place so a resize/move applied to the in-memory model (Width/Height/AnchorOffsetX/Y) is
        // not silently discarded. Must run after PreserveSourcePackageParts (the part must already be
        // at its final path) and is a no-op when no sheet has a source-loaded drawing object.
        if (featurePlan.HasSourceLoadedDrawingObjects)
        {
            packageStream.Position = 0;
            XlsxSourceDrawingGeometryRewriter.Save(packageStream, workbook, GetWorksheetPathMap());
        }

        // P7: slicer/timeline selection/range/level lives in preserved native parts. PreserveSourcePackageParts
        // restored the ORIGINAL slicer/timeline/slicerCache/timelineCache XML, replaying the original selection
        // state; rewrite ONLY those selection/range/level values in place from the model so an in-app change to a
        // slicer's selected items or a timeline's selected range/level survives save. Must run after
        // PreserveSourcePackageParts (the parts must already be at their final path) and is a strict no-op when a
        // control's model state is empty and the preserved part carries none — keeping selection-free source
        // slicer/timeline parts byte-stable. It never re-emits or reorders parts, so the critical package graph
        // is untouched.
        if (XlsxSlicerTimelineStateRewriter.HasSlicerTimelineState(workbook))
        {
            packageStream.Position = 0;
            XlsxSlicerTimelineStateRewriter.Save(packageStream, workbook);
        }

        if (sourceParts.HasDrawings)
        {
            packageStream.Position = 0;
            XlsxHeaderFooterPictureReaderWriter.RemoveClearedPictures(packageStream, workbook);
        }

        if (workbook.IndexedColors.Colors.Count > 0)
        {
            packageStream.Position = 0;
            XlsxIndexedColorPaletteMapper.Save(packageStream, workbook);
        }

        if (featurePlan.HasWorkbookReplayMetadata)
        {
            packageStream.Position = 0;
            XlsxWorkbookMetadataWriter.SaveSourcePackageReplayMetadata(packageStream, workbook);
        }

        SaveSourcePackageIndependentPostProcessingMetadata();

        if (featurePlan.HasReplayMetadata)
        {
            packageStream.Position = 0;
            XlsxWorksheetPostProcessingMetadataBatchWriter.Save(packageStream, workbook, GetWorksheetPathMap());
        }

        if (featurePlan.HasThreadedComments)
        {
            packageStream.Position = 0;
            XlsxWorksheetThreadedCommentMapper.NormalizePackageGraph(packageStream, workbook, GetWorksheetPathMap());
        }

        if (numberFormatIdMap.Any(pair => pair.Key != pair.Value))
        {
            packageStream.Position = 0;
            XlsxNumberFormatCatalogWriter.RemapPivotTableNumberFormats(packageStream, numberFormatIdMap);
        }

        NormalizeStylesheetForSchema();
        NormalizeSourcePackageForExcelCompatibility();
        NormalizeDocumentPropertiesPackageGraph();
        NormalizeWorkbookForSchema();

        packageStream.Position = 0;
        SourcePackages.Remove(workbook);
        SourcePackages.Add(workbook, XlsxSourcePackage.Capture(
            packageStream,
            workbook,
            currentModelFingerprint,
            sourcePackage?.WorksheetsWithPreservableSourceMetadata,
            sourcePackage?.HasUnsupportedConditionalFormatting) with
            {
                SourceNeedsPackageGraphNormalization = false
            });

        void SaveSourcePackageIndependentPostProcessingMetadata()
        {
            if (featurePlan.HasSourceIndependentMetadata)
            {
                packageStream.Position = 0;
                XlsxWorksheetSourceIndependentMetadataBatchWriter.Save(packageStream, workbook, GetWorksheetPathMap());
            }
        }

        void NormalizeStylesheetForSchema()
        {
            packageStream.Position = 0;
            XlsxStylesheetSchemaNormalizer.Normalize(packageStream);
        }

        void NormalizeSourcePackageForExcelCompatibility()
        {
            packageStream.Position = 0;
            var normalizationPlan =
                CreateExcelCompatibilityNormalizationPlan(sourcePackage, sourceParts, featurePlan) with
                {
                    RemoveCalcChain = removeSourceCalcChain
                };
            XlsxExcelCompatibilityNormalizer.NormalizeSourcePackageSave(
                packageStream,
                normalizationPlan);
        }

        void NormalizeDocumentPropertiesPackageGraph()
        {
            packageStream.Position = 0;
            XlsxDocumentPropertiesPreserver.NormalizePackageGraph(packageStream);
        }

        void NormalizeWorkbookForSchema()
        {
            packageStream.Position = 0;
            XlsxWorkbookSchemaNormalizer.Normalize(packageStream);
            packageStream.Position = 0;
            XlsxDrawingSchemaNormalizer.NormalizePackage(packageStream);
        }
    }

    private static XlsxExcelCompatibilityNormalizationPlan CreateExcelCompatibilityNormalizationPlan(
        XlsxSourcePackage? sourcePackage,
        SourcePackagePartSummary sourceParts,
        XlsxPostProcessingFeaturePlan featurePlan)
    {
        var shouldScanSourceWorksheetMetadata =
            sourcePackage?.WorksheetsWithPreservableSourceMetadata is null ||
            sourcePackage.WorksheetsWithPreservableSourceMetadata.Count > 0;

        return new XlsxExcelCompatibilityNormalizationPlan(
            ScanWorksheetCustomViews: shouldScanSourceWorksheetMetadata || featurePlan.HasCustomViews,
            ScanWorksheetFormulaText: featurePlan.HasCellFormulas,
            ScanWorksheetDrawingTargets:
                sourceParts.HasDrawings ||
                featurePlan.HasSupportedCharts ||
                featurePlan.HasSupportedDrawingObjects);
    }

    // Maps each source-package sheet to the drawing part it owns. The rebuilt chart writer reuses a chart
    // sheet's own drawing part (so its charts stay on that sheet) and avoids every other sheet's drawing,
    // which the source preservation restores at its original path. Without this, a chart sheet's rebuilt
    // drawing could claim the part name another sheet's source drawing owns and steal its charts.
    private static IReadOnlyDictionary<string, string> GetSourceDrawingPathsBySheet(Workbook workbook)
    {
        if (!SourcePackages.TryGetValue(workbook, out var sourcePackage))
            return EmptyDrawingPathsBySheet;

        try
        {
            using var sourceStream = sourcePackage.OpenRead();
            using var sourceArchive = new ZipArchive(sourceStream, ZipArchiveMode.Read);

            XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
            XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

            var workbookEntry = sourceArchive.GetEntry("xl/workbook.xml");
            if (workbookEntry is null)
                return EmptyDrawingPathsBySheet;

            var workbookXml = XlsxPackageXmlEditor.LoadXml(workbookEntry);
            var workbookRels = XlsxRelationshipReader.LoadTargets(
                sourceArchive, "xl/_rels/workbook.xml.rels", "xl/workbook.xml", packageRelNs);

            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (sheetName, worksheetPath) in
                     XlsxWorkbookSheetPathReader.GetWorkbookSheetPaths(workbookXml, workbookRels, workbookNs, relNs))
            {
                if (result.ContainsKey(sheetName))
                    continue;

                var worksheetEntry = sourceArchive.GetEntry(worksheetPath);
                if (worksheetEntry is null)
                    continue;

                var worksheetXml = XlsxPackageXmlEditor.LoadXml(worksheetEntry);
                var drawingRelId = worksheetXml.Root?
                    .Element(workbookNs + "drawing")?
                    .Attribute(relNs + "id")?
                    .Value;
                if (string.IsNullOrWhiteSpace(drawingRelId))
                    continue;

                var worksheetRels = XlsxRelationshipReader.LoadTargets(
                    sourceArchive, XlsxPackagePath.GetRelationshipPartPath(worksheetPath), worksheetPath, packageRelNs);
                if (worksheetRels.TryGetValue(drawingRelId, out var drawingPath))
                    result[sheetName] = XlsxPackagePath.NormalizePackagePath(drawingPath);
            }

            return result;
        }
        catch
        {
            return EmptyDrawingPathsBySheet;
        }
    }

    private static readonly IReadOnlyDictionary<string, string> EmptyDrawingPathsBySheet =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    // Returns the highest N found in xl/media/freexPictureN.* entries in the source package, or 0
    // if there is no source package or no such entries. The caller adds 1 to get the first safe
    // starting index for newly authored picture media, preventing the drawing object writer from
    // claiming a media path that is already reserved by the source package. Without this, the
    // authored-picture media would shadow the source media name in the generated archive, causing
    // MergeRelationshipParts to refuse to copy the source drawing .rels (it skips relationships whose
    // targets were produced by the current save pass).
    private static int GetSourceMaxPictureIndex(Workbook workbook)
    {
        if (!SourcePackages.TryGetValue(workbook, out var sourcePackage))
            return 0;

        try
        {
            using var sourceStream = sourcePackage.OpenRead();
            using var sourceArchive = new ZipArchive(sourceStream, ZipArchiveMode.Read);

            const string prefix = "xl/media/freexPicture";
            var max = 0;
            foreach (var entry in sourceArchive.Entries)
            {
                var name = entry.FullName;
                if (!name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    continue;

                var afterPrefix = name.AsSpan(prefix.Length);
                var dotIndex = afterPrefix.IndexOf('.');
                if (dotIndex <= 0)
                    continue;

                if (int.TryParse(afterPrefix[..dotIndex], out var index) && index > max)
                    max = index;
            }

            return max;
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>
    /// Returns true when <paramref name="sheet"/> has at least one rich-text run whose color kind
    /// is <see cref="CellRunColorKind.Auto"/>.  Used to gate the BX1 shared-strings post-processing
    /// pass so workbooks without Auto-color runs pay no cost.
    /// </summary>
    private static bool HasRichTextAutoColorRuns(Sheet sheet)
    {
        foreach (var runs in sheet.RichTextRuns.Values)
        {
            foreach (var run in runs)
            {
                if (run.FontColor is { Kind: CellRunColorKind.Auto })
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Rewrites the <c>xl/sharedStrings.xml</c> part in the package stream, replacing every
    /// <c>&lt;color rgb="00000000"/&gt;</c> (the transparent-black sentinel emitted by the
    /// full-save path for <see cref="CellRunColorKind.Auto"/>) with <c>&lt;color auto="1"/&gt;</c>.
    /// </summary>
    /// <remarks>
    /// ClosedXML cannot emit <c>&lt;color auto="1"/&gt;</c> for rich-text runs, so
    /// <see cref="MapRunColorToXLColor"/> uses the sentinel <c>XLColor.FromArgb(0,0,0,0)</c>
    /// (transparent black, <c>rgb="00000000"</c>).  That value is impossible in a real Excel
    /// file — Excel always writes alpha=FF for opaque colors — so the substitution is safe.
    /// The reader (<see cref="XlsxRichRunReader.TryReadRunColor"/>) already handles
    /// <c>auto="1"</c> and returns <see cref="CellRunColor.Auto()"/>.
    /// </remarks>
    private static void FixRichAutoColorRunsInSharedStrings(Stream packageStream)
    {
        const string sharedStringsPath = "xl/sharedStrings.xml";

        packageStream.Position = 0;
        using var archive = new ZipArchive(packageStream, ZipArchiveMode.Update, leaveOpen: true);
        var entry = archive.GetEntry(sharedStringsPath);
        if (entry is null)
            return;

        XDocument doc;
        doc = OpcXml.LoadXml(entry, LoadOptions.PreserveWhitespace);

        var root = doc.Root;
        if (root is null)
            return;

        XNamespace ns = root.Name.Namespace;
        var modified = false;

        // Walk every <color rgb="00000000"> in the shared strings and replace with <color auto="1"/>.
        // These arise only from the full-save sentinel for Auto run colors.
        foreach (var colorEl in root.Descendants(ns + "color").ToList())
        {
            var rgbAttr = colorEl.Attribute("rgb");
            if (rgbAttr is null)
                continue;
            if (!string.Equals(rgbAttr.Value, "00000000", StringComparison.OrdinalIgnoreCase))
                continue;

            // Replace <color rgb="00000000"/> with <color auto="1"/>
            rgbAttr.Remove();
            colorEl.SetAttributeValue("auto", "1");
            modified = true;
        }

        if (!modified)
            return;

        // Rewrite the shared strings entry.
        entry.Delete();
        var newEntry = archive.CreateEntry(sharedStringsPath);
        using var outStream = newEntry.Open();
        doc.Save(outStream);
    }

    private static bool HasIgnoredFormulaErrors(Sheet sheet)
    {
        foreach (var pair in sheet.GetOccupiedCellMap())
        {
            if (pair.Value.IgnoreFormulaError)
                return true;
        }

        return false;
    }

    private static bool HasPivotCustomNumberFormats(Sheet sheet)
    {
        foreach (var pivot in sheet.PivotTables)
        {
            foreach (var field in pivot.DataFields)
            {
                if (field.NumberFormatId is >= 164 &&
                    !string.IsNullOrWhiteSpace(field.NumberFormatCode))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool HasSupportedXlsxCharts(Sheet sheet)
    {
        foreach (var chart in sheet.Charts)
        {
            if (XlsxChartXmlWriter.IsSupportedXlsxChart(chart))
                return true;
        }

        return false;
    }

    private struct XlsxPostProcessingFeaturePlan
    {
        public bool HasNonDefaultDimensions;
        public bool HasColumnWidths;
        public bool HasFullCalculationOnLoad;
        public bool HasModeledPrinterAttributes;
        public bool HasPhoneticProperties;
        public bool HasAllowEditRanges;
        public bool HasAdvancedConditionalFormats;
        public bool HasX14DataValidations;
        public bool HasSparklines;
        public bool HasThreadedComments;
        public bool HasBackgroundImages;
        public bool HasHeaderFooterPictures;
        public bool HasPersistableViewState;
        public bool HasCodeNames;
        public bool HasIgnoredFormulaErrors;
        public bool HasCustomProperties;
        public bool HasWorksheetElementMetadata;
        public bool HasSupportedCharts;
        public bool HasSupportedDrawingObjects;
        /// <summary>
        /// True when any sheet has at least one source-loaded picture/text box/shape. Gates the F15
        /// anchor-geometry rewrite (<see cref="XlsxSourceDrawingGeometryRewriter"/>) that keeps a
        /// resize/move of a source-loaded drawing object from being discarded on save.
        /// </summary>
        public bool HasSourceLoadedDrawingObjects;
        public bool HasStructuredTables;
        public bool HasPivotTables;
        public bool HasPivotCustomNumberFormats;
        public bool HasWorkbookPostProcessingMetadata;
        public bool HasWorkbookReplayMetadata;
        public bool HasReplayMetadata;
        public bool HasSourceIndependentMetadata;
        public bool HasStyleOnlyCells;
        public bool HasCustomViews;
        public bool HasCellFormulas;
        public bool HasLegacyNotes;
        /// <summary>
        /// True when any sheet has a rich-text run with <see cref="CellRunColorKind.Auto"/> color.
        /// The full-save (ClosedXML) path cannot emit <c>&lt;color auto="1"/&gt;</c> directly;
        /// instead it emits the sentinel <c>rgb="00000000"</c> (transparent black, never a real color),
        /// which the post-processing pass replaces with <c>auto="1"</c> in the shared-strings part.
        /// </summary>
        public bool HasRichAutoColorRuns;

        public static XlsxPostProcessingFeaturePlan Create(Workbook workbook)
        {
            var plan = new XlsxPostProcessingFeaturePlan();
            plan.HasWorkbookPostProcessingMetadata = XlsxWorkbookMetadataWriter.HasPostProcessingMetadata(workbook);
            plan.HasWorkbookReplayMetadata = XlsxWorkbookMetadataWriter.HasSourcePackageReplayMetadata(workbook);
            plan.HasCustomViews = workbook.CustomViews.Count > 0;
            foreach (var sheet in workbook.Sheets)
                plan.Include(sheet);

            return plan;
        }

        private void Include(Sheet sheet)
        {
            HasNonDefaultDimensions |= XlsxWorksheetDimensionDefaultsWriter.HasNonDefaultDimensions(sheet);
            HasColumnWidths |= sheet.ColumnWidths.Count > 0;
            HasFullCalculationOnLoad |= sheet.FullCalculationOnLoad;
            HasModeledPrinterAttributes |= XlsxWorksheetPageSetupMetadataWriter.HasModeledPrinterAttributes(sheet);
            HasPhoneticProperties |= sheet.PhoneticProperties is not null;
            HasAllowEditRanges |= sheet.AllowEditRanges.Count > 0;
            HasAdvancedConditionalFormats |= XlsxAdvancedConditionalFormatWriter.HasAdvancedConditionalFormats(sheet);
            HasX14DataValidations |= XlsxX14DataValidationWriter.HasX14DataValidations(sheet);
            HasSparklines |= sheet.Sparklines.Count > 0;
            HasThreadedComments |= XlsxWorksheetThreadedCommentMapper.HasThreadedComments(sheet);
            HasBackgroundImages |= sheet.BackgroundImage is not null;
            HasHeaderFooterPictures |= XlsxHeaderFooterPictureReaderWriter.HasPictures(sheet);
            HasPersistableViewState |= XlsxWorksheetViewWriter.HasPersistableViewState(sheet);
            HasCodeNames |= !string.IsNullOrWhiteSpace(sheet.CodeName);
            IncludeCellFeatures(sheet);
            HasCustomProperties |= sheet.CustomProperties.Count > 0;
            HasWorksheetElementMetadata |= XlsxWorksheetPostProcessingMetadataBatchWriter.HasWorksheetElementMetadata(sheet);
            if (!HasSupportedCharts)
                HasSupportedCharts = HasSupportedXlsxCharts(sheet);
            HasSupportedDrawingObjects |= XlsxWorksheetDrawingObjectWriter.HasSupportedObjects(sheet);
            HasSourceLoadedDrawingObjects |= XlsxSourceDrawingGeometryRewriter.HasSourceLoadedDrawingObjects(sheet);
            HasStructuredTables |= sheet.StructuredTables.Count > 0;
            HasPivotTables |= sheet.PivotTables.Count > 0;
            if (!HasPivotCustomNumberFormats)
                HasPivotCustomNumberFormats = HasPivotCustomNumberFormats(sheet);
            HasReplayMetadata |= XlsxWorksheetPostProcessingMetadataBatchWriter.HasReplayMetadata(sheet);
            HasSourceIndependentMetadata |= XlsxWorksheetSourceIndependentMetadataBatchWriter.HasMetadata(sheet);
            HasLegacyNotes |= sheet.Comments.Count > 0;
            HasStyleOnlyCells |= sheet.HasStyleOnlyCells;
            if (!HasRichAutoColorRuns)
                HasRichAutoColorRuns = HasRichTextAutoColorRuns(sheet);
        }

        private void IncludeCellFeatures(Sheet sheet)
        {
            foreach (var pair in sheet.GetOccupiedCellMap())
            {
                HasIgnoredFormulaErrors |= pair.Value.IgnoreFormulaError;
                HasCellFormulas |= pair.Value.HasFormula;
                if (HasIgnoredFormulaErrors && HasCellFormulas)
                    return;
            }
        }
    }
}
