using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace FreeX.Core.IO;

internal static class XlsxClosedXmlStyleOnlyCellStripper
{
    public static MemoryStream Create(MemoryStream sourcePackage)
    {
        sourcePackage.Position = 0;
        MemoryStream? strippedPackage = null;
        ZipArchive? strippedArchive = null;
        var returnStrippedPackage = false;

        try
        {
            using (var sourceArchive = new ZipArchive(sourcePackage, ZipArchiveMode.Read, leaveOpen: true))
            {
                var sourceEntries = sourceArchive.Entries;
                for (var index = 0; index < sourceEntries.Count; index++)
                {
                    var sourceEntry = sourceEntries[index];
                    var shouldStripWorksheet = ShouldStripWorksheet(sourceEntry);

                    if (strippedArchive is null)
                    {
                        if (!shouldStripWorksheet)
                            continue;

                        strippedPackage = new MemoryStream();
                        strippedArchive = new ZipArchive(strippedPackage, ZipArchiveMode.Create, leaveOpen: true);
                        for (var priorIndex = 0; priorIndex < index; priorIndex++)
                            CopyEntry(sourceEntries[priorIndex], strippedArchive);

                        WriteStrippedWorksheetEntry(sourceEntry, strippedArchive);
                        continue;
                    }

                    if (shouldStripWorksheet)
                    {
                        WriteStrippedWorksheetEntry(sourceEntry, strippedArchive);
                        continue;
                    }

                    CopyEntry(sourceEntry, strippedArchive);
                }
            }

            sourcePackage.Position = 0;
            if (strippedPackage is null || strippedArchive is null)
                return sourcePackage;

            strippedArchive.Dispose();
            strippedArchive = null;
            strippedPackage.Position = 0;
            returnStrippedPackage = true;
            return strippedPackage;
        }
        finally
        {
            strippedArchive?.Dispose();
            if (!returnStrippedPackage)
                strippedPackage?.Dispose();
        }
    }

    private static bool ShouldStripWorksheet(ZipArchiveEntry sourceEntry)
    {
        if (!IsWorksheetXml(sourceEntry))
            return false;

        using (var scanStream = sourceEntry.Open())
        {
            if (!ContainsDuplicateStyleOnlyCells(scanStream))
                return false;
        }

        return true;
    }

    private static bool ContainsDuplicateStyleOnlyCells(Stream worksheetStream)
    {
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            IgnoreComments = true,
            IgnoreProcessingInstructions = true
        };
        using var reader = XmlReader.Create(worksheetStream, settings);
        HashSet<string>? seenStyleIndexes = null;

        while (reader.Read())
        {
            if (reader.NodeType != XmlNodeType.Element ||
                reader.LocalName != "c" ||
                reader.NamespaceURI != "http://schemas.openxmlformats.org/spreadsheetml/2006/main")
            {
                continue;
            }

            var styleIndex = reader.GetAttribute("s");
            if (string.IsNullOrEmpty(styleIndex) ||
                !IsStyleOnlyCell(reader))
            {
                continue;
            }

            seenStyleIndexes ??= new HashSet<string>(StringComparer.Ordinal);
            if (!seenStyleIndexes.Add(styleIndex))
                return true;
        }

        return false;
    }

    private static bool IsStyleOnlyCell(XmlReader cellReader)
    {
        if (cellReader.IsEmptyElement)
            return true;

        var depth = cellReader.Depth;
        while (cellReader.Read())
        {
            if (cellReader.NodeType == XmlNodeType.EndElement &&
                cellReader.Depth == depth)
            {
                return true;
            }

            if (cellReader.NodeType == XmlNodeType.Element)
                return false;
        }

        return true;
    }

    private static void CopyEntry(ZipArchiveEntry sourceEntry, ZipArchive strippedArchive)
    {
        var targetEntry = CreateTargetEntry(sourceEntry, strippedArchive);
        using var targetStream = targetEntry.Open();
        using var sourceStream = sourceEntry.Open();
        sourceStream.CopyTo(targetStream);
    }

    private static void WriteStrippedWorksheetEntry(ZipArchiveEntry sourceEntry, ZipArchive strippedArchive)
    {
        var targetEntry = CreateTargetEntry(sourceEntry, strippedArchive);
        using var targetStream = targetEntry.Open();
        using var sourceStream = sourceEntry.Open();
        StripRedundantStyleOnlyCells(sourceStream, targetStream);
    }

    private static ZipArchiveEntry CreateTargetEntry(ZipArchiveEntry sourceEntry, ZipArchive strippedArchive)
    {
        var targetEntry = strippedArchive.CreateEntry(sourceEntry.FullName, CompressionLevel.Optimal);
        targetEntry.LastWriteTime = sourceEntry.LastWriteTime;
        return targetEntry;
    }

    private static bool IsWorksheetXml(ZipArchiveEntry entry) =>
        entry.FullName.StartsWith("xl/worksheets/", StringComparison.OrdinalIgnoreCase) &&
        entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase);

    private static void StripRedundantStyleOnlyCells(Stream worksheetStream, Stream outputStream)
    {
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var cellName = worksheetNs + "c";
        var seenStyleIndexes = new HashSet<string>(StringComparer.Ordinal);

        var readerSettings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            IgnoreComments = false,
            IgnoreProcessingInstructions = false
        };
        using var reader = XmlReader.Create(worksheetStream, readerSettings);
        var writerSettings = new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            OmitXmlDeclaration = true,
            ConformanceLevel = ConformanceLevel.Auto
        };
        using (var writer = XmlWriter.Create(outputStream, writerSettings))
        {
            var hasNode = reader.Read();
            while (hasNode)
            {
                if (reader.NodeType == XmlNodeType.Element &&
                    reader.LocalName == cellName.LocalName &&
                    reader.NamespaceURI == cellName.NamespaceName &&
                    reader.GetAttribute("s") is { Length: > 0 } styleIndex)
                {
                    if (reader.IsEmptyElement)
                    {
                        if (seenStyleIndexes.Add(styleIndex))
                            WriteCurrentNode(reader, writer);

                        hasNode = reader.Read();
                        continue;
                    }

                    var cell = (XElement)XNode.ReadFrom(reader);
                    if (!cell.HasElements && !seenStyleIndexes.Add(styleIndex))
                    {
                        continue;
                    }

                    cell.WriteTo(writer);
                    hasNode = reader.ReadState != ReadState.EndOfFile;
                    continue;
                }

                WriteCurrentNode(reader, writer);
                hasNode = reader.Read();
            }
        }
    }

    private static void WriteCurrentNode(XmlReader reader, XmlWriter writer)
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

            case XmlNodeType.DocumentType:
                writer.WriteDocType(reader.Name, reader.GetAttribute("PUBLIC"), reader.GetAttribute("SYSTEM"), reader.Value);
                break;

            case XmlNodeType.EntityReference:
                writer.WriteEntityRef(reader.Name);
                break;
        }
    }
}
