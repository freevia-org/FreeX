using System.Xml.Linq;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

internal static class XlsxWorksheetDataConsolidationMapper
{
    private static readonly XNamespace WorksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    public static WorksheetDataConsolidationModel? Read(XElement? dataConsolidate)
    {
        if (dataConsolidate is null)
            return null;

        var model = new WorksheetDataConsolidationModel
        {
            Function = dataConsolidate.Attribute("function")?.Value,
            LeftLabels = XlsxXmlAttributeReader.ReadNullableBoolAttribute(dataConsolidate, "leftLabels"),
            TopLabels = XlsxXmlAttributeReader.ReadNullableBoolAttribute(dataConsolidate, "topLabels"),
            Link = XlsxXmlAttributeReader.ReadNullableBoolAttribute(dataConsolidate, "link"),
            NativeXml = dataConsolidate.ToString(SaveOptions.DisableFormatting),
            References = dataConsolidate
                .Element(WorksheetNs + "dataRefs")?
                .Elements(WorksheetNs + "dataRef")
                .Select(ReadReference)
                .ToList() ?? []
        };

        XlsxWorksheetNativeMetadataHelpers.ReadNativeAttributes(dataConsolidate, model.NativeAttributes, ["function", "leftLabels", "topLabels", "link"]);
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
            var dataConsolidationModel = sheet.DataConsolidation;
            if (dataConsolidationModel is null)
                continue;

            if (!session.TryGetWorksheet(sheet, out var edit))
                continue;

            var root = edit.Root;
            var existingDataConsolidate = root.Element(WorksheetNs + "dataConsolidate");
            var changed = existingDataConsolidate is not null;
            existingDataConsolidate?.Remove();

            if (ToXml(dataConsolidationModel) is { } dataConsolidate)
            {
                XlsxWorksheetElementOrder.Insert(root, dataConsolidate);
                changed = true;
            }

            if (changed)
                session.MarkDirty(edit);
        }
    }

    private static WorksheetDataConsolidationReferenceModel ReadReference(XElement element)
    {
        var model = new WorksheetDataConsolidationReferenceModel
        {
            Reference = element.Attribute("ref")?.Value,
            Sheet = element.Attribute("sheet")?.Value,
            Name = element.Attribute("name")?.Value
        };
        XlsxWorksheetNativeMetadataHelpers.ReadNativeAttributes(element, model.NativeAttributes, ["ref", "sheet", "name"]);
        return model;
    }

    private static XElement? ToXml(WorksheetDataConsolidationModel model)
    {
        if (XlsxWorksheetNativeMetadataHelpers.TryParseNativeElement(
                model.NativeXml,
                WorksheetNs + "dataConsolidate",
                XlsxWorksheetDataConsolidationNormalizer.NormalizeElement) is { } nativeElement)
        {
            if (!nativeElement.HasAttributes && !nativeElement.HasElements)
                return null;

            return nativeElement;
        }

        var element = new XElement(WorksheetNs + "dataConsolidate");
        XlsxWorksheetNativeMetadataHelpers.ApplyNativeAttributes(element, model.NativeAttributes, ["function", "leftLabels", "topLabels", "link"]);
        element.SetAttributeValue("function", XlsxWorksheetNativeMetadataHelpers.NullIfWhiteSpace(model.Function));
        element.SetAttributeValue("leftLabels", XlsxWorksheetNativeMetadataHelpers.ToBoolAttribute(model.LeftLabels));
        element.SetAttributeValue("topLabels", XlsxWorksheetNativeMetadataHelpers.ToBoolAttribute(model.TopLabels));
        element.SetAttributeValue("link", XlsxWorksheetNativeMetadataHelpers.ToBoolAttribute(model.Link));

        if (model.References.Count > 0)
        {
            element.Add(new XElement(
                WorksheetNs + "dataRefs",
                new XAttribute("count", model.References.Count),
                model.References.Select(ToXml)));
        }

        XlsxWorksheetDataConsolidationNormalizer.NormalizeElement(element);
        return element.HasAttributes || element.HasElements ? element : null;
    }

    private static XElement ToXml(WorksheetDataConsolidationReferenceModel model)
    {
        var element = new XElement(WorksheetNs + "dataRef");
        XlsxWorksheetNativeMetadataHelpers.ApplyNativeAttributes(element, model.NativeAttributes, ["ref", "sheet", "name"]);
        element.SetAttributeValue("ref", XlsxWorksheetNativeMetadataHelpers.NullIfWhiteSpace(model.Reference));
        element.SetAttributeValue("sheet", XlsxWorksheetNativeMetadataHelpers.NullIfWhiteSpace(model.Sheet));
        element.SetAttributeValue("name", XlsxWorksheetNativeMetadataHelpers.NullIfWhiteSpace(model.Name));
        return element;
    }

}
