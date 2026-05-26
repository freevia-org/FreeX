using FluentAssertions;

namespace Freexcel.Core.IO.Tests;

public class XlsxCorpusScaffoldTests
{
    private static readonly HashSet<string> AllowedStatuses =
    [
        "supported-pass",
        "supported-known-gap",
        "supported-pivot-metadata-pass",
        "supported-metadata-pass",
        "public-pass",
        "excluded-warning-pass",
        "corrupt-or-invalid"
    ];

    private static readonly string[] ExpectedManifestHeader =
    [
        "id",
        "path",
        "source_type",
        "source_url",
        "retrieved_on",
        "license",
        "feature_tags",
        "expected_warnings",
        "expected_status",
        "notes"
    ];

    [Fact]
    public void CorpusManifest_UsesDocumentedSchemaAndHasStarterGeneratedRows()
    {
        var manifestPath = FindWorkspaceFile("test-corpus", "manifest.csv");
        var rows = File.ReadAllLines(manifestPath)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToArray();

        rows.Should().HaveCountGreaterThanOrEqualTo(11, "the corpus starts with a header plus 10 generated fixture rows");
        rows[0].Split(',').Should().Equal(ExpectedManifestHeader);

        var generatedRows = rows.Skip(1)
            .Select(line => line.Split(','))
            .Where(columns => columns.Length == ExpectedManifestHeader.Length)
            .Where(columns => columns[2] == "generated")
            .ToArray();

        generatedRows.Should().HaveCountGreaterThanOrEqualTo(10);
        generatedRows.Should().OnlyContain(columns => AllowedStatuses.Contains(columns[8]));
    }

    [Fact]
    public void CorpusPrivateFolder_IsGitIgnored()
    {
        var gitignore = File.ReadAllText(FindWorkspaceFile(".gitignore"));

        gitignore.Should().Contain("test-corpus/local-private/");
    }

    [Fact]
    public void CorpusReadme_StatesPrivateAndRedistributionPolicy()
    {
        var readme = File.ReadAllText(FindWorkspaceFile("test-corpus", "README.md"));

        readme.Should().Contain("local-private");
        readme.Should().Contain("must not be committed");
        readme.Should().Contain("redistribution");
    }

    [Fact]
    public void CorpusReport_PublishesWorkbookAndFeatureBucketPassRates()
    {
        var manifestRows = ReadManifestRows();
        var report = File.ReadAllText(FindWorkspaceFile("docs", "XLSX_CORPUS_REPORT.md"));
        var generatedSupportedCount = manifestRows.Count(row => row.SourceType == "generated" && row.ExpectedStatus == "supported-pass");
        var generatedMetadataCount = manifestRows.Count(row => row.SourceType == "generated" && row.ExpectedStatus == "supported-metadata-pass");
        var generatedKnownGapCount = manifestRows.Count(row => row.SourceType == "generated" && row.ExpectedStatus == "supported-known-gap");
        var publicCount = manifestRows.Count(row => row.SourceType == "public");
        var regressionCount = manifestRows.Count(row => row.SourceType == "regression");

        report.Should().Contain("## Pass Rate Summary");
        report.Should().Contain("| Workbook set | Executed | Passing | Pass rate |");
        report.Should().Contain($"| Generated supported-pass workbooks | {generatedSupportedCount} | {generatedSupportedCount} | 100% |");
        report.Should().Contain($"| Generated supported-metadata-pass workbooks | {generatedMetadataCount} | {generatedMetadataCount} | 100% |");
        report.Should().Contain($"| Generated known-gap warning workbooks | {generatedKnownGapCount} | {generatedKnownGapCount} | 100% |");
        report.Should().Contain($"| Generated known-gap retention workbooks | {generatedKnownGapCount} | {generatedKnownGapCount} | 100% |");
        report.Should().Contain($"| Public redistributed workbooks | {publicCount} | {publicCount} | 100% |");
        report.Should().Contain($"| Regression cached-result workbooks | {regressionCount} | {regressionCount} | 100% |");
        report.Should().Contain("| Feature bucket | Evidence | Pass rate |");
        report.Should().Contain("| PivotTables, pivot caches, and PivotChart binding |");
        report.Should().Contain("| Slicers, timelines, external links, printer settings, custom XML |");
    }

    private static IReadOnlyList<ManifestRow> ReadManifestRows()
    {
        var manifestPath = FindWorkspaceFile("test-corpus", "manifest.csv");
        return File.ReadAllLines(manifestPath)
            .Skip(1)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(ParseManifestRow)
            .ToArray();
    }

    private static ManifestRow ParseManifestRow(string line)
    {
        var columns = line.Split(',');
        columns.Should().HaveCount(ExpectedManifestHeader.Length);
        return new ManifestRow(columns[2], columns[8]);
    }

    private static string FindWorkspaceFile(params string[] relativeParts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(new[] { directory.FullName }.Concat(relativeParts).ToArray());
            if (File.Exists(candidate))
                return candidate;

            directory = directory.Parent;
        }

        throw new FileNotFoundException("Could not locate workspace file.", Path.Combine(relativeParts));
    }

    private sealed record ManifestRow(string SourceType, string ExpectedStatus);
}
