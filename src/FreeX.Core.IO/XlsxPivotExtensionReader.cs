using System.Xml.Linq;

namespace FreeX.Core.IO;

internal static class XlsxPivotExtensionReader
{
    public static readonly XNamespace FreeXNamespace = "urn:freex:pivot:2026";

    public static XElement? ReadElement(XElement root, XNamespace workbookNs, string localName)
    {
        var extensionList = root.Element(workbookNs + "extLst");
        if (extensionList is null)
            return null;

        foreach (var extension in extensionList.Elements(workbookNs + "ext"))
        {
            var element = extension.Element(FreeXNamespace + localName);
            if (element is not null)
                return element;
        }

        return null;
    }
}
