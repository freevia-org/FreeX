using System.Globalization;
using System.Xml.Linq;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

internal static class XlsxWorksheetPrimaryViewMetadataWriter
{
    private static readonly XNamespace WorksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    public static void Save(Stream xlsxStream, Workbook workbook, XlsxWorkbookWorksheetPathMap? worksheetPathMap)
    {
        if (worksheetPathMap is null)
            return;

        using var session = new XlsxWorksheetXmlEditSession(xlsxStream, worksheetPathMap);
        Save(session, workbook);
    }

    internal static void Save(XlsxWorksheetXmlEditSession session, Workbook workbook)
    {
        var activeSheet = ResolveActiveSheet(workbook);

        foreach (var sheet in workbook.Sheets)
        {
            var metadata = sheet.PrimaryViewMetadata;
            var isActiveSheet = ReferenceEquals(sheet, activeSheet);

            // A sheet with no preserved sheetView metadata at all (never had any non-modeled
            // attribute, e.g. a plain sheet that was never the active tab) has nothing here to
            // reconcile -- UNLESS it is the currently active sheet, in which case tabSelected="1"
            // must still be stamped onto its sheetView below even though there is no load-time bag
            // to drive anything else.
            if (metadata is null && !isActiveSheet)
                continue;

            if (!session.TryGetWorksheet(sheet, out var worksheetEdit))
                continue;

            var root = worksheetEdit.Root;
            var sheetViews = root.Element(WorksheetNs + "sheetViews");
            if (sheetViews is null)
            {
                sheetViews = new XElement(WorksheetNs + "sheetViews");
                root.AddFirst(sheetViews);
            }

            var sheetView = FindPrimarySheetView(sheetViews);
            if (sheetView is null)
            {
                sheetView = new XElement(WorksheetNs + "sheetView", new XAttribute("workbookViewId", "0"));
                sheetViews.AddFirst(sheetView);
            }

            // Every attribute name listed here must match XlsxWorksheetViewWriter.UpdateSheetView's own
            // SetOrRemoveAttributeIfChanged set (mirrors ModeledSheetViewMergeAttributes in
            // XlsxWorksheetMetadataPreserver.ModeledAttributes.cs): that writer already reconciled the
            // live sheetView against the current Sheet model earlier in the same save
            // (XlsxFileAdapter.SavePostProcessing.cs), including turning a flag OFF by removing the
            // attribute entirely. Reapplying a stale value from the load-time native metadata bag for
            // any of these would silently undo that intentional removal/change (e.g. resurrecting
            // rightToLeft="1" after the user toggled Sheet.IsRightToLeft to false -- see the regression
            // this guards against). tabSelected is excluded for the same reason: it is driven purely
            // by which sheet is currently active (Workbook.ActiveSheetIndex), never by the load-time
            // bag -- see the explicit tabSelected sync below.
            var (pvAttrs, pvChildren) = XmlNativeBagSerializer.Deserialize(metadata?.Get("sheetView"));
            XlsxWorksheetNativeMetadataHelpers.ApplyNativeAttributes(
                sheetView,
                pvAttrs,
                [
                    "workbookViewId", "view", "showGridLines", "showRowColHeaders", "showRuler", "zoomScale",
                    "showFormulas", "topLeftCell", "showZeros", "rightToLeft", "tabSelected",
                    "zoomScaleNormal", "zoomScaleSheetLayoutView", "zoomScalePageLayoutView"
                ]);

            RefreshPerViewModeZoom(sheetView, sheet, pvAttrs, metadata?.Get(XlsxWorksheetLayoutMetadataReader.LoadedViewModeBagKey));

            // Excel marks exactly one sheetView (the active tab) with tabSelected="1" and omits the
            // attribute on every other sheet; sync it here from the live Workbook.ActiveSheetIndex on
            // every save so switching the active sheet and saving actually repoints it, instead of
            // leaving whichever sheet was active at load time permanently marked selected.
            sheetView.SetAttributeValue("tabSelected", isActiveSheet ? "1" : null);

            if (pvChildren.Count > 0)
            {
                var activePaneName = PruneSelectionsForModeledActiveCell(sheetView, sheet);

                sheetView.Elements()
                    .Where(element => !IsModeledPrimaryViewElement(element.Name.LocalName))
                    .Remove();

                foreach (var childXml in pvChildren)
                {
                    TryApplyNativePrimaryViewChild(sheetView, childXml, activePaneName);
                }
            }

            XlsxWorksheetSheetViewNormalizer.NormalizeSheetViewElement(sheetView);
            session.MarkDirty(worksheetEdit);
        }
    }

