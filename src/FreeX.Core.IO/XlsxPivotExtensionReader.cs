using System.Xml.Linq;

namespace FreeX.Core.IO;

internal static class XlsxPivotExtensionReader
{
    public static readonly XNamespace FreeXNamespace = "urn:freex:pivot:2026";

    public static XElement? ReadElement(XElement root, XNamespace workbookNs, string localName) =>
        root.Element(workbookNs + "extLst")?
            .Elements(workbookNs + "ext")
            .Select(ext => ext.Element(FreeXNamespace + localName))
            .FirstOrDefault(element => element is not null);
}
