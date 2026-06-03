using FluentAssertions;

namespace FreeX.Core.IO.Tests;

public partial class XlsxCorpusRunnerTests
{
    [Fact]
    public void GeneratedSupportedChartRows_CreateWorkbooksWithCharts()
    {
        var rows = ReadGeneratedSupportedRowsWithTag("charts");

        rows.Should().NotBeEmpty("generated supported-pass chart rows should stay backed by deterministic fixtures");
        rows.Should().OnlyContain(row => XlsxCorpusFixtureFactory.CanCreate(row.Id));

        foreach (var row in rows)
        {
            var workbook = XlsxCorpusFixtureFactory.Create(row.Id);

            workbook.Sheets
                .SelectMany(sheet => sheet.Charts)
                .Should()
                .NotBeEmpty(row.Id);
        }
    }

    [Fact]
    public void GeneratedSupportedNamedRangeRows_CreateWorkbooksWithNamedRanges()
    {
        var rows = ReadGeneratedSupportedRowsWithTag("named-ranges");

        rows.Should().NotBeEmpty("generated supported-pass named range rows should stay backed by deterministic fixtures");
        rows.Should().OnlyContain(row => XlsxCorpusFixtureFactory.CanCreate(row.Id));

        foreach (var row in rows)
        {
            var workbook = XlsxCorpusFixtureFactory.Create(row.Id);

            workbook.NamedRanges.Should().NotBeEmpty(row.Id);
        }
    }

    private static ManifestRow[] ReadGeneratedSupportedRowsWithTag(string tag)
    {
        return ReadManifestRows()
            .Where(row => row.SourceType == "generated")
            .Where(row => row.ExpectedStatus == "supported-pass")
            .Where(row => HasFeatureTag(row, tag))
            .ToArray();
    }

    private static bool HasFeatureTag(ManifestRow row, string tag)
    {
        return row.FeatureTags
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Contains(tag, StringComparer.Ordinal);
    }
}
