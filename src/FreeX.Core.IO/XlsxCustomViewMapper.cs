using System;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

internal static class XlsxCustomViewMapper
{
    public static IReadOnlyList<XlsxWorksheetCustomViewState> ReadWorksheetViews(
        XDocument worksheetXml,
        XNamespace worksheetNs)
    {
        var customViews = new List<XlsxWorksheetCustomViewState>();
        foreach (var customSheetView in worksheetXml.Root?
                     .Element(worksheetNs + "customSheetViews")?
                     .Elements(worksheetNs + "customSheetView") ?? [])
        {
            var id = customSheetView.Attribute("guid")?.Value;
            if (string.IsNullOrWhiteSpace(id))
                continue;

            var pane = customSheetView.Element(worksheetNs + "pane");
            var paneState = pane?.Attribute("state")?.Value;
            // CT_Pane's xSplit/ySplit are the row/column index under state="frozen"/"frozenSplit";
            // WorksheetCustomViewState.SplitRow/SplitColumn (the non-frozen "split" case) mirror
            // Sheet.SplitRow/SplitColumn, which are themselves row/column indexes (see Sheet.cs) --
            // this mapper reads/writes both cases as the literal index, matching FileAdapterSmoke's
            // and NativeJsonAdapter's established modeling of split panes.
            var rawYSplit = XlsxWorksheetXmlValueParser.ParsePaneSplit(pane?.Attribute("ySplit")?.Value);
            var rawXSplit = XlsxWorksheetXmlValueParser.ParsePaneSplit(pane?.Attribute("xSplit")?.Value);
            var frozenRows = paneState is "frozen" or "frozenSplit" ? rawYSplit ?? 0 : 0;
            var frozenCols = paneState is "frozen" or "frozenSplit" ? rawXSplit ?? 0 : 0;
            var splitRow = frozenRows == 0 && frozenCols == 0 ? rawYSplit : null;
            var splitColumn = frozenRows == 0 && frozenCols == 0 ? rawXSplit : null;
            var topLeftCell = customSheetView.Attribute("topLeftCell")?.Value;
            var activeCell = ReadActiveSelectionCellReference(customSheetView, pane, worksheetNs);

            var pageMargins = customSheetView.Element(worksheetNs + "pageMargins");
            var pageSetup = customSheetView.Element(worksheetNs + "pageSetup");
            var printOptions = customSheetView.Element(worksheetNs + "printOptions");
            var autoFilter = XlsxWorksheetAutoFilterXmlMapper.Read(customSheetView.Element(worksheetNs + "autoFilter"));
            var paperSizeCode = ParsePaperSizeCode(pageSetup);
            var fitToPage = XlsxXmlAttributeReader.ReadNullableBoolAttribute(customSheetView, "fitToPage");

            customViews.Add(new XlsxWorksheetCustomViewState(
                id,
                new WorksheetCustomViewState(
                    string.Empty,
                    XlsxWorksheetXmlValueParser.ParseWorksheetViewMode(customSheetView.Attribute("view")?.Value),
                    frozenRows,
                    frozenCols,
                    splitRow,
                    splitColumn,
                    ShowGridlines: !XlsxWorksheetXmlValueParser.IsFalse(customSheetView.Attribute("showGridLines")?.Value),
                    ShowHeadings: !XlsxWorksheetXmlValueParser.IsFalse(customSheetView.Attribute("showRowCol")?.Value),
                    ShowRulers: !XlsxWorksheetXmlValueParser.IsFalse(customSheetView.Attribute("showRuler")?.Value),
                    ZoomPercent: XlsxWorksheetValueSanitizer.ValidZoomPercentOrDefault(XlsxXmlAttributeReader.ReadIntAttribute(customSheetView, "scale") ?? 100),
                    ShowFormulas: XlsxWorksheetXmlValueParser.IsTruthy(customSheetView.Attribute("showFormulas")?.Value),
                    ActiveRow: ParseCellRow(activeCell),
                    ActiveCol: ParseCellColumn(activeCell),
                    ViewTopRow: ParseCellRow(topLeftCell),
                    ViewLeftCol: ParseCellColumn(topLeftCell),
                    // N13: hidden-rows/cols state has no dedicated slot in CT_CustomSheetView
                    // (ECMA-376 §18.3.1.90) — Excel itself does not snapshot per-view hidden-row/
                    // column lists there (only the live sheetData row/col hidden attributes) — so
                    // that is left uncaptured here rather than guessing at a non-standard
                    // extension. FitToPage IS a real customSheetView attribute (CT_CustomSheetView.
                    // FitToPage, distinct from the worksheet-level sheetPr/pageSetUpPr/@fitToPage
                    // flag) and AutoFilter/print settings also have schema support (nested
                    // autoFilter/pageMargins/pageSetup/printOptions elements), so all of those
                    // round-trip below.
                    FitToPage: fitToPage,
                    AutoFilter: autoFilter,
                    PageOrientation: ParseOrientation(pageSetup?.Attribute("orientation")?.Value),
                    PaperSize: paperSizeCode is { } code && PaperSizeCodes.TryGetEnum(code, out var paperSize) ? paperSize : null,
                    PaperSizeCode: paperSizeCode,
                    PageMargins: ParsePageMargins(pageMargins),
                    HeaderMargin: ParseNullableDouble(pageMargins, "header"),
                    FooterMargin: ParseNullableDouble(pageMargins, "footer"),
                    PrintGridlines: XlsxXmlAttributeReader.ReadNullableBoolAttribute(printOptions, "gridLines"),
                    PrintHeadings: XlsxXmlAttributeReader.ReadNullableBoolAttribute(printOptions, "headings"),
                    ScaleToFit: ParseScaleToFit(pageSetup, fitToPage))));
        }

        return customViews;
    }

