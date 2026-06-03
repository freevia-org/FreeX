using FreeX.Core.IO;
using FreeX.Core.Model;
using FluentAssertions;
using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;
using DocumentFormat.OpenXml.Packaging;

namespace FreeX.Core.IO.Tests;

public partial class XlsxCorpusRunnerTests
{
    [Fact]
    public void GeneratedCorpusRows_IncludeSurfaceChartCoverage()
    {
        var rows = ReadManifestRows()
            .Where(row => row.SourceType == "generated")
            .Where(row => row.ExpectedStatus == "supported-pass")
            .Where(row => row.FeatureTags.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Contains("surface-charts"))
            .ToArray();

        rows.Should().ContainSingle("surface charts are now a supported native chart family and need deterministic corpus coverage");
        rows.Should().OnlyContain(row => XlsxCorpusFixtureFactory.CanCreate(row.Id));

        var workbook = XlsxCorpusFixtureFactory.Create(rows[0].Id);
        workbook.Sheets
            .SelectMany(sheet => sheet.Charts)
            .Select(chart => chart.Type)
            .Should().Contain([ChartType.Surface, ChartType.ThreeDSurface]);
    }

    [Fact]
    public void ChartSummary_IncludesProtectionAndPrintSettings()
    {
        var sheetId = SheetId.New();
        var baseline = new ChartModel
        {
            Type = ChartType.Column,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 4, 2))
        };
        var withNativeMetadata = new ChartModel
        {
            Type = baseline.Type,
            DataRange = baseline.DataRange,
            Protection = new ChartProtectionModel { ChartObject = true, Data = false, Formatting = true, Selection = false, UserInterface = true },
            PrintSettings = new ChartPrintSettingsModel
            {
                PageMargins = new ChartPageMarginsModel { Left = 0.7, Right = 0.7, Top = 0.75, Bottom = 0.75, Header = 0.3, Footer = 0.3 },
                PageSetup = new ChartPageSetupModel { PaperSize = "9", Orientation = "portrait", Copies = 2, BlackAndWhite = true, Draft = false }
            }
        };

        CaptureChartSummary(withNativeMetadata).Should().NotBe(CaptureChartSummary(baseline));
    }


    [Fact]
    public void GeneratedChartSeriesCountPackage_RetainsFiveSeriesInChartXml()
    {
        using var package = XlsxCorpusFixtureFactory.CreateKnownGapRetentionPackage("generated-chart-series-count-003");
        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);

        var chartEntry = archive.Entries.FirstOrDefault(e => e.FullName.StartsWith("xl/charts/", StringComparison.OrdinalIgnoreCase) && e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase));
        chartEntry.Should().NotBeNull("generated-chart-series-count-003 must contain at least one chart part under xl/charts/");

        XDocument chartXml;
        using (var stream = chartEntry!.Open())
            chartXml = XDocument.Load(stream);

        XNamespace chartNs = "http://schemas.openxmlformats.org/drawingml/2006/chart";
        var serElements = chartXml.Descendants()
            .Where(e => e.Name.LocalName == "ser" && e.Name.Namespace == chartNs)
            .ToArray();
        serElements.Should().HaveCount(5, "generated-chart-series-count-003 embeds five <c:ser> elements in its chart XML");
    }


    [Fact]
    public void GeneratedUnsupportedChartFixture_UsesCurrentlyUnsupportedChartFamily()
    {
        using var package = XlsxCorpusFixtureFactory.CreateKnownGapPackage("generated-unsupported-chart-001");
        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: false);

        var chartXml = LoadPackageXml(archive.GetEntry("xl/charts/chart1.xml")!).ToString();

        chartXml.Should().Contain("mapChart");
        chartXml.Should().NotContain("treemapChart", "treemap charts have a renderable chartEx writer path now and should not anchor the unsupported-chart fixture");
        chartXml.Should().NotContain("radarChart", "radar charts are supported now and should not anchor the unsupported-chart fixture");
        chartXml.Should().NotContain("surfaceChart", "surface charts are supported now and should not anchor the unsupported-chart fixture");
    }

}
