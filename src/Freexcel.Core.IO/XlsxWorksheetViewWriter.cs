using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;
using Freexcel.Core.Model;

namespace Freexcel.Core.IO;

internal static class XlsxWorksheetViewWriter
{
    public static bool HasPersistableViewState(Sheet sheet) =>
        !sheet.ShowGridlines ||
        !sheet.ShowHeadings ||
        !sheet.ShowRulers ||
        XlsxWorksheetValueSanitizer.ValidEnumOrDefault(sheet.ViewMode, WorksheetViewMode.Normal) != WorksheetViewMode.Normal ||
        sheet.ZoomPercent != 100 ||
        sheet.ShowFormulas ||
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
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var root = worksheetXml.Root;
        if (root is null)
            return;

        var changed = false;
        var sheetViews = root.Element(worksheetNs + "sheetViews");
        if (sheetViews is null)
        {
            sheetViews = new XElement(worksheetNs + "sheetViews");
            root.AddFirst(sheetViews);
            changed = true;
        }

        var sheetView = sheetViews.Elements(worksheetNs + "sheetView").FirstOrDefault();
        if (sheetView is null)
        {
            sheetView = new XElement(worksheetNs + "sheetView", new XAttribute("workbookViewId", "0"));
            sheetViews.Add(sheetView);
            changed = true;
        }

        changed |= SetAttributeIfDifferent(sheetView, "view", ToXlsxWorksheetViewMode(
            XlsxWorksheetValueSanitizer.ValidEnumOrDefault(sheet.ViewMode, WorksheetViewMode.Normal)));
        changed |= SetAttributeIfDifferent(sheetView, "showGridLines", sheet.ShowGridlines ? null : "0");
        changed |= SetAttributeIfDifferent(sheetView, "showRowColHeaders", sheet.ShowHeadings ? null : "0");
        changed |= SetAttributeIfDifferent(sheetView, "showRuler", sheet.ShowRulers ? null : "0");
        changed |= SetAttributeIfDifferent(sheetView, "zoomScale", sheet.ZoomPercent == 100 ? null : sheet.ZoomPercent.ToString(CultureInfo.InvariantCulture));
        changed |= SetAttributeIfDifferent(sheetView, "showFormulas", sheet.ShowFormulas ? "1" : null);
        changed |= SetAttributeIfDifferent(sheetView, "topLeftCell", ToOptionalA1(sheet.ViewTopRow, sheet.ViewLeftCol));
        if (ToOptionalA1(sheet.ActiveRow, sheet.ActiveCol) is { } activeCell)
        {
            var selections = sheetView.Elements(worksheetNs + "selection").ToList();
            if (selections.Count != 1 ||
                !string.Equals(selections[0].Attribute("activeCell")?.Value, activeCell, StringComparison.Ordinal) ||
                !string.Equals(selections[0].Attribute("sqref")?.Value, activeCell, StringComparison.Ordinal))
            {
                selections.Remove();
                sheetView.Add(new XElement(
                    worksheetNs + "selection",
                    new XAttribute("activeCell", activeCell),
                    new XAttribute("sqref", activeCell)));
                changed = true;
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

        if (changed)
            XlsxPackageXmlEditor.ReplaceXml(archive, worksheetPath, worksheetXml);
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
        using var stream = entry.Open();
        return XDocument.Load(stream);
    }

    private static bool SetAttributeIfDifferent(XElement element, XName name, string? value)
    {
        if (value is null)
        {
            if (element.Attribute(name) is null)
                return false;

            element.SetAttributeValue(name, null);
            return true;
        }

        if (string.Equals(element.Attribute(name)?.Value, value, StringComparison.Ordinal))
            return false;

        element.SetAttributeValue(name, value);
        return true;
    }
}
