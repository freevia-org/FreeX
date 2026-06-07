using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

public sealed partial class XlsxChartExWriterTests
{
    [Theory]
    [InlineData(ChartType.Treemap)]
    [InlineData(ChartType.Sunburst)]
    [InlineData(ChartType.Histogram)]
    [InlineData(ChartType.Pareto)]
    [InlineData(ChartType.BoxAndWhisker)]
    [InlineData(ChartType.Waterfall)]
    [InlineData(ChartType.Funnel)]
    public void Save_LoadedEditedChartExModelKeepsNativePayloadAndAppliesModeledLegend(ChartType chartType)
    {
        var source = SaveWorkbookWithChart(chartType, configureChart: chart =>
        {
            chart.ShowLegend = true;
            chart.LegendPosition = ChartLegendPosition.Right;
        });
        using (var archive = new ZipArchive(source, ZipArchiveMode.Update, leaveOpen: true))
        {
            var chartXml = LoadChartXml(archive);
            chartXml.Root!.Add(new XElement(ChartExNs + "sourceMarker", "original-source-chart"));
            ReplacePackageXml(archive, "xl/charts/chart1.xml", chartXml);
        }

        source.Position = 0;
        var workbook = new XlsxFileAdapter().Load(source);
        var chart = workbook.GetSheetAt(0).Charts.Should().ContainSingle().Subject;
        chart.ShowLegend = true;
        chart.LegendPosition = ChartLegendPosition.Bottom;
        chart.LegendOverlay = true;

        var saved = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, saved);
        saved.Position = 0;

