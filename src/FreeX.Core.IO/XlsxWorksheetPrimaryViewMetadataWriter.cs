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
        foreach (var sheet in workbook.Sheets)
        {
            var metadata = sheet.PrimaryViewMetadata;
            if (metadata is null)
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

            var (pvAttrs, pvChildren) = XmlNativeBagSerializer.Deserialize(metadata.Get("sheetView"));
            XlsxWorksheetNativeMetadataHelpers.ApplyNativeAttributes(
                sheetView,
                pvAttrs,
                [
                    "workbookViewId", "view", "showGridLines", "showRowColHeaders", "showRuler", "zoomScale",
                    "showFormulas", "topLeftCell",
                    "zoomScaleNormal", "zoomScaleSheetLayoutView", "zoomScalePageLayoutView"
                ]);

            RefreshPerViewModeZoom(sheetView, sheet, pvAttrs);

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
        IReadOnlyDictionary<string, string>? pvAttrs)
    {
        foreach (var attributeName in PerViewModeZoomAttributeNames)
        {
            if (pvAttrs is not null && pvAttrs.TryGetValue(attributeName, out var staleValue))
                XlsxWorksheetNativeMetadataHelpers.TrySetNativeAttribute(sheetView, attributeName, staleValue);
        }

        var currentZoomAttribute = XlsxWorksheetValueSanitizer.ValidEnumOrDefault(sheet.ViewMode, WorksheetViewMode.Normal) switch
        {
            WorksheetViewMode.PageBreakPreview => "zoomScaleSheetLayoutView",
            WorksheetViewMode.PageLayout => "zoomScalePageLayoutView",
            _ => "zoomScaleNormal"
        };

        var zoomValue = sheet.ZoomPercent.ToString(CultureInfo.InvariantCulture);
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
                    // cursor so a resurrected native cursor can never linger.
                    selection.SetAttributeValue("activeCell", activeCell);
                    selection.SetAttributeValue("sqref", activeCell);
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
            // native sqref -- one whose native activeCell names a different cell than the model's
            // current active cell -- must never overwrite it. But when the native selection names
            // the SAME active cell as the model, its sqref is the genuine preserved selection RANGE
            // (e.g. A1:F2) that the model does not itself track, so it must merge through. Other
            // custom attributes (e.g. a preserved marker) always merge through.
            if (isActivePane && attribute.Name.LocalName is "sqref" &&
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
