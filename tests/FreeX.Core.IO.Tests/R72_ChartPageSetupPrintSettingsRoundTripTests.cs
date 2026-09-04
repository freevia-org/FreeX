using System.IO;
using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R72-io-print-setup-4-1 / R72-io-print-setup-4-2: the chart/chartsheet &lt;c:pageSetup&gt; writer
/// never emitted <c>usePrinterDefaults</c>, so a chart saved with an explicit
/// <c>usePrinterDefaults="0"</c> (custom paper size/orientation) silently reverted to the OOXML
/// default of "printer defaults" (true) on save -- the value was faithfully read
/// (<see cref="ChartPageSetupModel.UsePrinterDefaults"/> via
/// <c>XlsxChartMetadataReader.ReadPrintSettings</c>) but dropped on write. Likewise
/// <c>firstPageNumber</c> was written unconditionally even though OOXML only honors it when the
/// sibling <c>useFirstPageNumber</c> flag is true (mirroring the worksheet-level fix in
/// <c>XlsxFileAdapter.cs</c> around the "First page number" checkbox) -- <see cref="ChartPageSetupModel"/>
/// had no flag to track that and the writer never emitted the attribute.
/// </summary>
public sealed class R72_ChartPageSetupPrintSettingsRoundTripTests
{
    private static readonly XNamespace ChartNs = "http://schemas.openxmlformats.org/drawingml/2006/chart";

    [Fact]
    public void XlsxAdapter_SaveLoad_ExplicitUsePrinterDefaultsFalse_SurvivesRoundTrip()
    {
        var workbook = CreateColumnChartWorkbook(chart =>
        {
            chart.PrintSettings = new ChartPrintSettingsModel
            {
                PageSetup = new ChartPageSetupModel
                {
                    PaperSize = "9",
                    Orientation = "landscape",
                    UsePrinterDefaults = false
                }
            };
        });

        var saved = SaveToStream(workbook);
        var chartXml = ReadChartXml(saved);

        var pageSetup = chartXml.Descendants(ChartNs + "pageSetup").Should().ContainSingle().Subject;
        pageSetup.Attribute("usePrinterDefaults")!.Value.Should().Be(
            "0", "an explicit usePrinterDefaults=\"0\" (custom paper size/orientation) must not "
            + "silently flip back to the printer-defaults-true OOXML default on save");

        saved.Position = 0;
        var loadedChart = new XlsxFileAdapter().Load(saved).GetSheetAt(0).Charts.Should().ContainSingle().Subject;
        loadedChart.PrintSettings?.PageSetup?.UsePrinterDefaults.Should().BeFalse(
            "reloading a saved usePrinterDefaults=\"0\" chart pageSetup must still report false");
    }

    [Fact]
    public void XlsxAdapter_SaveLoad_NoExplicitUsePrinterDefaults_OmitsAttribute()
    {
        // Sibling no-regression case: when the model never set UsePrinterDefaults, the writer
        // must not invent an attribute value (leaving it to the OOXML default of true-when-omitted).
        var workbook = CreateColumnChartWorkbook(chart =>
        {
            chart.PrintSettings = new ChartPrintSettingsModel
            {
                PageSetup = new ChartPageSetupModel
                {
                    PaperSize = "9",
                    Orientation = "landscape"
                }
            };
        });

        var saved = SaveToStream(workbook);
        var chartXml = ReadChartXml(saved);

        var pageSetup = chartXml.Descendants(ChartNs + "pageSetup").Should().ContainSingle().Subject;
        pageSetup.Attribute("usePrinterDefaults").Should().BeNull(
            "an unset UsePrinterDefaults must not spuriously emit usePrinterDefaults=\"1\" or \"0\"");
    }

    [Fact]
    public void XlsxAdapter_SaveLoad_FirstPageNumberWithUseFlagTrue_SurvivesRoundTrip()
    {
        var workbook = CreateColumnChartWorkbook(chart =>
        {
            chart.PrintSettings = new ChartPrintSettingsModel
            {
                PageSetup = new ChartPageSetupModel
                {
                    FirstPageNumber = 10,
                    UseFirstPageNumber = true
                }
            };
        });

        var saved = SaveToStream(workbook);
        var chartXml = ReadChartXml(saved);

        var pageSetup = chartXml.Descendants(ChartNs + "pageSetup").Should().ContainSingle().Subject;
        pageSetup.Attribute("firstPageNumber")!.Value.Should().Be("10");
        pageSetup.Attribute("useFirstPageNumber")!.Value.Should().Be(
            "1", "an explicit useFirstPageNumber=true flag must be preserved so Excel actually "
            + "honors the custom firstPageNumber instead of treating it as inert leftover data");

        saved.Position = 0;
        var loadedChart = new XlsxFileAdapter().Load(saved).GetSheetAt(0).Charts.Should().ContainSingle().Subject;
        loadedChart.PrintSettings?.PageSetup?.FirstPageNumber.Should().Be(10);
        loadedChart.PrintSettings?.PageSetup?.UseFirstPageNumber.Should().BeTrue();
    }

    [Fact]
    public void XlsxAdapter_SaveLoad_FirstPageNumberWithoutUseFlag_DoesNotSpuriouslyActivate()
    {
        // Sibling no-regression case: firstPageNumber present but the "activate" flag was never
        // set on the model -- the writer must not emit useFirstPageNumber="1" out of thin air.
        var workbook = CreateColumnChartWorkbook(chart =>
        {
            chart.PrintSettings = new ChartPrintSettingsModel
            {
                PageSetup = new ChartPageSetupModel
                {
                    FirstPageNumber = 10
                }
            };
        });

        var saved = SaveToStream(workbook);
        var chartXml = ReadChartXml(saved);

        var pageSetup = chartXml.Descendants(ChartNs + "pageSetup").Should().ContainSingle().Subject;
        pageSetup.Attribute("firstPageNumber")!.Value.Should().Be("10");
        pageSetup.Attribute("useFirstPageNumber").Should().BeNull(
            "firstPageNumber with no explicit UseFirstPageNumber flag must not spuriously activate it");

        saved.Position = 0;
        var loadedChart = new XlsxFileAdapter().Load(saved).GetSheetAt(0).Charts.Should().ContainSingle().Subject;
        loadedChart.PrintSettings?.PageSetup?.FirstPageNumber.Should().Be(10);
        loadedChart.PrintSettings?.PageSetup?.UseFirstPageNumber.Should().NotBe(true);
    }

    private static Workbook CreateColumnChartWorkbook(System.Action<ChartModel> configure)
    {
        var workbook = new Workbook("ChartPageSetupPrintSettings");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Month"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Sales"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Jan"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("Feb"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new TextValue("Mar"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 2), new NumberValue(30));

        var chart = new ChartModel
        {
            Type = ChartType.Column,
            Title = "Sales",
            DataRange = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 4, 2)),
        };
        configure(chart);
        sheet.Charts.Add(chart);
        return workbook;
    }

    private static MemoryStream SaveToStream(Workbook workbook)
    {
        var stream = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, stream);
        stream.Position = 0;
        return stream;
    }

    private static XDocument ReadChartXml(MemoryStream saved)
    {
        saved.Position = 0;
        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true);
        return XlsxPackageTestFixtures.LoadPackageXml(
            archive,
            "xl/charts/chart1.xml",
            "http://schemas.openxmlformats.org/drawingml/2006/chart");
    }
}
