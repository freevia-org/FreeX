using System.IO.Compression;
using System.Xml.Linq;

namespace Freexcel.Core.IO;

internal static class XlsxClosedXmlStyleOnlyCellStripper
{
    public static MemoryStream Create(MemoryStream sourcePackage)
    {
        sourcePackage.Position = 0;
        var strippedPackage = new MemoryStream();
        var removedAny = false;

        using (var sourceArchive = new ZipArchive(sourcePackage, ZipArchiveMode.Read, leaveOpen: true))
        using (var strippedArchive = new ZipArchive(strippedPackage, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var sourceEntry in sourceArchive.Entries)
            {
                var targetEntry = strippedArchive.CreateEntry(sourceEntry.FullName, CompressionLevel.Optimal);
                targetEntry.LastWriteTime = sourceEntry.LastWriteTime;
                using var targetStream = targetEntry.Open();

                if (IsWorksheetXml(sourceEntry))
                {
                    using var sourceStream = sourceEntry.Open();
                    var worksheetXml = XDocument.Load(sourceStream);
                    if (StripRedundantStyleOnlyCells(worksheetXml))
                    {
                        removedAny = true;
                        worksheetXml.Save(targetStream);
                        continue;
                    }
                }

                using var sourceStreamCopy = sourceEntry.Open();
                sourceStreamCopy.CopyTo(targetStream);
            }
        }

        sourcePackage.Position = 0;
        if (!removedAny)
        {
            strippedPackage.Dispose();
            return sourcePackage;
        }

        strippedPackage.Position = 0;
        return strippedPackage;
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