        using var savedArchive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: false);
        var savedChartXml = LoadChartXml(savedArchive);
        var savedText = savedChartXml.ToString(SaveOptions.DisableFormatting);
        savedText.Should().Contain("original-source-chart");
        var legend = savedChartXml.Root!
            .Element(ChartExNs + "chart")!
            .Element(ChartExNs + "legend");
        legend.Should().NotBeNull();
        // chartEx legend position/overlay are attributes on <cx:legend>, not child elements.
        legend!.Attribute("pos")!.Value.Should().Be("b");
        legend.Attribute("overlay")!.Value.Should().Be("1");
    }

    [Fact]
    public void Save_LoadedEditedChartExModelKeepsNativePayloadAndAppliesModeledTitle()
    {
        var source = SaveWorkbookWithChart(ChartType.Treemap);
        using (var archive = new ZipArchive(source, ZipArchiveMode.Update, leaveOpen: true))
        {
            var chartXml = LoadChartXml(archive);
            chartXml.Root!.Add(new XElement(ChartExNs + "sourceMarker", "original-source-chart"));
            ReplacePackageXml(archive, "xl/charts/chart1.xml", chartXml);
        }

        source.Position = 0;
        var workbook = new XlsxFileAdapter().Load(source);
        workbook.GetSheetAt(0).Charts.Should().ContainSingle().Subject.Title = "Edited ChartEx Title";

        var saved = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, saved);
        saved.Position = 0;

        using var savedArchive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: false);
        var savedChartXml = LoadChartXml(savedArchive);
        savedChartXml.ToString(SaveOptions.DisableFormatting)
            .Should().Contain("original-source-chart")
            .And.Contain("Edited ChartEx Title");
    }

    [Fact]
    public void Save_LoadedEditedChartExModelKeepsNativePayloadAndAppliesModeledDataRange()
    {
        var source = SaveWorkbookWithChart(ChartType.Treemap);
        using (var archive = new ZipArchive(source, ZipArchiveMode.Update, leaveOpen: true))
        {
            var chartXml = LoadChartXml(archive);
            chartXml.Root!.Add(new XElement(ChartExNs + "sourceMarker", "original-source-chart"));
            ReplacePackageXml(archive, "xl/charts/chart1.xml", chartXml);
        }

        source.Position = 0;
        var workbook = new XlsxFileAdapter().Load(source);
        var sheet = workbook.GetSheetAt(0);
        sheet.SetCell(new CellAddress(sheet.Id, 5, 1), new TextValue("D"));
        sheet.SetCell(new CellAddress(sheet.Id, 5, 2), new NumberValue(40));
        var chart = sheet.Charts.Should().ContainSingle().Subject;
        chart.DataRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 2));

        var saved = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, saved);
        saved.Position = 0;

        using var savedArchive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: false);
        var savedChartXml = LoadChartXml(savedArchive);
        var savedText = savedChartXml.ToString(SaveOptions.DisableFormatting);
        savedText.Should().Contain("original-source-chart");
        savedText.Should().Contain("$A$2:$A$5");
        savedText.Should().Contain("$B$2:$B$5");
        savedText.Should().NotContain("$B$2:$B$4");
    }

    [Fact]
    public void Save_LoadedEditedChartExModelPreservesSourceStyleColorSidecarRelationshipsWhenIdsCollide()
    {
        var source = SaveWorkbookWithChart(ChartType.Histogram);
        using (var archive = new ZipArchive(source, ZipArchiveMode.Update, leaveOpen: true))
        {
            RepointChartExStyleColorSidecarsToCustomParts(archive);
        }

        source.Position = 0;
        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);
        workbook.GetSheetAt(0).Charts.Should().ContainSingle().Subject.Title = "Edited ChartEx Sidecars";

        var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        saved.Position = 0;

        using var savedArchive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: false);
        var chartRelationshipsXml = XlsxPackageTestFixtures.LoadPackageXml(
            savedArchive,
            "xl/charts/_rels/chart1.xml.rels",
            "xl/charts/_rels/chart1.xml.rels");
        var chartRelationships = chartRelationshipsXml.Root!
            .Elements(PackageRelNs + "Relationship")
            .ToList();

        chartRelationships
            .Where(element => element.Attribute("Type")?.Value == ChartExStyleRelationshipType)
            .Should()
            .ContainSingle()
            .Which.Attribute("Target")!.Value.Should().Be("customStyle1.xml");
        chartRelationships
            .Where(element => element.Attribute("Type")?.Value == ChartExColorStyleRelationshipType)
            .Should()
            .ContainSingle()
            .Which.Attribute("Target")!.Value.Should().Be("customColors1.xml");
        chartRelationships
            .Select(element => element.Attribute("Id")?.Value)
            .OfType<string>()
            .Should()
            .OnlyHaveUniqueItems();

        XlsxPackageTestFixtures.LoadPackageXml(savedArchive, "xl/charts/customStyle1.xml")
            .Root!.Attribute("id")!.Value.Should().Be("901");
        XlsxPackageTestFixtures.LoadPackageXml(savedArchive, "xl/charts/customColors1.xml")
            .Root!.Attribute("id")!.Value.Should().Be("77");
    }

    private static void RepointChartExStyleColorSidecarsToCustomParts(ZipArchive archive)
    {
        const string customStylePart = "xl/charts/customStyle1.xml";
        const string customColorsPart = "xl/charts/customColors1.xml";

        var styleXml = XlsxPackageTestFixtures.LoadPackageXml(archive, "xl/charts/style1.xml");
        styleXml.Root!.SetAttributeValue("id", "901");
        ReplacePackageXml(archive, customStylePart, styleXml);

        var colorsXml = XlsxPackageTestFixtures.LoadPackageXml(archive, "xl/charts/colors1.xml");
        colorsXml.Root!.SetAttributeValue("id", "77");
        ReplacePackageXml(archive, customColorsPart, colorsXml);

        var relationshipsXml = XlsxPackageTestFixtures.LoadPackageXml(
            archive,
            "xl/charts/_rels/chart1.xml.rels",
            "xl/charts/_rels/chart1.xml.rels");
        foreach (var relationship in relationshipsXml.Root!.Elements(PackageRelNs + "Relationship"))
        {
            if (relationship.Attribute("Type")?.Value == ChartExStyleRelationshipType)
            {
                relationship.SetAttributeValue("Id", "rId1");
                relationship.SetAttributeValue("Target", "customStyle1.xml");
            }
            else if (relationship.Attribute("Type")?.Value == ChartExColorStyleRelationshipType)
            {
                relationship.SetAttributeValue("Id", "rId2");
                relationship.SetAttributeValue("Target", "customColors1.xml");
            }
        }

        ReplacePackageXml(archive, "xl/charts/_rels/chart1.xml.rels", relationshipsXml);
        EnsureContentTypeOverride(archive, $"/{customStylePart}", ChartExStyleContentType);
        EnsureContentTypeOverride(archive, $"/{customColorsPart}", ChartExColorStyleContentType);
    }

    private static void EnsureContentTypeOverride(ZipArchive archive, string partName, string contentType)
    {
        var contentTypesXml = XlsxPackageTestFixtures.LoadPackageXml(archive, "[Content_Types].xml");
        contentTypesXml.Root!
            .Elements(ContentTypesNs + "Override")
            .Where(element => element.Attribute("PartName")?.Value == partName)
            .Remove();
        contentTypesXml.Root!.Add(new XElement(
            ContentTypesNs + "Override",
            new XAttribute("PartName", partName),
            new XAttribute("ContentType", contentType)));
        ReplacePackageXml(archive, "[Content_Types].xml", contentTypesXml);
    }

}
