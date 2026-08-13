using System.IO.Compression;
using System.Xml.Linq;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

internal static partial class XlsxWorksheetMetadataPreserver
{
    internal static bool HasPreservableSourceWorksheetMetadata(XDocument sourceWorksheetXml, XNamespace workbookNs)
    {
        var retainedChildNames = GetRetainedWorksheetChildNames(workbookNs);
        var sourceBlocks = retainedChildNames
            .Select(name => sourceWorksheetXml.Root?.Element(name))
            .Where(element => element is not null)
            .Cast<XElement>()
            .ToList();
        var sourceSheetProperties = sourceWorksheetXml.Root?.Element(workbookNs + "sheetPr");
        var sourceSheetFormatProperties = sourceWorksheetXml.Root?.Element(workbookNs + "sheetFormatPr");
        var sourceDimension = sourceWorksheetXml.Root?.Element(workbookNs + "dimension");
        var sourcePrintOptions = sourceWorksheetXml.Root?.Element(workbookNs + "printOptions");
        var sourcePageMargins = sourceWorksheetXml.Root?.Element(workbookNs + "pageMargins");
        var sourcePageSetup = sourceWorksheetXml.Root?.Element(workbookNs + "pageSetup");
        var sourceHeaderFooter = sourceWorksheetXml.Root?.Element(workbookNs + "headerFooter");
        var sourceMergeCells = sourceWorksheetXml.Root?.Element(workbookNs + "mergeCells");
        var sourceColumns = sourceWorksheetXml.Root?.Element(workbookNs + "cols");
        var sourceSheetData = sourceWorksheetXml.Root?.Element(workbookNs + "sheetData");
        var sourceSheetProtection = sourceWorksheetXml.Root?.Element(workbookNs + "sheetProtection");
        var sourceSheetViews = sourceWorksheetXml.Root?.Element(workbookNs + "sheetViews");
        var sourceHyperlinks = sourceWorksheetXml.Root?.Element(workbookNs + "hyperlinks");
        var sourceExtensionList = sourceWorksheetXml.Root?.Element(workbookNs + "extLst");

        return HasPreservableSourceWorksheetMetadata(
            sourceBlocks,
            sourceSheetProperties,
            sourceSheetFormatProperties,
            sourceDimension,
            sourcePrintOptions,
            sourcePageMargins,
            sourcePageSetup,
            sourceHeaderFooter,
            sourceMergeCells,
            sourceColumns,
            sourceSheetData,
            sourceSheetProtection,
            sourceSheetViews,
            sourceHyperlinks,
            sourceExtensionList,
            workbookNs);
    }

    public static void Preserve(ZipArchive sourceArchive, ZipArchive targetArchive, Workbook workbook)
    {
        var context = XlsxSourcePackagePreservationContext.TryCreate(sourceArchive, targetArchive, workbook);
        Preserve(workbook, context);
    }

    public static void Preserve(
        Workbook workbook,
        XlsxSourcePackagePreservationContext? context,
        IReadOnlySet<string>? worksheetsWithPreservableSourceMetadata = null)
    {
        if (context is null)
            return;

        var sourceArchive = context.SourceArchive;
        var targetArchive = context.TargetArchive;
        var retainedChildNames = GetRetainedWorksheetChildNames(context.WorkbookNs);

        PreserveWorksheetMetadata(
            sourceArchive,
            targetArchive,
            workbook,
            retainedChildNames,
            context.WorkbookNs,
            context.RelNs,
            context.SourceSheets,
            context.TargetSheets,
            context,
            worksheetsWithPreservableSourceMetadata);
        RebindWorksheetCustomPropertyRelationships(
            context,
            worksheetsWithPreservableSourceMetadata);
    }

