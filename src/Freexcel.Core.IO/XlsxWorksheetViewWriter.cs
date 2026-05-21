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
        var workbookEntry = archive.GetEntry("xl/workbook.xml");
        var relsEntry = archive.GetEntry("xl/_rels/workbook.xml.rels");
        if (workbookEntry is null || relsEntry is null)
            return;

        var workbookXml = LoadXml(workbookEntry);
        var relsXml = LoadXml(relsEntry);

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

        var viewSheets = workbook.Sheets
            .Where(HasPersistableViewState)
            .ToDictionary(sheet => sheet.Name, StringComparer.OrdinalIgnoreCase);

        foreach (var sheetElement in workbookXml.Root?.Element(workbookNs + "sheets")?.Elements(workbookNs + "sheet") ?? [])
        {
            var name = sheetElement.Attribute("name")?.Value;
            var relId = sheetElement.Attribute(relNs + "id")?.Value;
            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(relId))
                continue;
            if (!viewSheets.TryGetValue(name, out var sheet))
                continue;
            if (!relTargets.TryGetValue(relId, out var worksheetPath))
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

        var sheetViews = root.Element(worksheetNs + "sheetViews");
        if (sheetViews is null)
        {
            sheetViews = new XElement(worksheetNs + "sheetViews");
            root.AddFirst(sheetViews);
        }

        var sheetView = sheetViews.Elements(worksheetNs + "sheetView").FirstOrDefault();
        if (sheetView is null)
        {
            sheetView = new XElement(worksheetNs + "sheetView", new XAttribute("workbookViewId", "0"));
            sheetViews.Add(sheetView);
        }

        sheetView.SetAttributeValue("view", ToXlsxWorksheetViewMode(
            XlsxWorksheetValueSanitizer.ValidEnumOrDefault(sheet.ViewMode, WorksheetViewMode.Normal)));
        sheetView.SetAttributeValue("showGridLines", sheet.ShowGridlines ? null : "0");
        sheetView.SetAttributeValue("showRowColHeaders", sheet.ShowHeadings ? null : "0");
        sheetView.SetAttributeValue("showRuler", sheet.ShowRulers ? null : "0");
        sheetView.SetAttributeValue("zoomScale", sheet.ZoomPercent == 100 ? null : sheet.ZoomPercent);
        sheetView.SetAttributeValue("showFormulas", sheet.ShowFormulas ? "1" : null);
        sheetView.SetAttributeValue("topLeftCell", ToOptionalA1(sheet.ViewTopRow, sheet.ViewLeftCol));
        if (ToOptionalA1(sheet.ActiveRow, sheet.ActiveCol) is { } activeCell)
        {
            sheetView.Elements(worksheetNs + "selection").Remove();
            sheetView.Add(new XElement(
                worksheetNs + "selection",
                new XAttribute("activeCell", activeCell),
                new XAttribute("sqref", activeCell)));
        }

        if (sheet.FrozenRows == 0 && sheet.FrozenCols == 0 &&
            (sheet.SplitRow.HasValue || sheet.SplitColumn.HasValue))
        {
            sheetView.Elements(worksheetNs + "pane").Remove();
            sheetView.AddFirst(new XElement(
                worksheetNs + "pane",
                sheet.SplitColumn is { } splitColumn ? new XAttribute("xSplit", splitColumn) : null,
                sheet.SplitRow is { } splitRow ? new XAttribute("ySplit", splitRow) : null,
                new XAttribute("state", "split")));
        }

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
}
