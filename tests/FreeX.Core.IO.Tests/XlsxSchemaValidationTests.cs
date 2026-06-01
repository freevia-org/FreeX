using System.IO;
using System.Linq;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using FluentAssertions;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Guards that FreeX's XLSX output is schema-valid OOXML so Microsoft Excel will open it. A
/// schema-invalid theme part (incomplete fmtScheme / fontScheme) previously made Excel reject every
/// FreeX-authored workbook; this validates the saved package with the Open XML SDK validator.
/// </summary>
public sealed class XlsxSchemaValidationTests
{
    [Fact]
    public void XlsxAdapter_Save_ProducesSchemaValidWorkbook()
    {
        var workbook = new Workbook("SchemaValid");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Hello"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new NumberValue(42));

        var schemaErrors = SchemaErrors(workbook);
        schemaErrors.Should().BeEmpty();
    }

    [Fact]
    public void XlsxAdapter_Save_ProducesSchemaValidThemePart()
    {
        var workbook = new Workbook("ThemeValid");
        workbook.AddSheet("Data");

        // The theme part (xl/theme/theme1.xml) is the part that previously broke Excel.
        var themeErrors = SchemaErrors(workbook).Where(e => e.Contains("a:theme", System.StringComparison.Ordinal)).ToList();
        themeErrors.Should().BeEmpty();
    }

    [Theory]
    // Classic (c:) charts — a schema-valid title/axis text body (a:bodyPr) is required for Excel to open them.
    [InlineData(ChartType.Column)]
    [InlineData(ChartType.Bar)]
    [InlineData(ChartType.Line)]
    [InlineData(ChartType.Pie)]
    [InlineData(ChartType.Area)]
    [InlineData(ChartType.Scatter)]
    // Modern (cx:) chartEx families.
    [InlineData(ChartType.Histogram)]
    [InlineData(ChartType.Waterfall)]
    [InlineData(ChartType.Treemap)]
    [InlineData(ChartType.Sunburst)]
    [InlineData(ChartType.Pareto)]
    [InlineData(ChartType.Funnel)]
    [InlineData(ChartType.BoxAndWhisker)]
    public void XlsxAdapter_Save_ProducesSchemaValidChartWorkbook(ChartType chartType)
    {
        var workbook = new Workbook("ChartExValid");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Category"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Amount"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("A"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("B"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new TextValue("C"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 2), new NumberValue(30));
        sheet.Charts.Add(new ChartModel
        {
            Type = chartType,
            DataRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 2)),
            Title = chartType.ToString(),
            ShowLegend = true,
            LegendPosition = ChartLegendPosition.Bottom,
        });

        SchemaErrors(workbook).Should().BeEmpty();
    }

    private static System.Collections.Generic.List<string> SchemaErrors(Workbook workbook)
    {
        var stream = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, stream);
        stream.Position = 0;

        using var document = SpreadsheetDocument.Open(stream, false);
        var validator = new OpenXmlValidator(FileFormatVersions.Microsoft365);
        return validator.Validate(document)
            .Where(error => error.ErrorType == ValidationErrorType.Schema)
            .Select(error => $"{error.Description} @ {error.Path?.XPath}")
            .ToList();
    }
}
