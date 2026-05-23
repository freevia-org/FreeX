using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using Freexcel.Core.Model;

namespace Freexcel.Core.IO;

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
            var rowSplit = ParsePaneSplit(pane?.Attribute("ySplit")?.Value);
            var columnSplit = ParsePaneSplit(pane?.Attribute("xSplit")?.Value);
            var frozenRows = paneState is "frozen" or "frozenSplit" ? rowSplit ?? 0 : 0;
            var frozenCols = paneState is "frozen" or "frozenSplit" ? columnSplit ?? 0 : 0;
            var splitRow = frozenRows == 0 && frozenCols == 0 ? rowSplit : null;
            var splitColumn = frozenRows == 0 && frozenCols == 0 ? columnSplit : null;

            customViews.Add(new XlsxWorksheetCustomViewState(
                id,
                new WorksheetCustomViewState(
                    string.Empty,
                    ParseWorksheetViewMode(customSheetView.Attribute("view")?.Value),
                    frozenRows,
                    frozenCols,
                    splitRow,
                    splitColumn,
                    ShowGridlines: !IsFalse(customSheetView.Attribute("showGridLines")?.Value),
                    ShowHeadings: !IsFalse(customSheetView.Attribute("showRowCol")?.Value),
                    ShowRulers: !IsFalse(customSheetView.Attribute("showRuler")?.Value),
                    ZoomPercent: XlsxWorksheetValueSanitizer.ValidZoomPercentOrDefault(XlsxXmlAttributeReader.ReadIntAttribute(customSheetView, "scale") ?? 100),
                    ShowFormulas: IsTruthy(customSheetView.Attribute("showFormulas")?.Value))));
        }

        return customViews;
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
                item.View.IncludePrintSettings ? new XAttribute("includePrintSettings", "1") : new XAttribute("includePrintSettings", "0"),
                item.View.IncludeHiddenRowsColumnsAndFilterSettings ? new XAttribute("includeHiddenRowCol", "1") : new XAttribute("includeHiddenRowCol", "0"),
                new XAttribute("autoUpdate", "0"),
                new XAttribute("mergeInterval", "0"),
                new XAttribute("personalView", "0")))));
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
            return $"{{{guid:D}}}";

        return trimmed;
    }

    private static string CreateDeterministicId(string name, int index)
    {
        var bytes = MD5.HashData(Encoding.UTF8.GetBytes($"Freexcel.CustomView:{index}:{name}"));
        return $"{{{new Guid(bytes):D}}}";
    }

    private static XElement ToCustomSheetViewXml(XNamespace workbookNs, string id, WorksheetCustomViewState state)
    {
        var frozenRows = ValidFrozenRowsOrZero(state.FrozenRows);
        var frozenCols = ValidFrozenColumnsOrZero(state.FrozenCols);
        var hasFrozenPanes = frozenRows > 0 || frozenCols > 0;
        var splitRow = hasFrozenPanes ? null : state.SplitRow;
        var splitColumn = hasFrozenPanes ? null : state.SplitColumn;

        var customSheetView = new XElement(
            workbookNs + "customSheetView",
            new XAttribute("guid", id),
            XlsxWorksheetViewWriter.ToXlsxWorksheetViewMode(XlsxWorksheetValueSanitizer.ValidEnumOrDefault(state.ViewMode, WorksheetViewMode.Normal)) is { } view
                ? new XAttribute("view", view)
                : null,
            state.ShowGridlines ? null : new XAttribute("showGridLines", "0"),
            state.ShowHeadings ? null : new XAttribute("showRowCol", "0"),
            state.ShowRulers ? null : new XAttribute("showRuler", "0"),
            state.ZoomPercent == 100 ? null : new XAttribute("scale", XlsxWorksheetValueSanitizer.ValidZoomPercentOrDefault(state.ZoomPercent)),
            state.ShowFormulas ? new XAttribute("showFormulas", "1") : null,
            new XAttribute("state", "visible"));

        if (hasFrozenPanes || splitRow.HasValue || splitColumn.HasValue)
        {
            customSheetView.Add(new XElement(
                workbookNs + "pane",
                splitColumn is { } splitColumnValue ? new XAttribute("xSplit", splitColumnValue) : null,
                splitRow is { } splitRowValue ? new XAttribute("ySplit", splitRowValue) : null,
                frozenCols > 0 ? new XAttribute("xSplit", frozenCols) : null,
                frozenRows > 0 ? new XAttribute("ySplit", frozenRows) : null,
                new XAttribute("state", hasFrozenPanes ? "frozen" : "split")));
        }

        return customSheetView;
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

        var insertionPoint = workbookRoot.Elements()
            .FirstOrDefault(element =>
                element.Name.Namespace == workbookNs &&
                laterWorkbookElements.Contains(element.Name.LocalName, StringComparer.Ordinal));
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

        var insertionPoint = worksheetRoot.Elements()
            .FirstOrDefault(element =>
                element.Name.Namespace == workbookNs &&
                laterWorksheetElements.Contains(element.Name.LocalName, StringComparer.Ordinal));
        if (insertionPoint is null)
            worksheetRoot.Add(customSheetViews);
        else
            insertionPoint.AddBeforeSelf(customSheetViews);
    }

    private static uint? ParsePaneSplit(string? value) =>
        uint.TryParse(value, out var parsed) ? parsed : null;

    private static WorksheetViewMode ParseWorksheetViewMode(string? value) =>
        value switch
        {
            "pageLayout" => WorksheetViewMode.PageLayout,
            "pageBreakPreview" => WorksheetViewMode.PageBreakPreview,
            _ => WorksheetViewMode.Normal
        };

    private static bool IsTruthy(string? value) =>
        value is "1" ||
        value is not null && bool.TryParse(value, out var parsed) && parsed;

    private static bool IsFalse(string? value) =>
        value is "0" ||
        value is not null && bool.TryParse(value, out var parsed) && !parsed;

    private static uint ValidFrozenRowsOrZero(uint row) =>
        row <= CellAddress.MaxRow ? row : 0;

    private static uint ValidFrozenColumnsOrZero(uint column) =>
        column <= CellAddress.MaxCol ? column : 0;
}

internal sealed record XlsxWorksheetCustomViewState(string Id, WorksheetCustomViewState State);
