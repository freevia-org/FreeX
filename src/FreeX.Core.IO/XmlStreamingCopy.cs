using System.Xml;

namespace FreeX.Core.IO;

internal static class XmlStreamingCopy
{
    public static void WriteCurrentNode(
        XmlReader reader,
        XmlWriter writer,
        bool writeXmlDeclarationAsProcessingInstruction = false)
    {
        switch (reader.NodeType)
        {
            case XmlNodeType.Element:
                writer.WriteStartElement(reader.Prefix, reader.LocalName, reader.NamespaceURI);
                if (reader.HasAttributes)
                {
                    while (reader.MoveToNextAttribute())
                    {
                        writer.WriteStartAttribute(reader.Prefix, reader.LocalName, reader.NamespaceURI);
                        writer.WriteString(reader.Value);
                        writer.WriteEndAttribute();
                    }

                    reader.MoveToElement();
                }

                if (reader.IsEmptyElement)
                    writer.WriteEndElement();
                break;

            case XmlNodeType.EndElement:
                writer.WriteFullEndElement();
                break;

            case XmlNodeType.Text:
                writer.WriteString(reader.Value);
                break;

            case XmlNodeType.CDATA:
                writer.WriteCData(reader.Value);
                break;

            case XmlNodeType.Whitespace:
            case XmlNodeType.SignificantWhitespace:
                writer.WriteWhitespace(reader.Value);
                break;

            case XmlNodeType.Comment:
                writer.WriteComment(reader.Value);
                break;

            case XmlNodeType.ProcessingInstruction:
                writer.WriteProcessingInstruction(reader.Name, reader.Value);
                break;

            case XmlNodeType.XmlDeclaration when writeXmlDeclarationAsProcessingInstruction:
                writer.WriteProcessingInstruction(reader.Name, reader.Value);
                break;

            case XmlNodeType.DocumentType:
                writer.WriteDocType(reader.Name, reader.GetAttribute("PUBLIC"), reader.GetAttribute("SYSTEM"), reader.Value);
                break;

            case XmlNodeType.EntityReference:
                writer.WriteEntityRef(reader.Name);
                break;
        }
    }
}
