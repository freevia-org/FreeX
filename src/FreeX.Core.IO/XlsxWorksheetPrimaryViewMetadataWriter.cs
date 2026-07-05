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
                PruneSelectionsForModeledActiveCell(sheetView, sheet);

                sheetView.Elements()
                    .Where(element => !IsModeledPrimaryViewElement(element.Name.LocalName))
                    .Remove();

                foreach (var childXml in pvChildren)
                {
                    TryApplyNativePrimaryViewChild(sheetView, childXml);
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

    private static void TryApplyNativePrimaryViewChild(XElement sheetView, string? childXml)
    {
        if (string.IsNullOrWhiteSpace(childXml))
            return;

        try
        {
            var nativeChild = XElement.Parse(childXml);
            if (nativeChild.Name == WorksheetNs + "selection")
            {
                MergeMatchingSelectionNativeAttributes(sheetView, nativeChild);
                return;
            }

            sheetView.Add(nativeChild);
        }
        catch
        {
            // Skip malformed native payloads in authored native JSON files.
        }
    }

    private static void PruneSelectionsForModeledActiveCell(XElement sheetView, Sheet sheet)
    {
        if (sheet.ActiveRow is not { } row || sheet.ActiveCol is not { } col)
            return;

        var activeCell = new CellAddress(sheet.Id, row, col).ToA1();
        var matchingSelectionKept = false;
        foreach (var selection in sheetView.Elements(WorksheetNs + "selection").ToList())
        {
            var isModeledSelection =
                string.Equals(selection.Attribute("activeCell")?.Value, activeCell, StringComparison.Ordinal) &&
                string.Equals(selection.Attribute("sqref")?.Value, activeCell, StringComparison.Ordinal);
            if (!isModeledSelection || matchingSelectionKept)
                selection.Remove();
            else
                matchingSelectionKept = true;
        }
    }

    private static void MergeMatchingSelectionNativeAttributes(XElement sheetView, XElement nativeSelection)
    {
        var nativeActiveCell = nativeSelection.Attribute("activeCell")?.Value;
        var nativeSelectionRef = nativeSelection.Attribute("sqref")?.Value;
        if (string.IsNullOrWhiteSpace(nativeActiveCell) || string.IsNullOrWhiteSpace(nativeSelectionRef))
            return;

        var targetSelection = FindMatchingSelection(sheetView, nativeActiveCell, nativeSelectionRef);
        if (targetSelection is null)
        {
            if (!sheetView.Elements(WorksheetNs + "selection").Any())
                sheetView.Add(new XElement(nativeSelection));
            return;
        }

        foreach (var attribute in nativeSelection.Attributes())
        {
            if (attribute.IsNamespaceDeclaration || attribute.Name.LocalName is "activeCell")
                continue;

            targetSelection.SetAttributeValue(attribute.Name, attribute.Value);
        }
    }

    private static XElement? FindMatchingSelection(XElement sheetView, string nativeActiveCell, string nativeSelectionRef)
    {
        foreach (var selection in sheetView.Elements(WorksheetNs + "selection"))
        {
            if (string.Equals(selection.Attribute("activeCell")?.Value, nativeActiveCell, StringComparison.Ordinal) &&
                string.Equals(selection.Attribute("sqref")?.Value, nativeSelectionRef, StringComparison.Ordinal))
                return selection;
        }

        foreach (var selection in sheetView.Elements(WorksheetNs + "selection"))
        {
            if (string.Equals(selection.Attribute("activeCell")?.Value, nativeActiveCell, StringComparison.Ordinal))
                return selection;
        }

        return null;
    }
}