    private static string? ReadActiveSelectionCellReference(XElement customSheetView, XElement? pane, XNamespace worksheetNs)
    {
        // Mirrors XlsxFileAdapter.SheetXmlLayout.ReadActiveSelectionCell: when the view is frozen/
        // split into panes, Excel writes one <selection> per pane and marks the pane holding the
        // true cursor via pane/@activePane (defaulting to "topLeft" when no pane element is
        // present). A <selection> with no @pane attribute implicitly belongs to "topLeft". Picking
        // the first <selection> in document order (rather than the one matching the active pane)
        // silently reports the wrong active cell whenever the user's cursor was left in any pane
        // other than the first one Excel happened to write.
        var activePaneName = pane?.Attribute("activePane")?.Value;
        if (string.IsNullOrWhiteSpace(activePaneName))
            activePaneName = "topLeft";

        string? fallbackReference = null;
        foreach (var selection in customSheetView.Elements(worksheetNs + "selection"))
        {
            var reference = selection.Attribute("activeCell")?.Value ?? selection.Attribute("sqref")?.Value;
            if (string.IsNullOrWhiteSpace(reference))
                continue;

            fallbackReference ??= reference;

            var selectionPaneName = selection.Attribute("pane")?.Value;
            if (string.IsNullOrWhiteSpace(selectionPaneName))
                selectionPaneName = "topLeft";

            if (string.Equals(selectionPaneName, activePaneName, StringComparison.Ordinal))
                return reference;
        }

        return fallbackReference;
    }

    private static WorksheetPageOrientation? ParseOrientation(string? value) => value switch
    {
        "landscape" => WorksheetPageOrientation.Landscape,
        "portrait" => WorksheetPageOrientation.Portrait,
        _ => null
    };

    private static int? ParsePaperSizeCode(XElement? pageSetup) =>
        pageSetup is null ? null : XlsxXmlAttributeReader.ReadIntAttribute(pageSetup, "paperSize");

    private static double? ParseNullableDouble(XElement? element, string attributeName) =>
        element is null ? null : XlsxXmlAttributeReader.ReadDoubleAttribute(element, attributeName);

