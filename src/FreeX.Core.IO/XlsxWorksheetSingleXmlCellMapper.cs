using System.Globalization;
using System.Xml.Linq;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

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
                Reference = XlsxWorksheetNativeMetadataHelpers.NullIfWhiteSpace(cellElement.Attribute("r")?.Value),
                XmlCellPropertyId = ReadOptionalInt(cellElement.Attribute("xmlCellPrId")?.Value)
            };
            XlsxWorksheetNativeMetadataHelpers.ReadNativeAttributes(cellElement, cell.NativeAttributes, ["id", "r", "xmlCellPrId"]);

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

        using var session = new XlsxWorksheetXmlEditSession(xlsxStream, worksheetPathMap);
        Save(session, workbook);
    }

    internal static void Save(XlsxWorksheetXmlEditSession session, Workbook workbook)
    {
        foreach (var sheet in workbook.Sheets)
        {
            var singleXmlCells = sheet.SingleXmlCells;
            if (singleXmlCells is null)
                continue;

            if (!session.TryGetWorksheet(sheet, out var edit))
                continue;

            var changed = false;
            while (edit.Root.Element(WorksheetNs + "singleXmlCells") is { } existingElement)
            {
                existingElement.Remove();
                changed = true;
            }

            var xml = ToXml(singleXmlCells);
            if (xml is not null)
            {
                InsertSingleXmlCells(edit.Root, xml);
                changed = true;
            }

            if (changed)
                session.MarkDirty(edit);
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

            XlsxWorksheetNativeMetadataHelpers.TrySetNativeAttribute(element, attribute.Key, attribute.Value);
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

                XlsxWorksheetNativeMetadataHelpers.TrySetNativeAttribute(cellElement, attribute.Key, attribute.Value);
            }

            if (cellElement.HasAttributes)
                element.Add(cellElement);
        }

        return element.HasAttributes || element.HasElements ? element : null;
    }

    private static void InsertSingleXmlCells(XElement root, XElement singleXmlCells)
    {
        string[] laterWorksheetElements =
        [
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

        var insertionPoint = root.Elements()
            .FirstOrDefault(element =>
                element.Name.Namespace == WorksheetNs &&
                laterWorksheetElements.Contains(element.Name.LocalName, StringComparer.Ordinal));
        if (insertionPoint is not null)
            insertionPoint.AddBeforeSelf(singleXmlCells);
        else
            root.Add(singleXmlCells);
    }

    private static bool IsModeledSingleXmlCellAttribute(string name) =>
        name is "id" or "r" or "xmlCellPrId";

    private static int? ReadOptionalInt(string? value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result)
            ? result
            : null;

    private static void SetOptionalIntAttribute(XElement element, string name, int? value)
    {
        if (value is not null)
            element.SetAttributeValue(name, value.Value.ToString(CultureInfo.InvariantCulture));
    }
}