    private static void PreserveWorksheetMetadata(
        ZipArchive sourceArchive,
        ZipArchive targetArchive,
        Workbook workbook,
        XName[] retainedChildNames,
        XNamespace workbookNs,
        XNamespace relNs,
        IReadOnlyDictionary<string, string> sourceSheets,
        IReadOnlyDictionary<string, string> targetSheets,
        XlsxSourcePackagePreservationContext? context = null,
        IReadOnlySet<string>? worksheetsWithPreservableSourceMetadata = null)
    {
        XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
        foreach (var (sheetName, sourceWorksheetPath) in sourceSheets)
        {
            // R102-io-rename-worksheet-exclusion-sweep-1: sheetName is the LOAD-TIME name; a plain
            // rename makes the direct targetSheets lookup fail even though the sheet's own worksheet
            // part -- and all the unmodeled metadata this preserver carries forward (merge cells,
            // page setup, sheet protection, retained hyperlinks, etc.) -- is completely unaffected.
            // Fall back to a match on the sheet's own (rename-stable) worksheet part path, and resolve
            // the sheet's CURRENT name too since every workbook.GetSheet/GetModeled* call below only
            // knows sheets by their current (post-rename) name.
            //
            // R124-io-metadata-preserver-swap-identity-gap: a plain direct name lookup (and the
            // path-with-guard fallback) both stop being sound the moment a load-time NAME is reused by
            // a genuinely DIFFERENT physical sheet in the same save (a two-sheet swap, or delete-then-
            // rename-to-freed-name) -- see XlsxRenamedSourceSheetResolver's header comment for the full
            // R103 analysis. XlsxWorksheetFormControlPreserver and XlsxWorksheetDrawingReferencePreserver
            // already delegate to that shared, identity-verified resolver; this preserver used to
            // reimplement just the two unsound heuristics inline, so a swap misattributed sheet A's raw
            // unmodeled metadata (protectedRanges, sheetProtection, scenarios, rowBreaks/colBreaks,
            // oleObjects, controls, customSheetViews, page setup, ...) onto sheet B's physical part and
            // vice versa. Route through the shared resolver instead so this preserver inherits the same
            // Sheet.Id-verified resolution (falling back to the legacy string-based heuristics only when
            // no context -- and therefore no identity data -- is available at all).
            string targetWorksheetPath;
            string currentSheetName;
            if (context is not null)
            {
                if (!XlsxRenamedSourceSheetResolver.TryResolveCurrentSheet(
                        context, sheetName, sourceWorksheetPath, out currentSheetName, out targetWorksheetPath))
                {
                    continue;
                }
            }
            else if (targetSheets.TryGetValue(sheetName, out var directTargetPath))
            {
                targetWorksheetPath = directTargetPath;
                currentSheetName = sheetName;
            }
            else if (TryResolveTargetWorksheetPathByPath(sourceSheets, targetSheets, sourceWorksheetPath, out targetWorksheetPath))
            {
                currentSheetName = targetSheets
                    .First(pair => string.Equals(
                        XlsxPackagePath.NormalizePackagePath(pair.Value),
                        XlsxPackagePath.NormalizePackagePath(targetWorksheetPath),
                        StringComparison.OrdinalIgnoreCase))
                    .Key;
            }
            else
            {
                continue;
            }
            if (worksheetsWithPreservableSourceMetadata is not null &&
                !worksheetsWithPreservableSourceMetadata.Contains(sheetName))
            {
                continue;
            }

            var sourceWorksheetEntry = sourceArchive.GetEntry(sourceWorksheetPath);
            if (sourceWorksheetEntry is null)
                continue;

            if (worksheetsWithPreservableSourceMetadata is null &&
                !HasPreservableSourceWorksheetMetadata(sourceWorksheetEntry, workbookNs))
            {
                continue;
            }

            var targetWorksheetEntry = targetArchive.GetEntry(targetWorksheetPath);
            var sourceWorksheetXml = context?.GetSourceWorksheetXml(sourceWorksheetPath);
            if (sourceWorksheetXml is null)
            {
                sourceWorksheetXml = XlsxPackageXmlEditor.LoadXml(sourceWorksheetEntry);
            }

            if (targetWorksheetEntry is null)
                continue;

            var sourceBlocks = retainedChildNames
                .Select(name => sourceWorksheetXml.Root?.Element(name))
                .Where(element => element is not null)
                .Cast<XElement>()
                .ToList();
            var sourceSheetProperties = sourceWorksheetXml.Root?.Element(workbookNs + "sheetPr");
            var sourceSheetFormatProperties = sourceWorksheetXml.Root?.Element(workbookNs + "sheetFormatPr");
            var sourceDimension = sourceWorksheetXml.Root?.Element(workbookNs + "dimension");
            var sourcePrintOptions = sourceWorksheetXml.Root?.Element(workbookNs + "printOptions");
            var sourcePageMargins = sourceWorksheetXml.Root?.Element(workbookNs + "pageMargins");
            var sourcePageSetup = sourceWorksheetXml.Root?.Element(workbookNs + "pageSetup");
            var sourceHeaderFooter = sourceWorksheetXml.Root?.Element(workbookNs + "headerFooter");
            var sourceMergeCells = sourceWorksheetXml.Root?.Element(workbookNs + "mergeCells");
            var sourceColumns = sourceWorksheetXml.Root?.Element(workbookNs + "cols");
            var sourceSheetData = sourceWorksheetXml.Root?.Element(workbookNs + "sheetData");
            var sourceSheetProtection = sourceWorksheetXml.Root?.Element(workbookNs + "sheetProtection");
            var sourceSheetViews = sourceWorksheetXml.Root?.Element(workbookNs + "sheetViews");
            var sourceHyperlinks = sourceWorksheetXml.Root?.Element(workbookNs + "hyperlinks");
            var sourceExtensionList = sourceWorksheetXml.Root?.Element(workbookNs + "extLst");
            if (!HasPreservableSourceWorksheetMetadata(
                    sourceBlocks,
                    sourceSheetProperties,
                    sourceSheetFormatProperties,
                    sourceDimension,
                    sourcePrintOptions,
                    sourcePageMargins,
                    sourcePageSetup,
                    sourceHeaderFooter,
                    sourceMergeCells,
                    sourceColumns,
                    sourceSheetData,
                    sourceSheetProtection,
                    sourceSheetViews,
                    sourceHyperlinks,
                    sourceExtensionList,
                    workbookNs))
            {
                continue;
            }

            var targetWorksheetXml = XlsxPackageXmlEditor.LoadXml(targetWorksheetEntry);
            var targetRoot = targetWorksheetXml.Root;
            if (targetRoot is null)
                continue;

            var changed = false;
            Dictionary<string, XElement>? targetCellsByAddress = null;
            IReadOnlyDictionary<string, XElement> GetTargetCellsByAddress() =>
                targetCellsByAddress ??= BuildCellLookup(targetRoot.Element(workbookNs + "sheetData"), workbookNs);

            if (MergeWorksheetSheetProperties(sourceSheetProperties, targetRoot, workbookNs))
                changed = true;
            if (MergeWorksheetSheetFormatProperties(sourceSheetFormatProperties, targetRoot, workbookNs))
                changed = true;
            if (MergeWorksheetNativeOnlyElementAttributes(
                    sourceDimension,
                    targetRoot,
                    workbookNs + "dimension",
                    ModeledDimensionAttributes))
                changed = true;
            if (MergeWorksheetNativeOnlyElementAttributes(
                    sourcePrintOptions,
                    targetRoot,
                    workbookNs + "printOptions",
                    ModeledPrintOptionsAttributes))
                changed = true;
            if (MergeWorksheetNativeOnlyElementAttributes(
                    sourcePageMargins,
                    targetRoot,
                    workbookNs + "pageMargins",
                    ModeledPageMarginsAttributes))
                changed = true;
            if (MergeWorksheetNativeOnlyElementAttributes(
                    sourcePageSetup,
                    targetRoot,
                    workbookNs + "pageSetup",
                    ModeledPageSetupAttributes))
                changed = true;
            if (MergeWorksheetNativeOnlyElementAttributes(
                    sourceHeaderFooter,
                    targetRoot,
                    workbookNs + "headerFooter",
                    ModeledHeaderFooterAttributes))
                changed = true;
            if (MergeWorksheetColumnAttributes(sourceColumns, targetRoot, workbookNs))
                changed = true;
            if (MergeWorksheetRowAttributes(sourceSheetData, targetRoot, workbookNs))
                changed = true;
            if (MergeWorksheetCellNativeMetadata(sourceSheetData, GetTargetCellsByAddress, targetArchive, workbookNs))
                changed = true;
            if (MergeWorksheetMergedCellMetadata(sourceMergeCells, targetRoot, workbookNs))
                changed = true;
            if (MergeWorksheetSheetProtection(sourceSheetProtection, targetRoot, workbookNs, workbook.GetSheet(currentSheetName)))
                changed = true;
            if (MergeWorksheetSheetViews(sourceSheetViews, targetRoot, workbookNs, workbook.GetSheet(currentSheetName)))
                changed = true;
            if (MergeWorksheetHyperlinkMetadata(
                    sourceHyperlinks,
                    targetRoot,
                    workbookNs,
                    relNs,
                    workbook.GetSheet(currentSheetName),
                    sourceArchive,
                    targetArchive,
                    sourceWorksheetPath,
                    targetWorksheetPath,
                    packageRelNs))
                changed = true;
            foreach (var sourceBlock in sourceBlocks)
            {
                if (sourceBlock.Name == workbookNs + "protectedRanges")
                {
                    if (MergeWorksheetProtectedRanges(
                        sourceBlock,
                        targetRoot,
                        workbookNs,
                        XlsxAllowEditRangeMapper.GetModeledReferences(workbook, currentSheetName)))
                    {
                        changed = true;
                    }

                    continue;
                }
                if (sourceBlock.Name == workbookNs + "sheetCalcPr")
                {
                    if (MergeWorksheetCalculationProperties(sourceBlock, targetRoot, workbookNs))
                    {
                        changed = true;
                    }

                    continue;
                }
                if (sourceBlock.Name == workbookNs + "phoneticPr")
                {
                    if (MergeWorksheetPhoneticProperties(sourceBlock, targetRoot, workbookNs))
                    {
                        changed = true;
                    }

                    continue;
                }
                if (sourceBlock.Name == workbookNs + "customSheetViews")
                {
                    if (MergeWorksheetCustomSheetViews(
                        sourceBlock,
                        targetRoot,
                        workbookNs,
                        XlsxCustomViewMapper.GetModeledIds(workbook)))
                    {
                        changed = true;
                    }

                    continue;
                }
                if (sourceBlock.Name == workbookNs + "customProperties")
                {
                    if (MergeWorksheetCustomProperties(
                        sourceBlock,
                        targetRoot,
                        workbookNs,
                        XlsxWorksheetCustomPropertyMapper.GetModeledNames(workbook, currentSheetName)))
                    {
                        changed = true;
                    }

                    continue;
                }
                if (sourceBlock.Name == workbookNs + "rowBreaks")
                {
                    if (MergeWorksheetBreaks(
                            sourceBlock,
                            targetRoot,
                            workbookNs,
                            GetModeledWorksheetBreakIds(workbook, currentSheetName, rowBreaks: true),
                            CellAddress.MaxRow))
                    {
                        changed = true;
                    }

                    continue;
                }
                if (sourceBlock.Name == workbookNs + "colBreaks")
                {
                    if (MergeWorksheetBreaks(
                            sourceBlock,
                            targetRoot,
                            workbookNs,
                            GetModeledWorksheetBreakIds(workbook, currentSheetName, rowBreaks: false),
                            CellAddress.MaxCol))
                    {
                        changed = true;
                    }

                    continue;
                }
                if (sourceBlock.Name == workbookNs + "ignoredErrors" &&
                    XlsxWorksheetDiagnosticsMapper.MergeIgnoredErrors(
                        sourceBlock,
                        targetRoot,
                        workbookNs,
                        XlsxWorksheetDiagnosticsMapper.GetModeledIgnoredErrorCells(workbook, currentSheetName)))
                {
                    changed = true;
                }
                if (sourceBlock.Name == workbookNs + "ignoredErrors")
                    continue;
                if (sourceBlock.Name == workbookNs + "cellWatches" &&
                    XlsxWorksheetDiagnosticsMapper.MergeCellWatches(
                        sourceBlock,
                        targetRoot,
                        workbookNs,
                        XlsxWorksheetDiagnosticsMapper.GetModeledCellWatchReferences(workbook, currentSheetName)))
                {
                    changed = true;
                    continue;
                }

                if (sourceBlock.Name == workbookNs + "scenarios" &&
                    MergeWorksheetScenarios(
                        sourceBlock,
                        targetRoot,
                        workbookNs,
                        XlsxWorksheetScenarioMapper.GetModeledNamesForSheet(workbook, currentSheetName)))
                {
                    changed = true;
                }
                if (sourceBlock.Name == workbookNs + "scenarios")
                    continue;

                if (ShouldSkipClearedModeledWorksheetBlock(
                        sourceBlock.Name, workbookNs, workbook, currentSheetName, sourceArchive, sourceWorksheetPath))
                    continue;

                if (targetRoot.Element(sourceBlock.Name) is not null)
                    continue;

                targetRoot.Add(CreateReboundRetainedWorksheetBlock(
                    sourceBlock,
                    sourceArchive,
                    targetArchive,
                    sourceWorksheetPath,
                    targetWorksheetPath,
                    workbookNs,
                    relNs,
                    packageRelNs));
                changed = true;
            }

            var extensionRelationshipIdMap =
                XlsxExtensionListPackageRelationshipRebinder.BuildRelationshipIdMap(
                    sourceArchive,
                    targetArchive,
                    sourceWorksheetPath,
                    targetWorksheetPath);
            if (XlsxNativeXmlMerger.MergeExtensionList(
                    sourceExtensionList,
                    targetRoot,
                    workbookNs,
                    extensionRelationshipIdMap))
                changed = true;

            if (changed)
                XlsxPackageXmlEditor.ReplaceXml(targetArchive, targetWorksheetPath, targetWorksheetXml);
        }
    }

