using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

/// <summary>
/// R95-io-chart-hyperlink-real-pipeline: a chart graphicFrame's object-level hyperlink and its chart
/// title's hyperlink, as read from the TRUE source package (see
/// <see cref="XlsxWorksheetChartWriter.ReadSourceChartHyperlinks"/>). Either or both may be null.
/// </summary>
internal readonly record struct ChartHyperlinkPair(
    (string Target, string? TargetMode)? ObjectHyperlink,
    (string Target, string? TargetMode)? TitleHyperlink);

internal static class XlsxWorksheetChartWriter
{
    private const string ChartExRelationshipType = "http://schemas.microsoft.com/office/2014/relationships/chartEx";
    private const string ChartExDrawingUri = "http://schemas.microsoft.com/office/drawing/2014/chartex";
    private const string ChartExColorStyleContentType = "application/vnd.ms-office.chartcolorstyle+xml";
    private const string ChartExStyleContentType = "application/vnd.ms-office.chartstyle+xml";
    private const string ChartExColorStyleRelationshipType = "http://schemas.microsoft.com/office/2011/relationships/chartColorStyle";
    private const string ChartExStyleRelationshipType = "http://schemas.microsoft.com/office/2011/relationships/chartStyle";
    private const string ChartExChoiceNamespace = "http://schemas.microsoft.com/office/drawing/2015/9/8/chartex";
    private const string HyperlinkRelationshipType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/hyperlink";

    public static bool HasSupportedCharts(Workbook workbook, Func<ChartModel, bool> isSupportedChart)
    {
        foreach (var sheet in workbook.Sheets)
        {
            if (HasSupportedCharts(sheet, isSupportedChart))
                return true;
        }

        return false;
    }

    public static bool HasSupportedCharts(Sheet sheet, Func<ChartModel, bool> isSupportedChart)
    {
        foreach (var chart in sheet.Charts)
        {
            if (isSupportedChart(chart))
                return true;
        }

        return false;
    }

