using FluentAssertions;

namespace FreeX.Core.IO.Tests;

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

    private static readonly string[] BidirectionalCoverageTags =
    [
        "basic-grid",
        "cached-results",
        "charts",
        "comments",
        "conditional-formatting",
        "data-validation",
        "external-links",
        "formatting",
        "formulas",
        "hyperlinks",
        "images",
        "merged-cells",
        "named-ranges",
        "page-setup",
        "pivot-caches",
        "pivottables",
        "protection",
        "shapes",
        "slicers",
        "sparklines",
        "styles",
        "tables",
        "threaded-comments",
        "timelines"
    ];

    [Fact]
    public void CorpusManifest_UsesDocumentedSchemaAndHasStarterGeneratedRows()
    {
        var rows = TestWorkspaceFiles.ReadWorkspaceLines("test-corpus", "manifest.csv")
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToArray();

        rows.Should().HaveCountGreaterThanOrEqualTo(11, "the corpus starts with a header plus 10 generated fixture rows");
        rows[0].Split(',').Should().Equal(ExpectedManifestHeader);

        var manifestRows = rows.Skip(1)
            .Select(line => line.Split(','))
            .Where(columns => columns.Length == ExpectedManifestHeader.Length)
            .ToArray();
        var generatedRows = manifestRows
            .Where(columns => columns[2] == "generated")
            .ToArray();

        generatedRows.Should().HaveCountGreaterThanOrEqualTo(10);
        manifestRows.Should().OnlyContain(columns => AllowedStatuses.Contains(columns[8]));
    }

    [Fact]
    public void CorpusPrivateFolder_IsGitIgnored()
    {
        var gitignore = TestWorkspaceFiles.ReadWorkspaceText(".gitignore");

        gitignore.Should().Contain("test-corpus/local-private/");
    }

    [Fact]
    public void CorpusReadme_StatesPrivateAndRedistributionPolicy()
    {
        var readme = TestWorkspaceFiles.ReadWorkspaceText("test-corpus", "README.md");

        readme.Should().Contain("local-private");
        readme.Should().Contain("must not be committed");
        readme.Should().Contain("redistribution");
    }









    [Fact]
    public void CorpusManifest_UsesPublicPassOnlyForPublicRows()
    {
        var manifestRows = ReadManifestRows();

        manifestRows
            .Where(row => row.ExpectedStatus == "public-pass")
            .Should()
            .OnlyContain(row => row.SourceType == "public", "`public-pass` is reserved for redistributed public corpus files");
    }

    [Fact]
    public void CorpusManifest_IncludesCoinToolLocalPrivateOpenPerformanceFixture()
    {
        var manifestRows = ReadManifestRows();

        manifestRows.Should().Contain(row =>
            row.Path == "local-private/COIN_Tool_v1_FULL_exampledata.xlsm" &&
            row.SourceType == "local-private" &&
            row.SourceUrl == "user-approved-local" &&
            row.License == "private-local" &&
            row.ExpectedWarnings == "excluded VBA macro disclosed" &&
            row.ExpectedStatus == "supported-known-gap" &&
            row.FeatureTags.Contains("performance", StringComparison.Ordinal) &&
            row.FeatureTags.Contains("open-load", StringComparison.Ordinal) &&
            row.Notes.Contains("large private workbook open/load performance profiling", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ExcelOpenSmokeCorpusResolver_TreatsXlsmRowsAsOpenableAndMissingPrivateFilesAsSkips()
    {
        var resolverSource = TestWorkspaceFiles.ReadWorkspaceText(
            "tools",
            "FreeX.ExcelOpenSmoke",
            "CorpusManifestResolver.cs");

        resolverSource.Should().Contain("IsSupportedOpenXmlSpreadsheetWorkbook(fullPath)");
        resolverSource.Should().Contain("extension.Equals(\".xlsm\", StringComparison.OrdinalIgnoreCase)");
        resolverSource.Should().Contain("skipped.Add(new CorpusManifestSkip(row, \"missing-file\", fullPath));");
        resolverSource.Should().Contain("Path.GetFullPath(Path.Combine(manifestDirectory, row.RelativePath))");
    }

    [Fact]
    public void CorpusManifest_PreservesBidirectionalCoverageForSupportedFeatureTags()
    {
        var supportedRows = ReadManifestRows()
            .Where(row =>
                row.ExpectedStatus is "supported-pass" or "supported-metadata-pass" or "supported-pivot-metadata-pass" or "public-pass")
            .ToArray();

        foreach (var tag in BidirectionalCoverageTags)
        {
            supportedRows
                .Where(row => row.SourceType == "generated")
                .Should()
                .Contain(row => HasFeatureTag(row, tag), $"{tag} should retain FreeX-generated coverage");
            supportedRows
                .Where(row => row.SourceType != "generated")
                .Should()
                .Contain(row => HasFeatureTag(row, tag), $"{tag} should retain independent Excel-authored, public, regression, or local-private coverage");
        }
    }

    [Fact]
    public void CorpusManifest_KnownGapRowsDeclareWarningsAndNotes()
    {
        var manifestRows = ReadManifestRows();

        manifestRows
            .Where(row => row.ExpectedStatus == "supported-known-gap")
            .Should()
            .OnlyContain(row => !string.IsNullOrWhiteSpace(row.ExpectedWarnings) && !string.IsNullOrWhiteSpace(row.Notes));
    }

    [Fact]
    public void CorpusManifest_NonPublicUnsupportedFeatureTagsDeclareWarningExpectations()
    {
        var manifestRows = ReadManifestRows();

        manifestRows
            .Where(row => row.SourceType != "public")
            .Select(row => new { Row = row, ExpectedWarnings = ExpectedWarningsFor(row) })
            .Where(entry => entry.ExpectedWarnings.Count > 0)
            .Should()
            .OnlyContain(
                entry =>
                    entry.Row.ExpectedStatus == "supported-known-gap" &&
                    entry.ExpectedWarnings.All(warning =>
                        entry.Row.ExpectedWarnings.Contains(warning, StringComparison.Ordinal)),
                "non-public corpus rows with unsupported feature tags should be known-gap rows with explicit warning text");
    }























    [Fact]
    public void RegressionFormulaCachedWorkbooks_AreAllRepresentedInCorpusManifest()
    {
        var manifestRows = ReadManifestRows();
        var corpusDirectory = TestWorkspaceFiles.FindWorkspaceFileDirectory("test-corpus", "manifest.csv");
        var regressionDirectory = Path.Combine(corpusDirectory, "regressions", "formula-cached");
        var regressionWorkbookPaths = Directory
            .EnumerateFiles(regressionDirectory, "*.xlsx", SearchOption.TopDirectoryOnly)
            .Select(path => Path.GetRelativePath(corpusDirectory, path).Replace(Path.DirectorySeparatorChar, '/'))
            .Order(StringComparer.Ordinal)
            .ToArray();
        var manifestRegressionPaths = manifestRows
            .Where(row => row.SourceType == "regression")
            .Select(row => row.Path)
            .Order(StringComparer.Ordinal)
            .ToArray();

        manifestRegressionPaths.Should().Equal(regressionWorkbookPaths);
    }


    private static IReadOnlyList<ManifestRow> ReadManifestRows()
    {
        return TestWorkspaceFiles.ReadWorkspaceLines("test-corpus", "manifest.csv")
            .Skip(1)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(ParseManifestRow)
            .ToArray();
    }

    private static ManifestRow ParseManifestRow(string line)
    {
        var columns = line.Split(',');
        columns.Should().HaveCount(ExpectedManifestHeader.Length);
        return new ManifestRow(columns[1], columns[2], columns[3], columns[4], columns[5], columns[6], columns[7], columns[8], columns[9]);
    }

    private static string ReadLastUpdatedDate(string markdown)
    {
        var line = markdown
            .Split(["\r\n", "\n"], StringSplitOptions.None)
            .First(line => line.StartsWith("**Last updated:**", StringComparison.Ordinal));

        return line
            .Replace("**Last updated:**", string.Empty, StringComparison.Ordinal)
            .Trim()
            .TrimEnd();
    }

    private static bool IsUserApprovedLocalPrivatePath(string path)
    {
        if (path.StartsWith("local-private/", StringComparison.Ordinal))
            return true;

        if (path.Length < 3)
            return false;

        var first = path[0];
        var isDriveLetter = (first >= 'A' && first <= 'Z') || (first >= 'a' && first <= 'z');
        return isDriveLetter && path[1] == ':' && (path[2] == '\\' || path[2] == '/');
    }

    private static IReadOnlyList<string> ExpectedWarningsFor(ManifestRow row)
    {
        var tags = row.FeatureTags.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var warnings = new List<string>();

        if (tags.Contains("unsupported-chart-family"))
            warnings.Add("unsupported chart package disclosed");
        if (tags.Contains("embedded-objects"))
            warnings.Add("unsupported embedded object disclosed");
        if (tags.Contains("track-changes") || tags.Contains("revision-history"))
            warnings.Add("unsupported track changes disclosed");
        if (tags.Contains("form-controls") || tags.Contains("activex"))
            warnings.Add("unsupported form control disclosed");
        if (tags.Contains("digital-signatures"))
            warnings.Add("unsupported digital signature disclosed");
        if (tags.Contains("custom-ribbon-ui"))
            warnings.Add("unsupported custom ribbon UI disclosed");
        if (tags.Contains("office-addins") || tags.Contains("webextensions"))
            warnings.Add("unsupported Office add-in disclosed");
        if (tags.Contains("live-web-queries") || tags.Contains("web-publish"))
            warnings.Add("unsupported live web query disclosed");
        if (tags.Contains("sensitivity-labels") || tags.Contains("irm"))
            warnings.Add("unsupported sensitivity label disclosed");
        if (tags.Contains("smartart") || tags.Contains("diagrams"))
            warnings.Add("unsupported SmartArt diagram disclosed");
        if (tags.Contains("chart-sheets") || tags.Contains("dialog-sheets") || tags.Contains("macro-sheets") || tags.Contains("unsupported-sheet-types"))
            warnings.Add("unsupported sheet type disclosed");
        if (tags.Contains("macros"))
            warnings.Add("excluded VBA macro disclosed");
        if (tags.Contains("power-query"))
            warnings.Add("excluded Power Query disclosed");
        if (tags.Contains("data-model") || tags.Contains("power-pivot"))
            warnings.Add("excluded Data Model disclosed");
        if (tags.Contains("linked-data-types") || tags.Contains("rich-data"))
            warnings.Add("excluded linked data type disclosed");

        return warnings;
    }

    private static bool HasFeatureTag(ManifestRow row, string tag) =>
        row.FeatureTags
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Contains(tag, StringComparer.Ordinal);

    private sealed record ManifestRow(
        string Path,
        string SourceType,
        string SourceUrl,
        string RetrievedOn,
        string License,
        string FeatureTags,
        string ExpectedWarnings,
        string ExpectedStatus,
        string Notes);
}
