using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;
using Free.Shared.Opc;

namespace FreeX.Core.IO;

internal static class XlsxWorksheetRelationshipIdReader
{
    public static List<string> ReadAll(
        ZipArchiveEntry worksheetEntry,
        XName elementName,
        XName relationshipAttributeName)
    {
        var result = new List<string>();
        using var stream = worksheetEntry.Open();
        var settings = SecureXmlReaderSettings.Create();
        settings.IgnoreComments = true;
        settings.IgnoreProcessingInstructions = true;
        settings.IgnoreWhitespace = true;
        using var reader = XmlReader.Create(stream, settings);

        while (reader.Read())
        {
            if (reader.NodeType != XmlNodeType.Element ||
                !string.Equals(reader.LocalName, elementName.LocalName, StringComparison.Ordinal) ||
                !string.Equals(reader.NamespaceURI, elementName.NamespaceName, StringComparison.Ordinal))
            {
                continue;
            }

            var relationshipId = reader.GetAttribute(
                relationshipAttributeName.LocalName,
                relationshipAttributeName.NamespaceName);
            if (!string.IsNullOrWhiteSpace(relationshipId))
                result.Add(relationshipId);
        }

        return result;
    }
}
