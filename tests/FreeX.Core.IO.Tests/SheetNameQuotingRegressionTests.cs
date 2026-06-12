using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Regression tests that guard against the three-way divergence in sheet-name quoting that
/// caused XlsxSparklineMapper and XlsxChartXmlWriter.Metadata to emit unquoted references
/// for sheet names such as "Q1-Q2", making Excel parse them as subtraction expressions.
/// </summary>
public sealed class SheetNameQuotingRegressionTests
{
    // -----------------------------------------------------------------------
    // Sparkline writer
    // -----------------------------------------------------------------------

    [Fact]
    public void SparklineMapper_SheetNameWithHyphen_EmitsQuotedReference()
    {
        var workbook = new Workbook("SparklineQuoting");
        var sheet = workbook.AddSheet("Q1-Q2");

        for (uint col = 1; col <= 3; col++)
            sheet.SetCell(new CellAddress(sheet.Id, 1, col), new NumberValue(col));

        sheet.Sparklines.Add(new SparklineModel
        {
            DataRange = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 1, 3)),
            Location = new CellAddress(sheet.Id, 1, 4),
            Kind = SparklineKind.Line
        });

        using var stream = XlsxPackageTestHelper.SaveWorkbook(workbook);
        var formulaText = ReadSparklineFormulaText(stream);

        // Must start with the quoted sheet name to be a valid Excel external reference.
        formulaText.Should().StartWith("'Q1-Q2'!");
    }

    [Fact]
    public void SparklineMapper_SimpleSheetName_EmitsUnquotedReference()
    {
        var workbook = new Workbook("SparklineSimple");
        var sheet = workbook.AddSheet("Sheet1");

        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(42));

        sheet.Sparklines.Add(new SparklineModel
        {
            DataRange = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 1, 1)),
            Location = new CellAddress(sheet.Id, 1, 2),
            Kind = SparklineKind.Line
        });

        using var stream = XlsxPackageTestHelper.SaveWorkbook(workbook);
        var formulaText = ReadSparklineFormulaText(stream);

        // Simple name — no quotes needed.
        formulaText.Should().StartWith("Sheet1!");
    }

    // -----------------------------------------------------------------------
    // Chart pivot-source writer
    // -----------------------------------------------------------------------

    [Fact]
    public void ChartMetadataWriter_PivotSourceSheetNameWithHyphen_EmitsQuotedReference()
    {
        var workbook = new Workbook("ChartPivotQuoting");
        var sheet = workbook.AddSheet("Q1-Q2");

        var chart = new ChartModel
        {
            Type = ChartType.Bar,
            DataRange = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 3, 2)),
            IsPivotChart = true,
            PivotTableName = "PivotTable1",
            PivotSourceSheetName = "Q1-Q2"
        };
        sheet.Charts.Add(chart);

        using var stream = XlsxPackageTestHelper.SaveWorkbook(workbook);
        var pivotSourceName = ReadPivotSourceName(stream);

        pivotSourceName.Should().StartWith("'Q1-Q2'!");
    }

    // -----------------------------------------------------------------------
    // Chart series range writer — keyword and cell-address sheet names
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData("TRUE")]
    [InlineData("FALSE")]
    [InlineData("A1")]
    [InlineData("B2")]
    public void ChartSeriesWriter_KeywordOrCellAddressSheetName_EmitsQuotedRange(string sheetName)
    {
        // A sheet named TRUE, FALSE, A1, or B2 must be quoted in chart XML data references.
        // Previously FormatSheetRange always forced quotes but used a bare apostrophe-escape
        // instead of routing through SheetNameFormatter — the two implementations could drift.
        // This test guards that the canonical QuoteIfNeeded path is used and produces correct output.
        var workbook = new Workbook("ChartKeywordSheetQuoting");
        var sheet = workbook.AddSheet(sheetName);

        for (uint row = 1; row <= 3; row++)
        {
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new NumberValue(row));
            sheet.SetCell(new CellAddress(sheet.Id, row, 2), new NumberValue(row * 2));
        }

        var chart = new ChartModel
        {
            Type = ChartType.Bar,
            DataRange = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 3, 2))
        };
        sheet.Charts.Add(chart);

        using var stream = XlsxPackageTestHelper.SaveWorkbook(workbook);
        var seriesFormulas = ReadChartSeriesFormulas(stream);

        // Every series formula must reference the sheet with quotes.
        seriesFormulas.Should().NotBeEmpty();
        seriesFormulas.Should().OnlyContain(f => f.StartsWith($"'{sheetName}'!", StringComparison.Ordinal));
    }

    [Fact]
    public void ChartSeriesWriter_SimpleSheetName_EmitsUnquotedRange()
    {
        var workbook = new Workbook("ChartSimpleSheetRange");
        var sheet = workbook.AddSheet("Sheet1");

        for (uint row = 1; row <= 3; row++)
        {
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new NumberValue(row));
            sheet.SetCell(new CellAddress(sheet.Id, row, 2), new NumberValue(row * 2));
        }

        var chart = new ChartModel
        {
            Type = ChartType.Bar,
            DataRange = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 3, 2))
        };
        sheet.Charts.Add(chart);

        using var stream = XlsxPackageTestHelper.SaveWorkbook(workbook);
        var seriesFormulas = ReadChartSeriesFormulas(stream);

        seriesFormulas.Should().NotBeEmpty();
        seriesFormulas.Should().OnlyContain(f => f.StartsWith("Sheet1!", StringComparison.Ordinal));
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static IReadOnlyList<string> ReadChartSeriesFormulas(MemoryStream stream)
    {
        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        var chartEntry = archive.Entries
            .FirstOrDefault(e => e.FullName.StartsWith("xl/charts/chart", StringComparison.OrdinalIgnoreCase) &&
                                 e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase));
        if (chartEntry is null)
            return [];

        var chartXml = LoadPackageXml(chartEntry);
        return chartXml.Descendants()
            .Where(e => string.Equals(e.Name.LocalName, "f", StringComparison.OrdinalIgnoreCase))
            .Select(e => e.Value)
            .Where(v => !string.IsNullOrEmpty(v))
            .ToList();
    }

    private static string ReadSparklineFormulaText(MemoryStream stream)
    {
        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        // Sparklines are emitted to sheet1 (index 0).
        var worksheetXml = LoadPackageXml(archive, "xl/worksheets/sheet1.xml");
        return worksheetXml.Descendants()
            .Where(e => string.Equals(e.Name.LocalName, "f", StringComparison.OrdinalIgnoreCase))
            .Select(e => e.Value)
            .First();
    }

    private static string ReadPivotSourceName(MemoryStream stream)
    {
        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        // Charts are in xl/charts/chart*.xml.
        var chartEntry = archive.Entries
            .FirstOrDefault(e => e.FullName.StartsWith("xl/charts/chart", StringComparison.OrdinalIgnoreCase));
        if (chartEntry is null)
            return string.Empty;

        var chartXml = LoadPackageXml(chartEntry);
        return chartXml.Descendants()
            .Where(e => string.Equals(e.Name.LocalName, "pivotSource", StringComparison.OrdinalIgnoreCase))
            .SelectMany(ps => ps.Elements())
            .Where(e => string.Equals(e.Name.LocalName, "name", StringComparison.OrdinalIgnoreCase))
            .Select(e => e.Value)
            .FirstOrDefault() ?? string.Empty;
    }

    private static XDocument LoadPackageXml(ZipArchive archive, string entryName)
    {
        var entry = archive.GetEntry(entryName)
                    ?? throw new InvalidOperationException($"Entry not found: {entryName}");
        return LoadPackageXml(entry);
    }

    private static XDocument LoadPackageXml(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        return XDocument.Load(stream);
    }
}