    // R102-io-rename-worksheet-exclusion-sweep-1: mirrors XlsxRenamedSourceSheetResolver, operating on
    // the raw sourceSheets/targetSheets dictionaries this method already receives (which are exactly
    // context.SourceSheets/TargetSheets on the live call path, but this method's legacy 3-arg overload
    // builds them locally without a context at all).
    private static bool TryResolveTargetWorksheetPathByPath(
        IReadOnlyDictionary<string, string> sourceSheets,
        IReadOnlyDictionary<string, string> targetSheets,
        string sourceWorksheetPath,
        out string targetWorksheetPath)
    {
        var normalizedSourcePath = XlsxPackagePath.NormalizePackagePath(sourceWorksheetPath);
        foreach (var (candidateName, candidatePath) in targetSheets)
        {
            // R102-io-rename-worksheet-exclusion-sweep-1-falsepositive: reject a candidate whose name
            // already existed at load time -- its path coincidence is a renumbering shift of that
            // (still-existing, matched-by-name) sheet, not evidence of a rename. See
            // XlsxRenamedSourceSheetResolver's header comment for the concrete delete+renumber repro
            // (this is the exact bug FileAdapterSmokeTests.
            // XlsxAdapter_LoadedWorkbookSave_DoesNotResurrectDeletedSheetUnsupportedWorksheetArtifacts
            // guards against).
            if (sourceSheets.ContainsKey(candidateName))
                continue;

            if (string.Equals(
                    XlsxPackagePath.NormalizePackagePath(candidatePath),
                    normalizedSourcePath,
                    StringComparison.OrdinalIgnoreCase))
            {
                targetWorksheetPath = candidatePath;
                return true;
            }
        }

        targetWorksheetPath = "";
        return false;
    }

