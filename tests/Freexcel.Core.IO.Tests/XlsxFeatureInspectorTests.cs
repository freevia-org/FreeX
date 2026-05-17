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
    public void Inspect_SupportedNativeChartPackage_DoesNotReportUnsupportedChart()
    {
        using var package = CreatePackageWithContent(("xl/charts/chart1.xml", """
            <c:chartSpace xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart"
                          xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
              <c:chart>
                <c:title><c:tx><c:rich><a:p><a:r><a:t>Sales</a:t></a:r></a:p></c:rich></c:tx></c:title>
                <c:plotArea>
                  <c:barChart>
                    <c:barDir val="col"/>
                    <c:ser>
                      <c:tx><c:strRef><c:f>Sheet1!$B$1</c:f></c:strRef></c:tx>
                      <c:cat><c:strRef><c:f>Sheet1!$A$2:$A$4</c:f></c:strRef></c:cat>
                      <c:val><c:numRef><c:f>Sheet1!$B$2:$B$4</c:f></c:numRef></c:val>
                    </c:ser>
                  </c:barChart>
                </c:plotArea>
              </c:chart>
            </c:chartSpace>
            """));

        var report = XlsxFeatureInspector.Inspect(package);

        report.Features.Should().NotContain(f => f.Kind == XlsxUnsupportedFeatureKind.Charts);
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

    [Fact]
    public void Inspect_PowerQueryAndDataModelPackage_DetectsBothFeatures()
    {
        using var package = CreatePackage(
            "customXml/item1.xml",
            "xl/model/item.data",
            "xl/connections.xml",
            "xl/queries/query1.xml");

        var report = XlsxFeatureInspector.Inspect(package);

        report.Features.Select(f => f.Kind).Should().Contain(XlsxUnsupportedFeatureKind.PowerQuery);
        report.Features.Select(f => f.Kind).Should().Contain(XlsxUnsupportedFeatureKind.DataModel);
    }

    [Fact]
    public void Inspect_ThemePackage_DoesNotReportUnsupportedFeatures()
    {
        using var package = CreatePackage("xl/theme/theme1.xml");

        var report = XlsxFeatureInspector.Inspect(package);

        report.HasUnsupportedFeatures.Should().BeFalse(
            "Freexcel now loads and saves the workbook theme part, so ordinary Excel files should not warn only because they contain xl/theme/theme1.xml");
        report.Features.Should().BeEmpty();
    }

    [Fact]
    public void Inspect_WorksheetWithUnsupportedConditionalFormatting_DetectsConditionalFormats()
    {
        using var package = CreatePackageWithContent(("xl/worksheets/sheet1.xml", """
            <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <conditionalFormatting sqref="A1:A5">
                <cfRule type="colorScale" priority="1">
                  <colorScale/>
                </cfRule>
              </conditionalFormatting>
            </worksheet>
            """));

        var report = XlsxFeatureInspector.Inspect(package);

        report.Features.Should().Contain(f =>
            f.Kind == XlsxUnsupportedFeatureKind.ConditionalFormats &&
            f.PackagePart == "xl/worksheets/sheet1.xml");
    }

    [Fact]
    public void Inspect_WorksheetWithSparklineGroups_DetectsSparklines()
    {
        using var package = CreatePackageWithContent(("xl/worksheets/sheet1.xml", """
            <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                       xmlns:x14="http://schemas.microsoft.com/office/spreadsheetml/2009/9/main">
              <extLst>
                <ext>
                  <x14:sparklineGroups/>
                </ext>
              </extLst>
            </worksheet>
            """));

        var report = XlsxFeatureInspector.Inspect(package);

        report.Features.Should().Contain(f =>
            f.Kind == XlsxUnsupportedFeatureKind.Sparklines &&
            f.PackagePart == "xl/worksheets/sheet1.xml");
    }

    [Fact]
    public void Inspect_WorksheetWithUnsupportedConditionalFormattingAndSparklines_DetectsBothFeatures()
    {
        using var package = CreatePackageWithContent(("xl/worksheets/sheet1.xml", """
            <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                       xmlns:x14="http://schemas.microsoft.com/office/spreadsheetml/2009/9/main">
              <conditionalFormatting sqref="A1:A5">
                <cfRule type="dataBar" priority="1">
                  <dataBar/>
                </cfRule>
              </conditionalFormatting>
              <extLst>
                <ext>
                  <x14:sparklineGroups/>
                </ext>
              </extLst>
            </worksheet>
            """));

        var report = XlsxFeatureInspector.Inspect(package);

        report.Features.Should().Contain(f =>
            f.Kind == XlsxUnsupportedFeatureKind.ConditionalFormats &&
            f.PackagePart == "xl/worksheets/sheet1.xml");
        report.Features.Should().Contain(f =>
            f.Kind == XlsxUnsupportedFeatureKind.Sparklines &&
            f.PackagePart == "xl/worksheets/sheet1.xml");
    }

    [Fact]
    public void Inspect_DrawingWithShapeAndPicture_DetectsDrawingObjects()
    {
        using var package = CreatePackageWithContent(("xl/drawings/drawing1.xml", """
            <xdr:wsDr xmlns:xdr="http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing"
                      xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
              <xdr:twoCellAnchor>
                <xdr:sp/>
                <xdr:pic/>
              </xdr:twoCellAnchor>
            </xdr:wsDr>
            """));

        var report = XlsxFeatureInspector.Inspect(package);

        report.Features.Should().Contain(f =>
            f.Kind == XlsxUnsupportedFeatureKind.DrawingObjects &&
            f.PackagePart == "xl/drawings/drawing1.xml");
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
}
