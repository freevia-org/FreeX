using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;

namespace FreeX.Core.IO.Tests;

public partial class XlsxFeatureInspectorTests
{
    private static MemoryStream CreatePackage(params string[] entries)
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var entryName in entries)
            {
                var entry = archive.CreateEntry(entryName);
                using var writer = new StreamWriter(entry.Open());
                writer.Write("test");
            }
        }

        stream.Position = 0;
        return stream;
    }

    private static MemoryStream CreatePackageWithContent(params (string Name, string Content)[] entries)
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (entryName, content) in entries)
            {
                var entry = archive.CreateEntry(entryName);
                using var writer = new StreamWriter(entry.Open());
                writer.Write(content);
            }
        }

        stream.Position = 0;
        return stream;
    }

    private static string BuildChartExPackageXml(string layoutId, bool includeParetoLine = false)
    {
        XNamespace chartExNs = "http://schemas.microsoft.com/office/drawing/2014/chartex";
        var chartDataId = "data0";
        var region = new XElement(chartExNs + "plotAreaRegion",
            new XElement(chartExNs + "series",
                new XAttribute("layoutId", layoutId),
                new XElement(chartExNs + "dataId", new XAttribute("val", chartDataId))));
        if (includeParetoLine)
        {
            region.Add(new XElement(chartExNs + "series",
                new XAttribute("layoutId", "paretoLine"),
                new XElement(chartExNs + "dataId", new XAttribute("val", chartDataId))));
        }

        return new XDocument(
            new XElement(chartExNs + "chartSpace",
                new XAttribute(XNamespace.Xmlns + "cx", chartExNs),
                new XElement(chartExNs + "chartData",
                    new XElement(chartExNs + "data",
                        new XAttribute("id", chartDataId),
                        new XElement(chartExNs + "strDim",
                            new XAttribute("type", "cat"),
                            new XElement(chartExNs + "f", "Sheet1!$A$2:$A$4")),
                        new XElement(chartExNs + "numDim",
                            new XAttribute("type", "val"),
                            new XElement(chartExNs + "f", "Sheet1!$B$2:$B$4"),
                            new XElement(chartExNs + "nf", "Sheet1!$B$1")))),
                new XElement(chartExNs + "chart",
                    new XElement(chartExNs + "plotArea", region))))
            .ToString(SaveOptions.DisableFormatting);
    }

    private static string FindWorkspaceFile(params string[] relativeParts) => TestWorkspaceFiles.FindWorkspaceFile(relativeParts);
}