    public static void Save(
        Stream xlsxStream,
        Workbook workbook,
        Func<ChartModel, bool> isSupportedChart,
        Func<ChartModel, Workbook, Sheet, XDocument> createChartXml,
        Func<ChartModel, string> getChartContentType,
        Func<ChartModel, string> getChartRelationshipType,
        IReadOnlyDictionary<string, string>? sourceDrawingPathsBySheet = null,
        // R95-io-chart-hyperlink-real-pipeline / R96-io-chart-hyperlink-name-key: per-sheet hyperlink
        // pairs read from the TRUE source .xlsx package -- see XlsxFileAdapter.GetSourceChartHyperlinksBySheet
        // -- keyed by each chart graphicFrame's stable cNvPr@name (the same name ChartModel.Name
        // round-trips through load/save) rather than document-order position, so a chart keeps its own
        // hyperlink even if the sheet's chart set is added to, deleted from, or reordered between load
        // and save. Through the real XlsxFileAdapter.Save pipeline, `archive` below (xlsxStream) is the
        // in-progress, freshly-ClosedXML-generated package, which NEVER contains the original source
        // chart/drawing bytes (ClosedXML always builds a brand-new, chart-less workbook). The old
        // archive-based ReadOldChartGraphicFrameHyperlinks/ReadOldChartTitleHyperlink fallback below is
        // kept only so direct unit tests that hand-seed a package and call this method without going
        // through XlsxFileAdapter keep working; the real product entry point always supplies this
        // parameter.
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, ChartHyperlinkPair>>? sourceChartHyperlinksBySheet = null,
        // drawing-zorder-share-part (residual-gap closure): when supplied, receives sheet name ->
        // drawing part path for every sheet whose chart drawing part this pass allocated FRESH (i.e.
        // the sheet has no source drawing part of its own, so the shadow/merger route below cannot
        // apply). XlsxWorksheetDrawingObjectWriter, which runs right after us, reuses that same part
        // for the sheet's pictures/shapes/text boxes and preserves the chart anchors already in it
        // instead of allocating a second drawing part and orphaning ours -- see the comment on
        // XlsxWorksheetChartDrawingShadow.
        IDictionary<string, string>? freshlyAllocatedDrawingPathsBySheet = null)
    {
        using var archive = new ZipArchive(xlsxStream, ZipArchiveMode.Update, leaveOpen: true);
        var workbookEntry = archive.GetEntry("xl/workbook.xml");
        var relsEntry = archive.GetEntry("xl/_rels/workbook.xml.rels");
        if (workbookEntry is null || relsEntry is null)
            return;

        var workbookXml = XlsxPackageXmlEditor.LoadXml(workbookEntry);
        var relsXml = XlsxPackageXmlEditor.LoadXml(relsEntry);

        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

        var relTargets = relsXml.Root?
            .Elements(packageRelNs + "Relationship")
            .Where(e => e.Attribute("Id") is not null && e.Attribute("Target") is not null)
            .ToDictionary(
                e => e.Attribute("Id")!.Value,
                e => XlsxPackagePath.NormalizeWorkbookTarget(e.Attribute("Target")!.Value),
                StringComparer.OrdinalIgnoreCase)
            ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var sheetsByName = workbook.Sheets.ToDictionary(sheet => sheet.Name, StringComparer.OrdinalIgnoreCase);
        var sourceDrawingPaths = sourceDrawingPathsBySheet ?? EmptyDrawingPathsBySheet;
        // Every drawing part the source assigns to a sheet is off-limits for a *fresh* allocation; a sheet
        // reuses only its own.
        var reservedDrawingPaths = sourceDrawingPaths.Values.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var usedDrawingPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var chartIndex = 1;
        foreach (var sheetElement in workbookXml.Root?.Element(workbookNs + "sheets")?.Elements(workbookNs + "sheet") ?? [])
        {
            var name = sheetElement.Attribute("name")?.Value;
            var relId = sheetElement.Attribute(relNs + "id")?.Value;
            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(relId))
                continue;
            if (!sheetsByName.TryGetValue(name, out var sheet))
                continue;
            var supportedCharts = sheet.Charts
                .Where(isSupportedChart)
                .ToList();
            if (supportedCharts.Count == 0)
                continue;
            if (!relTargets.TryGetValue(relId, out var worksheetPath))
                continue;

            // Reuse the sheet's own source drawing part when it has one (so its rebuilt charts and any
            // preserved drawing content stay on the same sheet); otherwise allocate a drawing name that
            // collides with neither another sheet's source drawing nor an already-claimed part.
            //
            // drawing-zorder-share-part: whenever we reuse the sheet's own source drawing path,
            // XlsxWorksheetDrawingObjectWriter (which runs right after us in SavePostProcessing) will
            // independently resolve that exact same path for this sheet's pictures/shapes/text boxes
            // (if any) and unconditionally delete-and-rewrite it, discarding the chart anchors we are
            // about to write. We cannot prevent that from here, but we CAN stash a throwaway copy of
            // our anchors at a private shadow path (see XlsxWorksheetChartDrawingShadow) that survives
            // that deletion; XlsxWorksheetDrawingPartMerger picks it back up later (after both writers
            // and the source-package preservation pass have run) and merges the chart anchors back into
            // the final drawing part. This only applies to the "reused own source drawing path" branch:
            // reaching it guarantees the workbook's source package snapshot has a drawings/ folder,
            // which is exactly the precondition for XlsxWorksheetDrawingPartMerger to run at all later
            // in this same save -- see the comment on XlsxWorksheetChartDrawingShadow for the residual
            // gap (fresh/no-prior-drawing sheets) this does not cover.
            var hasOwnSourceDrawingPath = sourceDrawingPaths.TryGetValue(name, out var ownDrawingPath) && ownDrawingPath is not null;
            var reusesOwnSourceDrawingPath = hasOwnSourceDrawingPath && usedDrawingPaths.Add(ownDrawingPath!);
            var drawingPath = reusesOwnSourceDrawingPath
                ? ownDrawingPath!
                : AllocateFreshDrawingPath(archive, reservedDrawingPaths, usedDrawingPaths);
            if (!reusesOwnSourceDrawingPath && freshlyAllocatedDrawingPathsBySheet is not null)
                freshlyAllocatedDrawingPathsBySheet[name] = drawingPath;
            var sourceChartHyperlinks = sourceChartHyperlinksBySheet?.TryGetValue(name, out var sheetHyperlinks) == true
                ? sheetHyperlinks
                : null;
            // R98-io-chart-hyperlink-model-field: whether the CALLER is the real XlsxFileAdapter save
            // pipeline at all -- distinct from sourceChartHyperlinks (which is this SHEET's own
            // per-sheet lookup and goes null just as easily because this sheet's true source simply has
            // no chart hyperlinks recorded, e.g. a brand-new sheet). See WriteWorksheetCharts for why
            // this distinction matters: whenever the real pipeline is driving, ChartModel.Hyperlink was
            // reliably populated (possibly to null) at load for EVERY chart, so it must be trusted
            // outright -- falling back to sourceChartHyperlinks/oldChartHyperlinksByPosition when the
            // model says "no hyperlink" would resurrect exactly the sheet-name-keyed misattribution bug
            // this field exists to fix (e.g. a moved chart with no hyperlink of its own landing on a
            // sheet whose OWN same-named native chart DOES have one).
            var isRealPipeline = sourceChartHyperlinksBySheet is not null;
            WriteWorksheetCharts(archive, worksheetPath, workbook, sheet, supportedCharts, drawingPath, reusesOwnSourceDrawingPath, ref chartIndex, createChartXml, getChartContentType, getChartRelationshipType, sourceChartHyperlinks, isRealPipeline);
        }
    }

    private static readonly IReadOnlyDictionary<string, string> EmptyDrawingPathsBySheet =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    // Picks the next xl/drawings/drawingN.xml part name that is free: not reserved by a source-package
    // drawing (those get restored at their original paths for the sheets that own them), not already present
    // in the package, and not already claimed by another sheet's chart drawing in this pass.
    private static string AllocateFreshDrawingPath(ZipArchive archive, IReadOnlySet<string> reserved, HashSet<string> used)
    {
        var index = 1;
        while (true)
        {
            var path = $"xl/drawings/drawing{index}.xml";
            if (!reserved.Contains(path) && !used.Contains(path) && archive.GetEntry(path) is null)
            {
                used.Add(path);
                return path;
            }

            index++;
        }
    }

    private static void WriteWorksheetCharts(
        ZipArchive archive,
        string worksheetPath,
        Workbook workbook,
        Sheet sheet,
        IReadOnlyList<ChartModel> charts,
        string drawingPath,
        bool reusesOwnSourceDrawingPath,
        ref int chartIndex,
        Func<ChartModel, Workbook, Sheet, XDocument> createChartXml,
        Func<ChartModel, string> getChartContentType,
        Func<ChartModel, string> getChartRelationshipType,
        IReadOnlyDictionary<string, ChartHyperlinkPair>? sourceChartHyperlinks,
        bool isRealPipeline = false)
    {
        var worksheetEntry = archive.GetEntry(worksheetPath);
        if (worksheetEntry is null)
            return;

        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
        XNamespace drawingNs = "http://schemas.openxmlformats.org/drawingml/2006/main";
        XNamespace spreadsheetDrawingNs = "http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing";
        XNamespace chartNs = "http://schemas.openxmlformats.org/drawingml/2006/chart";
        XNamespace chartExNs = "http://schemas.microsoft.com/office/drawing/2014/chartex";
        XNamespace markupCompatNs = "http://schemas.openxmlformats.org/markup-compatibility/2006";

        var drawingRelsPath = XlsxPackagePath.GetRelationshipPartPath(drawingPath);

        // R41-io-hyperlink-drawing-rels-3-1 / R95-io-chart-hyperlink-real-pipeline / R96-io-chart-hyperlink-name-key:
        // capture each existing chart graphicFrame's object-level hyperlink BEFORE the drawing part is
        // deleted and every chart's anchor is rebuilt from ChartModel (which has no Hyperlink property to
        // carry this across).
        //
        // Prefer sourceChartHyperlinks (read by the caller from the TRUE source package -- see the
        // XlsxWorksheetChartWriter.Save doc comment) when supplied; that is what every real save through
        // XlsxFileAdapter provides. It is keyed by the chart graphicFrame's stable cNvPr@name, the SAME
        // name ChartModel.Name round-trips through load/save (XlsxFileAdapter.cs's
        // `chart.Name = chartPart.Name` on load; `DrawingName(chart.Name, ...)` below on save), so each
        // CURRENT chart's own pair is looked up by its own name below rather than by a document-order
        // position -- a position desyncs (misattributing one chart's hyperlink onto a different chart)
        // the moment the sheet's chart COUNT or ORDER changes between load and save (add/delete/reorder/
        // move-to-another-sheet).
        //
        // Fall back to reading `archive` itself, positionally, only for direct-call unit tests that
        // hand-seed the package and never go through XlsxFileAdapter -- through the real pipeline
        // `archive` is a freshly-ClosedXML-generated package with no old drawing content to read.
        var oldChartHyperlinksByPosition = sourceChartHyperlinks is null
            ? ReadOldChartGraphicFrameHyperlinks(
                archive, drawingPath, drawingRelsPath, spreadsheetDrawingNs, drawingNs, relNs, packageRelNs)
            : null;

        archive.GetEntry(drawingPath)?.Delete();
        archive.GetEntry(drawingRelsPath)?.Delete();

        var drawingRelsXml = new XDocument(new XElement(packageRelNs + "Relationships"));
        var anchors = new List<XElement>();
        var chartContentTypes = new Dictionary<int, string>();
        var chartExStyleParts = new Dictionary<int, ChartExStylePackageParts>();
        var chartPosition = 0;
        foreach (var chart in charts)
        {
            var currentChartIndex = chartIndex++;
            chartContentTypes[currentChartIndex] = getChartContentType(chart);
            var chartPath = $"xl/charts/chart{currentChartIndex}.xml";

            // R96-io-chart-hyperlink-name-key: this chart's own hyperlink pair, looked up by its stable
            // ChartModel.Name -- never by position -- so it can only ever be null (no name / no matching
            // source entry, e.g. a chart authored fresh in FreeX that was never in the source file) or
            // its OWN pair, never another chart's.
            ChartHyperlinkPair? sourceHyperlinkPair = sourceChartHyperlinks is not null
                && !string.IsNullOrEmpty(chart.Name)
                && sourceChartHyperlinks.TryGetValue(chart.Name, out var namedPair)
                    ? namedPair
                    : null;

            // R41-io-hyperlink-drawing-rels-3-2 / R95-io-chart-hyperlink-real-pipeline: capture the OLD
            // chart part's main-title hyperlink (if any) BEFORE it is deleted/overwritten below, so it
            // can be grafted onto the rebuilt title. Falls back to the chartPath-identity archive read
            // only for direct-call unit tests that never go through XlsxFileAdapter.
            var titleHyperlink = sourceChartHyperlinks is not null
                ? sourceHyperlinkPair?.TitleHyperlink
                : ReadOldChartTitleHyperlink(archive, chartPath, packageRelNs, chartNs, drawingNs, relNs);

            archive.GetEntry(chartPath)?.Delete();
            var chartXml = createChartXml(chart, workbook, sheet);
            string? titleHyperlinkRelId = null;
            if (titleHyperlink is not null)
            {
                titleHyperlinkRelId = "rIdFreeXChartTitleHyperlink";
                XlsxChartXmlWriter.ApplyVerbatimTitleHyperlink(chartXml, chartNs, drawingNs, relNs, titleHyperlinkRelId);
            }

            var chartEntry = archive.CreateEntry(chartPath);
            using (var chartStream = chartEntry.Open())
                chartXml.Save(chartStream);

            var styleParts = ChartTypeSupport.IsChartExFamily(chart.Type)
                ? WriteChartExStyleParts(archive, currentChartIndex, chart.Type)
                : (ChartExStylePackageParts?)null;
            if (styleParts is { } chartExParts)
                chartExStyleParts[currentChartIndex] = chartExParts;
            WriteChartRelationships(archive, chartPath, chart, styleParts, packageRelNs, titleHyperlink, titleHyperlinkRelId);

            var chartRelId = $"rIdFreeXChart{currentChartIndex}";
            var chartRelationshipType = getChartRelationshipType(chart);
            drawingRelsXml.Root!.Add(new XElement(
                packageRelNs + "Relationship",
                new XAttribute("Id", chartRelId),
                new XAttribute("Type", chartRelationshipType),
                new XAttribute("Target", XlsxPackagePath.GetRelationshipTarget(drawingPath, chartPath))));

            // R98-io-chart-hyperlink-model-field: PREFER chart.Hyperlink -- populated at load PER
            // CHART (from that chart's own graphicFrame, never a sheet-name-keyed guess) and carried
            // through clone/paste (DuplicateSheetDrawingCloner.CloneChart) and move
            // (MoveChartCommand/MoveChartToNewSheetCommand relocate this SAME ChartModel instance) --
            // over the OLD sourceChartHyperlinks/oldChartHyperlinksByPosition mechanism below, which is
            // keyed by the chart's CURRENT host sheet's OWN original source dictionary and so silently
            // drops the hyperlink when a chart moves to a different sheet (that sheet's dictionary
            // never contained it) or misattributes a different chart's hyperlink when the destination
            // sheet already has its own same-named chart.
            //
            // Falling back to the old mechanism whenever chart.Hyperlink is merely null is NOT safe:
            // through the real pipeline (isRealPipeline), load ALWAYS populates chart.Hyperlink --
            // including to null, for a chart that genuinely has none -- so null is a fully trustworthy
            // answer, not "unknown, go check elsewhere". Falling through in that case resurrects the
            // exact misattribution bug this field fixes (a moved chart with NO hyperlink of its own
            // landing on a sheet whose OWN same-named native chart has one). The old
            // sourceChartHyperlinks/oldChartHyperlinksByPosition fallback is therefore reserved for
            // isRealPipeline == false only -- direct-call unit tests that hand-seed a package and call
            // this method without ever going through XlsxFileAdapter.Load, so ChartModel.Hyperlink was
            // never populated by anything at all.
            (string Target, string? TargetMode, string? Tooltip)? resolvedObjectHyperlink;
            if (chart.Hyperlink is { } modelHyperlink)
            {
                resolvedObjectHyperlink = (modelHyperlink.Target, modelHyperlink.TargetMode, modelHyperlink.Tooltip);
            }
            else if (isRealPipeline)
            {
                resolvedObjectHyperlink = null;
            }
            else
            {
                var oldObjectHyperlink = sourceChartHyperlinks is not null
                    ? sourceHyperlinkPair?.ObjectHyperlink
                    : (chartPosition < oldChartHyperlinksByPosition!.Count ? oldChartHyperlinksByPosition[chartPosition] : null);
                resolvedObjectHyperlink = oldObjectHyperlink is { } resolvedOld
                    ? (resolvedOld.Target, resolvedOld.TargetMode, (string?)null)
                    : null;
            }

            (string RelId, string? Tooltip)? objectHyperlinkRelId = null;
            if (resolvedObjectHyperlink is { } resolved)
            {
                var relId = "rIdFreeXChartHyperlink" + currentChartIndex;
                drawingRelsXml.Root!.Add(new XElement(
                    packageRelNs + "Relationship",
                    new XAttribute("Id", relId),
                    new XAttribute("Type", HyperlinkRelationshipType),
                    new XAttribute("Target", resolved.Target),
                    string.IsNullOrWhiteSpace(resolved.TargetMode) ? null : new XAttribute("TargetMode", resolved.TargetMode)));
                objectHyperlinkRelId = (relId, resolved.Tooltip);
            }

            anchors.Add(ToChartAnchor(chart, sheet, currentChartIndex, chartRelId, chartRelationshipType, objectHyperlinkRelId, spreadsheetDrawingNs, drawingNs, chartNs, chartExNs, relNs, markupCompatNs));
            chartPosition++;
        }

        XlsxPackageXmlEditor.ReplaceXml(archive, drawingPath, new XDocument(
            new XElement(spreadsheetDrawingNs + "wsDr",
                new XAttribute(XNamespace.Xmlns + "xdr", spreadsheetDrawingNs),
                new XAttribute(XNamespace.Xmlns + "a", drawingNs),
                new XAttribute(XNamespace.Xmlns + "c", chartNs),
                new XAttribute(XNamespace.Xmlns + "cx", chartExNs),
                new XAttribute(XNamespace.Xmlns + "r", relNs),
                anchors)));
        XlsxPackageXmlEditor.ReplaceXml(archive, drawingRelsPath, drawingRelsXml);

        // drawing-zorder-share-part: see the call-site comment in Save(). When we are about to write
        // into a drawing part the sheet's source package already owns, XlsxWorksheetDrawingObjectWriter
        // is about to reuse and unconditionally overwrite this exact same part, discarding the chart
        // anchors above. Stash a throwaway copy at a private shadow path so
        // XlsxWorksheetDrawingPartMerger can merge them back into the final drawing part later in this
        // same save, after both writers have run. Cloning every element (rather than reusing the
        // `anchors`/`drawingRelsXml` instances) keeps this independent of XElement's already-has-a-parent
        // auto-clone behavior on Add, so it stays correct even if the write order above ever changes.
        if (reusesOwnSourceDrawingPath && anchors.Count > 0)
        {
            var shadowPath = XlsxWorksheetChartDrawingShadow.GetShadowPath(drawingPath);
            var shadowRelsPath = XlsxPackagePath.GetRelationshipPartPath(shadowPath);
            XlsxPackageXmlEditor.ReplaceXml(archive, shadowPath, new XDocument(
                new XElement(spreadsheetDrawingNs + "wsDr",
                    new XAttribute(XNamespace.Xmlns + "xdr", spreadsheetDrawingNs),
                    new XAttribute(XNamespace.Xmlns + "a", drawingNs),
                    new XAttribute(XNamespace.Xmlns + "c", chartNs),
                    new XAttribute(XNamespace.Xmlns + "cx", chartExNs),
                    new XAttribute(XNamespace.Xmlns + "r", relNs),
                    anchors.Select(anchor => new XElement(anchor)))));
            XlsxPackageXmlEditor.ReplaceXml(archive, shadowRelsPath, new XDocument(new XElement(drawingRelsXml.Root!)));
        }

        XlsxPackageXmlEditor.EnsureSpecificContentType(archive, $"/{drawingPath}", "application/vnd.openxmlformats-officedocument.drawing+xml");
        foreach (var (index, contentType) in chartContentTypes)
            XlsxPackageXmlEditor.EnsureSpecificContentType(archive, $"/xl/charts/chart{index}.xml", contentType);
        foreach (var (_, styleParts) in chartExStyleParts)
        {
            XlsxPackageXmlEditor.EnsureSpecificContentType(archive, $"/{styleParts.ColorStylePath}", ChartExColorStyleContentType);
            XlsxPackageXmlEditor.EnsureSpecificContentType(archive, $"/{styleParts.StylePath}", ChartExStyleContentType);
        }

        var relsPath = XlsxPackagePath.GetRelationshipPartPath(worksheetPath);
        var worksheetRelsXml = archive.GetEntry(relsPath) is { } relsEntry
            ? XlsxPackageXmlEditor.LoadXml(relsEntry)
            : new XDocument(new XElement(packageRelNs + "Relationships"));

        var drawingRelId = XlsxPackageXmlEditor.NextRelationshipId(worksheetRelsXml, packageRelNs);
        worksheetRelsXml.Root!.Add(new XElement(
            packageRelNs + "Relationship",
            new XAttribute("Id", drawingRelId),
            new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/drawing"),
            new XAttribute("Target", XlsxPackagePath.GetRelationshipTarget(worksheetPath, drawingPath))));
        XlsxPackageXmlEditor.ReplaceXml(archive, relsPath, worksheetRelsXml);

        var worksheetXml = XlsxPackageXmlEditor.LoadXml(worksheetEntry);
        var root = worksheetXml.Root;
        if (root is null)
            return;

        root.SetAttributeValue(XNamespace.Xmlns + "r", relNs.NamespaceName);
        XlsxWorksheetDrawingPlacement.SetWorksheetDrawing(root, worksheetNs, relNs, drawingRelId);
        XlsxPackageXmlEditor.ReplaceXml(archive, worksheetPath, worksheetXml);
    }

    private static void WriteChartRelationships(
        ZipArchive archive,
        string chartPath,
        ChartModel chart,
        ChartExStylePackageParts? chartExStyleParts,
        XNamespace packageRelNs,
        (string Target, string? TargetMode)? titleHyperlink = null,
        string? titleHyperlinkRelId = null)
    {
        var relsXml = new XDocument(new XElement(packageRelNs + "Relationships"));
        var relationships = relsXml.Root!;
        if (titleHyperlink is { } titleHyperlinkTarget && titleHyperlinkRelId is not null)
        {
            // R41-io-hyperlink-drawing-rels-3-2: re-attach the main chart title's hyperlink relationship
            // captured from the pre-rebuild chart part (see ReadOldChartTitleHyperlink).
            relationships.Add(new XElement(
                packageRelNs + "Relationship",
                new XAttribute("Id", titleHyperlinkRelId),
                new XAttribute("Type", HyperlinkRelationshipType),
                new XAttribute("Target", titleHyperlinkTarget.Target),
                string.IsNullOrWhiteSpace(titleHyperlinkTarget.TargetMode)
                    ? null
                    : new XAttribute("TargetMode", titleHyperlinkTarget.TargetMode)));
        }

        if (chart.ExternalData is { } externalData &&
            !string.IsNullOrWhiteSpace(externalData.RelationshipId) &&
            !string.IsNullOrWhiteSpace(externalData.RelationshipType) &&
            !string.IsNullOrWhiteSpace(externalData.Target))
        {
            relationships.Add(new XElement(
                packageRelNs + "Relationship",
                new XAttribute("Id", externalData.RelationshipId),
                new XAttribute("Type", externalData.RelationshipType),
                new XAttribute("Target", externalData.Target),
                string.IsNullOrWhiteSpace(externalData.TargetMode)
                    ? null
                    : new XAttribute("TargetMode", externalData.TargetMode)));
        }

        if (chart.UserShapes is { } userShapes &&
            !string.IsNullOrWhiteSpace(userShapes.RelationshipId) &&
            !string.IsNullOrWhiteSpace(userShapes.RelationshipType) &&
            !string.IsNullOrWhiteSpace(userShapes.Target))
        {
            relationships.Add(new XElement(
                packageRelNs + "Relationship",
                new XAttribute("Id", userShapes.RelationshipId),
                new XAttribute("Type", userShapes.RelationshipType),
                new XAttribute("Target", userShapes.Target),
                string.IsNullOrWhiteSpace(userShapes.TargetMode)
                    ? null
                    : new XAttribute("TargetMode", userShapes.TargetMode)));
        }

        if (chartExStyleParts is { } styleParts)
        {
            relationships.Add(new XElement(
                packageRelNs + "Relationship",
                new XAttribute("Id", XlsxPackageXmlEditor.NextRelationshipId(relsXml, packageRelNs)),
                new XAttribute("Type", ChartExStyleRelationshipType),
                new XAttribute("Target", GetSameDirectoryRelationshipTarget(styleParts.StylePath))));
            relationships.Add(new XElement(
                packageRelNs + "Relationship",
                new XAttribute("Id", XlsxPackageXmlEditor.NextRelationshipId(relsXml, packageRelNs)),
                new XAttribute("Type", ChartExColorStyleRelationshipType),
                new XAttribute("Target", GetSameDirectoryRelationshipTarget(styleParts.ColorStylePath))));
        }

        if (!relationships.HasElements)
        {
            return;
        }

        var relsPath = XlsxPackagePath.GetRelationshipPartPath(chartPath);
        XlsxPackageXmlEditor.ReplaceXml(archive, relsPath, relsXml);
    }

    /// <summary>
    /// R41-io-hyperlink-drawing-rels-3-1: reads the CURRENT (pre-rebuild) drawing part's chart
    /// graphicFrames, in document order, and resolves each one's object-level hyperlink (an
    /// <c>a:hlinkClick</c> on its <c>xdr:cNvPr</c>) via the drawing's OWN current relationships part.
    /// Returns one entry per chart graphicFrame found (null where that chart has no hyperlink), so the
    /// caller can re-attach each hyperlink to the rebuilt chart at the same position. Returns an empty
    /// list if the drawing part doesn't exist or can't be parsed (nothing to preserve).
    /// </summary>
    private static List<(string Target, string? TargetMode)?> ReadOldChartGraphicFrameHyperlinks(
        ZipArchive archive,
        string drawingPath,
        string drawingRelsPath,
        XNamespace spreadsheetDrawingNs,
        XNamespace drawingNs,
        XNamespace relNs,
        XNamespace packageRelNs)
    {
        var result = new List<(string, string?)?>();
        if (archive.GetEntry(drawingPath) is not { } oldDrawingEntry)
            return result;

        XDocument oldDrawingXml;
        try
        {
            oldDrawingXml = XlsxPackageXmlEditor.LoadXml(oldDrawingEntry);
        }
        catch
        {
            return result;
        }

        var oldRelTargets = new Dictionary<string, (string Target, string? TargetMode)>(StringComparer.OrdinalIgnoreCase);
        if (archive.GetEntry(drawingRelsPath) is { } oldRelsEntry)
        {
            try
            {
                var oldRelsXml = XlsxPackageXmlEditor.LoadXml(oldRelsEntry);
                foreach (var rel in oldRelsXml.Root?.Elements(packageRelNs + "Relationship") ?? [])
                {
                    var id = rel.Attribute("Id")?.Value;
                    var target = rel.Attribute("Target")?.Value;
                    if (string.IsNullOrEmpty(id) || target is null)
                        continue;
                    oldRelTargets[id] = (target, rel.Attribute("TargetMode")?.Value);
                }
            }
            catch
            {
                // Malformed rels part: fall through with no resolvable relationships, so every
                // hyperlink below resolves to null (nothing preserved for this sheet).
            }
        }

        foreach (var graphicFrame in oldDrawingXml.Descendants(spreadsheetDrawingNs + "graphicFrame"))
        {
            // Only graphicFrame elements that host a chart (as opposed to some other graphic type)
            // carry the object-level hyperlink this finding is about.
            var isChart = graphicFrame.Descendants(drawingNs + "graphicData")
                .Any(graphicData => (graphicData.Attribute("uri")?.Value ?? "").Contains("chart", StringComparison.OrdinalIgnoreCase));
            if (!isChart)
                continue;

            var hlinkClick = graphicFrame
                .Element(spreadsheetDrawingNs + "nvGraphicFramePr")?
                .Element(spreadsheetDrawingNs + "cNvPr")?
                .Element(drawingNs + "hlinkClick");
            var relId = hlinkClick?.Attribute(relNs + "id")?.Value;
            result.Add(relId is not null && oldRelTargets.TryGetValue(relId, out var resolved) ? resolved : null);
        }

        return result;
    }

    /// <summary>
    /// R95-io-chart-hyperlink-real-pipeline: reads a sheet's chart graphicFrames from the TRUE source
    /// .xlsx package's drawing part (<paramref name="drawingPath"/>, resolved against
    /// <paramref name="sourceArchive"/> -- the caller's snapshot of the file as originally opened, NOT
    /// the in-progress package a real <see cref="XlsxFileAdapter"/> save is rebuilding), in document
    /// order, and resolves each one's:
    /// <list type="bullet">
    /// <item>object-level hyperlink -- an <c>a:hlinkClick</c> on the graphicFrame's <c>xdr:cNvPr</c>,
    /// via the drawing part's own relationships.</item>
    /// <item>chart title hyperlink -- an <c>a:hlinkClick</c> on the chart's main-title run's
    /// <c>a:rPr</c>, via the ACTUAL chart part this graphicFrame's own <c>c:chart</c>/<c>cx:chart</c>
    /// relationship points at (resolved through the drawing's relationships, not assumed by naming
    /// convention), and that chart part's own relationships.</item>
    /// </list>
    /// Returns one <see cref="ChartHyperlinkPair"/> per chart graphicFrame found, KEYED by that
    /// graphicFrame's stable <c>xdr:cNvPr@name</c> (R96-io-chart-hyperlink-name-key) -- the same name
    /// <see cref="XlsxFileAdapter"/> round-trips onto <c>ChartModel.Name</c> at load (see
    /// <c>chart.Name = chartPart.Name</c>) and this file re-emits verbatim via
    /// <c>DrawingName(chart.Name, ...)</c> at save. <c>WriteWorksheetCharts</c> looks each CURRENT
    /// chart's pair up by its own <c>ChartModel.Name</c>, so the match survives the sheet's chart set
    /// being added to, deleted from, or reordered between load and save -- a plain document-order
    /// position would desync the moment the chart COUNT or ORDER changed (the bug this name key fixes).
    /// A chart graphicFrame with no (or an empty) <c>cNvPr@name</c> is skipped -- Excel always assigns
    /// one (e.g. "Chart 1"), so this only affects hand-authored packages missing it, and skipping means
    /// "nothing to preserve" rather than guessing. Returns an empty dictionary if the drawing part
    /// doesn't exist or can't be parsed.
    /// </summary>
    internal static Dictionary<string, ChartHyperlinkPair> ReadSourceChartHyperlinks(
        ZipArchive sourceArchive,
        string drawingPath,
        XNamespace spreadsheetDrawingNs,
        XNamespace drawingNs,
        XNamespace chartNs,
        XNamespace relNs,
        XNamespace packageRelNs)
    {
        var result = new Dictionary<string, ChartHyperlinkPair>(StringComparer.Ordinal);
        if (sourceArchive.GetEntry(drawingPath) is not { } drawingEntry)
            return result;

        XDocument drawingXml;
        try
        {
            drawingXml = XlsxPackageXmlEditor.LoadXml(drawingEntry);
        }
        catch
        {
            return result;
        }

        var drawingRelsPath = XlsxPackagePath.GetRelationshipPartPath(drawingPath);
        var drawingRelTargets = LoadRelationshipTargets(sourceArchive, drawingRelsPath, packageRelNs);

        foreach (var graphicFrame in drawingXml.Descendants(spreadsheetDrawingNs + "graphicFrame"))
        {
            var graphicData = graphicFrame.Descendants(drawingNs + "graphicData")
                .FirstOrDefault(gd => (gd.Attribute("uri")?.Value ?? "").Contains("chart", StringComparison.OrdinalIgnoreCase));
            if (graphicData is null)
                continue;

            var cNvPr = graphicFrame
                .Element(spreadsheetDrawingNs + "nvGraphicFramePr")?
                .Element(spreadsheetDrawingNs + "cNvPr");
            // R96-io-chart-hyperlink-name-key: the stable join key -- see the doc comment above.
            var name = cNvPr?.Attribute("name")?.Value;
            if (string.IsNullOrEmpty(name))
                continue;

            var hlinkClick = cNvPr?.Element(drawingNs + "hlinkClick");
            var objectRelId = hlinkClick?.Attribute(relNs + "id")?.Value;
            (string, string?)? objectHyperlink = objectRelId is not null && drawingRelTargets.TryGetValue(objectRelId, out var resolvedObject)
                ? resolvedObject
                : null;

            // Resolve the ACTUAL chart part this graphicFrame points at (its own c:chart/cx:chart r:id),
            // not an assumed "chart{N}.xml" identity -- the chart writer's own numbering only matches
            // the source file's numbering when nothing structurally changed.
            (string, string?)? titleHyperlink = null;
            var chartRelId = graphicData.Elements().FirstOrDefault(e => e.Attribute(relNs + "id") is not null)?.Attribute(relNs + "id")?.Value;
            if (chartRelId is not null && drawingRelTargets.TryGetValue(chartRelId, out var chartRelTarget))
            {
                var chartPath = XlsxPackagePath.ResolveRelationshipTarget(drawingPath, chartRelTarget.Target);
                titleHyperlink = ReadSourceChartTitleHyperlink(sourceArchive, chartPath, packageRelNs, chartNs, drawingNs, relNs);
            }

            result[name] = new ChartHyperlinkPair(objectHyperlink, titleHyperlink);
        }

        return result;
    }

    private static Dictionary<string, (string Target, string? TargetMode)> LoadRelationshipTargets(
        ZipArchive archive, string relsPath, XNamespace packageRelNs)
    {
        var targets = new Dictionary<string, (string, string?)>(StringComparer.OrdinalIgnoreCase);
        if (archive.GetEntry(relsPath) is not { } relsEntry)
            return targets;

        try
        {
            var relsXml = XlsxPackageXmlEditor.LoadXml(relsEntry);
            foreach (var rel in relsXml.Root?.Elements(packageRelNs + "Relationship") ?? [])
            {
                var id = rel.Attribute("Id")?.Value;
                var target = rel.Attribute("Target")?.Value;
                if (string.IsNullOrEmpty(id) || target is null)
                    continue;
                targets[id] = (target, rel.Attribute("TargetMode")?.Value);
            }
        }
        catch
        {
            // Malformed rels part: fall through with no resolvable relationships.
        }

        return targets;
    }

    /// <summary>
    /// R95-io-chart-hyperlink-real-pipeline sibling of <see cref="ReadOldChartTitleHyperlink"/>: same
    /// title-hyperlink resolution, but against an explicit <paramref name="sourceArchive"/>/<paramref
    /// name="chartPath"/> pair resolved by the caller (<see cref="ReadSourceChartHyperlinks"/>) rather
    /// than an assumed "chart{currentChartIndex}.xml" identity.
    /// </summary>
    private static (string Target, string? TargetMode)? ReadSourceChartTitleHyperlink(
        ZipArchive sourceArchive,
        string chartPath,
        XNamespace packageRelNs,
        XNamespace chartNs,
        XNamespace drawingNs,
        XNamespace relNs)
    {
        if (sourceArchive.GetEntry(chartPath) is not { } chartEntry)
            return null;

        XDocument chartXml;
        try
        {
            chartXml = XlsxPackageXmlEditor.LoadXml(chartEntry);
        }
        catch
        {
            return null;
        }

        var hlinkClick = chartXml.Root?
            .Element(chartNs + "chart")?
            .Element(chartNs + "title")?
            .Element(chartNs + "tx")?
            .Element(chartNs + "rich")?
            .Element(drawingNs + "p")?
            .Element(drawingNs + "r")?
            .Element(drawingNs + "rPr")?
            .Element(drawingNs + "hlinkClick");
        var relId = hlinkClick?.Attribute(relNs + "id")?.Value;
        if (relId is null)
            return null;

        var chartRelsPath = XlsxPackagePath.GetRelationshipPartPath(chartPath);
        var chartRelTargets = LoadRelationshipTargets(sourceArchive, chartRelsPath, packageRelNs);
        return chartRelTargets.TryGetValue(relId, out var resolved) ? resolved : null;
    }

    /// <summary>
    /// R41-io-hyperlink-drawing-rels-3-2: reads the CURRENT (pre-rebuild) chart part at
    /// <paramref name="chartPath"/> and resolves its main title's hyperlink (an <c>a:hlinkClick</c> on
    /// the first title run's <c>a:rPr</c>) via the chart part's OWN current relationships. Returns null
    /// if the chart part doesn't exist, has no title hyperlink, or the hyperlink's relationship can't be
    /// resolved.
    /// </summary>
    private static (string Target, string? TargetMode)? ReadOldChartTitleHyperlink(
        ZipArchive archive,
        string chartPath,
        XNamespace packageRelNs,
        XNamespace chartNs,
        XNamespace drawingNs,
        XNamespace relNs)
    {
        if (archive.GetEntry(chartPath) is not { } oldChartEntry)
            return null;

        XDocument oldChartXml;
        try
        {
            oldChartXml = XlsxPackageXmlEditor.LoadXml(oldChartEntry);
        }
        catch
        {
            return null;
        }

        var hlinkClick = oldChartXml.Root?
            .Element(chartNs + "chart")?
            .Element(chartNs + "title")?
            .Element(chartNs + "tx")?
            .Element(chartNs + "rich")?
            .Element(drawingNs + "p")?
            .Element(drawingNs + "r")?
            .Element(drawingNs + "rPr")?
            .Element(drawingNs + "hlinkClick");
        var relId = hlinkClick?.Attribute(relNs + "id")?.Value;
        if (relId is null)
            return null;

        var chartRelsPath = XlsxPackagePath.GetRelationshipPartPath(chartPath);
        if (archive.GetEntry(chartRelsPath) is not { } oldChartRelsEntry)
            return null;

        try
        {
            var oldChartRelsXml = XlsxPackageXmlEditor.LoadXml(oldChartRelsEntry);
            var relationship = oldChartRelsXml.Root?
                .Elements(packageRelNs + "Relationship")
                .FirstOrDefault(e => string.Equals(e.Attribute("Id")?.Value, relId, StringComparison.OrdinalIgnoreCase));
            var target = relationship?.Attribute("Target")?.Value;
            return target is null ? null : (target, relationship?.Attribute("TargetMode")?.Value);
        }
        catch
        {
            return null;
        }
    }

    private static ChartExStylePackageParts WriteChartExStyleParts(ZipArchive archive, int chartIndex, ChartType chartType)
    {
        var colorStylePath = $"xl/charts/colors{chartIndex}.xml";
        var stylePath = $"xl/charts/style{chartIndex}.xml";
        XlsxPackageXmlEditor.ReplaceXml(archive, colorStylePath, CreateChartExColorStyleXml());
        XlsxPackageXmlEditor.ReplaceXml(archive, stylePath, CreateChartExStyleXml(chartType));
        return new ChartExStylePackageParts(colorStylePath, stylePath);
    }

    private static XDocument CreateChartExColorStyleXml()
    {
        XNamespace chartStyleNs = "http://schemas.microsoft.com/office/drawing/2012/chartStyle";
        XNamespace drawingNs = "http://schemas.openxmlformats.org/drawingml/2006/main";
        var accents = new[] { "accent1", "accent2", "accent3", "accent4", "accent5", "accent6" }
            .Select(color => new XElement(drawingNs + "schemeClr", new XAttribute("val", color)));

        return new XDocument(
            new XElement(chartStyleNs + "colorStyle",
                new XAttribute(XNamespace.Xmlns + "cs", chartStyleNs),
                new XAttribute(XNamespace.Xmlns + "a", drawingNs),
                new XAttribute("meth", "cycle"),
                new XAttribute("id", "10"),
                accents,
                new XElement(chartStyleNs + "variation"),
                ToChartExColorVariation(chartStyleNs, drawingNs, lumMod: 60000),
                ToChartExColorVariation(chartStyleNs, drawingNs, lumMod: 80000, lumOff: 20000),
                ToChartExColorVariation(chartStyleNs, drawingNs, lumMod: 80000),
                ToChartExColorVariation(chartStyleNs, drawingNs, lumMod: 60000, lumOff: 40000),
                ToChartExColorVariation(chartStyleNs, drawingNs, lumMod: 50000),
                ToChartExColorVariation(chartStyleNs, drawingNs, lumMod: 70000, lumOff: 30000),
                ToChartExColorVariation(chartStyleNs, drawingNs, lumMod: 70000),
                ToChartExColorVariation(chartStyleNs, drawingNs, lumMod: 50000, lumOff: 50000)));
    }

    private static XElement ToChartExColorVariation(
        XNamespace chartStyleNs,
        XNamespace drawingNs,
        int lumMod,
        int? lumOff = null) =>
        new(chartStyleNs + "variation",
            new XElement(drawingNs + "lumMod", new XAttribute("val", lumMod)),
            lumOff is null
                ? null
                : new XElement(drawingNs + "lumOff", new XAttribute("val", lumOff.Value)));

    private static XDocument CreateChartExStyleXml(ChartType chartType)
    {
        _ = chartType;

        // Excel emits the same native chartEx style profile for the supported modern chart family.
        return XDocument.Parse(
            """
            <cs:chartStyle xmlns:cs="http://schemas.microsoft.com/office/drawing/2012/chartStyle" xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main" id="201"><cs:axisTitle><cs:lnRef idx="0"/><cs:fillRef idx="0"/><cs:effectRef idx="0"/><cs:fontRef idx="minor"><a:schemeClr val="tx1"><a:lumMod val="65000"/><a:lumOff val="35000"/></a:schemeClr></cs:fontRef><cs:defRPr sz="1000" kern="1200"/></cs:axisTitle><cs:categoryAxis><cs:lnRef idx="0"/><cs:fillRef idx="0"/><cs:effectRef idx="0"/><cs:fontRef idx="minor"><a:schemeClr val="tx1"><a:lumMod val="65000"/><a:lumOff val="35000"/></a:schemeClr></cs:fontRef><cs:spPr><a:ln w="9525" cap="flat" cmpd="sng" algn="ctr"><a:solidFill><a:schemeClr val="tx1"><a:lumMod val="15000"/><a:lumOff val="85000"/></a:schemeClr></a:solidFill><a:round/></a:ln></cs:spPr><cs:defRPr sz="900" kern="1200"/></cs:categoryAxis><cs:chartArea mods="allowNoFillOverride allowNoLineOverride"><cs:lnRef idx="0"/><cs:fillRef idx="0"/><cs:effectRef idx="0"/><cs:fontRef idx="minor"><a:schemeClr val="tx1"/></cs:fontRef><cs:spPr><a:solidFill><a:schemeClr val="bg1"/></a:solidFill><a:ln w="9525" cap="flat" cmpd="sng" algn="ctr"><a:solidFill><a:schemeClr val="tx1"><a:lumMod val="15000"/><a:lumOff val="85000"/></a:schemeClr></a:solidFill><a:round/></a:ln></cs:spPr><cs:defRPr sz="1000" kern="1200"/></cs:chartArea><cs:dataLabel><cs:lnRef idx="0"/><cs:fillRef idx="0"/><cs:effectRef idx="0"/><cs:fontRef idx="minor"><a:schemeClr val="tx1"><a:lumMod val="75000"/><a:lumOff val="25000"/></a:schemeClr></cs:fontRef><cs:defRPr sz="900" kern="1200"/></cs:dataLabel><cs:dataLabelCallout><cs:lnRef idx="0"/><cs:fillRef idx="0"/><cs:effectRef idx="0"/><cs:fontRef idx="minor"><a:schemeClr val="dk1"><a:lumMod val="65000"/><a:lumOff val="35000"/></a:schemeClr></cs:fontRef><cs:spPr><a:solidFill><a:schemeClr val="lt1"/></a:solidFill><a:ln><a:solidFill><a:schemeClr val="dk1"><a:lumMod val="25000"/><a:lumOff val="75000"/></a:schemeClr></a:solidFill></a:ln></cs:spPr><cs:defRPr sz="900" kern="1200"/><cs:bodyPr rot="0" spcFirstLastPara="1" vertOverflow="clip" horzOverflow="clip" vert="horz" wrap="square" lIns="36576" tIns="18288" rIns="36576" bIns="18288" anchor="ctr" anchorCtr="1"><a:spAutoFit/></cs:bodyPr></cs:dataLabelCallout><cs:dataPoint><cs:lnRef idx="0"/><cs:fillRef idx="1"><cs:styleClr val="auto"/></cs:fillRef><cs:effectRef idx="0"/><cs:fontRef idx="minor"><a:schemeClr val="tx1"/></cs:fontRef></cs:dataPoint><cs:dataPoint3D><cs:lnRef idx="0"/><cs:fillRef idx="1"><cs:styleClr val="auto"/></cs:fillRef><cs:effectRef idx="0"/><cs:fontRef idx="minor"><a:schemeClr val="tx1"/></cs:fontRef></cs:dataPoint3D><cs:dataPointLine><cs:lnRef idx="0"><cs:styleClr val="auto"/></cs:lnRef><cs:fillRef idx="1"/><cs:effectRef idx="0"/><cs:fontRef idx="minor"><a:schemeClr val="tx1"/></cs:fontRef><cs:spPr><a:ln w="28575" cap="rnd"><a:solidFill><a:schemeClr val="phClr"/></a:solidFill><a:round/></a:ln></cs:spPr></cs:dataPointLine><cs:dataPointMarker><cs:lnRef idx="0"><cs:styleClr val="auto"/></cs:lnRef><cs:fillRef idx="1"><cs:styleClr val="auto"/></cs:fillRef><cs:effectRef idx="0"/><cs:fontRef idx="minor"><a:schemeClr val="tx1"/></cs:fontRef><cs:spPr><a:ln w="9525"><a:solidFill><a:schemeClr val="phClr"/></a:solidFill></a:ln></cs:spPr></cs:dataPointMarker><cs:dataPointMarkerLayout symbol="circle" size="5"/><cs:dataPointWireframe><cs:lnRef idx="0"><cs:styleClr val="auto"/></cs:lnRef><cs:fillRef idx="1"/><cs:effectRef idx="0"/><cs:fontRef idx="minor"><a:schemeClr val="tx1"/></cs:fontRef><cs:spPr><a:ln w="9525" cap="rnd"><a:solidFill><a:schemeClr val="phClr"/></a:solidFill><a:round/></a:ln></cs:spPr></cs:dataPointWireframe><cs:dataTable><cs:lnRef idx="0"/><cs:fillRef idx="0"/><cs:effectRef idx="0"/><cs:fontRef idx="minor"><a:schemeClr val="tx1"><a:lumMod val="65000"/><a:lumOff val="35000"/></a:schemeClr></cs:fontRef><cs:spPr><a:noFill/><a:ln w="9525" cap="flat" cmpd="sng" algn="ctr"><a:solidFill><a:schemeClr val="tx1"><a:lumMod val="15000"/><a:lumOff val="85000"/></a:schemeClr></a:solidFill><a:round/></a:ln></cs:spPr><cs:defRPr sz="900" kern="1200"/></cs:dataTable><cs:downBar><cs:lnRef idx="0"/><cs:fillRef idx="0"/><cs:effectRef idx="0"/><cs:fontRef idx="minor"><a:schemeClr val="dk1"/></cs:fontRef><cs:spPr><a:solidFill><a:schemeClr val="dk1"><a:lumMod val="65000"/><a:lumOff val="35000"/></a:schemeClr></a:solidFill><a:ln w="9525"><a:solidFill><a:schemeClr val="tx1"><a:lumMod val="65000"/><a:lumOff val="35000"/></a:schemeClr></a:solidFill></a:ln></cs:spPr></cs:downBar><cs:dropLine><cs:lnRef idx="0"/><cs:fillRef idx="0"/><cs:effectRef idx="0"/><cs:fontRef idx="minor"><a:schemeClr val="tx1"/></cs:fontRef><cs:spPr><a:ln w="9525" cap="flat" cmpd="sng" algn="ctr"><a:solidFill><a:schemeClr val="tx1"><a:lumMod val="35000"/><a:lumOff val="65000"/></a:schemeClr></a:solidFill><a:round/></a:ln></cs:spPr></cs:dropLine><cs:errorBar><cs:lnRef idx="0"/><cs:fillRef idx="0"/><cs:effectRef idx="0"/><cs:fontRef idx="minor"><a:schemeClr val="tx1"/></cs:fontRef><cs:spPr><a:ln w="9525" cap="flat" cmpd="sng" algn="ctr"><a:solidFill><a:schemeClr val="tx1"><a:lumMod val="65000"/><a:lumOff val="35000"/></a:schemeClr></a:solidFill><a:round/></a:ln></cs:spPr></cs:errorBar><cs:floor><cs:lnRef idx="0"/><cs:fillRef idx="0"/><cs:effectRef idx="0"/><cs:fontRef idx="minor"><a:schemeClr val="tx1"/></cs:fontRef><cs:spPr><a:noFill/><a:ln><a:noFill/></a:ln></cs:spPr></cs:floor><cs:gridlineMajor><cs:lnRef idx="0"/><cs:fillRef idx="0"/><cs:effectRef idx="0"/><cs:fontRef idx="minor"><a:schemeClr val="tx1"/></cs:fontRef><cs:spPr><a:ln w="9525" cap="flat" cmpd="sng" algn="ctr"><a:solidFill><a:schemeClr val="tx1"><a:lumMod val="15000"/><a:lumOff val="85000"/></a:schemeClr></a:solidFill><a:round/></a:ln></cs:spPr></cs:gridlineMajor><cs:gridlineMinor><cs:lnRef idx="0"/><cs:fillRef idx="0"/><cs:effectRef idx="0"/><cs:fontRef idx="minor"><a:schemeClr val="tx1"/></cs:fontRef><cs:spPr><a:ln w="9525" cap="flat" cmpd="sng" algn="ctr"><a:solidFill><a:schemeClr val="tx1"><a:lumMod val="5000"/><a:lumOff val="95000"/></a:schemeClr></a:solidFill><a:round/></a:ln></cs:spPr></cs:gridlineMinor><cs:hiLoLine><cs:lnRef idx="0"/><cs:fillRef idx="0"/><cs:effectRef idx="0"/><cs:fontRef idx="minor"><a:schemeClr val="tx1"/></cs:fontRef><cs:spPr><a:ln w="9525" cap="flat" cmpd="sng" algn="ctr"><a:solidFill><a:schemeClr val="tx1"><a:lumMod val="75000"/><a:lumOff val="25000"/></a:schemeClr></a:solidFill><a:round/></a:ln></cs:spPr></cs:hiLoLine><cs:leaderLine><cs:lnRef idx="0"/><cs:fillRef idx="0"/><cs:effectRef idx="0"/><cs:fontRef idx="minor"><a:schemeClr val="tx1"/></cs:fontRef><cs:spPr><a:ln w="9525" cap="flat" cmpd="sng" algn="ctr"><a:solidFill><a:schemeClr val="tx1"><a:lumMod val="35000"/><a:lumOff val="65000"/></a:schemeClr></a:solidFill><a:round/></a:ln></cs:spPr></cs:leaderLine><cs:legend><cs:lnRef idx="0"/><cs:fillRef idx="0"/><cs:effectRef idx="0"/><cs:fontRef idx="minor"><a:schemeClr val="tx1"><a:lumMod val="65000"/><a:lumOff val="35000"/></a:schemeClr></cs:fontRef><cs:defRPr sz="900" kern="1200"/></cs:legend><cs:plotArea mods="allowNoFillOverride allowNoLineOverride"><cs:lnRef idx="0"/><cs:fillRef idx="0"/><cs:effectRef idx="0"/><cs:fontRef idx="minor"><a:schemeClr val="tx1"/></cs:fontRef></cs:plotArea><cs:plotArea3D mods="allowNoFillOverride allowNoLineOverride"><cs:lnRef idx="0"/><cs:fillRef idx="0"/><cs:effectRef idx="0"/><cs:fontRef idx="minor"><a:schemeClr val="tx1"/></cs:fontRef></cs:plotArea3D><cs:seriesAxis><cs:lnRef idx="0"/><cs:fillRef idx="0"/><cs:effectRef idx="0"/><cs:fontRef idx="minor"><a:schemeClr val="tx1"><a:lumMod val="65000"/><a:lumOff val="35000"/></a:schemeClr></cs:fontRef><cs:defRPr sz="900" kern="1200"/></cs:seriesAxis><cs:seriesLine><cs:lnRef idx="0"/><cs:fillRef idx="0"/><cs:effectRef idx="0"/><cs:fontRef idx="minor"><a:schemeClr val="tx1"/></cs:fontRef><cs:spPr><a:ln w="9525" cap="flat" cmpd="sng" algn="ctr"><a:solidFill><a:schemeClr val="tx1"><a:lumMod val="35000"/><a:lumOff val="65000"/></a:schemeClr></a:solidFill><a:round/></a:ln></cs:spPr></cs:seriesLine><cs:title><cs:lnRef idx="0"/><cs:fillRef idx="0"/><cs:effectRef idx="0"/><cs:fontRef idx="minor"><a:schemeClr val="tx1"><a:lumMod val="65000"/><a:lumOff val="35000"/></a:schemeClr></cs:fontRef><cs:defRPr sz="1400" b="0" kern="1200" spc="0" baseline="0"/></cs:title><cs:trendline><cs:lnRef idx="0"><cs:styleClr val="auto"/></cs:lnRef><cs:fillRef idx="0"/><cs:effectRef idx="0"/><cs:fontRef idx="minor"><a:schemeClr val="tx1"/></cs:fontRef><cs:spPr><a:ln w="19050" cap="rnd"><a:solidFill><a:schemeClr val="phClr"/></a:solidFill><a:prstDash val="sysDot"/></a:ln></cs:spPr></cs:trendline><cs:trendlineLabel><cs:lnRef idx="0"/><cs:fillRef idx="0"/><cs:effectRef idx="0"/><cs:fontRef idx="minor"><a:schemeClr val="tx1"><a:lumMod val="65000"/><a:lumOff val="35000"/></a:schemeClr></cs:fontRef><cs:defRPr sz="900" kern="1200"/></cs:trendlineLabel><cs:upBar><cs:lnRef idx="0"/><cs:fillRef idx="0"/><cs:effectRef idx="0"/><cs:fontRef idx="minor"><a:schemeClr val="dk1"/></cs:fontRef><cs:spPr><a:solidFill><a:schemeClr val="lt1"/></a:solidFill><a:ln w="9525"><a:solidFill><a:schemeClr val="tx1"><a:lumMod val="15000"/><a:lumOff val="85000"/></a:schemeClr></a:solidFill></a:ln></cs:spPr></cs:upBar><cs:valueAxis><cs:lnRef idx="0"/><cs:fillRef idx="0"/><cs:effectRef idx="0"/><cs:fontRef idx="minor"><a:schemeClr val="tx1"><a:lumMod val="65000"/><a:lumOff val="35000"/></a:schemeClr></cs:fontRef><cs:defRPr sz="900" kern="1200"/></cs:valueAxis><cs:wall><cs:lnRef idx="0"/><cs:fillRef idx="0"/><cs:effectRef idx="0"/><cs:fontRef idx="minor"><a:schemeClr val="tx1"/></cs:fontRef><cs:spPr><a:noFill/><a:ln><a:noFill/></a:ln></cs:spPr></cs:wall></cs:chartStyle>
            """);
    }

    private static string GetSameDirectoryRelationshipTarget(string targetPath)
    {
        var slash = targetPath.LastIndexOf('/');
        return slash < 0 ? targetPath : targetPath[(slash + 1)..];
    }

    private static XElement ToChartAnchor(
        ChartModel chart,
        Sheet sheet,
        int chartIndex,
        string chartRelId,
        string chartRelationshipType,
        // R98-io-chart-hyperlink-model-field: RelId + Tooltip pair -- was a bare string? before this
        // field carried a Tooltip to re-emit (mirrors DrawingObjectHyperlink.Tooltip -- see
        // XlsxWorksheetDrawingObjectWriter's identical hlinkClick@tooltip re-emit for
        // pictures/shapes/text-boxes, R97-model-drawing-hyperlink-2-2).
        (string RelId, string? Tooltip)? objectHyperlinkRelId,
        XNamespace spreadsheetDrawingNs,
        XNamespace drawingNs,
        XNamespace chartNs,
        XNamespace chartExNs,
        XNamespace relNs,
        XNamespace markupCompatNs)
    {
        if (IsChartExRelationshipType(chartRelationshipType))
            return ToTwoCellChartAnchor(chart, sheet, chartIndex, chartRelId, chartRelationshipType, objectHyperlinkRelId, spreadsheetDrawingNs, drawingNs, chartNs, chartExNs, relNs, markupCompatNs);

        return chart.DrawingAnchorKind switch
        {
            ChartDrawingAnchorKind.OneCell => ToOneCellChartAnchor(chart, sheet, chartIndex, chartRelId, chartRelationshipType, objectHyperlinkRelId, spreadsheetDrawingNs, drawingNs, chartNs, chartExNs, relNs, markupCompatNs),
            ChartDrawingAnchorKind.TwoCell => ToTwoCellChartAnchor(chart, sheet, chartIndex, chartRelId, chartRelationshipType, objectHyperlinkRelId, spreadsheetDrawingNs, drawingNs, chartNs, chartExNs, relNs, markupCompatNs),
            _ => ToAbsoluteChartAnchor(chart, chartIndex, chartRelId, chartRelationshipType, objectHyperlinkRelId, spreadsheetDrawingNs, drawingNs, chartNs, chartExNs, relNs, markupCompatNs)
        };
    }

    private static XElement ToAbsoluteChartAnchor(
        ChartModel chart,
        int chartIndex,
        string chartRelId,
        string chartRelationshipType,
        (string RelId, string? Tooltip)? objectHyperlinkRelId,
        XNamespace spreadsheetDrawingNs,
        XNamespace drawingNs,
        XNamespace chartNs,
        XNamespace chartExNs,
        XNamespace relNs,
        XNamespace markupCompatNs) =>
        new(spreadsheetDrawingNs + "absoluteAnchor",
            new XElement(spreadsheetDrawingNs + "pos",
                new XAttribute("x", DrawingMlCoordinateUnits.PixelsToEmuSigned(chart.Left)),
                new XAttribute("y", DrawingMlCoordinateUnits.PixelsToEmuSigned(chart.Top))),
            new XElement(spreadsheetDrawingNs + "ext",
                new XAttribute("cx", DrawingMlCoordinateUnits.PixelsToEmu(chart.Width)),
                new XAttribute("cy", DrawingMlCoordinateUnits.PixelsToEmu(chart.Height))),
            ToChartFrameOrAlternateContent(chart, chartIndex, chartRelId, chartRelationshipType, objectHyperlinkRelId, spreadsheetDrawingNs, drawingNs, chartNs, chartExNs, relNs, markupCompatNs),
            new XElement(spreadsheetDrawingNs + "clientData"));

    private static XElement ToOneCellChartAnchor(
        ChartModel chart,
        Sheet sheet,
        int chartIndex,
        string chartRelId,
        string chartRelationshipType,
        (string RelId, string? Tooltip)? objectHyperlinkRelId,
        XNamespace spreadsheetDrawingNs,
        XNamespace drawingNs,
        XNamespace chartNs,
        XNamespace chartExNs,
        XNamespace relNs,
        XNamespace markupCompatNs)
    {
        var from = ToAnchorMarker(sheet, chart.Left, chart.Top);
        return new XElement(spreadsheetDrawingNs + "oneCellAnchor",
            ToAnchorMarkerXml("from", from, spreadsheetDrawingNs),
            new XElement(spreadsheetDrawingNs + "ext",
                new XAttribute("cx", DrawingMlCoordinateUnits.PixelsToEmu(chart.Width)),
                new XAttribute("cy", DrawingMlCoordinateUnits.PixelsToEmu(chart.Height))),
            ToChartFrameOrAlternateContent(chart, chartIndex, chartRelId, chartRelationshipType, objectHyperlinkRelId, spreadsheetDrawingNs, drawingNs, chartNs, chartExNs, relNs, markupCompatNs),
            new XElement(spreadsheetDrawingNs + "clientData"));
    }

    private static XElement ToTwoCellChartAnchor(
        ChartModel chart,
        Sheet sheet,
        int chartIndex,
        string chartRelId,
        string chartRelationshipType,
        (string RelId, string? Tooltip)? objectHyperlinkRelId,
        XNamespace spreadsheetDrawingNs,
        XNamespace drawingNs,
        XNamespace chartNs,
        XNamespace chartExNs,
        XNamespace relNs,
        XNamespace markupCompatNs)
    {
        var from = ToAnchorMarker(sheet, chart.Left, chart.Top);
        var to = ToAnchorMarker(sheet, chart.Left + chart.Width, chart.Top + chart.Height);
        return new XElement(spreadsheetDrawingNs + "twoCellAnchor",
            ToAnchorMarkerXml("from", from, spreadsheetDrawingNs),
            ToAnchorMarkerXml("to", to, spreadsheetDrawingNs),
            ToChartFrameOrAlternateContent(chart, chartIndex, chartRelId, chartRelationshipType, objectHyperlinkRelId, spreadsheetDrawingNs, drawingNs, chartNs, chartExNs, relNs, markupCompatNs),
            new XElement(spreadsheetDrawingNs + "clientData"));
    }

    private static XElement ToChartFrameOrAlternateContent(
        ChartModel chart,
        int chartIndex,
        string chartRelId,
        string chartRelationshipType,
        (string RelId, string? Tooltip)? objectHyperlinkRelId,
        XNamespace spreadsheetDrawingNs,
        XNamespace drawingNs,
        XNamespace chartNs,
        XNamespace chartExNs,
        XNamespace relNs,
        XNamespace markupCompatNs)
    {
        var graphicFrame = ToChartGraphicFrame(chart, chartIndex, chartRelId, chartRelationshipType, objectHyperlinkRelId, spreadsheetDrawingNs, drawingNs, chartNs, chartExNs, relNs);
        if (!IsChartExRelationshipType(chartRelationshipType))
            return graphicFrame;

        return new XElement(markupCompatNs + "AlternateContent",
            new XAttribute(XNamespace.Xmlns + "mc", markupCompatNs),
            new XElement(markupCompatNs + "Choice",
                new XAttribute(XNamespace.Xmlns + "cx1", ChartExChoiceNamespace),
                new XAttribute("Requires", "cx1"),
                graphicFrame),
            new XElement(markupCompatNs + "Fallback",
                ToChartExFallbackShape(chart, spreadsheetDrawingNs, drawingNs)));
    }

    private static XElement ToChartGraphicFrame(
        ChartModel chart,
        int chartIndex,
        string chartRelId,
        string chartRelationshipType,
        // R98-io-chart-hyperlink-model-field: RelId + Tooltip pair -- was a bare string? before this
        // field carried a Tooltip to re-emit (mirrors DrawingObjectHyperlink.Tooltip -- see
        // XlsxWorksheetDrawingObjectWriter's identical hlinkClick@tooltip re-emit for
        // pictures/shapes/text-boxes, R97-model-drawing-hyperlink-2-2).
        (string RelId, string? Tooltip)? objectHyperlinkRelId,
        XNamespace spreadsheetDrawingNs,
        XNamespace drawingNs,
        XNamespace chartNs,
        XNamespace chartExNs,
        XNamespace relNs)
    {
        var isChartEx = IsChartExRelationshipType(chartRelationshipType);
        return new XElement(spreadsheetDrawingNs + "graphicFrame",
            isChartEx ? new XAttribute("macro", "") : null,
            new XElement(spreadsheetDrawingNs + "nvGraphicFramePr",
                new XElement(spreadsheetDrawingNs + "cNvPr",
                    new XAttribute("id", chartIndex + 1),
                    new XAttribute("name", DrawingName(chart.Name, $"Chart {chartIndex}")),
                    // R80-app-accessibility-a11y-5-1: re-emit the chart's Alt Text title/description
                    // (Excel's View Alt Text) -- matches the picture/shape/text-box writers' existing
                    // title/descr pattern (see XlsxWorksheetDrawingObjectWriter). Without this the
                    // chart's alt text was silently dropped on every open+save round-trip.
                    string.IsNullOrWhiteSpace(chart.AltTextTitle) ? null : new XAttribute("title", chart.AltTextTitle),
                    string.IsNullOrWhiteSpace(chart.AltTextDescription) ? null : new XAttribute("descr", chart.AltTextDescription),
                    // R41-io-hyperlink-drawing-rels-3-1 / R98-io-chart-hyperlink-model-field: re-attach
                    // the chart-object hyperlink, PREFERRING ChartModel.Hyperlink (see the caller,
                    // WriteWorksheetCharts) and falling back to the pre-rebuild drawing part only when
                    // the model carries none. Tooltip re-emitted to match
                    // XlsxWorksheetDrawingObjectWriter's identical hlinkClick@tooltip for
                    // pictures/shapes/text-boxes (R97-model-drawing-hyperlink-2-2) -- the OLD chart
                    // writer never emitted this attribute at all.
                    objectHyperlinkRelId is not { } resolvedRel
                        ? null
                        : new XElement(
                            drawingNs + "hlinkClick",
                            new XAttribute(relNs + "id", resolvedRel.RelId),
                            string.IsNullOrWhiteSpace(resolvedRel.Tooltip) ? null : new XAttribute("tooltip", resolvedRel.Tooltip))),
                new XElement(spreadsheetDrawingNs + "cNvGraphicFramePr")),
            new XElement(spreadsheetDrawingNs + "xfrm",
                isChartEx
                    ? new object[]
                    {
                        new XElement(drawingNs + "off",
                            new XAttribute("x", "0"),
                            new XAttribute("y", "0")),
                        new XElement(drawingNs + "ext",
                            new XAttribute("cx", "0"),
                            new XAttribute("cy", "0"))
                    }
                    : []),
            new XElement(drawingNs + "graphic",
                new XElement(drawingNs + "graphicData",
                    new XAttribute("uri", ToChartDrawingUri(chartRelationshipType)),
                    new XElement(ToChartDrawingElementName(chartRelationshipType, chartNs, chartExNs), new XAttribute(relNs + "id", chartRelId)))));
    }

    private static XElement ToChartExFallbackShape(
        ChartModel chart,
        XNamespace spreadsheetDrawingNs,
        XNamespace drawingNs) =>
        new(spreadsheetDrawingNs + "sp",
            new XAttribute("macro", ""),
            new XAttribute("textlink", ""),
            new XElement(spreadsheetDrawingNs + "nvSpPr",
                new XElement(spreadsheetDrawingNs + "cNvPr",
                    new XAttribute("id", "0"),
                    new XAttribute("name", "")),
                new XElement(spreadsheetDrawingNs + "cNvSpPr",
                    new XElement(drawingNs + "spLocks", new XAttribute("noTextEdit", "1")))),
            new XElement(spreadsheetDrawingNs + "spPr",
                new XElement(drawingNs + "xfrm",
                    new XElement(drawingNs + "off",
                        new XAttribute("x", DrawingMlCoordinateUnits.PixelsToEmu(chart.Left)),
                        new XAttribute("y", DrawingMlCoordinateUnits.PixelsToEmu(chart.Top))),
                    new XElement(drawingNs + "ext",
                        new XAttribute("cx", DrawingMlCoordinateUnits.PixelsToEmu(chart.Width)),
                        new XAttribute("cy", DrawingMlCoordinateUnits.PixelsToEmu(chart.Height)))),
                new XElement(drawingNs + "prstGeom",
                    new XAttribute("prst", "rect"),
                    new XElement(drawingNs + "avLst")),
                new XElement(drawingNs + "solidFill",
                    new XElement(drawingNs + "prstClr", new XAttribute("val", "white"))),
                new XElement(drawingNs + "ln",
                    new XAttribute("w", "1"),
                    new XElement(drawingNs + "solidFill",
                        new XElement(drawingNs + "prstClr", new XAttribute("val", "green"))))),
            new XElement(spreadsheetDrawingNs + "txBody",
                new XElement(drawingNs + "bodyPr",
                    new XAttribute("vertOverflow", "clip"),
                    new XAttribute("horzOverflow", "clip")),
                new XElement(drawingNs + "lstStyle"),
                new XElement(drawingNs + "p",
                    new XElement(drawingNs + "r",
                        new XElement(drawingNs + "rPr",
                            new XAttribute("lang", "en-US"),
                            new XAttribute("sz", "1100")),
                        new XElement(drawingNs + "t",
                            "This chart isn't available in your version of Excel.\n\nEditing this shape or saving this workbook into a different file format will permanently break the chart.")))));

    private static string ToChartDrawingUri(string chartRelationshipType) =>
        IsChartExRelationshipType(chartRelationshipType)
            ? ChartExDrawingUri
            : "http://schemas.openxmlformats.org/drawingml/2006/chart";

    private static XName ToChartDrawingElementName(string chartRelationshipType, XNamespace chartNs, XNamespace chartExNs) =>
        IsChartExRelationshipType(chartRelationshipType)
            ? chartExNs + "chart"
            : chartNs + "chart";

    private static bool IsChartExRelationshipType(string chartRelationshipType) =>
        string.Equals(chartRelationshipType, ChartExRelationshipType, StringComparison.OrdinalIgnoreCase);

    private static XElement ToAnchorMarkerXml(string name, AnchorMarker marker, XNamespace spreadsheetDrawingNs) =>
        new(spreadsheetDrawingNs + name,
            new XElement(spreadsheetDrawingNs + "col", marker.Column),
            new XElement(spreadsheetDrawingNs + "colOff", DrawingMlCoordinateUnits.PixelsToEmu(marker.ColumnOffset)),
            new XElement(spreadsheetDrawingNs + "row", marker.Row),
            new XElement(spreadsheetDrawingNs + "rowOff", DrawingMlCoordinateUnits.PixelsToEmu(marker.RowOffset)));

    // Excel's real ceilings: 16,384 columns (XFD) vs. 1,048,576 rows.
    private const uint MaxColumnIndex = 16384;
    private const uint MaxRowIndex = 1048576;

    private static AnchorMarker ToAnchorMarker(Sheet sheet, double left, double top) =>
        new(
            ToMarkerIndex(left, sheet.DefaultColumnWidth * 8, MaxColumnIndex, column => sheet.IsColEffectivelyHidden(column), column => sheet.ColumnWidths.GetValueOrDefault(column, sheet.DefaultColumnWidth) * 8),
            ToMarkerIndex(top, sheet.DefaultRowHeight, MaxRowIndex, row => sheet.IsRowEffectivelyHidden(row), row => sheet.RowHeights.GetValueOrDefault(row, sheet.DefaultRowHeight)));

    private static MarkerAxis ToMarkerIndex(double pixels, double defaultSize, uint maxIndex, Func<uint, bool> isHidden, Func<uint, double> getSize)
    {
        var remaining = Math.Max(0, pixels);
        var index = 0u;
        while (index < maxIndex)
        {
            var oneBasedIndex = index + 1;
            var size = isHidden(oneBasedIndex) ? 0 : Math.Max(0, getSize(oneBasedIndex));
            if (size <= 0)
            {
                index++;
                continue;
            }

            if (remaining < size)
                return new MarkerAxis(index, remaining);

            remaining -= size;
            index++;
        }

        // R110: the walk exhausted every column/row (e.g. because most of the sheet is hidden,
        // so remaining pixel distance never fit within a visible column/row). `index` is now
        // `maxIndex`, one past Excel's real zero-based ceiling (16383 columns / 1048575 rows) --
        // matching XlsxDrawingAnchorApplier's read-side MaxColumnIndexZeroBased/MaxRowIndexZeroBased.
        // Clamp so we never write an out-of-range <xdr:col>/<xdr:row> that Excel would reject/repair.
        return new MarkerAxis(maxIndex - 1, Math.Min(remaining, Math.Max(0, defaultSize)));
    }

    private readonly record struct MarkerAxis(uint Index, double Offset);

    private readonly record struct AnchorMarker(MarkerAxis ColumnAxis, MarkerAxis RowAxis)
    {
        public uint Column => ColumnAxis.Index;
        public double ColumnOffset => ColumnAxis.Offset;
        public uint Row => RowAxis.Index;
        public double RowOffset => RowAxis.Offset;
    }

    private static string DrawingName(string? name, string fallback) =>
        string.IsNullOrWhiteSpace(name) ? fallback : name;

    private readonly record struct ChartExStylePackageParts(string ColorStylePath, string StylePath);
}