    private static bool ShouldSkipClearedModeledWorksheetBlock(
        XName sourceBlockName,
        XNamespace workbookNs,
        Workbook workbook,
        string sheetName,
        ZipArchive sourceArchive,
        string sourceWorksheetPath)
    {
        var sheet = workbook.GetSheet(sheetName);
        if (sheet is null)
            return false;

        if (sourceBlockName == workbookNs + "sortState")
            return sheet.SortState is null;

        if (sourceBlockName == workbookNs + "dataConsolidate")
            return sheet.DataConsolidation is null;

        if (sourceBlockName == workbookNs + "singleXmlCells")
            return true;

        if (sourceBlockName == workbookNs + "smartTags")
            return sheet.SmartTags is null;

        if (sourceBlockName == workbookNs + "autoFilter")
            return sheet.AutoFilter is null;

        if (sourceBlockName == workbookNs + "legacyDrawing")
        {
            // The plain <legacyDrawing> marker points at the VML part that holds legacy (VML) cell-
            // comment note geometry AND legacy form-control shape geometry. When the user has deleted
            // every legacy note and there are no legacy form controls left, nothing in the model needs
            // the marker any more, so restoring it verbatim would keep a dangling reference alive and
            // block XlsxLegacyCommentPreserver's companion VML purge (which conservatively refuses to
            // remove a VML part still pointed at by a live <legacyDrawing> marker). Mirror the comment-
            // side gate that XlsxWorksheetVmlReferencePreserver.CanPreserveLegacyDrawing uses
            // (sheet.Comments.Count) and the form-control side (sheet.FormControls). (Header/footer VML
            // uses the distinct <legacyDrawingHF> marker handled below, so it is unaffected.)
            //
            // Crucially, only skip when the source worksheet ACTUALLY had modeled legacy comments that
            // are now gone — i.e. the empty model is a deletion, not a legacyDrawing FreeX never
            // modeled as comments. A <legacyDrawing> can also mark an unknown/unmodeled VML that must
            // round-trip verbatim (e.g. the generated-worksheet-legacy-drawing-001 corpus fixture: a
            // legacyDrawing→VML with an image and no comments part at all) or Excel's legacy threaded-
            // comment shim (never surfaced into Sheet.Comments); in both cases Sheet.Comments is
            // legitimately empty with nothing deleted, so dropping the marker would orphan a VML part
            // that is still needed. XlsxWorksheetCommentReader.Read applies the same shim filtering the
            // loader used, so a positive count means real notes were modeled from this source sheet.
            return sheet.Comments.Count == 0 &&
                   sheet.FormControls.Count == 0 &&
                   XlsxWorksheetCommentReader.Read(sourceArchive, sourceWorksheetPath).Count > 0;
        }

        if (sourceBlockName == workbookNs + "legacyDrawingHF")
            return !XlsxHeaderFooterPictureReaderWriter.HasPictures(sheet);

        // NOTE (deferred P27): a cleared sheet BACKGROUND image (Sheet.BackgroundImage set to null)
        // can leave the source <picture> block retained here, resurrecting the deleted background on
        // save. We cannot gate on `sheet.BackgroundImage is null` because a worksheet <picture> may
        // instead be a preserved EXTERNAL-target background (TargetMode="External") that FreeX never
        // loads into the model (XlsxWorksheetBackgroundReaderWriter.Read returns null when the image
        // part is not embedded in the package) — dropping the block in that case destroys a
        // legitimately-preserved external background (breaks the FileAdapterSmoke /
        // XlsxNonChartSchemaValidation picture-preservation round-trips). A correct fix needs a
        // load-time "modeled background" flag to tell "cleared an internal background" from "never
        // modeled". Left as a follow-up rather than risk the regression.

        return false;
    }

