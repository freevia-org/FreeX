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
        var report = File.ReadAllText(FindWorkspaceFile("docs", "XLSX_CORPUS_REPORT.md"));

        report.Should().Contain("## Pass Rate Summary");
        report.Should().Contain("| Workbook set | Executed | Passing | Pass rate |");
        report.Should().Contain("| Generated supported-pass workbooks | 26 | 26 | 100% |");
        report.Should().Contain("| Public redistributed workbooks | 25 | 25 | 100% |");
        report.Should().Contain("| Feature bucket | Evidence | Pass rate |");
        report.Should().Contain("| PivotTables, pivot caches, and PivotChart binding |");
        report.Should().Contain("| Slicers, timelines, external links, printer settings, custom XML |");
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
}
