using System.Xml;
using System.Xml.Linq;

namespace FreeX.Core.IO;

internal static class XmlReaderElementMaterializer
{
    public static XElement CreateShallowElement(XmlReader reader)
    {
        var element = new XElement(XName.Get(reader.LocalName, reader.NamespaceURI));
        if (!reader.HasAttributes)
            return element;

        for (var i = 0; i < reader.AttributeCount; i++)
        {
            reader.MoveToAttribute(i);
            element.Add(new XAttribute(GetAttributeName(reader), reader.Value));
        }

        reader.MoveToElement();
        return element;
    }

    private static XName GetAttributeName(XmlReader reader)
    {
        if (reader.Prefix == "xmlns")
            return XNamespace.Xmlns + reader.LocalName;
        if (reader.Name == "xmlns")
            return XName.Get("xmlns");
        if (reader.NamespaceURI.Length == 0)
            return XName.Get(reader.LocalName);

        return XName.Get(reader.LocalName, reader.NamespaceURI);
    }
}