    private static bool IsModeledPrimaryViewElement(string name) =>
        name is "pane" or "selection";

    // Mirrors the same workbook-view-index -> Sheet resolution XlsxWorkbookMetadataWriter uses for
    // bookViews/workbookView/@activeTab (ClampToVisibleSheetIndex), INCLUDING the hidden-sheet
    // redirect: an out-of-range or absent ActiveSheetIndex falls back to the first sheet, and an
    // in-range index that names a hidden/veryHidden sheet is redirected to the first VISIBLE sheet
    // in document order, matching Excel's own "always exactly one selected tab, and it is visible"
    // invariant. This must stay in lockstep with bookViews/@activeTab -- otherwise a hidden sheet
    // ends up with sheetView/@tabSelected="1" while no visible sheet has it, a self-contradictory
    // state Excel never writes.
    private static Sheet? ResolveActiveSheet(Workbook workbook)
    {
        if (workbook.Sheets.Count == 0)
            return null;

        var index = workbook.ActiveSheetIndex ?? 0;
        if (index < 0)
            index = 0;
        else if (index >= workbook.Sheets.Count)
            index = workbook.Sheets.Count - 1;

        var target = workbook.Sheets[index];
        if (!target.IsHidden && !target.IsVeryHidden)
            return target;

        foreach (var sheet in workbook.Sheets)
        {
            if (!sheet.IsHidden && !sheet.IsVeryHidden)
                return sheet;
        }

        // No visible sheet at all -- shouldn't happen in a valid workbook, but fall back to the
        // originally clamped index rather than leaving no sheet marked selected.
        return target;
    }

    private static readonly string[] PerViewModeZoomAttributeNames =
        ["zoomScaleNormal", "zoomScaleSheetLayoutView", "zoomScalePageLayoutView"];

