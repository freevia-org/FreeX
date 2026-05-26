using System.IO.Compression;
using System.Xml.Linq;
using Freexcel.Core.Model;

namespace Freexcel.Core.IO;

internal static class XlsxWorksheetSortStateMapper
{
    private static readonly XNamespace WorksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    public static WorksheetSortStateModel? Read(XElement? sortState)
    {
        if (sortState is null)
            return null;

        var model = new WorksheetSortStateModel
        {
            Reference = sortState.Attribute("ref")?.Value,
            ColumnSort = XlsxXmlAttributeReader.ReadNullableBoolAttribute(sortState, "columnSort"),
            CaseSensitive = XlsxXmlAttributeReader.ReadNullableBoolAttribute(sortState, "caseSensitive"),
            SortMethod = sortState.Attribute("sortMethod")?.Value,
            NativeXml = sortState.ToString(SaveOptions.DisableFormatting),
            Conditions = sortState.Elements(WorksheetNs + "sortCondition")
                .Select(ReadCondition)
                .ToList()
        };

        XlsxWorksheetNativeMetadataHelpers.ReadNativeAttributes(sortState, model.NativeAttributes, ["ref", "columnSort", "caseSensitive", "sortMethod"]);
        return model;
    }

    public static void Save(Stream xlsxStream, Workbook workbook, XlsxWorkbookWorksheetPathMap? worksheetPathMap)
    {
        if (worksheetPathMap is null)
            return;

        using var archive = new ZipArchive(xlsxStream, ZipArchiveMode.Update, leaveOpen: true);
        foreach (var sheet in workbook.Sheets.Where(sheet => sheet.SortState is not null))
        {
            if (!worksheetPathMap.SheetPathsByName.TryGetValue(sheet.Name, out var worksheetPath))
                continue;

            var entry = archive.GetEntry(worksheetPath);
            if (entry is null)
                continue;

            var worksheetXml = XlsxPackageXmlEditor.LoadXml(entry);
            var root = worksheetXml.Root;
            if (root is null)
                continue;

            root.Element(WorksheetNs + "sortState")?.Remove();
            if (ToXml(sheet.SortState!) is { } sortState)
                InsertSortState(root, sortState);

            XlsxPackageXmlEditor.ReplaceXml(archive, worksheetPath, worksheetXml);
        }
    }

    private static WorksheetSortConditionModel ReadCondition(XElement element)
    {
        var model = new WorksheetSortConditionModel
        {
            Reference = element.Attribute("ref")?.Value,
            Descending = XlsxXmlAttributeReader.ReadNullableBoolAttribute(element, "descending"),
            SortBy = element.Attribute("sortBy")?.Value,
            CustomList = element.Attribute("customList")?.Value,
            DxfId = element.Attribute("dxfId")?.Value,
            IconSet = element.Attribute("iconSet")?.Value,
            IconId = element.Attribute("iconId")?.Value
        };
        XlsxWorksheetNativeMetadataHelpers.ReadNativeAttributes(
            element,
            model.NativeAttributes,
            ["ref", "descending", "sortBy", "customList", "dxfId", "iconSet", "iconId"]);
        return model;
    }

    private static XElement? ToXml(WorksheetSortStateModel model)
    {
        if (!string.IsNullOrWhiteSpace(model.NativeXml))
        {
            try
            {
                var nativeElement = XElement.Parse(model.NativeXml);
                if (nativeElement.Name == WorksheetNs + "sortState")
                    return nativeElement;
            }
            catch
            {
                // Fall back to the structured model below.
            }
        }

        var element = new XElement(WorksheetNs + "sortState");
        XlsxWorksheetNativeMetadataHelpers.ApplyNativeAttributes(element, model.NativeAttributes, ["ref", "columnSort", "caseSensitive", "sortMethod"]);
        element.SetAttributeValue("ref", XlsxWorksheetNativeMetadataHelpers.NullIfWhiteSpace(model.Reference));
        element.SetAttributeValue("columnSort", XlsxWorksheetNativeMetadataHelpers.ToBoolAttribute(model.ColumnSort));
        element.SetAttributeValue("caseSensitive", XlsxWorksheetNativeMetadataHelpers.ToBoolAttribute(model.CaseSensitive));
        element.SetAttributeValue("sortMethod", XlsxWorksheetNativeMetadataHelpers.NullIfWhiteSpace(model.SortMethod));
        foreach (var condition in model.Conditions.Select(ToXml))
            element.Add(condition);

        return element.HasAttributes || element.HasElements ? element : null;
    }

    private static XElement ToXml(WorksheetSortConditionModel model)
    {
        var element = new XElement(WorksheetNs + "sortCondition");
        XlsxWorksheetNativeMetadataHelpers.ApplyNativeAttributes(
            element,
            model.NativeAttributes,
            ["ref", "descending", "sortBy", "customList", "dxfId", "iconSet", "iconId"]);
        element.SetAttributeValue("ref", XlsxWorksheetNativeMetadataHelpers.NullIfWhiteSpace(model.Reference));
        element.SetAttributeValue("descending", XlsxWorksheetNativeMetadataHelpers.ToBoolAttribute(model.Descending));
        element.SetAttributeValue("sortBy", XlsxWorksheetNativeMetadataHelpers.NullIfWhiteSpace(model.SortBy));
        element.SetAttributeValue("customList", XlsxWorksheetNativeMetadataHelpers.NullIfWhiteSpace(model.CustomList));
        element.SetAttributeValue("dxfId", XlsxWorksheetNativeMetadataHelpers.NullIfWhiteSpace(model.DxfId));
        element.SetAttributeValue("iconSet", XlsxWorksheetNativeMetadataHelpers.NullIfWhiteSpace(model.IconSet));
        element.SetAttributeValue("iconId", XlsxWorksheetNativeMetadataHelpers.NullIfWhiteSpace(model.IconId));
        return element;
    }

    private static void InsertSortState(XElement root, XElement sortState)
    {
        var insertionPoint = root.Elements()
            .FirstOrDefault(element =>
                element.Name.Namespace == WorksheetNs &&
                new[]
                {
                    "dataConsolidate",
                    "customSheetViews",
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
                }.Contains(element.Name.LocalName, StringComparer.Ordinal));
        if (insertionPoint is null)
            root.Add(sortState);
        else
            insertionPoint.AddBeforeSelf(sortState);
    }

}
