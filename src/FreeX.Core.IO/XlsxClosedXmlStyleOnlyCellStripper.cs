using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace FreeX.Core.IO;

internal static class XlsxClosedXmlStyleOnlyCellStripper
{
    public static MemoryStream Create(MemoryStream sourcePackage)
        => Create(sourcePackage, worksheetPathsToStrip: null);

    internal static MemoryStream Create(
        MemoryStream sourcePackage,
        IReadOnlySet<string>? worksheetPathsToStrip)
    {
        sourcePackage.Position = 0;
        MemoryStream? strippedPackage = null;
        ZipArchive? strippedArchive = null;
        var returnStrippedPackage = false;

        try
        {
            ZipArchive sourceArchive;
            try
            {
                sourceArchive = new ZipArchive(sourcePackage, ZipArchiveMode.Read, leaveOpen: true);
            }
            catch (InvalidDataException)
            {
                // Not a valid zip archive at all (e.g. a truncated download or a non-OOXML file
                // renamed to .xlsx). Reopening these same unreadable bytes here is guaranteed to
                // fail identically to the caller's own attempt; surface the graceful, typed error
                // instead of letting a raw low-level zip exception escape from this fallback path.
                throw new WorkbookInvalidException(
                    "The workbook could not be read because the file is not a valid .xlsx package (it may be corrupted, truncated, or not actually an Excel file).");
            }

            using (sourceArchive)
            {
                var sourceEntries = sourceArchive.Entries;
                for (var index = 0; index < sourceEntries.Count; index++)
                {
                    var sourceEntry = sourceEntries[index];
                    var shouldStripWorksheet = ShouldStripWorksheet(sourceEntry, worksheetPathsToStrip);

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

    internal static bool ShouldStripWorksheet(
        ZipArchiveEntry sourceEntry,
        IReadOnlySet<string>? worksheetPathsToStrip)
    {
        if (!IsWorksheetXml(sourceEntry))
            return false;

        if (worksheetPathsToStrip is not null)
            return worksheetPathsToStrip.Contains(XlsxPackagePath.NormalizePackagePath(sourceEntry.FullName));

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

    internal static void StripRedundantStyleOnlyCells(Stream worksheetStream, Stream outputStream)
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
                            XmlStreamingCopy.WriteCurrentNode(reader, writer);

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

                XmlStreamingCopy.WriteCurrentNode(reader, writer);
                hasNode = reader.Read();
            }
        }
    }

}