    // FreeX models a single live Sheet.ZoomPercent for whichever view mode is current (Sheet.ViewMode)
    // and has no per-view-mode zoom memory of its own. Excel additionally remembers the zoom last used
    // in each of the three view modes via zoomScaleNormal/zoomScaleSheetLayoutView/
    // zoomScalePageLayoutView. Those three attributes are excluded from the bulk ApplyNativeAttributes
    // call above so a stale load-time bag value is never blindly reapplied over a live zoom change; here
    // we re-seed all three from the load-time bag (preserving Excel's per-view-mode zoom memory for the
    // two view modes the user is not currently in), then overwrite just the current view mode's
    // attribute with the live Sheet.ZoomPercent so it never goes stale relative to the live zoomScale
    // that XlsxWorksheetViewWriter already wrote.
    private static void RefreshPerViewModeZoom(
        XElement sheetView,
        Sheet sheet,
        IReadOnlyDictionary<string, string>? pvAttrs,
        string? loadedViewModeRaw)
    {
        foreach (var attributeName in PerViewModeZoomAttributeNames)
        {
            if (pvAttrs is not null && pvAttrs.TryGetValue(attributeName, out var staleValue))
                XlsxWorksheetNativeMetadataHelpers.TrySetNativeAttribute(sheetView, attributeName, staleValue);
        }

        var currentViewMode = XlsxWorksheetValueSanitizer.ValidEnumOrDefault(sheet.ViewMode, WorksheetViewMode.Normal);
        var currentZoomAttribute = currentViewMode switch
        {
            WorksheetViewMode.PageBreakPreview => "zoomScaleSheetLayoutView",
            WorksheetViewMode.PageLayout => "zoomScalePageLayoutView",
            _ => "zoomScaleNormal"
        };

        var zoomValue = sheet.ZoomPercent.ToString(CultureInfo.InvariantCulture);

        // If we know the view mode this sheetView was actually loaded with (see
        // XlsxWorksheetLayoutMetadataReader.ReadWorksheetPrimaryViewMetadata's LoadedViewModeBagKey)
        // and the sheet is STILL in that exact same mode, there is no possibility a mode switch ever
        // happened this session -- so the live zoom cannot have been merely inherited from some other
        // mode. In that case the current mode's own attribute must be updated unconditionally to the
        // live zoom, even if it happens to numerically coincide with another mode's remembered value
        // (e.g. the user deliberately sets Normal's zoom to the same 150% Page Layout remembers from
        // an earlier session): a coincidence check would otherwise misclassify that genuine change as
        // "inherited" and silently drop it (see R100_ZoomChangedToCoincidentallyMatchingOtherModeValue_
        // StillPersistsToCurrentModesAttribute).
        if (loadedViewModeRaw is not null &&
            XlsxWorksheetXmlValueParser.ParseWorksheetViewMode(loadedViewModeRaw) == currentViewMode)
        {
            XlsxWorksheetNativeMetadataHelpers.TrySetNativeAttribute(sheetView, currentZoomAttribute, zoomValue);
            return;
        }

        // FreeX models a single live Sheet.ZoomPercent shared by all three view modes (no per-view-
        // mode zoom memory of its own -- see the type comment above), so switching view mode with no
        // zoom action at all simply carries the PREVIOUS mode's zoom value into ZoomPercent
        // unchanged. Blindly overwriting the newly-current mode's own remembered zoomScale<Mode>
        // attribute with that merely-inherited value would silently discard Excel's genuine
        // per-mode zoom memory for a save that never touched zoom (see the regression this guards
        // against). When we don't positively know the mode is unchanged since load (either the mode
        // really did just change, or -- for a sheet with no LoadedViewModeBagKey signal at all -- we
        // simply don't know), the only available signal is whether the live value matches one of the
        // file's OTHER per-mode zoom attributes as loaded: if so, it was almost certainly just
        // inherited from the mode the user switched out of, so the current mode's own stale value
        // (already reseeded above) is left untouched. Only a live value that matches none of the
        // other modes' loaded zoom is treated as a genuine zoom change and persisted into the
        // current mode's attribute.
        var matchesAnotherModesLoadedZoom = pvAttrs is not null &&
            PerViewModeZoomAttributeNames.Any(attributeName =>
                attributeName != currentZoomAttribute &&
                pvAttrs.TryGetValue(attributeName, out var otherLoadedValue) &&
                string.Equals(otherLoadedValue, zoomValue, StringComparison.Ordinal));

        if (!matchesAnotherModesLoadedZoom)
            XlsxWorksheetNativeMetadataHelpers.TrySetNativeAttribute(sheetView, currentZoomAttribute, zoomValue);
    }

    private static XElement? FindPrimarySheetView(XElement sheetViews)
    {
        foreach (var element in sheetViews.Elements(WorksheetNs + "sheetView"))
        {
            if (string.Equals(element.Attribute("workbookViewId")?.Value ?? "0", "0", StringComparison.Ordinal))
                return element;
        }

        return null;
    }

    private static void TryApplyNativePrimaryViewChild(XElement sheetView, string? childXml, string activePaneName)
    {
        if (string.IsNullOrWhiteSpace(childXml))
            return;

        try
        {
            var nativeChild = XElement.Parse(childXml);
            if (nativeChild.Name == WorksheetNs + "selection")
            {
                MergeMatchingSelectionNativeAttributes(sheetView, nativeChild, activePaneName);
                return;
            }

            sheetView.Add(nativeChild);
        }
        catch
        {
            // Skip malformed native payloads in authored native JSON files.
        }
    }

