using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

public sealed partial class XlsxFileAdapter
{
    private static void ApplyPackagePostProcessing(
        Workbook workbook,
        Stream packageStream,
        string? currentModelFingerprint = null,
        bool removeSourceCalcChain = false,
        bool preserveVbaProject = true)
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

        // R62-io-defined-name-print-6-2: also run SaveToPackage when any sheet has a live
        // AutoFilter, even if the workbook has zero ordinary named ranges/formulas. SaveToPackage
        // (via CreateDefinedNameEntries) is the ONLY code path that emits/keeps in sync the
        // built-in _xlnm._FilterDatabase name for a sheet's AutoFilter (XlsxNamedRangeMapper.cs
        // documents this as "actively managed... on every save"), so gating the whole call solely
        // on NamedRanges/NamedFormulas being non-empty silently skipped _FilterDatabase for any
        // workbook whose ONLY defined-name-worthy content is an AutoFilter (e.g. every first save
        // of a brand-new workbook that only has an AutoFilter applied). The AutoFilter check itself
        // is batched into featurePlan.HasLiveAutoFilter (computed once in the sheet-feature-detection
        // pass) rather than re-scanning workbook.Sheets here.
        if (workbook.NamedRanges.Count > 0 ||
            workbook.NamedFormulas.Count > 0 ||
            workbook.ScopedNamedRanges.Count > 0 ||
            workbook.ScopedNamedFormulas.Count > 0 ||
            featurePlan.HasLiveAutoFilter)
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

        // R55-io-hyperlink-round-trip-5-1: strip ClosedXML's fabricated "<Sheet>!" prefix off a
        // bang-less defined-name hyperlink's saved "location" attribute. Must run on every FULL
        // save regardless of hasSourcePackage -- worksheet XML content always comes straight from
        // ClosedXML's own SaveAs output; PreserveSourcePackageParts (below, on the source-package
        // path) never restores worksheet parts wholesale, so this fixup is the only place that ever
        // sees (and can correct) the fabricated value.
        if (featurePlan.HasBareInternalHyperlinkBookmarks)
        {
            packageStream.Position = 0;
            FixFabricatedDefinedNameHyperlinkLocations(packageStream, workbook, GetWorksheetPathMap());
        }

        // R96-io-hyperlink-external-bookmark: backfill the "location" sub-address ClosedXML's
        // XLHyperlink can never write alongside an r:id relationship for the same <hyperlink>
        // element (its writer branches exclusively on IsExternal -- see CreateXlsxHyperlink and
        // FixExternalHyperlinkBookmarkLocations for the full explanation). Must run on every FULL
        // save for the same reason as the fixup above: worksheet XML always comes straight from
        // ClosedXML's own SaveAs output.
        if (featurePlan.HasExternalHyperlinkBookmarks)
        {
            packageStream.Position = 0;
            FixExternalHyperlinkBookmarkLocations(packageStream, workbook, GetWorksheetPathMap());
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
            // R87-io-comments-notes-5-1: pass along each person's ORIGINAL userId/providerId/extLst
            // (read from the source package's xl/persons/person.xml) so WritePersonsPart preserves
            // them instead of always defaulting sourcePersonRecordsById to null and re-emitting bare
            // displayName+id records -- see XlsxWorksheetThreadedCommentMapper.ReadPersonRecords/
            // BuildPersonElement, whose R74 preservation support was previously never reached from
            // this, the sole production call site.
            IReadOnlyDictionary<string, PersonRecord>? sourcePersonRecordsById = null;
            if (SourcePackages.TryGetValue(workbook, out var threadedCommentSourcePackage))
            {
                using var sourceStream = threadedCommentSourcePackage.OpenRead();
                using var sourceArchive = new ZipArchive(sourceStream, ZipArchiveMode.Read);
                sourcePersonRecordsById = XlsxWorksheetThreadedCommentMapper.ReadPersonRecords(sourceArchive);
            }

            packageStream.Position = 0;
            XlsxWorksheetThreadedCommentMapper.Save(
                packageStream,
                workbook,
                GetWorksheetPathMap(),
                sourcePersonRecordsById);
        }

        if (featurePlan.HasBackgroundImages)
        {
            packageStream.Position = 0;
            XlsxWorksheetBackgroundReaderWriter.Save(packageStream, workbook, GetSourceMediaEntryNames(workbook));
        }

