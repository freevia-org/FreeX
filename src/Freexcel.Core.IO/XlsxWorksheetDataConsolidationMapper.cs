using System.IO.Compression;
using System.Xml.Linq;
using Freexcel.Core.Model;

namespace Freexcel.Core.IO;

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

        ReadNativeAttributes(dataConsolidate, model.NativeAttributes, ["function", "leftLabels", "topLabels", "link"]);
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
        ReadNativeAttributes(element, model.NativeAttributes, ["ref", "sheet", "name"]);
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
        ApplyNativeAttributes(element, model.NativeAttributes, ["function", "leftLabels", "topLabels", "link"]);
        element.SetAttributeValue("function", NullIfWhiteSpace(model.Function));
        element.SetAttributeValue("leftLabels", ToBoolAttribute(model.LeftLabels));
        element.SetAttributeValue("topLabels", ToBoolAttribute(model.TopLabels));
        element.SetAttributeValue("link", ToBoolAttribute(model.Link));

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
        ApplyNativeAttributes(element, model.NativeAttributes, ["ref", "sheet", "name"]);
        element.SetAttributeValue("ref", NullIfWhiteSpace(model.Reference));
        element.SetAttributeValue("sheet", NullIfWhiteSpace(model.Sheet));
        element.SetAttributeValue("name", NullIfWhiteSpace(model.Name));
        return element;
    }

    private static void ReadNativeAttributes(
        XElement element,
        Dictionary<string, string> target,
        IReadOnlyCollection<string> modeledNames)
    {
        foreach (var attribute in element.Attributes())
        {
            if (attribute.IsNamespaceDeclaration || modeledNames.Contains(attribute.Name.LocalName, StringComparer.Ordinal))
                continue;

            target[attribute.Name.ToString()] = attribute.Value;
        }
    }

    private static void ApplyNativeAttributes(
        XElement element,
        Dictionary<string, string> attributes,
        IReadOnlyCollection<string> modeledNames)
    {
        foreach (var attribute in attributes)
        {
            if (string.IsNullOrWhiteSpace(attribute.Key) || modeledNames.Contains(attribute.Key, StringComparer.Ordinal))
                continue;

            element.SetAttributeValue(XName.Get(attribute.Key), attribute.Value);
        }
    }

    private static string? ToBoolAttribute(bool? value) =>
        value is { } boolValue ? boolValue ? "1" : "0" : null;

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}
