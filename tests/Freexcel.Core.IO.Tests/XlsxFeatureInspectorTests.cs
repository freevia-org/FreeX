using System.IO.Compression;
using FluentAssertions;
using Freexcel.Core.IO;

namespace Freexcel.Core.IO.Tests;

public class XlsxFeatureInspectorTests
{
    [Fact]
    public void Inspect_CleanWorkbookPackage_HasNoUnsupportedFeatures()
    {
        using var package = CreatePackage(
            "[Content_Types].xml",
            "_rels/.rels",
            "xl/workbook.xml",
            "xl/worksheets/sheet1.xml");

        var report = XlsxFeatureInspector.Inspect(package);

        report.HasUnsupportedFeatures.Should().BeFalse();
        report.Features.Should().BeEmpty();
    }

    [Fact]
    public void Inspect_MacroPackage_DetectsMacros()
    {
        using var package = CreatePackage("xl/vbaProject.bin");

        var report = XlsxFeatureInspector.Inspect(package);

        report.Features.Should().Contain(f => f.Kind == XlsxUnsupportedFeatureKind.Macros);
    }

    [Fact]
    public void Inspect_PivotAndChartPackage_DetectsBothFeatures()
    {
        using var package = CreatePackage(
            "xl/pivotTables/pivotTable1.xml",
            "xl/pivotCache/pivotCacheDefinition1.xml",
            "xl/charts/chart1.xml");

        var report = XlsxFeatureInspector.Inspect(package);

        report.Features.Select(f => f.Kind).Should().Contain(XlsxUnsupportedFeatureKind.PivotTables);
        report.Features.Select(f => f.Kind).Should().Contain(XlsxUnsupportedFeatureKind.Charts);
    }

    [Fact]
    public void Inspect_ExternalLinkEmbeddedObjectAndCustomXml_DetectsAllFeatures()
    {
        using var package = CreatePackage(
            "xl/externalLinks/externalLink1.xml",
            "xl/embeddings/oleObject1.bin",
            "customXml/item1.xml");

        var report = XlsxFeatureInspector.Inspect(package);

        report.Features.Select(f => f.Kind).Should().Contain(XlsxUnsupportedFeatureKind.ExternalLinks);
        report.Features.Select(f => f.Kind).Should().Contain(XlsxUnsupportedFeatureKind.EmbeddedObjects);
        report.Features.Select(f => f.Kind).Should().Contain(XlsxUnsupportedFeatureKind.CustomXmlParts);
    }

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
}