    private static WorksheetPageMargins? ParsePageMargins(XElement? pageMargins)
    {
        if (pageMargins is null)
            return null;

        var left = XlsxXmlAttributeReader.ReadDoubleAttribute(pageMargins, "left");
        var right = XlsxXmlAttributeReader.ReadDoubleAttribute(pageMargins, "right");
        var top = XlsxXmlAttributeReader.ReadDoubleAttribute(pageMargins, "top");
        var bottom = XlsxXmlAttributeReader.ReadDoubleAttribute(pageMargins, "bottom");
        if (left is null || right is null || top is null || bottom is null)
            return null;

        return XlsxWorksheetValueSanitizer.ValidPageMarginsOrDefault(
            new WorksheetPageMargins(left.Value, right.Value, top.Value, bottom.Value),
            WorksheetPageMargins.Narrow);
    }

    // R115: CT_CustomSheetView's nested <pageSetup> can carry scale/fitToWidth/fitToHeight
    // attributes together even though only one mode is ever actually active -- Excel is known to
    // leave the inactive mode's attribute(s) behind as stale leftovers (the same "sibling attribute
    // goes stale" quirk this codebase already documents for firstPageNumber/useFirstPageNumber; see
    // XlsxFileAdapter.cs). The sibling customSheetView/@fitToPage attribute (read into `fitToPage`
    // above) is the SOLE discriminator for which mode is actually live, mirroring exactly how
    // XlsxFileAdapter.cs resolves the identical ambiguity for the main worksheet via ClosedXML's
    // PagesWide/PagesTall (which itself defers to sheetPr/pageSetUpPr/@fitToPage). Resolving the
    // ambiguity here -- rather than passing all three raw attributes through unconditionally -- keeps
    // the resulting WorksheetScaleToFit from ever encoding "both modes active" the way the main
    // sheet's ScaleToFit never does, so every downstream consumer (ToPageSetupXml below,
    // XlsxWorksheetPageSetupMetadataWriter.DetermineEffectiveFitToPage, and
    // CustomViewCommands.ApplyState copying this straight onto Sheet.ScaleToFit when a view is
    // shown) sees an already-unambiguous value instead of having to re-derive the same priority
    // decision -- and possibly disagreeing with each other, as they did before this fix.
    private static WorksheetScaleToFit? ParseScaleToFit(XElement? pageSetup, bool? fitToPage)
    {
        if (pageSetup is null)
            return null;

        var scale = XlsxXmlAttributeReader.ReadIntAttribute(pageSetup, "scale");
        var fitToWidth = XlsxXmlAttributeReader.ReadIntAttribute(pageSetup, "fitToWidth");
        var fitToHeight = XlsxXmlAttributeReader.ReadIntAttribute(pageSetup, "fitToHeight");
        if (scale is null && fitToWidth is null && fitToHeight is null)
            return null;

        var resolved = fitToPage is true
            ? new WorksheetScaleToFit(null, fitToWidth, fitToHeight)
            : new WorksheetScaleToFit(scale, null, null);

        return XlsxWorksheetValueSanitizer.ValidScaleToFitOrDefault(resolved, WorksheetScaleToFit.Default);
    }

