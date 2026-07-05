using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

internal static class XlsxWorksheetViewWriter
{
    public static bool HasPersistableViewState(Sheet sheet) =>
        !sheet.ShowGridlines ||
        !sheet.ShowHeadings ||
        !sheet.ShowRulers ||
        XlsxWorksheetValueSanitizer.ValidEnumOrDefault(sheet.ViewMode, WorksheetViewMode.Normal) != WorksheetViewMode.Normal ||
        sheet.ZoomPercent != 100 ||
        sheet.ShowFormulas ||
        !sheet.ShowZeros ||
        sheet.IsRightToLeft ||
        sheet.ViewTopRow.HasValue ||
        sheet.ViewLeftCol.HasValue ||
        sheet.ActiveRow.HasValue ||
        sheet.ActiveCol.HasValue ||
        (sheet.FrozenRows == 0 && sheet.FrozenCols == 0 &&
         (sheet.SplitRow.HasValue || sheet.SplitColumn.HasValue));

    public static void Save(Stream xlsxStream, Workbook workbook)
    {
        using var archive = new ZipArchive(xlsxStream, ZipArchiveMode.Update, leaveOpen: true);
        Save(archive, workbook, XlsxWorkbookWorksheetPathMap.TryCreate(archive));
    }

    public static void Save(Stream xlsxStream, Workbook workbook, XlsxWorkbookWorksheetPathMap? worksheetPathMap)
    {
        using var archive = new ZipArchive(xlsxStream, ZipArchiveMode.Update, leaveOpen: true);
        Save(archive, workbook, worksheetPathMap);
    }

    private static void Save(ZipArchive archive, Workbook workbook, XlsxWorkbookWorksheetPathMap? worksheetPathMap)
    {
        var workbookEntry = archive.GetEntry("xl/workbook.xml");
        var relsEntry = archive.GetEntry("xl/_rels/workbook.xml.rels");
        if (workbookEntry is null || relsEntry is null || worksheetPathMap is null)
            return;

        var viewSheets = workbook.Sheets
            .Where(HasPersistableViewState)
            .ToDictionary(sheet => sheet.Name, StringComparer.OrdinalIgnoreCase);

        foreach (var (name, worksheetPath) in worksheetPathMap.SheetPathsByName)
        {
            if (!viewSheets.TryGetValue(name, out var sheet))
                continue;

            UpdateSheetView(archive, worksheetPath, sheet);
        }
    }

    public static string? ToXlsxWorksheetViewMode(WorksheetViewMode viewMode) =>
        viewMode switch
        {
            WorksheetViewMode.PageBreakPreview => "pageBreakPreview",
            WorksheetViewMode.PageLayout => "pageLayout",
            _ => null
        };

    private static void UpdateSheetView(ZipArchive archive, string worksheetPath, Sheet sheet)
    {
        var worksheetEntry = archive.GetEntry(worksheetPath);
        if (worksheetEntry is null)
            return;

        var worksheetXml = LoadXml(worksheetEntry);
        if (UpdateSheetView(worksheetXml, sheet))
            XlsxPackageXmlEditor.ReplaceXml(archive, worksheetPath, worksheetXml);
    }