    private static XElement CreateReboundRetainedWorksheetBlock(
        XElement sourceBlock,
        ZipArchive sourceArchive,
        ZipArchive targetArchive,
        string sourceWorksheetPath,
        string targetWorksheetPath,
        XNamespace workbookNs,
        XNamespace relNs,
        XNamespace packageRelNs)
    {
        var copy = new XElement(sourceBlock);

        // R40-io-vml-shape-geometry-3-3: <picture> was the only retained-block kind whose r:id got
        // collision-safe rebound here. A <legacyDrawing> marker restored through this same fallback
        // (e.g. an OLE-object preview-icon VML shape that is neither a modeled comment nor a form
        // control, so neither XlsxWorksheetVmlReferencePreserver nor XlsxWorksheetFormControlPreserver
        // ever touch it) carries an r:id just like <picture> does, and its target vmlDrawing part can
        // just as easily collide with one of ClosedXML's own worksheet-local relationship ids and get
        // remapped by XlsxPackageMetadataMerger.MergeRelationshipParts (which always runs before this
        // preserver — see XlsxFileAdapter.SourcePackage.cs). Left un-rebound, the copied marker still
        // carries the stale source r:id and resolves to the wrong (or no) relationship in the target
        // package. Rebind it the same way <picture> is rebound.
        if (copy.Name == workbookNs + "picture" || copy.Name == workbookNs + "legacyDrawing")
            RebindWorksheetElementRelationshipId(copy, sourceArchive, targetArchive, sourceWorksheetPath, targetWorksheetPath, relNs, packageRelNs);

        return copy;
    }

