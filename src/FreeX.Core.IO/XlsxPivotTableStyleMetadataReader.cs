using System.Xml.Linq;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

internal static class XlsxPivotTableStyleMetadataReader
{
    public static List<PivotTableStyleModel> Load(Stream xlsxStream)
    {
        var stylesXml = XlsxStylesheetReader.Load(xlsxStream);
        return Load(stylesXml);
    }

    public static List<PivotTableStyleModel> Load(XDocument? stylesXml)
    {
        var result = new List<PivotTableStyleModel>();
        try
        {
            if (stylesXml?.Root is null)
                return result;

            XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            foreach (var styleElement in stylesXml.Root
                         .Element(workbookNs + "tableStyles")?
                         .Elements(workbookNs + "tableStyle") ?? [])
            {
                var name = styleElement.Attribute("name")?.Value;
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                var appliesToPivotTables = XlsxXmlAttributeReader.ReadBoolAttribute(styleElement, "pivot");
                if (!appliesToPivotTables)
                    continue;

                var model = new PivotTableStyleModel
                {
                    Name = name,
                    AppliesToPivotTables = true,
                    AppliesToTables = XlsxXmlAttributeReader.ReadBoolAttribute(styleElement, "table")
                };
                foreach (var element in styleElement.Elements(workbookNs + "tableStyleElement"))
                {
                    var type = element.Attribute("type")?.Value;
                    if (string.IsNullOrWhiteSpace(type))
                        continue;

                    model.Elements.Add(new PivotTableStyleElementModel(
                        type,
                        XlsxXmlAttributeReader.ReadIntAttribute(element, "dxfId"),
                        XlsxXmlAttributeReader.ReadIntAttribute(element, "size")));
                }

                result.Add(model);
            }
        }
        catch
        {
            return result;
        }

        return result;
    }
}