    // A frozen/split sheetView can carry one <selection> per pane (topLeft/topRight/bottomLeft/
    // bottomRight), each keyed by its own @pane attribute (missing @pane means "topLeft"). Only the
    // pane holding the true cursor -- named by pane/@activePane (defaulting to "topLeft" per OOXML
    // when no pane element is present) -- is kept in sync with the model; every other pane's
    // <selection> must be left completely untouched here, mirroring XlsxWorksheetViewWriter's own
    // pane-scoped update, or this writer clobbers the per-pane selections that writer just wrote.
    //
    // The active pane's selection must WIN with the model's current active cell even when a stale
    // native selection (restored from the load-time metadata bag, e.g. by source-package
    // preservation) still names an older cell: rather than removing a non-matching active-pane
    // selection and relying on the native-merge fallback to re-add it (which resurrects the stale
    // cell -- see the regression this guards against), update the active-pane selection's
    // activeCell/sqref in place so it always reflects the model, while any other attributes it
    // carries (e.g. a preserved native marker) survive untouched. Returns the active pane name so
    // the caller can tell the native-merge step which pane must not have its activeCell/sqref
    // clobbered back to a stale value.
    private static string PruneSelectionsForModeledActiveCell(XElement sheetView, Sheet sheet)
    {
        var activePaneName = GetActivePaneName(sheetView);

        if (sheet.ActiveRow is not { } row || sheet.ActiveCol is not { } col)
            return activePaneName;

        var activeCell = new CellAddress(sheet.Id, row, col).ToA1();
        XElement? activePaneSelection = null;
        foreach (var selection in sheetView.Elements(WorksheetNs + "selection").ToList())
        {
            if (!string.Equals(GetSelectionPaneName(selection), activePaneName, StringComparison.Ordinal))
                continue; // leave other panes' selections untouched

            if (activePaneSelection is null)
            {
                activePaneSelection = selection;
                var currentActiveCell = selection.Attribute("activeCell")?.Value;
                if (!string.Equals(currentActiveCell, activeCell, StringComparison.Ordinal))
                {
                    // Stale native selection (names an older cell than the model's current active
                    // cell): the model wins -- overwrite both activeCell and its sqref to the model
                    // cursor so a resurrected native cursor can never linger. The collapsed sqref is
                    // now a single area, so any activeCellId (an index into a multi-area sqref list
                    // per ECMA-376 CT_Selection) that referenced a since-discarded area is no longer
                    // valid and must be cleared -- leaving it would point past the new single-area
                    // sqref and require Excel to repair the file on open.
                    selection.SetAttributeValue("activeCell", activeCell);
                    selection.SetAttributeValue("sqref", activeCell);
                    selection.SetAttributeValue("activeCellId", null);
                }
                // else: the native selection already names the model's active cell -- preserve it
                // verbatim, including a multi-cell sqref range (e.g. A1:F2) that the model does not
                // itself track, so a genuine preserved selection range is not narrowed to one cell.
            }
            else
            {
                // A duplicate selection for the same pane can only be stale/malformed; drop it.
                selection.Remove();
            }
        }

        return activePaneName;
    }

    private static string GetActivePaneName(XElement sheetView)
    {
        var paneElement = sheetView.Element(WorksheetNs + "pane");
        var activePaneName = paneElement?.Attribute("activePane")?.Value;
        return string.IsNullOrWhiteSpace(activePaneName) ? "topLeft" : activePaneName;
    }

    private static string GetSelectionPaneName(XElement selection)
    {
        var paneName = selection.Attribute("pane")?.Value;
        return string.IsNullOrWhiteSpace(paneName) ? "topLeft" : paneName;
    }

