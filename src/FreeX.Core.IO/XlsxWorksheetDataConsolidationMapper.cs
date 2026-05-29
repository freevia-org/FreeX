using System.IO.Compression;
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

        using var archive = new ZipArchive(xlsxStream, ZipArchiveMode.Update, leaveOpen: true);
        foreach (var sheet in workbook.Sheets.Where(sheet => sheet.DataConsolidation is not null))
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

            root.Element(WorksheetNs + "dataConsolidate")?.Remove();
            if (ToXml(sheet.DataConsolidation!) is { } dataConsolidate)
                root.Add(dataConsolidate);

            XlsxPackageXmlEditor.ReplaceXml(archive, worksheetPath, worksheetXml);
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
        if (!string.IsNullOrWhiteSpace(model.NativeXml))
        {
            try
            {
                var nativeElement = XElement.Parse(model.NativeXml);
                if (nativeElement.Name == WorksheetNs + "dataConsolidate")
                    return nativeElement;
            }
            catch
            {
                // Fall back to the structured model below.
            }
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