    public static void Save(Stream packageStream, Workbook workbook)
    {
        using var archive = new ZipArchive(packageStream, ZipArchiveMode.Update, leaveOpen: true);
        var workbookEntry = archive.GetEntry("xl/workbook.xml");
        if (workbookEntry is null)
            return;

        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

        var workbookXml = XlsxPackageXmlEditor.LoadXml(workbookEntry);
        var workbookRels = XlsxRelationshipReader.LoadTargets(
            archive,
            "xl/_rels/workbook.xml.rels",
            "xl/workbook.xml",
            packageRelNs);
        var sheetPaths = XlsxWorkbookSheetPathReader.GetWorkbookSheetPaths(workbookXml, workbookRels, workbookNs, relNs)
            .ToDictionary(pair => pair.SheetName, pair => pair.WorksheetPath, StringComparer.OrdinalIgnoreCase);
        var sheetsElement = workbookXml.Root?.Element(workbookNs + "sheets");

        var customViews = workbook.CustomViews
            .Select((view, index) => new
            {
                View = view,
                Id = NormalizeId(view.Id) ?? CreateDeterministicId(view.Name, index),
                States = view.Sheets
                    .GroupBy(state => state.SheetName, StringComparer.OrdinalIgnoreCase)
                    .Select(group => group.Last())
                    .ToList()
            })
            .Where(item => !string.IsNullOrWhiteSpace(item.View.Name) && item.States.Count > 0)
            .ToList();
        if (customViews.Count == 0)
            return;

        workbookXml.Root?.Element(workbookNs + "customWorkbookViews")?.Remove();
        InsertWorkbookCustomViewsInOrder(workbookXml.Root, workbookNs, new XElement(
            workbookNs + "customWorkbookViews",
            customViews.Select(item => new XElement(
                workbookNs + "customWorkbookView",
                new XAttribute("name", item.View.Name),
                new XAttribute("guid", item.Id),
                new XAttribute("activeSheetId", GetActiveSheetId(sheetsElement, workbookNs, workbook, item.View)),
                item.View.IncludePrintSettings ? new XAttribute("includePrintSettings", "1") : new XAttribute("includePrintSettings", "0"),
                item.View.IncludeHiddenRowsColumnsAndFilterSettings ? new XAttribute("includeHiddenRowCol", "1") : new XAttribute("includeHiddenRowCol", "0")))));
        // R90-io-sheet-view-custom-views-5-3: autoUpdate/mergeInterval/personalView are
        // intentionally NOT written above. Their CT_CustomWorkbookView schema defaults
        // (false/0/false) already match omission, and leaving them absent lets
        // XlsxWorkbookMetadataPreserver.MergeCustomWorkbookViews (MergeMissingAttributes)
        // restore the source file's true values for this view's guid -- hardcoding "0"
        // previously masked those attributes as "already present" and silently discarded
        // the source's real autoUpdate/mergeInterval/personalView on every save.
        XlsxPackageXmlEditor.ReplaceXml(archive, "xl/workbook.xml", workbookXml);

        var customViewsBySheet = new Dictionary<string, List<XElement>>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in customViews)
        {
            foreach (var state in item.States)
            {
                if (!customViewsBySheet.TryGetValue(state.SheetName, out var elements))
                {
                    elements = [];
                    customViewsBySheet[state.SheetName] = elements;
                }

                elements.Add(ToCustomSheetViewXml(workbookNs, item.Id, state));
            }
        }