        if (featurePlan.HasHeaderFooterPictures)
        {
            IReadOnlySet<string>? sheetsToPreserve = null;
            IReadOnlySet<int>? reservedHeaderFooterVmlIndices = null;
            if (SourcePackages.TryGetValue(workbook, out var headerFooterSourcePackage))
            {
                using var sourceStream = headerFooterSourcePackage.OpenRead();
                sheetsToPreserve = XlsxHeaderFooterPictureReaderWriter.FindSheetsWithUnchangedSourcePictures(
                    sourceStream,
                    workbook);

                // R112-io-hf-vml-path-collision: learn which "freexHeaderFooterN.vml" indices the
                // sheets we are ABOUT to skip (sheetsToPreserve) still reference in the SOURCE
                // package, so the Save() call below never restarts its own counter onto one of
                // those numbers and overwrites a picture the preservation pass further down this
                // method (XlsxWorksheetVmlReferencePreserver, via PreserveSourcePackageParts) is
                // about to copy into the SAME path.
                if (sheetsToPreserve.Count > 0)
                {
                    sourceStream.Position = 0;
                    reservedHeaderFooterVmlIndices = XlsxHeaderFooterPictureReaderWriter.GetPreservedVmlIndices(
                        sourceStream,
                        workbook,
                        sheetsToPreserve);
                }
            }

            packageStream.Position = 0;
            XlsxHeaderFooterPictureReaderWriter.Save(packageStream, workbook, sheetsToPreserve, reservedHeaderFooterVmlIndices);
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

        // drawing-zorder-share-part (residual-gap closure): drawing parts the chart writer allocates
        // FRESH for a sheet (a sheet with no source drawing part of its own -- every sheet of a
        // never-saved workbook, a brand-new or duplicated sheet, ...). A worksheet can reference only
        // ONE drawing part, so the drawing-object writer must write this sheet's pictures/shapes/text
        // boxes into that same part rather than allocating a second one and repointing the worksheet at
        // it, which orphaned the charts. The sheets NOT listed here (those reusing their own source
        // drawing part) stay on the existing chart-shadow + XlsxWorksheetDrawingPartMerger route.
        var chartDrawingPathsBySheet = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
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
                GetSourceDrawingPathsBySheet(workbook),
                GetSourceChartHyperlinksBySheet(workbook),
                chartDrawingPathsBySheet);
        }

        if (featurePlan.HasSupportedDrawingObjects)
        {
            packageStream.Position = 0;
            XlsxWorksheetDrawingObjectWriter.Save(
                packageStream,
                workbook,
                GetSourceDrawingPathsBySheet(workbook),
                startPictureIndex: GetSourceMaxPictureIndex(workbook) + 1,
                sourceObjectHyperlinksBySheet: GetSourceDrawingObjectHyperlinksBySheet(workbook),
                chartDrawingPathsBySheet: chartDrawingPathsBySheet);
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

        // R78-selfreg-twin-sweep-1: no source package exists at all, so there is no
        // XlsxSharedStringMetadataPreserver.PreserveRichTextAndPhonetics pass (called from inside
        // PreserveSourcePackageParts, further down -- unreachable on this fresh-workbook path) to
        // fall back on; this is the only chance to re-emit a cell's phonetic guide before the
        // package is final. Must run before the early-return path below, like the legacy-comment
        // normalizer immediately above.
        if (!hasSourcePackage && featurePlan.HasCellPhoneticGuides)
        {
            packageStream.Position = 0;
            XlsxWorksheetCellPhoneticGuideWriter.Save(packageStream, workbook, GetWorksheetPathMap());
        }

        if (!hasSourcePackage)
        {
            SaveSourcePackageIndependentPostProcessingMetadata();
            // R96-io-external-link-writer-1: a brand-new (never-loaded-from-.xlsx) workbook has no
            // source package for XlsxExternalLinkAuthoringWriter's sibling call (inside
            // PreserveSourcePackageParts) to ever run against, so a freshly typed bracketed
            // external-workbook reference needs its own call here on this path.
            if (featurePlan.HasCellFormulas)
            {
                packageStream.Position = 0;
                XlsxExternalLinkAuthoringWriter.Save(packageStream, workbook);
            }
            NormalizeStylesheetForSchema();
            NormalizeDocumentPropertiesPackageGraph();
            NormalizeWorkbookForSchema();
            return;
        }

        packageStream.Position = 0;
        var sourceParts = PreserveSourcePackageParts(workbook, packageStream, preserveVbaProject);

        // R78-selfreg-twin-sweep-1: run AFTER source-part preservation (specifically after
        // XlsxSharedStringMetadataPreserver.PreserveRichTextAndPhonetics, called from inside
        // PreserveSourcePackageParts above) so that mechanism -- which restores a guide that came
        // verbatim from the SOURCE package by patching the shared-string entry in place, keeping an
        // untouched cell's t="s" encoding byte-stable -- gets first chance to fix an already-correct
        // cell. This writer's own already-has-markup checks then see that fix and skip it, only
        // converting a cell that STILL has no phonetic guide -- e.g. one whose guide only exists in
        // the CURRENT in-memory model (a brand-new cell, copy/pasted, that never had ANY content at
        // this address in the source, so PreserveRichTextAndPhonetics has nothing to cross-check it
        // against).
        if (featurePlan.HasCellPhoneticGuides)
        {
            packageStream.Position = 0;
            XlsxWorksheetCellPhoneticGuideWriter.Save(packageStream, workbook, GetWorksheetPathMap());
        }

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

        // P8 (R44-io-pivot-filter-page-3-1): on this source-package path, XlsxPivotTableWriter.Save --
        // the only code that regenerates <pivotFields>/<items> hidden flags and <pageFields> from the
        // current PivotTableModel -- is gated behind !hasSourcePackage above, so it never runs here. The
        // pivotTableDefinition part(s) PreserveSourcePackageParts (and the generic unknown-part
        // passthrough it drives) just restored are the workbook's ORIGINAL, pre-edit XML, copied
        // verbatim. Rewrite ONLY the page/report-filter selection and the manual item-filter (per-item
        // hidden flags) on those preserved parts in place from the model, so an in-app edit to either
        // survives save; mirrors the P7 slicer/timeline selection rewrite immediately below. Must run
        // after PreserveSourcePackageParts (the parts must already be at their final path).
        if (featurePlan.HasPivotTables)
        {
            // R82-io-pivot-layout-5-1: must run BEFORE RewritePivotTableFilterState/RewritePivotTableLayoutState
            // below -- moving a field between Rows/Columns/Filters (ConfigurePivotTableLayoutCommand) only
            // mutates PivotTableModel.RowFields/ColumnFields/PageFields in memory; nothing else on this
            // source-package save path regenerates the preserved part's <rowFields>/<colFields>/<pageFields>
            // containers or each <pivotField>'s own axis attribute from the CURRENT model, so the move
            // silently reverted to its pre-edit area on reload. This IS the structural rewrite the older
            // comment here used to say was "intentionally out of scope."
            packageStream.Position = 0;
            RewritePivotTableFieldAxes(packageStream, workbook);

            packageStream.Position = 0;
            RewritePivotTableFilterState(packageStream, workbook);

            // R75-io-pivottable-layout-4-1: sibling of RewritePivotTableFilterState above -- rewrites the
            // preserved pivotTableDefinition's grand-total visibility, report-layout (compact/outline) form,
            // and per-data-field summary function/number-format/showDataAs from the CURRENT model, all of
            // which XlsxPivotTableWriter.Save (gated behind !hasSourcePackage) would otherwise silently drop
            // on this source-package save path.
            packageStream.Position = 0;
            RewritePivotTableLayoutState(packageStream, workbook, numberFormatIdMap);
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

    // R82-io-pivot-layout-5-1: rewrites which axis (Row/Column/Filter) each field is assigned to on a
    // PRESERVED pivotTableDefinition part -- sibling of RewritePivotTableFilterState/
    // RewritePivotTableLayoutState below, gated the same way (matches a pivot table to its model purely
    // via PivotTableModel.PackagePart; a brand-new pivot table added since Load() has no PackagePart yet
    // and is intentionally skipped -- it needs a fully regenerated part, not a patch of one that doesn't
    // exist yet). Moving a field between Rows/Columns/Filters (ConfigurePivotTableLayoutCommand) only
    // mutates PivotTableModel.RowFields/ColumnFields/PageFields in memory; nothing else on this
    // hasSourcePackage save path regenerates the preserved part's <rowFields>/<colFields>/<pageFields>
    // containers or each <pivotField>'s own axis attribute from the CURRENT model.
    private static void RewritePivotTableFieldAxes(Stream packageStream, Workbook workbook)
    {
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        using var archive = new ZipArchive(packageStream, ZipArchiveMode.Update, leaveOpen: true);

        var cachesById = new Dictionary<int, PivotCacheModel>();
        foreach (var cache in workbook.PivotCaches)
        {
            if (cache.CacheId > 0)
                cachesById[cache.CacheId] = cache;
        }

        foreach (var sheet in workbook.Sheets)
        {
            foreach (var pivot in sheet.PivotTables)
            {
                if (string.IsNullOrWhiteSpace(pivot.PackagePart))
                    continue;

                var pivotPath = XlsxPackagePath.NormalizePackagePath(pivot.PackagePart);
                var entry = archive.GetEntry(pivotPath);
                if (entry is null)
                    continue;

                var pivotXml = XlsxPackageXmlEditor.LoadXml(entry);
                var root = pivotXml.Root;
                if (root is null || root.Name != workbookNs + "pivotTableDefinition")
                    continue;

                cachesById.TryGetValue(pivot.CacheId, out var cache);
                if (RewritePreservedPivotFieldAxes(root, pivot, cache, workbookNs))
                    XlsxPackageXmlEditor.ReplaceXml(archive, pivotPath, pivotXml);
            }
        }
    }

    // Reads the SourceFieldIndex list a preserved <rowFields>/<colFields> container currently encodes,
    // skipping the x="-2" "Σ Values" pseudo-field marker (PivotTableModel never represents that marker as
    // a real field -- see XlsxPivotTableReader.Fields.cs's ReadPivotFieldIndexes for the matching read
    // path), so this compares like-for-like against PivotFieldModel.SourceFieldIndex.
    private static List<int> ReadPreservedPivotFieldCollectionIndexes(XElement? fieldsElement, XNamespace workbookNs) =>
        fieldsElement?
            .Elements(workbookNs + "field")
            .Select(field => XlsxXmlAttributeReader.ReadIntAttribute(field, "x"))
            .Where(index => index is not null && index.Value != -2)
            .Select(index => index!.Value)
            .ToList()
        ?? [];

    // Sibling of ReadPreservedPivotFieldCollectionIndexes, for the preserved <pageFields> container.
    private static List<int> ReadPreservedPivotPageFieldIndexes(XElement? fieldsElement, XNamespace workbookNs) =>
        fieldsElement?
            .Elements(workbookNs + "pageField")
            .Select(field => XlsxXmlAttributeReader.ReadIntAttribute(field, "fld"))
            .Where(index => index is not null)
            .Select(index => index!.Value)
            .ToList()
        ?? [];

    // Rewrites, in place, ONLY what actually differs from the preserved XML: each affected <pivotField>'s
    // own axis attribute, plus a wholesale regeneration of the <rowFields>/<colFields>/<pageFields>
    // containers themselves (membership AND order both matter, so there is no cheaper in-place patch that
    // preserves byte-stability the way the item-filter/page-selection rewrites above do). Fields whose
    // SourceFieldIndex is beyond the preserved part's existing <pivotFields> range are left untouched --
    // that only happens for a field newly introduced to an axis that never had ANY field in the original
    // file, which needs the same full regeneration a brand-new pivot table does, not a patch.
    private static bool RewritePreservedPivotFieldAxes(
        XElement root,
        PivotTableModel pivot,
        PivotCacheModel? cache,
        XNamespace workbookNs)
    {
        var desiredRowIndexes = pivot.RowFields.Select(field => field.SourceFieldIndex).ToList();
        var desiredColumnIndexes = pivot.ColumnFields.Select(field => field.SourceFieldIndex).ToList();
        var desiredPageIndexes = pivot.PageFields.Select(field => field.SourceFieldIndex).ToList();

        var existingRowIndexes = ReadPreservedPivotFieldCollectionIndexes(root.Element(workbookNs + "rowFields"), workbookNs);
        var existingColumnIndexes = ReadPreservedPivotFieldCollectionIndexes(root.Element(workbookNs + "colFields"), workbookNs);
        var existingPageIndexes = ReadPreservedPivotPageFieldIndexes(root.Element(workbookNs + "pageFields"), workbookNs);

        if (existingRowIndexes.SequenceEqual(desiredRowIndexes) &&
            existingColumnIndexes.SequenceEqual(desiredColumnIndexes) &&
            existingPageIndexes.SequenceEqual(desiredPageIndexes))
        {
            return false;
        }

        var pivotFieldsElement = root.Element(workbookNs + "pivotFields");
        if (pivotFieldsElement is not null)
        {
            var pivotFieldElements = pivotFieldsElement.Elements(workbookNs + "pivotField").ToList();
            for (var index = 0; index < pivotFieldElements.Count; index++)
            {
                var desiredAxis =
                    desiredRowIndexes.Contains(index) ? "axisRow" :
                    desiredColumnIndexes.Contains(index) ? "axisCol" :
                    desiredPageIndexes.Contains(index) ? "axisPage" :
                    null;

                var element = pivotFieldElements[index];
                if (string.Equals(element.Attribute("axis")?.Value, desiredAxis, StringComparison.Ordinal))
                    continue;

                if (desiredAxis is null)
                    element.Attribute("axis")?.Remove();
                else
                    element.SetAttributeValue("axis", desiredAxis);
            }
        }

        ReplacePreservedPivotFieldContainer(root, workbookNs, "rowFields", XlsxPivotTableWriter.ToPivotFieldCollectionXml("rowFields", pivot.RowFields, workbookNs));
        ReplacePreservedPivotFieldContainer(root, workbookNs, "colFields", XlsxPivotTableWriter.ToPivotFieldCollectionXml("colFields", pivot.ColumnFields, workbookNs));
        ReplacePreservedPivotFieldContainer(root, workbookNs, "pageFields", XlsxPivotTableWriter.ToPivotPageFieldsXml(pivot.PageFields, cache, workbookNs));

        return true;
    }

    // Canonical CT_pivotTableDefinition child order for the rowFields/colFields/pageFields containers and
    // everything that can legitimately follow them in a part this codebase writes or preserves -- used to
    // find the correct insertion point when a container needs to be newly added (or removed) because a
    // field moved onto (or off of) an axis that had no field on it at all in the original file.
    // R83-meta-1: pivotTableStyleInfo comes BEFORE filters (and the invented valueFilters/labelFilters/
    // pivotSorts elements), matching the real CT_pivotTableDefinition child sequence XlsxPivotTableWriter
    // itself emits (see the R82-io-pivot-layout-5-2 comment on ToPivotTableDefinitionXml) -- the old order
    // here had it backwards, which anchored a newly-inserted pageFields/rowFields/colFields container
    // AFTER pivotTableStyleInfo (near the very end of the element sequence) whenever the part already had
    // a native <filters> element, instead of near the front where it belongs.
    private static readonly string[] PivotFieldContainerCanonicalOrder =
    [
        "rowFields", "colFields", "pageFields", "dataFields", "calculatedItems",
        "pivotTableStyleInfo", "valueFilters", "labelFilters", "filters", "pivotSorts", "extLst",
    ];

    private static void ReplacePreservedPivotFieldContainer(XElement root, XNamespace workbookNs, string elementName, XElement? newElement)
    {
        var existing = root.Element(workbookNs + elementName);
        if (newElement is null)
        {
            existing?.Remove();
            return;
        }

        if (existing is not null)
        {
            existing.ReplaceWith(newElement);
            return;
        }

        var startIndex = Array.IndexOf(PivotFieldContainerCanonicalOrder, elementName) + 1;
        XElement? anchor = null;
        for (var i = startIndex; i < PivotFieldContainerCanonicalOrder.Length; i++)
        {
            anchor = root.Element(workbookNs + PivotFieldContainerCanonicalOrder[i]);
            if (anchor is not null)
                break;
        }

        if (anchor is not null)
            anchor.AddBeforeSelf(newElement);
        else
            root.Add(newElement);
    }

    // P8 (R44-io-pivot-filter-page-3-1): rewrites just the page/report-filter selection and the manual
    // item-filter (per-item hidden flags) on each PRESERVED pivotTableDefinition part so edits made to
    // the loaded PivotTableModel after Load() survive Save() on the hasSourcePackage path, where
    // XlsxPivotTableWriter.Save never runs. Matches a pivot table part to its model purely via
    // PivotTableModel.PackagePart (the exact archive path the pivot table was loaded from); a pivot
    // table added since Load() has no PackagePart yet and is intentionally skipped -- a brand-new pivot
    // table needs a fully regenerated part, not a patch of a part that doesn't exist yet.
    private static void RewritePivotTableFilterState(Stream packageStream, Workbook workbook)
    {
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        using var archive = new ZipArchive(packageStream, ZipArchiveMode.Update, leaveOpen: true);

        var cachesById = new Dictionary<int, PivotCacheModel>();
        foreach (var cache in workbook.PivotCaches)
        {
            if (cache.CacheId > 0)
                cachesById[cache.CacheId] = cache;
        }

        foreach (var sheet in workbook.Sheets)
        {
            foreach (var pivot in sheet.PivotTables)
            {
                if (string.IsNullOrWhiteSpace(pivot.PackagePart))
                    continue;

                var pivotPath = XlsxPackagePath.NormalizePackagePath(pivot.PackagePart);
                var entry = archive.GetEntry(pivotPath);
                if (entry is null)
                    continue;

                var pivotXml = XlsxPackageXmlEditor.LoadXml(entry);
                var root = pivotXml.Root;
                if (root is null || root.Name != workbookNs + "pivotTableDefinition")
                    continue;

                cachesById.TryGetValue(pivot.CacheId, out var cache);

                var changedItemFilters = RewritePreservedPivotFieldItemFilters(root, pivot, cache, workbookNs);
                var changedPageFields = RewritePreservedPivotPageFieldSelections(root, pivot, cache, workbookNs);
                var changedValueLabelFilters = RewritePreservedPivotValueAndLabelFilters(root, pivot, workbookNs);
                if (changedItemFilters || changedPageFields || changedValueLabelFilters)
                    XlsxPackageXmlEditor.ReplaceXml(archive, pivotPath, pivotXml);
            }
        }
    }

    // R75-io-pivottable-layout-4-1: sibling of RewritePivotTableFilterState above, gated the same way (the
    // hasSourcePackage save path never runs XlsxPivotTableWriter.Save, so nothing else ever rewrites a
    // PRESERVED pivotTableDefinition part's grand-total visibility, report-layout form, or data-field
    // summary settings from the CURRENT model). Rewrites, in place, ONLY what actually differs from the
    // preserved XML so an untouched pivot table's saved part stays byte-stable.
    private static void RewritePivotTableLayoutState(
        Stream packageStream,
        Workbook workbook,
        IReadOnlyDictionary<int, int> numberFormatIdMap)
    {
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        using var archive = new ZipArchive(packageStream, ZipArchiveMode.Update, leaveOpen: true);

        foreach (var sheet in workbook.Sheets)
        {
            foreach (var pivot in sheet.PivotTables)
            {
                if (string.IsNullOrWhiteSpace(pivot.PackagePart))
                    continue;

                var pivotPath = XlsxPackagePath.NormalizePackagePath(pivot.PackagePart);
                var entry = archive.GetEntry(pivotPath);
                if (entry is null)
                    continue;

                var pivotXml = XlsxPackageXmlEditor.LoadXml(entry);
                var root = pivotXml.Root;
                if (root is null || root.Name != workbookNs + "pivotTableDefinition")
                    continue;

                var changedGrandTotals = RewritePreservedPivotGrandTotals(root, pivot);
                var changedReportLayout = RewritePreservedPivotReportLayout(root, pivot, workbookNs);
                var changedDataFields = RewritePreservedPivotDataFieldSummaries(root, pivot, workbookNs, numberFormatIdMap);
                if (changedGrandTotals || changedReportLayout || changedDataFields)
                    XlsxPackageXmlEditor.ReplaceXml(archive, pivotPath, pivotXml);
            }
        }
    }

    // OOXML CT_pivotTableDefinition spells grand-total visibility as rowGrandTotals/colGrandTotals, both
    // defaulting to true when omitted (mirrors XlsxPivotTableReader.cs's ReadGrandTotal).
    private static bool RewritePreservedPivotGrandTotals(XElement root, PivotTableModel pivot)
    {
        var changed = SetPreservedBoolAttribute(root, "rowGrandTotals", pivot.ShowRowGrandTotals, defaultValue: true);
        changed |= SetPreservedBoolAttribute(root, "colGrandTotals", pivot.ShowColumnGrandTotals, defaultValue: true);
        return changed;
    }

    // OOXML CT_pivotTableDefinition's compact/compactData default to true, outline/outlineData default to
    // false, when omitted (see XlsxPivotTableWriter.Converters.cs's PivotReportLayoutAttributes, which this
    // reuses for a fresh save). gridDropZones is Excel's separate "Classic PivotTable Layout" checkbox
    // (driven by ShowClassicLayout, not ReportLayout) and is intentionally left untouched here.
    private static readonly Dictionary<string, bool> PivotReportLayoutRootAttributeDefaults = new(StringComparer.Ordinal)
    {
        ["compact"] = true,
        ["compactData"] = true,
        ["outline"] = false,
        ["outlineData"] = false,
    };

    private static bool RewritePreservedPivotReportLayout(XElement root, PivotTableModel pivot, XNamespace workbookNs)
    {
        var rootChanged = false;
        foreach (var attribute in XlsxPivotTableWriter.PivotReportLayoutAttributes(pivot.ReportLayout))
        {
            var name = attribute.Name.LocalName;
            if (!PivotReportLayoutRootAttributeDefaults.TryGetValue(name, out var defaultValue))
                continue; // gridDropZones: not part of ReportLayout, left untouched.

            if (SetPreservedBoolAttribute(root, name, attribute.Value == "1", defaultValue))
                rootChanged = true;
        }

        // The table-wide report layout is unchanged from what the preserved part already encodes -- leave
        // any existing per-field compact/outline settings (which may be genuinely distinct per field, e.g.
        // set via a real Excel per-field Layout dialog) untouched rather than risk normalizing them away on
        // every save that merely happens to touch this pivot table for an unrelated reason.
        if (!rootChanged)
            return false;

        // R52-io-pivot-layout-3-4: CT_PivotField's OWN compact/outline attributes are what a real Excel
        // client actually renders -- the root attributes above are only the defaults Excel seeds onto
        // newly-added fields, not a live override of an existing field's own attributes. The table-wide
        // report layout genuinely changed, so mirror what clicking Excel's PivotTable Layout ribbon command
        // does: re-apply the newly-chosen form to EVERY existing row/column axis field, not just the
        // table-level defaults future fields will inherit.
        var pivotFieldsElement = root.Element(workbookNs + "pivotFields");
        if (pivotFieldsElement is not null)
        {
            foreach (var fieldElement in pivotFieldsElement.Elements(workbookNs + "pivotField"))
            {
                if (fieldElement.Attribute("axis")?.Value is not ("axisRow" or "axisCol"))
                    continue;

                SetPreservedBoolAttribute(fieldElement, "compact", pivot.ReportLayout == PivotReportLayout.Compact, defaultValue: true);
                SetPreservedBoolAttribute(fieldElement, "outline", pivot.ReportLayout != PivotReportLayout.Tabular, defaultValue: false);
            }
        }

        return true;
    }

    // Sets a boolean attribute to its OOXML "1"/"0" wire form, but ONLY when the attribute's CURRENT
    // effective value (an absent attribute takes on <paramref name="defaultValue"/> per schema) actually
    // differs from the desired value -- keeps an untouched pivot's preserved definition byte-stable rather
    // than adding a redundant explicit attribute every save.
    private static bool SetPreservedBoolAttribute(XElement element, string name, bool value, bool defaultValue)
    {
        var existing = element.Attribute(name)?.Value;
        var effective = existing is null
            ? defaultValue
            : existing == "1" || string.Equals(existing, "true", StringComparison.OrdinalIgnoreCase);
        if (effective == value)
            return false;

        element.SetAttributeValue(name, value ? "1" : "0");
        return true;
    }

    // R75-io-pivottable-layout-4-1: rewrites each preserved <dataField>'s summary function (subtotal),
    // number format (numFmtId, remapped through the same numberFormatIdMap a fresh save applies), and
    // showDataAs from the corresponding PivotDataFieldModel. Matched purely by position -- the same order
    // XlsxPivotTableWriter.ToPivotDataFieldsXml emits them in and XlsxPivotTableReader.Fields.cs's
    // ReadPivotDataFields reads them back in.
    private static bool RewritePreservedPivotDataFieldSummaries(
        XElement root,
        PivotTableModel pivot,
        XNamespace workbookNs,
        IReadOnlyDictionary<int, int> numberFormatIdMap)
    {
        var dataFieldsElement = root.Element(workbookNs + "dataFields");
        if (dataFieldsElement is null || pivot.DataFields.Count == 0)
            return false;

        var dataFieldElements = dataFieldsElement.Elements(workbookNs + "dataField").ToList();
        var changed = false;
        for (var index = 0; index < dataFieldElements.Count && index < pivot.DataFields.Count; index++)
        {
            var model = pivot.DataFields[index];
            var element = dataFieldElements[index];

            var desiredSubtotal = string.IsNullOrWhiteSpace(model.SummaryFunction) ? "sum" : model.SummaryFunction;
            if (SetPreservedStringAttribute(element, "subtotal", desiredSubtotal, defaultValue: "sum"))
                changed = true;

            var desiredShowDataAs = model.ShowValuesAs == PivotShowValuesAs.None
                ? null
                : XlsxPivotTableWriter.ToPivotShowValuesAsText(model.ShowValuesAs);
            if (SetPreservedOptionalAttribute(element, "showDataAs", desiredShowDataAs))
                changed = true;

            string? desiredNumFmtId = null;
            if (model.NumberFormatId is { } numberFormatId)
            {
                var mappedId = numberFormatIdMap.TryGetValue(numberFormatId, out var remapped) ? remapped : numberFormatId;
                desiredNumFmtId = mappedId.ToString(CultureInfo.InvariantCulture);
            }

            if (SetPreservedOptionalAttribute(element, "numFmtId", desiredNumFmtId))
                changed = true;
        }

        return changed;
    }

    // Sets attributeName to value when non-null, or removes it when null; returns true only when the XML
    // actually changed. Mirrors XlsxSlicerTimelineStateRewriter.cs's SetOptionalAttribute.
    private static bool SetPreservedOptionalAttribute(XElement element, string attributeName, string? value)
    {
        var attribute = element.Attribute(attributeName);
        if (value is null)
        {
            if (attribute is null)
                return false;
            attribute.Remove();
            return true;
        }

        if (attribute is not null && string.Equals(attribute.Value, value, StringComparison.Ordinal))
            return false;

        element.SetAttributeValue(attributeName, value);
        return true;
    }

    // Like SetPreservedOptionalAttribute, but for a required-with-schema-default string attribute (e.g.
    // CT_DataField's "subtotal", schema default "sum"): an absent attribute is treated as already carrying
    // defaultValue, so writing exactly the default leaves an untouched part's omitted attribute omitted.
    private static bool SetPreservedStringAttribute(XElement element, string attributeName, string value, string defaultValue)
    {
        var existing = element.Attribute(attributeName)?.Value ?? defaultValue;
        if (string.Equals(existing, value, StringComparison.Ordinal))
            return false;

        element.SetAttributeValue(attributeName, value);
        return true;
    }

    // Rewrites each preserved <pageFields>/<pageField>'s item/name selection from the corresponding
    // PivotFieldModel.SelectedItem. Field matching is by @fld (source field index); a pageField with no
    // corresponding model entry (e.g. removed from the Filters axis) is left untouched -- axis
    // reassignment is out of scope for this patch-style rewrite.
    private static bool RewritePreservedPivotPageFieldSelections(
        XElement pivotTableDefinitionRoot,
        PivotTableModel pivot,
        PivotCacheModel? cache,
        XNamespace workbookNs)
    {
        var pageFieldsElement = pivotTableDefinitionRoot.Element(workbookNs + "pageFields");
        if (pageFieldsElement is null)
            return false;

        var changed = false;
        foreach (var pageFieldElement in pageFieldsElement.Elements(workbookNs + "pageField"))
        {
            var fieldIndex = XlsxXmlAttributeReader.ReadIntAttribute(pageFieldElement, "fld");
            if (fieldIndex is null)
                continue;

            var model = pivot.PageFields.LastOrDefault(field => field.SourceFieldIndex == fieldIndex.Value);
            if (model is null)
                continue;

            if (RewritePreservedPageFieldSelection(pageFieldElement, model, cache))
                changed = true;
        }

        return changed;
    }

    private static bool RewritePreservedPageFieldSelection(
        XElement pageFieldElement,
        PivotFieldModel model,
        PivotCacheModel? cache)
    {
        var existingItem = pageFieldElement.Attribute("item")?.Value;
        var existingName = pageFieldElement.Attribute("name")?.Value;

        if (string.IsNullOrWhiteSpace(model.SelectedItem))
        {
            if (existingItem is null && existingName is null)
                return false;

            pageFieldElement.Attribute("item")?.Remove();
            pageFieldElement.Attribute("name")?.Remove();
            return true;
        }

        var resolvedIndex = ResolvePreservedPageFieldItemIndex(model, cache);
        if (resolvedIndex is { } index)
        {
            var desiredItem = index.ToString(CultureInfo.InvariantCulture);
            if (existingItem == desiredItem && existingName is null)
                return false;

            pageFieldElement.SetAttributeValue("item", desiredItem);
            pageFieldElement.Attribute("name")?.Remove();
            return true;
        }

        if (existingName == model.SelectedItem && existingItem is null)
            return false;

        pageFieldElement.SetAttributeValue("name", model.SelectedItem);
        pageFieldElement.Attribute("item")?.Remove();
        return true;
    }

    // Mirrors XlsxPivotTableWriter's ResolvePivotPageFieldSelectedItemIndex: resolves the model's
    // selected item TEXT to its position in the pivot cache field's materialized SharedItems list, so
    // the native @item index attribute (Excel's preferred form) can be written instead of @name.
    private static int? ResolvePreservedPageFieldItemIndex(PivotFieldModel field, PivotCacheModel? cache)
    {
        if (string.IsNullOrWhiteSpace(field.SelectedItem) ||
            cache is null ||
            field.SourceFieldIndex < 0 ||
            field.SourceFieldIndex >= cache.Fields.Count ||
            cache.Fields[field.SourceFieldIndex].SharedItems is not { Count: > 0 } sharedItems)
        {
            return null;
        }

        for (var index = 0; index < sharedItems.Count; index++)
        {
            if (string.Equals(sharedItems[index], field.SelectedItem, StringComparison.Ordinal))
                return index;
        }

        return null;
    }

    // Rewrites each preserved <pivotField>'s <items><item hidden="..."/></items> flags from the
    // corresponding PivotFieldModel.SelectedItems (the manual item-filter's visible-item list), for any
    // row/column/page field the model records an explicit selection for. A field with SelectedItems ==
    // null has no explicit selection recorded (the user never touched its item filter) and is left
    // completely untouched, preserving whatever the source file originally carried.
    private static bool RewritePreservedPivotFieldItemFilters(
        XElement pivotTableDefinitionRoot,
        PivotTableModel pivot,
        PivotCacheModel? cache,
        XNamespace workbookNs)
    {
        if (cache is null)
            return false;

        var pivotFieldsElement = pivotTableDefinitionRoot.Element(workbookNs + "pivotFields");
        if (pivotFieldsElement is null)
            return false;

        var pivotFieldElements = pivotFieldsElement.Elements(workbookNs + "pivotField").ToList();
        var changed = false;
        for (var fieldIndex = 0; fieldIndex < pivotFieldElements.Count && fieldIndex < cache.Fields.Count; fieldIndex++)
        {
            var model = FindPreservedPivotField(pivot, fieldIndex);
            if (model?.SelectedItems is not { } selectedItems)
                continue;

            var cacheField = cache.Fields[fieldIndex];
            if (cacheField.SharedItems is not { Count: > 0 } sharedItems)
                continue;

            var itemsElement = pivotFieldElements[fieldIndex].Element(workbookNs + "items");
            if (itemsElement is null)
                continue;

            var itemElements = itemsElement.Elements(workbookNs + "item").ToList();
            var rawToMaterialized = ResolvePreservedRawToMaterializedIndexMap(
                itemElements, cacheField.SharedItemCount, sharedItems.Count);
            if (rawToMaterialized is null)
                continue;

            var selectedSet = new HashSet<string>(selectedItems, StringComparer.Ordinal);
            foreach (var itemElement in itemElements)
            {
                var rawIndex = XlsxXmlAttributeReader.ReadIntAttribute(itemElement, "x");
                if (rawIndex is null || !rawToMaterialized.TryGetValue(rawIndex.Value, out var materializedIndex))
                    continue;

                var value = sharedItems[materializedIndex];
                var shouldBeHidden = !selectedSet.Contains(value);
                var isHidden = XlsxXmlAttributeReader.ReadBoolAttribute(itemElement, "hidden");
                if (shouldBeHidden == isHidden)
                    continue;

                if (shouldBeHidden)
                    itemElement.SetAttributeValue("hidden", "1");
                else
                    itemElement.Attribute("hidden")?.Remove();
                changed = true;
            }
        }

        return changed;
    }

    private static PivotFieldModel? FindPreservedPivotField(PivotTableModel pivot, int sourceFieldIndex) =>
        pivot.RowFields
            .Concat(pivot.ColumnFields)
            .Concat(pivot.PageFields)
            .LastOrDefault(field => field.SourceFieldIndex == sourceFieldIndex);

    // Maps each raw OOXML shared-item index (the pivotField item's own "x" attribute, which includes any
    // dropped <m/> blank entries) to its materialized FreeX.Core.Model SharedItems index. When the
    // field's declared sharedItems count matches (or there is none), the mapping is the identity.
    // Otherwise reconstructs it purely from the preserved <items> list's own "m" (missing) flags --
    // mirroring XlsxPivotTableReader.Fields.cs's TryResolveHiddenIndexesAcrossMissingSharedItems -- and
    // returns null (decline to rewrite this field at all) when that reconstruction is ambiguous.
    private static Dictionary<int, int>? ResolvePreservedRawToMaterializedIndexMap(
        List<XElement> itemElements,
        int? declaredCount,
        int materializedCount)
    {
        if (declaredCount is not { } declared || declared <= materializedCount)
        {
            var identity = new Dictionary<int, int>();
            for (var index = 0; index < materializedCount; index++)
                identity[index] = index;
            return identity;
        }

        var seenRawIndexes = new HashSet<int>();
        var realRawIndexes = new List<int>();
        var missingRawIndexCount = 0;
        foreach (var itemElement in itemElements)
        {
            var rawIndex = XlsxXmlAttributeReader.ReadIntAttribute(itemElement, "x");
            if (rawIndex is not { } index)
                continue;

            if (index < 0 || index >= declared || !seenRawIndexes.Add(index))
                return null;

            if (XlsxXmlAttributeReader.ReadBoolAttribute(itemElement, "m"))
                missingRawIndexCount++;
            else
                realRawIndexes.Add(index);
        }

        if (seenRawIndexes.Count != declared ||
            realRawIndexes.Count != materializedCount ||
            missingRawIndexCount != declared - materializedCount)
        {
            return null;
        }

        realRawIndexes.Sort();
        var map = new Dictionary<int, int>(realRawIndexes.Count);
        for (var rank = 0; rank < realRawIndexes.Count; rank++)
            map[realRawIndexes[rank]] = rank;

        return map;
    }

    // R54-io-pivot-filter-3-1: value/label/top-N pivot filter edits (add/change/clear) made to the
    // loaded PivotTableModel after Load() were silently dropped on save, because nothing on this
    // preserved-part path ever wrote pivot.ValueFilters/LabelFilters back -- XlsxPivotTableWriter.Save
    // (the only code that emits <valueFilters>/native <filters> XML) is gated behind
    // !hasSourcePackage and never runs here. Rewrite the preserved part's filter state from the model,
    // but ONLY when it actually differs from what is currently encoded there (decoded via the same
    // native-token/invented-token mappings XlsxPivotTableReader uses) -- this keeps a file the user never
    // touched byte-stable, rather than unconditionally converting a perfectly valid, Excel-authored
    // native <filters> element into FreeX's own non-schema <valueFilters>/<labelFilters> elements on
    // every single save.
    // R83-order-guard-invented-sweep-1: mirrors XlsxPivotTableWriter's own fresh-part fix
    // (R82-io-pivot-layout-5-2) -- only AboveAverage/BelowAverage value filters (which have no real
    // ST_PivotFilterType token, see ToNativePivotValueFilterKindText) still go through the invented
    // <valueFilters> shape; every other value-filter kind, plus every label-filter kind, now goes
    // through the real CT_PivotFilters <filters> shape via ToPivotFiltersXml instead of the invented
    // <labelFilters> element, which is never written by this path any more.
    private static bool RewritePreservedPivotValueAndLabelFilters(
        XElement pivotTableDefinitionRoot,
        PivotTableModel pivot,
        XNamespace workbookNs)
    {
        var existingValueFilters = ReadPreservedPivotValueFilters(pivotTableDefinitionRoot, workbookNs);
        var existingLabelFilters = ReadPreservedPivotLabelFilters(pivotTableDefinitionRoot, workbookNs);

        var valueFiltersChanged = !existingValueFilters.SequenceEqual(pivot.ValueFilters);
        var labelFiltersChanged = !existingLabelFilters.SequenceEqual(pivot.LabelFilters);
        if (!valueFiltersChanged && !labelFiltersChanged)
            return false;

        // Remove every previously-preserved representation (the real native <filters> element AND any
        // earlier FreeX-authored invented <valueFilters>/<labelFilters> elements) so re-adding below can't
        // create duplicates that XlsxPivotTableReader would concatenate back together on the next Load().
        pivotTableDefinitionRoot.Element(workbookNs + "filters")?.Remove();
        pivotTableDefinitionRoot.Element(workbookNs + "valueFilters")?.Remove();
        pivotTableDefinitionRoot.Element(workbookNs + "labelFilters")?.Remove();

        var newValueFilters = XlsxPivotTableWriter.ToPivotValueFiltersXml(
            pivot.ValueFilters.Where(filter => filter.Kind is PivotValueFilterKind.AboveAverage or PivotValueFilterKind.BelowAverage).ToList(),
            workbookNs);
        var newFilters = XlsxPivotTableWriter.ToPivotFiltersXml(pivot.ValueFilters, pivot.LabelFilters, workbookNs);

        // Mirror XlsxPivotTableWriter's own element order for a fresh part: valueFilters, then the real
        // <filters>, both AFTER pivotTableStyleInfo (required and always present) -- see R82-io-pivot-
        // layout-5-2 / R83-meta-1 for why pivotTableStyleInfo now precedes these elements instead of
        // following them. AddBeforeSelf places each newly-inserted element immediately before the
        // anchor, so the LAST call ends up closest to the anchor and thus last in document order:
        // insert newValueFilters first (pushed earlier) and newFilters second (ends up right before
        // the anchor) so the final order is valueFilters-then-filters, matching
        // XlsxPivotTableWriter.cs's own emission order for a fresh part.
        var anchor = pivotTableDefinitionRoot.Element(workbookNs + "pivotSorts")
            ?? pivotTableDefinitionRoot.Element(workbookNs + "extLst");
        InsertBeforeAnchorOrAppend(pivotTableDefinitionRoot, anchor, newValueFilters);
        InsertBeforeAnchorOrAppend(pivotTableDefinitionRoot, anchor, newFilters);

        return true;
    }

    private static void InsertBeforeAnchorOrAppend(XElement root, XElement? anchor, XElement? element)
    {
        if (element is null)
            return;

        if (anchor is not null)
            anchor.AddBeforeSelf(element);
        else
            root.Add(element);
    }

    // Decodes the CURRENTLY preserved value filters (both FreeX's invented <valueFilters> element and
    // the real native <filters> element), combined in the exact same order XlsxPivotTableReader.Load
    // combines them (invented first, then native), purely so RewritePreservedPivotValueAndLabelFilters
    // above can tell whether the model has actually changed since load.
    private static List<PivotValueFilterModel> ReadPreservedPivotValueFilters(XElement root, XNamespace workbookNs)
    {
        var invented = root.Element(workbookNs + "valueFilters")?
            .Elements(workbookNs + "valueFilter")
            .Select(filter => new PivotValueFilterModel(
                XlsxXmlAttributeReader.ReadIntAttribute(filter, "dataField") ?? -1,
                DecodeInventedPivotValueFilterKind(filter.Attribute("type")?.Value),
                XlsxXmlAttributeReader.ReadIntAttribute(filter, "count") ?? 0,
                XlsxXmlAttributeReader.ReadDoubleAttribute(filter, "comparisonValue"),
                XlsxXmlAttributeReader.ReadDoubleAttribute(filter, "comparisonValue2"),
                XlsxXmlAttributeReader.ReadIntAttribute(filter, "field")))
            .Where(filter => filter.DataFieldIndex >= 0 &&
                             (filter.Count > 0 ||
                              filter.ComparisonValue is not null ||
                              filter.Kind is PivotValueFilterKind.AboveAverage or PivotValueFilterKind.BelowAverage))
            .ToList()
            ?? [];

        var nativeFiltersElement = root.Element(workbookNs + "filters");
        var native = nativeFiltersElement?
            .Elements(workbookNs + "filter")
            .Select(filter =>
            {
                var kind = DecodeNativePivotValueFilterKind(filter.Attribute("type")?.Value);
                if (kind is null)
                    return null;

                return new PivotValueFilterModel(
                    XlsxXmlAttributeReader.ReadIntAttribute(filter, "iMeasureFld") ?? XlsxXmlAttributeReader.ReadIntAttribute(filter, "dataField") ?? 0,
                    kind.Value,
                    XlsxXmlAttributeReader.ReadIntAttribute(filter, "count") ?? XlsxXmlAttributeReader.ReadIntAttribute(filter, "val") ?? (kind.Value is PivotValueFilterKind.Top or PivotValueFilterKind.Bottom ? 10 : 0),
                    ReadNativePivotFilterDoubleValueLocal(filter, "stringValue1", "value1", "val"),
                    ReadNativePivotFilterDoubleValueLocal(filter, "stringValue2", "value2"),
                    XlsxXmlAttributeReader.ReadIntAttribute(filter, "fld") ?? XlsxXmlAttributeReader.ReadIntAttribute(filter, "field"));
            })
            .Where(filter => filter is not null)
            .Select(filter => filter!)
            .ToList()
            ?? [];

        return invented.Concat(native).ToList();
    }

    // Sibling of ReadPreservedPivotValueFilters, for label filters.
    private static List<PivotLabelFilterModel> ReadPreservedPivotLabelFilters(XElement root, XNamespace workbookNs)
    {
        var invented = root.Element(workbookNs + "labelFilters")?
            .Elements(workbookNs + "labelFilter")
            .Select(filter => new PivotLabelFilterModel(
                XlsxXmlAttributeReader.ReadIntAttribute(filter, "field") ?? -1,
                DecodeInventedPivotLabelFilterKind(filter.Attribute("type")?.Value),
                filter.Attribute("value")?.Value ?? "",
                filter.Attribute("value2")?.Value))
            .Where(filter => filter.SourceFieldIndex >= 0 && !string.IsNullOrEmpty(filter.Value))
            .ToList()
            ?? [];

        var nativeFiltersElement = root.Element(workbookNs + "filters");
        var native = nativeFiltersElement?
            .Elements(workbookNs + "filter")
            .Select(filter =>
            {
                var kind = DecodeNativePivotLabelFilterKind(filter.Attribute("type")?.Value);
                if (kind is null)
                    return null;

                var value = ReadNativePivotFilterTextValueLocal(filter, "stringValue1", "value1", "val");
                if (string.IsNullOrEmpty(value) && !PreservedPivotDateFilterKindsWithoutValue.Contains(kind.Value))
                    return null;

                return new PivotLabelFilterModel(
                    XlsxXmlAttributeReader.ReadIntAttribute(filter, "fld") ?? XlsxXmlAttributeReader.ReadIntAttribute(filter, "field") ?? -1,
                    kind.Value,
                    value ?? "",
                    ReadNativePivotFilterTextValueLocal(filter, "stringValue2", "value2"));
            })
            .Where(filter => filter is not null && filter.SourceFieldIndex >= 0)
            .Select(filter => filter!)
            .ToList()
            ?? [];

        return invented.Concat(native).ToList();
    }

    private static readonly HashSet<PivotLabelFilterKind> PreservedPivotDateFilterKindsWithoutValue =
    [
        PivotLabelFilterKind.Yesterday,
        PivotLabelFilterKind.Today,
        PivotLabelFilterKind.Tomorrow,
        PivotLabelFilterKind.LastWeek,
        PivotLabelFilterKind.ThisWeek,
        PivotLabelFilterKind.NextWeek,
        PivotLabelFilterKind.LastMonth,
        PivotLabelFilterKind.ThisMonth,
        PivotLabelFilterKind.NextMonth,
        PivotLabelFilterKind.LastQuarter,
        PivotLabelFilterKind.ThisQuarter,
        PivotLabelFilterKind.NextQuarter,
        PivotLabelFilterKind.LastYear,
        PivotLabelFilterKind.ThisYear,
        PivotLabelFilterKind.NextYear,
        PivotLabelFilterKind.YearToDate,
    ];

    // Mirrors XlsxPivotTableReader.Converters.cs's ReadPivotValueFilterKind (the invented-format token
    // decode) -- duplicated locally because that method is private to XlsxPivotTableReader.
    private static PivotValueFilterKind DecodeInventedPivotValueFilterKind(string? value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            "bottom" => PivotValueFilterKind.Bottom,
            "greaterthan" or "greater_than" => PivotValueFilterKind.GreaterThan,
            "greaterthanorequal" or "greater_than_or_equal" => PivotValueFilterKind.GreaterThanOrEqual,
            "lessthan" or "less_than" => PivotValueFilterKind.LessThan,
            "lessthanorequal" or "less_than_or_equal" => PivotValueFilterKind.LessThanOrEqual,
            "equals" or "equal" => PivotValueFilterKind.Equals,
            "doesnotequal" or "not_equal" => PivotValueFilterKind.DoesNotEqual,
            "between" => PivotValueFilterKind.Between,
            "notbetween" or "not_between" => PivotValueFilterKind.NotBetween,
            "aboveaverage" or "above_average" => PivotValueFilterKind.AboveAverage,
            "belowaverage" or "below_average" => PivotValueFilterKind.BelowAverage,
            _ => PivotValueFilterKind.Top
        };

    // Mirrors XlsxPivotTableReader.Converters.cs's ReadPivotLabelFilterKind.
    private static PivotLabelFilterKind DecodeInventedPivotLabelFilterKind(string? value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            "doesnotequal" or "not_equal" => PivotLabelFilterKind.DoesNotEqual,
            "beginswith" or "begins_with" => PivotLabelFilterKind.BeginsWith,
            "endswith" or "ends_with" => PivotLabelFilterKind.EndsWith,
            "contains" => PivotLabelFilterKind.Contains,
            "doesnotcontain" or "does_not_contain" => PivotLabelFilterKind.DoesNotContain,
            "greaterthan" or "greater_than" => PivotLabelFilterKind.GreaterThan,
            "greaterthanorequal" or "greater_than_or_equal" => PivotLabelFilterKind.GreaterThanOrEqual,
            "lessthan" or "less_than" => PivotLabelFilterKind.LessThan,
            "lessthanorequal" or "less_than_or_equal" => PivotLabelFilterKind.LessThanOrEqual,
            "between" => PivotLabelFilterKind.Between,
            _ => PivotLabelFilterKind.Equals
        };

    // Mirrors XlsxPivotTableReader.FiltersAndSorts.cs's ReadNativePivotValueFilterKind (the real
    // ST_PivotFilterType token decode) -- duplicated locally because that method is private to
    // XlsxPivotTableReader.
    private static PivotValueFilterKind? DecodeNativePivotValueFilterKind(string? value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            "count" or "topcount" or "top" => PivotValueFilterKind.Top,
            "bottomcount" or "bottom" => PivotValueFilterKind.Bottom,
            "valueequal" or "valueequals" => PivotValueFilterKind.Equals,
            "valuenotequal" or "valuedoesnotequal" => PivotValueFilterKind.DoesNotEqual,
            "valuegreaterthan" => PivotValueFilterKind.GreaterThan,
            "valuegreaterthanorequal" => PivotValueFilterKind.GreaterThanOrEqual,
            "valuelessthan" => PivotValueFilterKind.LessThan,
            "valuelessthanorequal" => PivotValueFilterKind.LessThanOrEqual,
            "valuebetween" => PivotValueFilterKind.Between,
            "valuenotbetween" => PivotValueFilterKind.NotBetween,
            _ => null
        };

    // Mirrors XlsxPivotTableReader.FiltersAndSorts.cs's ReadNativePivotLabelFilterKind.
    private static PivotLabelFilterKind? DecodeNativePivotLabelFilterKind(string? value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            "captionequal" or "captionequals" => PivotLabelFilterKind.Equals,
            "captionnotequal" or "captiondoesnotequal" => PivotLabelFilterKind.DoesNotEqual,
            "captionbeginswith" => PivotLabelFilterKind.BeginsWith,
            "captionendswith" => PivotLabelFilterKind.EndsWith,
            "captioncontains" => PivotLabelFilterKind.Contains,
            "captionnotcontains" or "captiondoesnotcontain" => PivotLabelFilterKind.DoesNotContain,
            "captiongreaterthan" => PivotLabelFilterKind.GreaterThan,
            "captiongreaterthanorequal" => PivotLabelFilterKind.GreaterThanOrEqual,
            "captionlessthan" => PivotLabelFilterKind.LessThan,
            "captionlessthanorequal" => PivotLabelFilterKind.LessThanOrEqual,
            "captionbetween" => PivotLabelFilterKind.Between,
            "dateequal" => PivotLabelFilterKind.DateEqual,
            "datenotequal" => PivotLabelFilterKind.DateNotEqual,
            "dateolderthan" => PivotLabelFilterKind.DateOlderThan,
            "dateolderthanorequal" => PivotLabelFilterKind.DateOlderThanOrEqual,
            "datenewerthan" => PivotLabelFilterKind.DateNewerThan,
            "datenewerthanorequal" => PivotLabelFilterKind.DateNewerThanOrEqual,
            "datebetween" => PivotLabelFilterKind.DateBetween,
            "datenotbetween" => PivotLabelFilterKind.DateNotBetween,
            "yesterday" => PivotLabelFilterKind.Yesterday,
            "today" => PivotLabelFilterKind.Today,
            "tomorrow" => PivotLabelFilterKind.Tomorrow,
            "lastweek" => PivotLabelFilterKind.LastWeek,
            "thisweek" => PivotLabelFilterKind.ThisWeek,
            "nextweek" => PivotLabelFilterKind.NextWeek,
            "lastmonth" => PivotLabelFilterKind.LastMonth,
            "thismonth" => PivotLabelFilterKind.ThisMonth,
            "nextmonth" => PivotLabelFilterKind.NextMonth,
            "lastquarter" => PivotLabelFilterKind.LastQuarter,
            "thisquarter" => PivotLabelFilterKind.ThisQuarter,
            "nextquarter" => PivotLabelFilterKind.NextQuarter,
            "lastyear" => PivotLabelFilterKind.LastYear,
            "thisyear" => PivotLabelFilterKind.ThisYear,
            "nextyear" => PivotLabelFilterKind.NextYear,
            "yeartodate" => PivotLabelFilterKind.YearToDate,
            _ => null
        };

    private static string? ReadNativePivotFilterTextValueLocal(XElement filter, params string[] attributeNames)
    {
        foreach (var attributeName in attributeNames)
        {
            var value = filter.Attribute(attributeName)?.Value;
            if (!string.IsNullOrEmpty(value))
                return value;
        }

        return null;
    }

    private static double? ReadNativePivotFilterDoubleValueLocal(XElement filter, params string[] attributeNames)
    {
        foreach (var attributeName in attributeNames)
        {
            if (double.TryParse(filter.Attribute(attributeName)?.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                return value;
        }

        return null;
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

    /// <summary>
    /// R95-io-chart-hyperlink-real-pipeline / R96-io-chart-hyperlink-name-key: maps each source-package
    /// sheet's OWN drawing part's chart graphicFrame hyperlinks (object-level AND chart-title) to
    /// <see cref="ChartHyperlinkPair"/> entries, KEYED by each chart graphicFrame's stable
    /// <c>cNvPr@name</c> -- read directly from the TRUE source .xlsx package via
    /// <see cref="XlsxWorksheetChartWriter.ReadSourceChartHyperlinks"/>.
    /// <para>
    /// This is the chart-writer sibling of <see cref="GetSourceDrawingObjectHyperlinksBySheet"/> below,
    /// fixing the identical bug for charts: <see cref="XlsxWorksheetChartWriter"/>'s R41
    /// hyperlink-preservation code used to read the CURRENT (pre-rebuild) drawing/chart bytes out of the
    /// in-progress package being built for this very save -- but through a real
    /// <see cref="XlsxFileAdapter.Save"/>, that package is a freshly-ClosedXML-generated workbook with
    /// no original drawing/chart parts at all (ClosedXML always builds brand new, chart-less XML), so
    /// every chart-object and chart-title hyperlink was silently and permanently dropped on the very
    /// first save after opening a file that has one. R41's own tests never caught this because they call
    /// <c>XlsxWorksheetChartWriter.Save</c> directly with a hand-seeded package standing in for "the
    /// archive", which is exactly the shape the real pipeline does not have.
    /// </para>
    /// <para>
    /// R96: R95 initially matched these pairs to the CURRENT save's charts by document-order position
    /// (mirroring how this file numbers chart parts positionally elsewhere), which desyncs -- silently
    /// misattributing one chart's hyperlink onto a different chart, not merely dropping it -- the moment
    /// a sheet's chart set is added to, deleted from, or reordered between load and save. Keying by
    /// <c>cNvPr@name</c> instead (the same name <see cref="ChartModel.Name"/> round-trips through
    /// load/save) fixes this exactly as <see cref="GetSourceDrawingObjectHyperlinksBySheet"/> already
    /// does for pictures/shapes/text boxes.
    /// </para>
    /// </summary>
    private static IReadOnlyDictionary<string, IReadOnlyDictionary<string, ChartHyperlinkPair>> GetSourceChartHyperlinksBySheet(Workbook workbook)
    {
        var drawingPathsBySheet = GetSourceDrawingPathsBySheet(workbook);
        if (drawingPathsBySheet.Count == 0 || !SourcePackages.TryGetValue(workbook, out var sourcePackage))
            return EmptyChartHyperlinksBySheet;

        try
        {
            using var sourceStream = sourcePackage.OpenRead();
            using var sourceArchive = new ZipArchive(sourceStream, ZipArchiveMode.Read);

            XNamespace spreadsheetDrawingNs = "http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing";
            XNamespace drawingNs = "http://schemas.openxmlformats.org/drawingml/2006/main";
            XNamespace chartNs = "http://schemas.openxmlformats.org/drawingml/2006/chart";
            XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
            XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

            var result = new Dictionary<string, IReadOnlyDictionary<string, ChartHyperlinkPair>>(StringComparer.OrdinalIgnoreCase);
            foreach (var (sheetName, drawingPath) in drawingPathsBySheet)
            {
                var hyperlinks = XlsxWorksheetChartWriter.ReadSourceChartHyperlinks(
                    sourceArchive, drawingPath, spreadsheetDrawingNs, drawingNs, chartNs, relNs, packageRelNs);
                if (hyperlinks.Count > 0)
                    result[sheetName] = hyperlinks;
            }

            return result;
        }
        catch
        {
            return EmptyChartHyperlinksBySheet;
        }
    }

    private static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, ChartHyperlinkPair>> EmptyChartHyperlinksBySheet =
        new Dictionary<string, IReadOnlyDictionary<string, ChartHyperlinkPair>>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// R95-io-drawing-hyperlink-2-2: maps each source-package sheet's OWN drawing part's
    /// picture/text-box/shape object-level hyperlinks (an <c>a:hlinkClick</c> on a <c>xdr:cNvPr</c>),
    /// keyed by the object's stable <c>cNvPr@name</c> -- read directly from the TRUE source .xlsx
    /// package via <see cref="XlsxWorksheetDrawingObjectWriter.ReadOldDrawingObjectHyperlinksByName"/>.
    /// <para>
    /// This must read the TRUE source package (like <see cref="GetSourceDrawingPathsBySheet"/> does),
    /// NOT the in-progress generated package: at the point <see cref="XlsxWorksheetDrawingObjectWriter.Save"/>
    /// runs, the generated package is a freshly built ClosedXML workbook with no drawing parts of its
    /// own yet, so it never carries the original hyperlink bytes. Without this, a fill/outline/gradient/
    /// effect edit on a shape (or a colour/rotation edit on a text box) -- which clears
    /// <c>IsSourceLoaded</c> so the writer reconstructs the object's anchor from the edited model --
    /// silently and permanently dropped any hyperlink the object carried, even though the edit itself
    /// has nothing to do with the hyperlink.
    /// </para>
    /// </summary>
    private static IReadOnlyDictionary<string, IReadOnlyDictionary<string, (string Target, string? TargetMode)>> GetSourceDrawingObjectHyperlinksBySheet(Workbook workbook)
    {
        var drawingPathsBySheet = GetSourceDrawingPathsBySheet(workbook);
        if (drawingPathsBySheet.Count == 0 || !SourcePackages.TryGetValue(workbook, out var sourcePackage))
            return EmptyDrawingObjectHyperlinksBySheet;

        try
        {
            using var sourceStream = sourcePackage.OpenRead();
            using var sourceArchive = new ZipArchive(sourceStream, ZipArchiveMode.Read);

            XNamespace spreadsheetDrawingNs = "http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing";
            XNamespace drawingNs = "http://schemas.openxmlformats.org/drawingml/2006/main";
            XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
            XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

            var result = new Dictionary<string, IReadOnlyDictionary<string, (string, string?)>>(StringComparer.OrdinalIgnoreCase);
            foreach (var (sheetName, drawingPath) in drawingPathsBySheet)
            {
                var drawingRelsPath = XlsxPackagePath.GetRelationshipPartPath(drawingPath);
                var hyperlinksByName = XlsxWorksheetDrawingObjectWriter.ReadOldDrawingObjectHyperlinksByName(
                    sourceArchive, drawingPath, drawingRelsPath, spreadsheetDrawingNs, drawingNs, relNs, packageRelNs);
                if (hyperlinksByName.Count > 0)
                    result[sheetName] = hyperlinksByName;
            }

            return result;
        }
        catch
        {
            return EmptyDrawingObjectHyperlinksBySheet;
        }
    }

    private static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, (string Target, string? TargetMode)>> EmptyDrawingObjectHyperlinksBySheet =
        new Dictionary<string, IReadOnlyDictionary<string, (string, string?)>>(StringComparer.OrdinalIgnoreCase);

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

    // Returns the set of xl/media/* entry names already present in the source package (or an empty
    // set when there is no source package). WriteBackground runs before PreserveSourcePackageParts
    // copies the source's own xl/media/* entries into the generated archive, so a background image
    // saved under the user's raw filename (e.g. "image1.png") can otherwise claim a media name the
    // source package already uses for an authored picture; CopyUnknownPackageParts then skips
    // copying that source media because the name is already taken, leaving other drawings pointing
    // at the wrong (background) image. Reserving these names lets the background writer pick a
    // collision-free name up front instead of silently shadowing preserved source media.
    private static IReadOnlySet<string> GetSourceMediaEntryNames(Workbook workbook)
    {
        if (!SourcePackages.TryGetValue(workbook, out var sourcePackage))
            return EmptyMediaEntryNames;

        try
        {
            using var sourceStream = sourcePackage.OpenRead();
            using var sourceArchive = new ZipArchive(sourceStream, ZipArchiveMode.Read);

            const string prefix = "xl/media/";
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in sourceArchive.Entries)
            {
                if (entry.FullName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    names.Add(entry.FullName);
            }

            return names;
        }
        catch
        {
            return EmptyMediaEntryNames;
        }
    }

    private static readonly IReadOnlySet<string> EmptyMediaEntryNames =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);

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
        /// <summary>
        /// True when any sheet has at least one cell with a preserved phonetic guide (furigana).
        /// Gates <see cref="XlsxWorksheetCellPhoneticGuideWriter"/> (R78-selfreg-twin-sweep-1),
        /// which re-emits a cell's <c>&lt;rPh&gt;</c>/<c>&lt;phoneticPr&gt;</c> markup after the
        /// full-save (ClosedXML) rich-text write above -- ApplyRichTextRuns' IXLRichText API has
        /// no way to express a phonetic guide, so it would otherwise be silently dropped.
        /// </summary>
        public bool HasCellPhoneticGuides;
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
        /// True when any sheet has a live AutoFilter. <see cref="XlsxNamedRangeMapper.SaveToPackage"/>
        /// is the ONLY code path that emits/keeps in sync the built-in <c>_xlnm._FilterDatabase</c>
        /// name for a sheet's AutoFilter (R62-io-defined-name-print-6-2), so the SaveToPackage gate
        /// must run whenever this is true even when the workbook has zero ordinary named
        /// ranges/formulas.
        /// </summary>
        public bool HasLiveAutoFilter;
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
        /// <summary>
        /// True when any sheet has an internal hyperlink whose Bookmark targets a bang-less defined
        /// name. Gates <see cref="FixFabricatedDefinedNameHyperlinkLocations"/> (R55-io-hyperlink-
        /// round-trip-5-1), the FULL-save counterpart of the PATCH-save R38-io-hyperlink-2-1 fix.
        /// </summary>
        public bool HasBareInternalHyperlinkBookmarks;
        /// <summary>
        /// True when any sheet has an EXTERNAL hyperlink (Existing File/Web Page, not
        /// PlaceInThisDocument) whose <see cref="HyperlinkMetadata.Bookmark"/> ("location" sub-
        /// address) is non-empty -- Excel's "Existing File &gt; Bookmark..." feature. Gates
        /// <see cref="FixExternalHyperlinkBookmarkLocations"/> (R96-io-hyperlink-external-bookmark),
        /// which backfills the "location" attribute ClosedXML's XLHyperlink can never emit alongside
        /// an r:id on the same element (its writer is mutually exclusive on IsExternal).
        /// </summary>
        public bool HasExternalHyperlinkBookmarks;

        public static XlsxPostProcessingFeaturePlan Create(Workbook workbook)
        {
            var plan = new XlsxPostProcessingFeaturePlan();
            plan.HasWorkbookPostProcessingMetadata = XlsxWorkbookMetadataWriter.HasPostProcessingMetadata(workbook);
            plan.HasWorkbookReplayMetadata = XlsxWorkbookMetadataWriter.HasSourcePackageReplayMetadata(workbook);
            plan.HasCustomViews = workbook.CustomViews.Count > 0;
            // Mirrors XlsxWorksheetSourceIndependentMetadataBatchWriter.Save's own workbook-level gate:
            // sheetView/@tabSelected must be kept in lockstep with bookViews/@activeTab (written
            // whenever workbook.ActiveSheetIndex is set) on every save, even for a workbook with no
            // other native worksheet metadata at all (e.g. any brand-new/never-loaded-from-xlsx
            // workbook).
            plan.HasSourceIndependentMetadata |= workbook.ActiveSheetIndex is not null;
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
            HasCellPhoneticGuides |= sheet.CellPhoneticGuides.Count > 0;
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
            HasLiveAutoFilter |= !string.IsNullOrWhiteSpace(
                XlsxWorksheetAutoFilterXmlMapper.GetEffectiveReference(sheet.AutoFilter));
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
            HasBareInternalHyperlinkBookmarks |= HasBareInternalHyperlinkBookmarks(sheet);
            HasExternalHyperlinkBookmarks |= HasExternalHyperlinkBookmarks(sheet);
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
