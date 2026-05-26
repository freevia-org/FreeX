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
        var localPrivateCount = manifestRows.Count(row => row.SourceType == "local-private");
        var regressionCount = manifestRows.Count(row => row.SourceType == "regression");

        report.Should().Contain($"Total manifest rows: {manifestRows.Count}.");
        report.Should().Contain($"| Generated deterministic supported-pass fixtures | {generatedSupportedCount} |");
        report.Should().Contain($"| Generated deterministic supported-metadata-pass fixtures | {generatedMetadataCount} |");
        report.Should().Contain($"| Generated deterministic known-gap fixtures | {generatedKnownGapCount} |");
        report.Should().Contain($"| Public redistributed workbooks | {publicCount} |");
        report.Should().Contain($"| Local private workbooks | {localPrivateCount} |");
        report.Should().Contain($"| Regression workbooks | {regressionCount} |");
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
        report.Should().Contain("| Slicers, timelines, external links, printer settings, calc chains, custom XML |");
    }

    [Fact]
    public void OutstandingBuild_StatesCurrentCorpusManifestCounts()
    {
        var manifestRows = ReadManifestRows();
        var outstandingBuild = File.ReadAllText(FindWorkspaceFile("docs", "OUTSTANDING_BUILD.md"));
        var nextPhasesPlan = File.ReadAllText(FindWorkspaceFile("docs", "NEXT_PHASES_PLAN.md"));
        var generatedCount = manifestRows.Count(row => row.SourceType == "generated");
        var publicCount = manifestRows.Count(row => row.SourceType == "public");
        var localPrivateCount = manifestRows.Count(row => row.SourceType == "local-private");
        var regressionCount = manifestRows.Count(row => row.SourceType == "regression");

        outstandingBuild.Should().Contain(
            $"Current manifest has {manifestRows.Count} rows: {generatedCount} generated rows, {publicCount} public Tealeg rows, {localPrivateCount} optional local-private rows, and {regressionCount} regression formula-cache workbooks.");
        nextPhasesPlan.Should().Contain($"current {manifestRows.Count}-row manifest baseline");
        nextPhasesPlan.Should().NotContain("prior 90-row manifest baseline");
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
