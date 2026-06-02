using System.Xml.Linq;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

internal static class XlsxStructuredTableStyleMetadataReader
{
    public static List<StructuredTableStyleModel> Load(XDocument? stylesXml)
    {
        var result = new List<StructuredTableStyleModel>();
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

                var appliesToTables = XlsxXmlAttributeReader.ReadBoolAttribute(styleElement, "table");
                var appliesToPivotTables = XlsxXmlAttributeReader.ReadBoolAttribute(styleElement, "pivot");
                if (!appliesToTables || appliesToPivotTables)
                    continue;

                result.Add(new StructuredTableStyleModel
                {
                    Name = name,
                    AppliesToTables = true,
                    AppliesToPivotTables = false,
                    NativeXml = styleElement.ToString(SaveOptions.DisableFormatting)
                });
            }
        }
        catch
        {
            return result;
        }

        return result;
    }
}