    /// <summary>
    /// Rebinds a retained worksheet block's <c>r:id</c> attribute from the source package's
    /// relationship id to whichever id the target package's <c>.rels</c> part now uses for the same
    /// relationship (matched by Type + resolved Target + TargetMode), after
    /// <see cref="XlsxPackageMetadataMerger.MergeRelationshipParts"/> may have renumbered it to avoid
    /// colliding with one of the regenerated worksheet's own relationship ids. Used for both
    /// <c>&lt;picture&gt;</c> and <c>&lt;legacyDrawing&gt;</c> retained blocks.
    /// </summary>
    private static void RebindWorksheetElementRelationshipId(
        XElement element,
        ZipArchive sourceArchive,
        ZipArchive targetArchive,
        string sourceWorksheetPath,
        string targetWorksheetPath,
        XNamespace relNs,
        XNamespace packageRelNs)
    {
        var sourceRelId = element.Attribute(relNs + "id")?.Value;
        if (string.IsNullOrWhiteSpace(sourceRelId))
            return;

        var sourceRelationship = FindRelationshipById(sourceArchive, sourceWorksheetPath, sourceRelId, packageRelNs);
        if (sourceRelationship is null)
            return;

        var targetRelationship = FindMatchingRelationship(targetArchive, targetWorksheetPath, sourceWorksheetPath, sourceRelationship, packageRelNs);
        var targetRelId = targetRelationship?.Attribute("Id")?.Value;
        if (!string.IsNullOrWhiteSpace(targetRelId))
            element.SetAttributeValue(relNs + "id", targetRelId);
    }