    private static void MergeMatchingSelectionNativeAttributes(XElement sheetView, XElement nativeSelection, string activePaneName)
    {
        var nativeActiveCell = nativeSelection.Attribute("activeCell")?.Value;
        var nativeSelectionRef = nativeSelection.Attribute("sqref")?.Value;
        if (string.IsNullOrWhiteSpace(nativeActiveCell) || string.IsNullOrWhiteSpace(nativeSelectionRef))
            return;

        var nativePaneName = GetSelectionPaneName(nativeSelection);
        var isActivePane = string.Equals(nativePaneName, activePaneName, StringComparison.Ordinal);

        // The active pane's <selection> was already normalized to the model's current active cell by
        // PruneSelectionsForModeledActiveCell, so it must always be findable by pane alone here --
        // never fall through to the "no live selection for this pane" branch below, which would
        // resurrect the stale native activeCell/sqref this reconciliation exists to prevent.
        var targetSelection = isActivePane
            ? sheetView.Elements(WorksheetNs + "selection")
                .FirstOrDefault(selection => string.Equals(GetSelectionPaneName(selection), nativePaneName, StringComparison.Ordinal))
            : FindMatchingSelection(sheetView, nativePaneName, nativeActiveCell, nativeSelectionRef);
        if (targetSelection is null)
        {
            // No live selection exists for this pane yet (either it was pruned as the active pane's
            // stale duplicate and replaced by the model's own selection, or this pane was never
            // touched by the live writer) -- only add the native fragment back when this pane truly
            // has no selection at all, so a pane other than the active one never silently loses its
            // preserved cursor position.
            if (!sheetView.Elements(WorksheetNs + "selection")
                    .Any(selection => string.Equals(GetSelectionPaneName(selection), nativePaneName, StringComparison.Ordinal)))
                sheetView.Add(new XElement(nativeSelection));
            return;
        }

        foreach (var attribute in nativeSelection.Attributes())
        {
            if (attribute.IsNamespaceDeclaration || attribute.Name.LocalName is "activeCell")
                continue;

            // The active pane's cursor is owned by the model (already written by Prune). A STALE
            // native sqref/activeCellId -- from a native selection whose activeCell names a
            // different cell than the model's current active cell -- must never overwrite it:
            // Prune already collapsed the pane's sqref to the model's single active cell (and
            // cleared activeCellId), so re-merging a stale native activeCellId here would re-index
            // into a multi-area sqref list that no longer exists (invalid per ECMA-376 CT_Selection).
            // But when the native selection names the SAME active cell as the model, its sqref/
            // activeCellId are the genuine preserved selection state (e.g. a multi-area A1:B2 D4:E5
            // with activeCellId pointing at the active area) that the model does not itself track,
            // so they must merge through together. Other custom attributes (e.g. a preserved marker)
            // always merge through.
            if (isActivePane && attribute.Name.LocalName is "sqref" or "activeCellId" &&
                !string.Equals(nativeActiveCell, targetSelection.Attribute("activeCell")?.Value, StringComparison.Ordinal))
                continue;

            targetSelection.SetAttributeValue(attribute.Name, attribute.Value);
        }
    }

    private static XElement? FindMatchingSelection(XElement sheetView, string nativePaneName, string nativeActiveCell, string nativeSelectionRef)
    {
        var paneSelections = sheetView.Elements(WorksheetNs + "selection")
            .Where(selection => string.Equals(GetSelectionPaneName(selection), nativePaneName, StringComparison.Ordinal))
            .ToList();

        foreach (var selection in paneSelections)
        {
            if (string.Equals(selection.Attribute("activeCell")?.Value, nativeActiveCell, StringComparison.Ordinal) &&
                string.Equals(selection.Attribute("sqref")?.Value, nativeSelectionRef, StringComparison.Ordinal))
                return selection;
        }

        foreach (var selection in paneSelections)
        {
            if (string.Equals(selection.Attribute("activeCell")?.Value, nativeActiveCell, StringComparison.Ordinal))
                return selection;
        }

        return paneSelections.Count > 0 ? paneSelections[0] : null;
    }
}
