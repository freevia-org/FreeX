using System.Globalization;
using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;
using Freexcel.Core.Model;

namespace Freexcel.Core.IO;

internal static class XlsxWorksheetSingleXmlCellMapper
{
    private static readonly XNamespace WorksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    public static WorksheetSingleXmlCellsModel? Read(XElement? singleXmlCells)
    {
        if (singleXmlCells is null)
            return null;

        var model = new WorksheetSingleXmlCellsModel();
        foreach (var attribute in singleXmlCells.Attributes())
        {
            if (attribute.IsNamespaceDeclaration)
                continue;

            model.NativeAttributes[attribute.Name.ToString()] = attribute.Value;
        }

        foreach (var cellElement in singleXmlCells.Elements(WorksheetNs + "singleXmlCell"))
        {
            var cell = new WorksheetSingleXmlCellModel
            {
                Id = ReadOptionalInt(cellElement.Attribute("id")?.Value),
                Reference = NullIfWhiteSpace(cellElement.Attribute("r")?.Value),
                XmlCellPropertyId = ReadOptionalInt(cellElement.Attribute("xmlCellPrId")?.Value)
            };
            foreach (var attribute in cellElement.Attributes())
            {
                if (attribute.IsNamespaceDeclaration || IsModeledSingleXmlCellAttribute(attribute.Name.LocalName))
                    continue;

                cell.NativeAttributes[attribute.Name.ToString()] = attribute.Value;
            }

            if (cell.Id is not null ||
                cell.Reference is not null ||
                cell.XmlCellPropertyId is not null ||
                cell.NativeAttributes.Count > 0)
            {
                model.Cells.Add(cell);
            }
        }

        return model.NativeAttributes.Count == 0 && model.Cells.Count == 0
            ? null
            : model;
    }

    public static void Save(Stream xlsxStream, Workbook workbook, XlsxWorkbookWorksheetPathMap? worksheetPathMap)
    {
        if (worksheetPathMap is null)
            return;

        using var archive = new ZipArchive(xlsxStream, ZipArchiveMode.Update, leaveOpen: true);
        foreach (var sheet in workbook.Sheets.Where(sheet => sheet.SingleXmlCells is not null))
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

            root.Elements(WorksheetNs + "singleXmlCells").Remove();
            var xml = ToXml(sheet.SingleXmlCells);
            if (xml is not null)
            {
                InsertSingleXmlCells(root, xml);
                XlsxPackageXmlEditor.ReplaceXml(archive, worksheetPath, worksheetXml);
            }
        }
    }

    private static XElement? ToXml(WorksheetSingleXmlCellsModel? model)
    {
        if (model is null)
            return null;

        var element = new XElement(WorksheetNs + "singleXmlCells");
        foreach (var attribute in model.NativeAttributes)
        {
            if (string.IsNullOrWhiteSpace(attribute.Key))
                continue;

            TrySetNativeAttribute(element, attribute.Key, attribute.Value);
        }

        foreach (var cell in model.Cells)
        {
            var cellElement = new XElement(WorksheetNs + "singleXmlCell");
            SetOptionalIntAttribute(cellElement, "id", cell.Id);
            if (!string.IsNullOrWhiteSpace(cell.Reference))
                cellElement.SetAttributeValue("r", cell.Reference);
            SetOptionalIntAttribute(cellElement, "xmlCellPrId", cell.XmlCellPropertyId);
            foreach (var attribute in cell.NativeAttributes)
            {
                if (string.IsNullOrWhiteSpace(attribute.Key) || IsModeledSingleXmlCellAttribute(attribute.Key))
                    continue;

                TrySetNativeAttribute(cellElement, attribute.Key, attribute.Value);
            }

            if (cellElement.HasAttributes)
                element.Add(cellElement);
        }

        return element.HasAttributes || element.HasElements ? element : null;
    }

    private static void InsertSingleXmlCells(XElement root, XElement singleXmlCells)
    {
        var smartTags = root.Element(WorksheetNs + "smartTags");
        if (smartTags is not null)
        {
            smartTags.AddBeforeSelf(singleXmlCells);
            return;
        }

        root.Add(singleXmlCells);
    }

    private static bool IsModeledSingleXmlCellAttribute(string name) =>
        name is "id" or "r" or "xmlCellPrId";

    private static int? ReadOptionalInt(string? value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result)
            ? result
            : null;

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;

    private static void SetOptionalIntAttribute(XElement element, string name, int? value)
    {
        if (value is not null)
            element.SetAttributeValue(name, value.Value.ToString(CultureInfo.InvariantCulture));
    }

    private static bool TrySetNativeAttribute(XElement element, string name, string value)
    {
        try
        {
            element.SetAttributeValue(XName.Get(name), value);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (XmlException)
        {
            return false;
        }
    }
}