    private static XElement? FindRelationshipById(
        ZipArchive archive,
        string sourcePartPath,
        string relationshipId,
        XNamespace packageRelNs)
    {
        var relsEntry = archive.GetEntry(XlsxPackagePath.GetRelationshipPartPath(sourcePartPath));
        if (relsEntry is null)
            return null;

        var relsXml = XlsxPackageXmlEditor.LoadXml(relsEntry);
        if (relsXml.Root is null)
            return null;

        foreach (var relationship in relsXml.Root.Elements(packageRelNs + "Relationship"))
        {
            if (string.Equals(relationship.Attribute("Id")?.Value, relationshipId, StringComparison.Ordinal))
                return relationship;
        }

        return null;
    }

    private static XElement? FindMatchingRelationship(
        ZipArchive archive,
        string targetWorksheetPath,
        string sourceWorksheetPath,
        XElement sourceRelationship,
        XNamespace packageRelNs)
    {
        var relsEntry = archive.GetEntry(XlsxPackagePath.GetRelationshipPartPath(targetWorksheetPath));
        if (relsEntry is null)
            return null;

        var relsXml = XlsxPackageXmlEditor.LoadXml(relsEntry);
        var sourceType = sourceRelationship.Attribute("Type")?.Value;
        var sourceTarget = sourceRelationship.Attribute("Target")?.Value;
        var sourceTargetMode = sourceRelationship.Attribute("TargetMode")?.Value;
        if (string.IsNullOrWhiteSpace(sourceType) || string.IsNullOrWhiteSpace(sourceTarget))
            return null;

        var sourceResolvedTarget = string.Equals(sourceTargetMode, "External", StringComparison.OrdinalIgnoreCase)
            ? sourceTarget.Trim()
            : XlsxPackagePath.ResolveRelationshipTarget(sourceWorksheetPath, sourceTarget);

        if (relsXml.Root is null)
            return null;

        foreach (var relationship in relsXml.Root.Elements(packageRelNs + "Relationship"))
        {
            if (string.Equals(relationship.Attribute("Type")?.Value, sourceType, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(relationship.Attribute("TargetMode")?.Value ?? "", sourceTargetMode ?? "", StringComparison.OrdinalIgnoreCase) &&
                RelationshipTargetsMatch(targetWorksheetPath, relationship, sourceResolvedTarget))
            {
                return relationship;
            }
        }

        return null;
    }

    private static bool RelationshipTargetsMatch(
        string targetWorksheetPath,
        XElement targetRelationship,
        string sourceResolvedTarget)
    {
        var target = targetRelationship.Attribute("Target")?.Value;
        if (string.IsNullOrWhiteSpace(target))
            return false;

        var targetMode = targetRelationship.Attribute("TargetMode")?.Value;
        var resolvedTarget = string.Equals(targetMode, "External", StringComparison.OrdinalIgnoreCase)
            ? target.Trim()
            : XlsxPackagePath.ResolveRelationshipTarget(targetWorksheetPath, target);

        return string.Equals(resolvedTarget, sourceResolvedTarget, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsEmpty(WorksheetSingleXmlCellsModel model) =>
        model.NativeAttributes.Count == 0 && model.Cells.Count == 0;
}
