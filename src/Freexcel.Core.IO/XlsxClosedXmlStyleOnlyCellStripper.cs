using System.IO.Compression;
using System.Xml.Linq;

namespace Freexcel.Core.IO;

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
                    var strippedWorksheet = TryStripWorksheet(sourceEntry);

                    if (strippedArchive is null)
                    {
                        if (strippedWorksheet is null)
                            continue;

                        strippedPackage = new MemoryStream();
                        strippedArchive = new ZipArchive(strippedPackage, ZipArchiveMode.Create, leaveOpen: true);
                        for (var priorIndex = 0; priorIndex < index; priorIndex++)
                            CopyEntry(sourceEntries[priorIndex], strippedArchive);

                        WriteEntry(sourceEntry, strippedArchive, strippedWorksheet);
                        continue;
                    }

                    if (strippedWorksheet is not null)
                    {
                        WriteEntry(sourceEntry, strippedArchive, strippedWorksheet);
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

    private static XDocument? TryStripWorksheet(ZipArchiveEntry sourceEntry)
    {
        if (!IsWorksheetXml(sourceEntry))
            return null;

        using var sourceStream = sourceEntry.Open();
        var worksheetXml = XDocument.Load(sourceStream);
        return StripRedundantStyleOnlyCells(worksheetXml) ? worksheetXml : null;
    }

    private static void CopyEntry(ZipArchiveEntry sourceEntry, ZipArchive strippedArchive)
    {
        var targetEntry = CreateTargetEntry(sourceEntry, strippedArchive);
        using var targetStream = targetEntry.Open();
        using var sourceStream = sourceEntry.Open();
        sourceStream.CopyTo(targetStream);
    }

    private static void WriteEntry(ZipArchiveEntry sourceEntry, ZipArchive strippedArchive, XDocument worksheetXml)
    {
        var targetEntry = CreateTargetEntry(sourceEntry, strippedArchive);
        using var targetStream = targetEntry.Open();
        worksheetXml.Save(targetStream);
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

    private static bool StripRedundantStyleOnlyCells(XDocument worksheetXml)
    {
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var changed = false;
        var seenStyleIndexes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var cell in worksheetXml.Descendants(worksheetNs + "c").ToList())
        {
            var styleIndex = cell.Attribute("s");
            if (styleIndex is null ||
                cell.Element(worksheetNs + "f") is not null ||
                cell.Element(worksheetNs + "v") is not null ||
                cell.Element(worksheetNs + "is") is not null ||
                cell.Elements().Any())
            {
                continue;
            }

            if (seenStyleIndexes.Add(styleIndex.Value))
                continue;

            cell.Remove();
            changed = true;
        }

        return changed;
    }
}
