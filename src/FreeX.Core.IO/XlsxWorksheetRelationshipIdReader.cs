using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;

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
        using var reader = XmlReader.Create(stream, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            IgnoreComments = true,
            IgnoreProcessingInstructions = true,
            IgnoreWhitespace = true,
        });

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