        foreach (var (sheetName, customSheetViews) in customViewsBySheet)
        {
            if (!sheetPaths.TryGetValue(sheetName, out var worksheetPath))
                continue;

            var worksheetEntry = archive.GetEntry(worksheetPath);
            if (worksheetEntry is null)
                continue;

            var worksheetXml = XlsxPackageXmlEditor.LoadXml(worksheetEntry);
            var root = worksheetXml.Root;
            if (root is null)
                continue;

            root.Element(workbookNs + "customSheetViews")?.Remove();
            InsertWorksheetCustomViewsInOrder(root, workbookNs, new XElement(
                workbookNs + "customSheetViews",
                customSheetViews));
            XlsxPackageXmlEditor.ReplaceXml(archive, worksheetPath, worksheetXml);
        }
    }

    public static HashSet<string> GetModeledIds(Workbook workbook)
    {
        return workbook.CustomViews
            .Select((view, index) => NormalizeId(view.Id) ?? CreateDeterministicId(view.Name, index))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public static string? NormalizeId(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return null;

        var trimmed = id.Trim();
        if (Guid.TryParse(trimmed.Trim('{', '}'), out var guid))
            return $"{{{guid:D}}}".ToUpperInvariant();

        return trimmed;
    }

    private static string CreateDeterministicId(string name, int index)
    {
        var bytes = MD5.HashData(Encoding.UTF8.GetBytes($"FreeX.CustomView:{index}:{name}"));
        return $"{{{new Guid(bytes):D}}}".ToUpperInvariant();
    }

    private static XElement ToCustomSheetViewXml(XNamespace workbookNs, string id, WorksheetCustomViewState state)
    {
        var frozenRows = XlsxWorksheetXmlValueParser.ValidFrozenRowsOrZero(state.FrozenRows);
        var frozenCols = XlsxWorksheetXmlValueParser.ValidFrozenColumnsOrZero(state.FrozenCols);
        var hasFrozenPanes = frozenRows > 0 || frozenCols > 0;
        var splitRow = hasFrozenPanes ? null : state.SplitRow;
        var splitColumn = hasFrozenPanes ? null : state.SplitColumn;
        var autoFilterXml = ToAutoFilterXml(workbookNs, state.AutoFilter);

        var customSheetView = new XElement(
            workbookNs + "customSheetView",
            new XAttribute("guid", id),
            ToCellReference(state.ViewTopRow, state.ViewLeftCol) is { } topLeftCell
                ? new XAttribute("topLeftCell", topLeftCell)
                : null,
            XlsxWorksheetViewWriter.ToXlsxWorksheetViewMode(XlsxWorksheetValueSanitizer.ValidEnumOrDefault(state.ViewMode, WorksheetViewMode.Normal)) is { } view
                ? new XAttribute("view", view)
                : null,
            state.ShowGridlines ? null : new XAttribute("showGridLines", "0"),
            state.ShowHeadings ? null : new XAttribute("showRowCol", "0"),
            state.ShowRulers ? null : new XAttribute("showRuler", "0"),
            state.ZoomPercent == 100 ? null : new XAttribute("scale", XlsxWorksheetValueSanitizer.ValidZoomPercentOrDefault(state.ZoomPercent)),
            state.ShowFormulas ? new XAttribute("showFormulas", "1") : null,
            DetermineEffectiveFitToPage(state) is true ? new XAttribute("fitToPage", "1") : null,
            // ShowAutoFilter ("Show AutoFilter Drop Down Controls") is distinct from the <autoFilter>
            // child element's own ref/criteria: without it, Excel restores the view without the
            // filter dropdown arrows even though the underlying autoFilter element round-trips.
            autoFilterXml is not null ? new XAttribute("showAutoFilter", "1") : null,
            new XAttribute("state", "visible"));

        var hasPanes = hasFrozenPanes || splitRow.HasValue || splitColumn.HasValue;
        var activeCell = ToCellReference(state.ActiveRow, state.ActiveCol);

        // Matches XlsxWorksheetViewWriter.UpdateSheetView's treatment of the primary sheetView:
        // when the view is frozen/split and the captured active cell falls outside the topLeft
        // quadrant, Excel always tags the <pane> with @activePane and the matching <selection>
        // with @pane naming that same quadrant (a <selection> with no @pane, or a <pane> with no
        // @activePane, both default to "topLeft" per ECMA-376 §18.3.1.66/§18.3.1.90). Computing
        // this from the same ComputeActivePaneName the primary writer uses keeps a Custom View
        // capture of a scrolled-past-the-freeze-line cursor from being recorded as though it were
        // sitting in the topLeft pane. Only computed when activeCell itself is valid (matches
        // ToCellReference's own row/col range check), so an out-of-range ActiveRow/ActiveCol never
        // tags the <pane> with an activePane whose corresponding <selection> was never written.
        string? activePaneName = hasPanes && activeCell is not null
            ? XlsxWorksheetViewWriter.ComputeActivePaneName(frozenRows, frozenCols, splitRow, splitColumn, state.ActiveRow!.Value, state.ActiveCol!.Value)
            : null;

        if (hasPanes)
        {
            // Mirrors ReadWorksheetViews above: both state="frozen" and state="split" are written
            // as the literal row/column index here (SplitRow/SplitColumn already model the index,
            // matching Sheet.SplitRow/SplitColumn), not a pane-bar pixel offset.
            customSheetView.Add(new XElement(
                workbookNs + "pane",
                !hasFrozenPanes && splitColumn is { } splitColumnValue ? new XAttribute("xSplit", splitColumnValue) : null,
                !hasFrozenPanes && splitRow is { } splitRowValue ? new XAttribute("ySplit", splitRowValue) : null,
                frozenCols > 0 ? new XAttribute("xSplit", frozenCols) : null,
                frozenRows > 0 ? new XAttribute("ySplit", frozenRows) : null,
                new XAttribute("state", hasFrozenPanes ? "frozen" : "split"),
                activePaneName is not null && !string.Equals(activePaneName, "topLeft", StringComparison.Ordinal)
                    ? new XAttribute("activePane", activePaneName)
                    : null));
        }

        if (activeCell is not null)
        {
            customSheetView.Add(new XElement(
                workbookNs + "selection",
                activePaneName is not null && !string.Equals(activePaneName, "topLeft", StringComparison.Ordinal)
                    ? new XAttribute("pane", activePaneName)
                    : null,
                new XAttribute("activeCell", activeCell),
                new XAttribute("sqref", activeCell)));
        }

        // N13: print settings and AutoFilter have dedicated slots in CT_CustomSheetView
        // (ECMA-376 §18.3.1.90, sequence: pane?, selection*, pageMargins?, printOptions?,
        // pageSetup?, headerFooter?, autoFilter?, extLst?) — hidden-rows/cols/FilterHiddenRows
        // do not (Excel keeps those on the live sheetData row/col elements), so that field is
        // intentionally not written here; see the matching read-side comment above.
        if (ToPageMarginsXml(workbookNs, state) is { } pageMarginsXml)
            customSheetView.Add(pageMarginsXml);
        if (ToPrintOptionsXml(workbookNs, state) is { } printOptionsXml)
            customSheetView.Add(printOptionsXml);
        if (ToPageSetupXml(workbookNs, state) is { } pageSetupXml)
            customSheetView.Add(pageSetupXml);
        if (autoFilterXml is not null)
            customSheetView.Add(autoFilterXml);

        return customSheetView;
    }

    /// <summary>
    /// Resolves the customSheetView/@fitToPage attribute from the captured
    /// <see cref="WorksheetCustomViewState.ScaleToFit"/> rather than trusting the raw
    /// <see cref="WorksheetCustomViewState.FitToPage"/> field, mirroring
    /// XlsxWorksheetPageSetupMetadataWriter.DetermineEffectiveFitToPage's identical treatment of
    /// Sheet.FitToPage for the main worksheet. WorksheetCustomViewState.FitToPage can be populated
    /// two ways: (1) round-tripped from a loaded file's customSheetView/@fitToPage, in which case it
    /// was the very attribute ParseScaleToFit above already used to resolve ScaleToFit -- so the two
    /// already agree; or (2) copied from Sheet.FitToPage when the user does View &gt; Custom Views &gt;
    /// Add (see CustomViewCommands/CustomViewStatePlanner.CaptureSheetState) -- and Sheet.FitToPage is
    /// documented (see XlsxWorksheetPageSetupMetadataWriter) as a load-time flag the Page Setup
    /// dialog's scale/fit-to-page toggle never updates, so it can be stale relative to the sheet's
    /// actual current Sheet.ScaleToFit. Deriving from ScaleToFit's own populated field first -- and
    /// only falling back to the raw flag when ScaleToFit itself was never captured -- keeps this
    /// element's fitToPage attribute from ever disagreeing with its own sibling &lt;pageSetup&gt;
    /// (written by ToPageSetupXml from that same ScaleToFit) the way it could before this fix.
    /// </summary>
    private static bool? DetermineEffectiveFitToPage(WorksheetCustomViewState state)
    {
        var scaleToFit = state.ScaleToFit;
        if (scaleToFit is null)
            return state.FitToPage;
        if (scaleToFit.Value.ScalePercent is not null)
            return false;
        if (scaleToFit.Value.FitToPagesWide is not null || scaleToFit.Value.FitToPagesTall is not null)
            return true;
        return state.FitToPage;
    }

    private static XElement? ToPageMarginsXml(XNamespace workbookNs, WorksheetCustomViewState state)
    {
        var margins = state.PageMargins;
        var header = state.HeaderMargin;
        var footer = state.FooterMargin;
        if (margins is null && header is null && footer is null)
            return null;

        var resolvedMargins = XlsxWorksheetValueSanitizer.ValidPageMarginsOrDefault(
            margins ?? WorksheetPageMargins.Narrow, WorksheetPageMargins.Narrow);
        return new XElement(
            workbookNs + "pageMargins",
            new XAttribute("left", resolvedMargins.Left),
            new XAttribute("right", resolvedMargins.Right),
            new XAttribute("top", resolvedMargins.Top),
            new XAttribute("bottom", resolvedMargins.Bottom),
            new XAttribute("header", header ?? 0.3),
            new XAttribute("footer", footer ?? 0.3));
    }

    private static XElement? ToPrintOptionsXml(XNamespace workbookNs, WorksheetCustomViewState state)
    {
        if (state.PrintGridlines is null && state.PrintHeadings is null)
            return null;

        return new XElement(
            workbookNs + "printOptions",
            state.PrintGridlines is true ? new XAttribute("gridLines", "1") : null,
            state.PrintHeadings is true ? new XAttribute("headings", "1") : null);
    }

    private static XElement? ToPageSetupXml(XNamespace workbookNs, WorksheetCustomViewState state)
    {
        if (state.PageOrientation is null && state.PaperSizeCode is null && state.PaperSize is null && state.ScaleToFit is null)
            return null;

        var paperSizeCode = state.PaperSizeCode ?? (state.PaperSize is { } paperSize ? PaperSizeCodes.GetCode(paperSize) : (int?)null);
        var scaleToFit = XlsxWorksheetValueSanitizer.ValidScaleToFitOrDefault(
            state.ScaleToFit ?? WorksheetScaleToFit.Default, WorksheetScaleToFit.Default);

        return new XElement(
            workbookNs + "pageSetup",
            paperSizeCode is { } code ? new XAttribute("paperSize", code) : null,
            state.PageOrientation is { } orientation
                ? new XAttribute("orientation", orientation == WorksheetPageOrientation.Landscape ? "landscape" : "portrait")
                : null,
            scaleToFit.FitToPagesWide is null && scaleToFit.FitToPagesTall is null
                ? new XAttribute("scale", scaleToFit.ScalePercent ?? 100)
                : null,
            scaleToFit.FitToPagesWide is { } fitToPagesWide ? new XAttribute("fitToWidth", fitToPagesWide) : null,
            scaleToFit.FitToPagesTall is { } fitToPagesTall ? new XAttribute("fitToHeight", fitToPagesTall) : null);
    }

    private static XElement? ToAutoFilterXml(XNamespace workbookNs, WorksheetAutoFilterModel? autoFilter)
    {
        if (autoFilter is null)
            return null;

        // Prefer replaying the exact captured XML (round-trips native attributes/child elements
        // this mapper doesn't model) and fall back to a minimal reference-only element so a
        // programmatically-created AutoFilter (no NativeXml yet) still survives the round-trip.
        if (!string.IsNullOrWhiteSpace(autoFilter.NativeXml))
        {
            try
            {
                var native = XElement.Parse(autoFilter.NativeXml);
                if (native.Name == workbookNs + "autoFilter")
                    return native;
            }
            catch (System.Xml.XmlException)
            {
                // Fall through to the minimal element below.
            }
        }

        return string.IsNullOrWhiteSpace(autoFilter.Reference)
            ? null
            : new XElement(workbookNs + "autoFilter", new XAttribute("ref", autoFilter.Reference));
    }

    /// <summary>
    /// Resolves the 0-based ActiveSheetIndex to the ACTUAL <c>sheetId</c> of the corresponding
    /// <c>&lt;sheet&gt;</c> element in the live <c>&lt;sheets&gt;</c> being written, per
    /// CT_CustomWorkbookView (customWorkbookView/@activeSheetId references a sheetId, not a
    /// position). Excel never reuses/renumbers sheetId across deletes/reorders, so a workbook whose
    /// sheetId values have drifted from position (extremely common) would otherwise get a wrong or
    /// unresolvable activeSheetId. Falls back to the 1-based position only when the index can't be
    /// resolved against &lt;sheets&gt; (e.g. missing/malformed sheetId attributes).
    /// </summary>
    private static int GetActiveSheetId(XElement? sheetsElement, XNamespace workbookNs, Workbook workbook, WorkbookCustomView view)
    {
        var index = view.ActiveSheetIndex ?? workbook.ActiveSheetIndex ?? 0;
        var sheetElements = sheetsElement?.Elements(workbookNs + "sheet").ToList();
        if (sheetElements is not null && index >= 0 && index < sheetElements.Count &&
            XlsxXmlAttributeReader.ReadIntAttribute(sheetElements[index], "sheetId") is { } sheetId and > 0)
        {
            return sheetId;
        }

        var maxSheetId = Math.Max(1, workbook.Sheets.Count);
        return Math.Clamp(index + 1, 1, maxSheetId);
    }

    private static string? ToCellReference(uint? row, uint? column)
    {
        if (row is not (>= 1 and <= CellAddress.MaxRow) ||
            column is not (>= 1 and <= CellAddress.MaxCol))
        {
            return null;
        }

        return new CellAddress(default, row.Value, column.Value).ToA1();
    }

    private static uint? ParseCellRow(string? reference) =>
        TryParseCellReference(reference, out var address) ? address.Row : null;

    private static uint? ParseCellColumn(string? reference) =>
        TryParseCellReference(reference, out var address) ? address.Col : null;

    private static bool TryParseCellReference(string? reference, out CellAddress address)
    {
        if (!string.IsNullOrWhiteSpace(reference))
        {
            var firstReference = reference
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(firstReference) &&
                CellAddress.TryParse(firstReference, default, out address))
            {
                return true;
            }
        }

        address = default;
        return false;
    }

    private static void InsertWorkbookCustomViewsInOrder(
        XElement? workbookRoot,
        XNamespace workbookNs,
        XElement customWorkbookViews)
    {
        if (workbookRoot is null)
            return;

        string[] laterWorkbookElements =
        [
            "pivotCaches",
            "smartTagPr",
            "smartTagTypes",
            "webPublishing",
            "fileRecoveryPr",
            "webPublishObjects",
            "extLst"
        ];

        var insertionPoint = FindFirstLaterElement(workbookRoot, workbookNs, laterWorkbookElements);
        if (insertionPoint is null)
            workbookRoot.Add(customWorkbookViews);
        else
            insertionPoint.AddBeforeSelf(customWorkbookViews);
    }

    private static void InsertWorksheetCustomViewsInOrder(
        XElement worksheetRoot,
        XNamespace workbookNs,
        XElement customSheetViews)
    {
        string[] laterWorksheetElements =
        [
            "mergeCells",
            "phoneticPr",
            "conditionalFormatting",
            "dataValidations",
            "hyperlinks",
            "printOptions",
            "pageMargins",
            "pageSetup",
            "headerFooter",
            "rowBreaks",
            "colBreaks",
            "customProperties",
            "cellWatches",
            "ignoredErrors",
            "singleXmlCells",
            "smartTags",
            "drawing",
            "legacyDrawing",
            "legacyDrawingHF",
            "picture",
            "oleObjects",
            "controls",
            "webPublishItems",
            "tableParts",
            "extLst"
        ];

        var insertionPoint = FindFirstLaterElement(worksheetRoot, workbookNs, laterWorksheetElements);
        if (insertionPoint is null)
            worksheetRoot.Add(customSheetViews);
        else
            insertionPoint.AddBeforeSelf(customSheetViews);
    }

    private static XElement? FindFirstLaterElement(
        XElement root,
        XNamespace workbookNs,
        string[] laterElementNames)
    {
        foreach (var element in root.Elements())
        {
            if (element.Name.Namespace != workbookNs)
                continue;

            foreach (var laterElementName in laterElementNames)
            {
                if (string.Equals(element.Name.LocalName, laterElementName, StringComparison.Ordinal))
                    return element;
            }
        }

        return null;
    }
}

internal sealed record XlsxWorksheetCustomViewState(string Id, WorksheetCustomViewState State);
