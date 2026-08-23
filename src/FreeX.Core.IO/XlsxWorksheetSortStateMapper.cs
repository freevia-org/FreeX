using System.Xml.Linq;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

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

        using var session = new XlsxWorksheetXmlEditSession(xlsxStream, worksheetPathMap);
        Save(session, workbook);
    }

    internal static void Save(XlsxWorksheetXmlEditSession session, Workbook workbook)
    {
        foreach (var sheet in workbook.Sheets)
        {
            var sortStateModel = sheet.SortState;
            if (sortStateModel is null)
                continue;

            if (!session.TryGetWorksheet(sheet, out var edit))
                continue;

            var root = edit.Root;
            var existingSortState = root.Element(WorksheetNs + "sortState");
            var changed = existingSortState is not null;
            existingSortState?.Remove();

            if (ToXml(sortStateModel) is { } sortState)
            {
                XlsxWorksheetElementOrder.Insert(root, sortState);
                changed = true;
            }

            if (changed)
                session.MarkDirty(edit);
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
        if (XlsxWorksheetNativeMetadataHelpers.TryParseNativeElement(
                model.NativeXml,
                WorksheetNs + "sortState",
                XlsxWorksheetSortStateNormalizer.NormalizeElement) is { } nativeElement)
        {
            if (!nativeElement.HasAttributes && !nativeElement.HasElements)
                return null;

            return nativeElement;
        }

        var element = new XElement(WorksheetNs + "sortState");
        XlsxWorksheetNativeMetadataHelpers.ApplyNativeAttributes(element, model.NativeAttributes, ["ref", "columnSort", "caseSensitive", "sortMethod"]);
        element.SetAttributeValue("ref", XlsxWorksheetNativeMetadataHelpers.NullIfWhiteSpace(model.Reference));
        element.SetAttributeValue("columnSort", XlsxWorksheetNativeMetadataHelpers.ToBoolAttribute(model.ColumnSort));
        element.SetAttributeValue("caseSensitive", XlsxWorksheetNativeMetadataHelpers.ToBoolAttribute(model.CaseSensitive));
        element.SetAttributeValue("sortMethod", XlsxWorksheetNativeMetadataHelpers.NullIfWhiteSpace(model.SortMethod));
        foreach (var condition in model.Conditions.Select(ToXml))
            element.Add(condition);

        XlsxWorksheetSortStateNormalizer.NormalizeElement(element);
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

}
