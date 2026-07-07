using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using FluentAssertions;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Cleanup batch MED14 — round-10 MED/LOW findings.
/// </summary>
public sealed class FreeXCleanupMED14Tests
{
    private static readonly XNamespace ChartNs = "http://schemas.openxmlformats.org/drawingml/2006/chart";

    // P86 (MED): data labels enabled on a Surface/3-D Surface chart previously wrote a schema-invalid
    // c:dLbls child into c:surfaceChart/c:surface3DChart (CT_SurfaceChart/CT_Surface3DChart have no
    // dLbls member per ECMA-376). Verify the saved package has no dLbls under either element and is
    // schema-valid end to end.
    [Theory]
    [InlineData(ChartType.Surface, "surfaceChart")]
    [InlineData(ChartType.ThreeDSurface, "surface3DChart")]
    public void XlsxAdapter_Save_SurfaceChartWithDataLabels_OmitsSchemaInvalidDLbls(
        ChartType chartType,
        string expectedPlotElementName)
    {
        var workbook = new Workbook("SurfaceChartDataLabels");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("A"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("B"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(15));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(25));
        sheet.Charts.Add(new ChartModel
        {
            Type = chartType,
            DataRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 2)),
            ShowDataLabels = true,
        });

        using var package = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, package);
        package.Position = 0;

        using (var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true))
        {
            var chartXml = XlsxPackageTestFixtures.LoadPackageXml(
                archive,
                "xl/charts/chart1.xml",
                "the XLSX package should contain xl/charts/chart1.xml");

            var plotChart = chartXml.Descendants(ChartNs + expectedPlotElementName).Should().ContainSingle().Subject;
            plotChart.Elements(ChartNs + "dLbls").Should().BeEmpty(
                $"CT_{expectedPlotElementName[0].ToString().ToUpperInvariant()}{expectedPlotElementName[1..]} has no dLbls member in the OOXML schema");
        }

        package.Position = 0;
        using var document = SpreadsheetDocument.Open(package, false);
        var validator = new OpenXmlValidator(FileFormatVersions.Microsoft365);
        var schemaErrors = validator.Validate(document)
            .Where(error => error.ErrorType == ValidationErrorType.Schema)
            .Select(error => $"{error.Description} @ {error.Path?.XPath}")
            .ToList();
        schemaErrors.Should().BeEmpty("the chart part must remain schema-valid with data labels enabled");
    }
}
