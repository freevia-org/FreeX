using System.Xml;
using System.Xml.Linq;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

internal static class XlsxWorksheetSmartTagMapper
{
    private static readonly XNamespace WorksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    public static WorksheetSmartTagsModel? Read(XElement? smartTags)
    {
        if (smartTags is null)
            return null;

        return new WorksheetSmartTagsModel
        {
            NativeXml = smartTags.ToString(SaveOptions.DisableFormatting),
            Cells = smartTags.Elements(WorksheetNs + "cellSmartTags")
                .Select(ReadCellSmartTags)
                .ToList()
        };
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
            var smartTagsModel = sheet.SmartTags;
            if (smartTagsModel is null)
                continue;

            if (!session.TryGetWorksheet(sheet, out var edit))
                continue;

            var root = edit.Root;
            var existingSmartTags = root.Element(WorksheetNs + "smartTags");
            var changed = existingSmartTags is not null;
            existingSmartTags?.Remove();

            if (ToXml(smartTagsModel) is { } smartTags)
            {
                XlsxWorksheetElementOrder.Insert(root, smartTags);
                changed = true;
            }

            if (changed)
                session.MarkDirty(edit);
        }
    }

    private static WorksheetCellSmartTagsModel ReadCellSmartTags(XElement element)
    {
        var model = new WorksheetCellSmartTagsModel
        {
            Reference = element.Attribute("r")?.Value,
            Tags = element.Elements(WorksheetNs + "cellSmartTag")
                .Select(ReadCellSmartTag)
                .ToList()
        };
        XlsxWorksheetNativeMetadataHelpers.ReadNativeAttributes(element, model.NativeAttributes, ["r"]);
        return model;
    }

    private static WorksheetCellSmartTagModel ReadCellSmartTag(XElement element)
    {
        var model = new WorksheetCellSmartTagModel
        {
            Type = element.Attribute("type")?.Value,
            Deleted = XlsxXmlAttributeReader.ReadNullableBoolAttribute(element, "deleted"),
            Properties = element.Elements(WorksheetNs + "cellSmartTagPr")
                .Select(ReadCellSmartTagProperty)
                .ToList()
        };
        XlsxWorksheetNativeMetadataHelpers.ReadNativeAttributes(element, model.NativeAttributes, ["type", "deleted"]);
        return model;
    }

    private static WorksheetCellSmartTagPropertyModel ReadCellSmartTagProperty(XElement element)
    {
        var model = new WorksheetCellSmartTagPropertyModel
        {
            Key = element.Attribute("key")?.Value,
            Value = element.Attribute("val")?.Value
        };
        XlsxWorksheetNativeMetadataHelpers.ReadNativeAttributes(element, model.NativeAttributes, ["key", "val"]);
        return model;
    }

    private static XElement? ToXml(WorksheetSmartTagsModel model)
    {
        if (XlsxWorksheetNativeMetadataHelpers.TryParseNativeElement(
                model.NativeXml,
                WorksheetNs + "smartTags") is { } nativeElement)
        {
            return nativeElement;
        }

        if (model.Cells.Count == 0)
            return null;

        return new XElement(
            WorksheetNs + "smartTags",
            model.Cells.Select(cell =>
            {
                var element = new XElement(WorksheetNs + "cellSmartTags");
                XlsxWorksheetNativeMetadataHelpers.ApplyNativeAttributes(element, cell.NativeAttributes, ["r"]);
                element.SetAttributeValue("r", XlsxWorksheetNativeMetadataHelpers.NullIfWhiteSpace(cell.Reference));
                foreach (var tag in cell.Tags)
                    element.Add(ToXml(tag));
                return element;
            }));
    }

    private static XElement ToXml(WorksheetCellSmartTagModel model)
    {
        var element = new XElement(WorksheetNs + "cellSmartTag");
        XlsxWorksheetNativeMetadataHelpers.ApplyNativeAttributes(element, model.NativeAttributes, ["type", "deleted"]);
        element.SetAttributeValue("type", XlsxWorksheetNativeMetadataHelpers.NullIfWhiteSpace(model.Type));
        element.SetAttributeValue("deleted", model.Deleted is { } deleted ? deleted ? "1" : "0" : null);
        foreach (var property in model.Properties)
            element.Add(ToXml(property));
        return element;
    }

    private static XElement ToXml(WorksheetCellSmartTagPropertyModel model)
    {
        var element = new XElement(WorksheetNs + "cellSmartTagPr");
        XlsxWorksheetNativeMetadataHelpers.ApplyNativeAttributes(element, model.NativeAttributes, ["key", "val"]);
        element.SetAttributeValue("key", XlsxWorksheetNativeMetadataHelpers.NullIfWhiteSpace(model.Key));
        element.SetAttributeValue("val", model.Value);
        return element;
    }
}