    internal static bool UpdateSheetView(XDocument worksheetXml, Sheet sheet)
    {
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var root = worksheetXml.Root;
        if (root is null)
            return false;

        var changed = false;
        var sheetViews = root.Element(worksheetNs + "sheetViews");
        if (sheetViews is null)
        {
            sheetViews = new XElement(worksheetNs + "sheetViews");
            root.AddFirst(sheetViews);
            changed = true;
        }

        XElement? sheetView = null;
        foreach (var candidateView in sheetViews.Elements(worksheetNs + "sheetView"))
        {
            if (IsPrimarySheetView(candidateView))
            {
                sheetView = candidateView;
                break;
            }
        }

        if (sheetView is null)
        {
            sheetView = new XElement(worksheetNs + "sheetView", new XAttribute("workbookViewId", "0"));
            sheetViews.AddFirst(sheetView);
            changed = true;
        }

        changed |= XlsxXmlNormalizationHelpers.SetOrRemoveAttributeIfChanged(sheetView, "view", ToXlsxWorksheetViewMode(
            XlsxWorksheetValueSanitizer.ValidEnumOrDefault(sheet.ViewMode, WorksheetViewMode.Normal)));
        changed |= XlsxXmlNormalizationHelpers.SetOrRemoveAttributeIfChanged(sheetView, "showGridLines", sheet.ShowGridlines ? null : "0");
        changed |= XlsxXmlNormalizationHelpers.SetOrRemoveAttributeIfChanged(sheetView, "showRowColHeaders", sheet.ShowHeadings ? null : "0");
        changed |= XlsxXmlNormalizationHelpers.SetOrRemoveAttributeIfChanged(sheetView, "showRuler", sheet.ShowRulers ? null : "0");
        changed |= XlsxXmlNormalizationHelpers.SetOrRemoveAttributeIfChanged(sheetView, "zoomScale", sheet.ZoomPercent == 100 ? null : sheet.ZoomPercent.ToString(CultureInfo.InvariantCulture));
        changed |= XlsxXmlNormalizationHelpers.SetOrRemoveAttributeIfChanged(sheetView, "showFormulas", sheet.ShowFormulas ? "1" : null);
        changed |= XlsxXmlNormalizationHelpers.SetOrRemoveAttributeIfChanged(sheetView, "showZeros", sheet.ShowZeros ? null : "0");
        changed |= XlsxXmlNormalizationHelpers.SetOrRemoveAttributeIfChanged(sheetView, "rightToLeft", sheet.IsRightToLeft ? "1" : null);
        changed |= XlsxXmlNormalizationHelpers.SetOrRemoveAttributeIfChanged(sheetView, "topLeftCell", ToOptionalA1(sheet.ViewTopRow, sheet.ViewLeftCol));
        if (ToOptionalA1(sheet.ActiveRow, sheet.ActiveCol) is { } activeCell)
        {
            // A frozen/split sheetView can carry one <selection> per pane (topLeft/topRight/
            // bottomLeft/bottomRight), each keyed by its own @pane attribute (missing @pane means
            // "topLeft"). Only the pane holding the true cursor -- named by pane/@activePane
            // (defaulting to "topLeft" per OOXML when no pane element is present) -- should be
            // updated from the model; the other panes' <selection> elements must be carried
            // through untouched, or a full-rebuild save silently destroys their cursor positions.
            var paneElement = sheetView.Element(worksheetNs + "pane");
            var activePaneName = paneElement?.Attribute("activePane")?.Value;
            if (string.IsNullOrWhiteSpace(activePaneName))
                activePaneName = "topLeft";

            var selections = sheetView.Elements(worksheetNs + "selection").ToList();
            XElement? activeSelection = null;
            foreach (var selection in selections)
            {
                var selectionPaneName = selection.Attribute("pane")?.Value;
                if (string.IsNullOrWhiteSpace(selectionPaneName))
                    selectionPaneName = "topLeft";

                if (string.Equals(selectionPaneName, activePaneName, StringComparison.Ordinal))
                {
                    activeSelection = selection;
                    break;
                }
            }

            if (activeSelection is null)
            {
                activeSelection = string.Equals(activePaneName, "topLeft", StringComparison.Ordinal)
                    ? new XElement(worksheetNs + "selection")
                    : new XElement(worksheetNs + "selection", new XAttribute("pane", activePaneName));
                sheetView.Add(activeSelection);
                changed = true;
            }

            if (!string.Equals(activeSelection.Attribute("activeCell")?.Value, activeCell, StringComparison.Ordinal) ||
                !string.Equals(activeSelection.Attribute("sqref")?.Value, activeCell, StringComparison.Ordinal))
            {
                changed |= XlsxXmlNormalizationHelpers.SetOrRemoveAttributeIfChanged(activeSelection, "activeCell", activeCell);
                changed |= XlsxXmlNormalizationHelpers.SetOrRemoveAttributeIfChanged(activeSelection, "sqref", activeCell);
            }
        }

        if (sheet.FrozenRows == 0 && sheet.FrozenCols == 0 &&
            (sheet.SplitRow.HasValue || sheet.SplitColumn.HasValue))
        {
            var pane = new XElement(
                worksheetNs + "pane",
                sheet.SplitColumn is { } splitColumn ? new XAttribute("xSplit", splitColumn) : null,
                sheet.SplitRow is { } splitRow ? new XAttribute("ySplit", splitRow) : null,
                new XAttribute("state", "split"));
            var existingPanes = sheetView.Elements(worksheetNs + "pane").ToList();
            if (existingPanes.Count != 1 ||
                !XNode.DeepEquals(existingPanes[0], pane))
            {
                existingPanes.Remove();
                sheetView.AddFirst(pane);
                changed = true;
            }
        }

        return changed;
    }

    private static string? ToOptionalA1(uint? row, uint? col)
    {
        return row is > 0 and <= CellAddress.MaxRow &&
               col is > 0 and <= CellAddress.MaxCol
            ? $"{CellAddress.NumberToColumnName(col.Value)}{row.Value}"
            : null;
    }

    private static XDocument LoadXml(ZipArchiveEntry entry)
    {
        return XlsxPackageXmlEditor.LoadXml(entry);
    }

    private static bool IsPrimarySheetView(XElement element) =>
        string.Equals(element.Attribute("workbookViewId")?.Value ?? "0", "0", StringComparison.Ordinal);
}
