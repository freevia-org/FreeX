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
    public void CorpusPlan_DocumentsAllAllowedManifestStatuses()
    {
        var plan = File.ReadAllText(FindWorkspaceFile("docs", "XLSX_TEST_CORPUS_PLAN.md"));

        foreach (var status in AllowedStatuses)
            plan.Should().Contain($"`{status}`");
    }

    [Fact]
    public void CorpusPlan_StatesCurrentManifestBaselineCounts()
    {
        var manifestRows = ReadManifestRows();
        var plan = File.ReadAllText(FindWorkspaceFile("docs", "XLSX_TEST_CORPUS_PLAN.md"));
        var generatedCount = manifestRows.Count(row => row.SourceType == "generated");
        var publicCount = manifestRows.Count(row => row.SourceType == "public");
        var localPrivateCount = manifestRows.Count(row => row.SourceType == "local-private");
        var regressionCount = manifestRows.Count(row => row.SourceType == "regression");

        plan.Should().Contain(
            $"Current executable manifest baseline: {manifestRows.Count} rows ({generatedCount} generated, {publicCount} public, {localPrivateCount} local-private, {regressionCount} regression).");
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
    public void CorpusReport_LastUpdatedMatchesCorpusPlan()
    {
        var plan = File.ReadAllText(FindWorkspaceFile("docs", "XLSX_TEST_CORPUS_PLAN.md"));
        var report = File.ReadAllText(FindWorkspaceFile("docs", "XLSX_CORPUS_REPORT.md"));

        report.Should().Contain($"**Last updated:** {ReadLastUpdatedDate(plan)}");
    }

    [Fact]
    public void CorpusReport_StatesTopFailureSummary()
    {
        var report = File.ReadAllText(FindWorkspaceFile("docs", "XLSX_CORPUS_REPORT.md"));

        report.Should().Contain("## Top Failures");
        report.Should().Contain("No active automated XLSX corpus failures are currently recorded.");
    }

    [Fact]
    public void CorpusReport_StatesPrioritizedFixListMappedToCommandParity()
    {
        var report = File.ReadAllText(FindWorkspaceFile("docs", "XLSX_CORPUS_REPORT.md"));

        report.Should().Contain("## Prioritized Fix List");
        report.Should().Contain("`docs/COMMAND_SURFACE_PARITY.md`");
    }

    [Fact]
    public void CorpusReport_DoesNotListCompletedLocalPrivateManifestRowsAsOpenGap()
    {
        var manifestRows = ReadManifestRows();
        var report = File.ReadAllText(FindWorkspaceFile("docs", "XLSX_CORPUS_REPORT.md"));

        manifestRows.Count(row => row.SourceType == "local-private")
            .Should().BeGreaterThan(0, "optional local-private workbook rows are already represented in the manifest");
        report.Should().NotContain(
            "Add local-private workbook rows",
            "the report gap list should not ask for manifest rows after they exist");
    }

    [Fact]
    public void CorpusReport_UsesCurrentManifestBaselineInExpansionGap()
    {
        var manifestRows = ReadManifestRows();
        var report = File.ReadAllText(FindWorkspaceFile("docs", "XLSX_CORPUS_REPORT.md"));

        report.Should().NotContain(
            "Continue expanding the 100-row corpus beyond the current baseline",
            "the executable manifest has moved past the original 100-row target");
        report.Should().Contain(
            $"Continue expanding the {manifestRows.Count}-row corpus baseline",
            "the report gap list should track the live manifest size");
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
    public void CorpusReport_StatesNonPublicUnsupportedAndExcludedWarningDeclarations()
    {
        var manifestRows = ReadManifestRows();
        var report = File.ReadAllText(FindWorkspaceFile("docs", "XLSX_CORPUS_REPORT.md"));
        var warningDeclarationCount = manifestRows
            .Where(row => row.SourceType != "public" && ExpectedWarningsFor(row).Count > 0)
            .Count();

        warningDeclarationCount.Should().BeGreaterThan(0);
        report.Should().Contain($"| Non-public unsupported/excluded warning declarations | {warningDeclarationCount}/{warningDeclarationCount} present in manifest |");
    }

    [Fact]
    public void CorpusReport_StatesPublicUnsupportedTagWarningDetectionCount()
    {
        var manifestRows = ReadManifestRows();
        var report = File.ReadAllText(FindWorkspaceFile("docs", "XLSX_CORPUS_REPORT.md"));
        var publicUnsupportedTagCount = manifestRows
            .Where(row => row.SourceType == "public" && ExpectedWarningsFor(row).Count > 0)
            .Count();

        publicUnsupportedTagCount.Should().BeGreaterThan(0);
        report.Should().Contain($"| Public unsupported-tag warning detection | {publicUnsupportedTagCount}/{publicUnsupportedTagCount} exercised by corpus runner |");
    }

    [Fact]
    public void CorpusReport_StatesPublicSourceMetadataCoverage()
    {
        var manifestRows = ReadManifestRows();
        var report = File.ReadAllText(FindWorkspaceFile("docs", "XLSX_CORPUS_REPORT.md"));
        var publicRows = manifestRows
            .Where(row => row.SourceType == "public")
            .ToArray();

        publicRows.Should().HaveCountGreaterThan(0);
        publicRows.Should().OnlyContain(
            row =>
                row.SourceUrl.StartsWith("https://", StringComparison.Ordinal) &&
                !string.IsNullOrWhiteSpace(row.RetrievedOn) &&
                !string.IsNullOrWhiteSpace(row.License),
            "redistributed public corpus rows need auditable provenance metadata");
        report.Should().Contain($"| Public source metadata coverage | {publicRows.Length}/{publicRows.Length} rows declare source URL, retrieval date, and license |");
    }

    [Fact]
    public void CorpusReport_StatesLocalPrivatePrivacyMetadataCoverage()
    {
        var manifestRows = ReadManifestRows();
        var report = File.ReadAllText(FindWorkspaceFile("docs", "XLSX_CORPUS_REPORT.md"));
        var localPrivateRows = manifestRows
            .Where(row => row.SourceType == "local-private")
            .ToArray();

        localPrivateRows.Should().HaveCountGreaterThan(0);
        localPrivateRows.Should().OnlyContain(
            row =>
                row.Path.StartsWith("local-private/", StringComparison.Ordinal) &&
                row.SourceUrl == "user-approved-local" &&
                row.License == "private-local",
            "local-private corpus rows should disclose capability coverage without leaking private workbook provenance");
        report.Should().Contain($"| Local-private privacy metadata coverage | {localPrivateRows.Length}/{localPrivateRows.Length} rows use local-only source markers and private-local license |");
    }

    [Fact]
    public void CorpusReport_StatesLocalPrivateKnownGapWarningsAreDeclared()
    {
        var manifestRows = ReadManifestRows();
        var report = File.ReadAllText(FindWorkspaceFile("docs", "XLSX_CORPUS_REPORT.md"));
        var localPrivateKnownGapCount = manifestRows.Count(row => row.SourceType == "local-private" && row.ExpectedStatus == "supported-known-gap");

        manifestRows
            .Where(row => row.SourceType == "local-private" && row.ExpectedStatus == "supported-known-gap")
            .Should()
            .OnlyContain(row => !string.IsNullOrWhiteSpace(row.ExpectedWarnings));
        report.Should().Contain("known-gap warning expectations are declared for optional private rows");
        report.Should().Contain($"| Local-private known-gap warning declarations | {localPrivateKnownGapCount}/{localPrivateKnownGapCount} present in manifest for skipped optional private rows |");
    }

    [Fact]
    public void CorpusReport_StatesUnsupportedSheetTypeWorkbookReferenceCoverage()
    {
        var manifestRows = ReadManifestRows();
        var report = File.ReadAllText(FindWorkspaceFile("docs", "XLSX_CORPUS_REPORT.md"));

        manifestRows.Should().Contain(row =>
            row.Path == "generated/unsupported-sheet-types-001.xlsx" &&
            row.FeatureTags.Contains("chart-sheets", StringComparison.Ordinal) &&
            row.FeatureTags.Contains("dialog-sheets", StringComparison.Ordinal) &&
            row.FeatureTags.Contains("macro-sheets", StringComparison.Ordinal));
        const string reportLine = "| Unsupported sheet type package references | Chartsheet, dialog sheet, and macro sheet workbook references and relationships are exercised by generated known-gap retention coverage |";
        report.Should().Contain(reportLine);
        report.Split(reportLine).Should().HaveCount(2, "the coverage line should appear exactly once in the Current Result table");
    }

    [Fact]
    public void CorpusReport_StatesThreadedCommentPackageReferenceCoverage()
    {
        var manifestRows = ReadManifestRows();
        var report = File.ReadAllText(FindWorkspaceFile("docs", "XLSX_CORPUS_REPORT.md"));

        manifestRows.Should().Contain(row =>
            row.Path == "generated/threaded-comments-001.xlsx" &&
            row.FeatureTags.Contains("threaded-comments", StringComparison.Ordinal));
        const string reportLine = "| Threaded comment package references | Worksheet threaded-comment relationships and workbook persons relationships are exercised by generated known-gap retention coverage |";
        report.Should().Contain(reportLine);
        report.Split(reportLine).Should().HaveCount(2, "the coverage line should appear exactly once in the Current Result table");
    }

    [Fact]
    public void CorpusReport_StatesCustomRibbonUiPackageReferenceCoverage()
    {
        var manifestRows = ReadManifestRows();
        var report = File.ReadAllText(FindWorkspaceFile("docs", "XLSX_CORPUS_REPORT.md"));

        manifestRows.Should().Contain(row =>
            row.Path == "generated/custom-ribbon-ui-001.xlsx" &&
            row.FeatureTags.Contains("custom-ribbon-ui", StringComparison.Ordinal));
        const string reportLine = "| Custom Ribbon UI package references | Package-root custom UI relationships are exercised by generated known-gap retention coverage |";
        report.Should().Contain(reportLine);
        report.Split(reportLine).Should().HaveCount(2, "the coverage line should appear exactly once in the Current Result table");
    }

    [Fact]
    public void CorpusReport_StatesFormControlPackageReferenceCoverage()
    {
        var manifestRows = ReadManifestRows();
        var report = File.ReadAllText(FindWorkspaceFile("docs", "XLSX_CORPUS_REPORT.md"));

        manifestRows.Should().Contain(row =>
            row.Path == "generated/form-controls-001.xlsx" &&
            row.FeatureTags.Contains("form-controls", StringComparison.Ordinal) &&
            row.FeatureTags.Contains("activex", StringComparison.Ordinal));
        const string reportLine = "| Form control and ActiveX package references | Worksheet control relationships and ActiveX binary relationships are exercised by generated known-gap retention coverage |";
        report.Should().Contain(reportLine);
        report.Split(reportLine).Should().HaveCount(2, "the coverage line should appear exactly once in the Current Result table");
    }

    [Fact]
    public void CorpusReport_StatesOfficeAddinsPackageReferenceCoverage()
    {
        var manifestRows = ReadManifestRows();
        var report = File.ReadAllText(FindWorkspaceFile("docs", "XLSX_CORPUS_REPORT.md"));

        manifestRows.Should().Contain(row =>
            row.Path == "generated/office-addins-001.xlsx" &&
            row.FeatureTags.Contains("office-addins", StringComparison.Ordinal) &&
            row.FeatureTags.Contains("webextensions", StringComparison.Ordinal));
        const string reportLine = "| Office add-in package references | Package-root task pane relationships and task pane webextension relationships are exercised by generated known-gap retention coverage |";
        report.Should().Contain(reportLine);
        report.Split(reportLine).Should().HaveCount(2, "the coverage line should appear exactly once in the Current Result table");
    }

    [Fact]
    public void CorpusReport_StatesPowerQueryPackageReferenceCoverage()
    {
        var manifestRows = ReadManifestRows();
        var report = File.ReadAllText(FindWorkspaceFile("docs", "XLSX_CORPUS_REPORT.md"));

        manifestRows.Should().Contain(row =>
            row.Path == "generated/power-query-001.xlsx" &&
            row.FeatureTags.Contains("power-query", StringComparison.Ordinal) &&
            row.FeatureTags.Contains("connections", StringComparison.Ordinal));
        const string reportLine = "| Power Query package references | Worksheet query table relationships are exercised by generated known-gap retention coverage |";
        report.Should().Contain(reportLine);
        report.Split(reportLine).Should().HaveCount(2, "the coverage line should appear exactly once in the Current Result table");
    }

    [Fact]
    public void CorpusReport_StatesSmartArtPackageReferenceCoverage()
    {
        var manifestRows = ReadManifestRows();
        var report = File.ReadAllText(FindWorkspaceFile("docs", "XLSX_CORPUS_REPORT.md"));

        manifestRows.Should().Contain(row =>
            row.Path == "generated/smartart-diagrams-001.xlsx" &&
            row.FeatureTags.Contains("smartart", StringComparison.Ordinal) &&
            row.FeatureTags.Contains("diagrams", StringComparison.Ordinal));
        const string reportLine = "| SmartArt diagram package references | Worksheet drawing relationships and drawing diagram relationships are exercised by generated known-gap retention coverage |";
        report.Should().Contain(reportLine);
        report.Split(reportLine).Should().HaveCount(2, "the coverage line should appear exactly once in the Current Result table");
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

    [Fact]
    public void RegressionFormulaCachedWorkbooks_AreAllRepresentedInCorpusManifest()
    {
        var manifestRows = ReadManifestRows();
        var regressionWorkbookPaths = Directory
            .EnumerateFiles(FindWorkspaceDirectory("test-corpus", "regressions", "formula-cached"), "*.xlsx", SearchOption.TopDirectoryOnly)
            .Select(path => Path.GetRelativePath(FindWorkspaceDirectory("test-corpus"), path).Replace(Path.DirectorySeparatorChar, '/'))
            .Order(StringComparer.Ordinal)
            .ToArray();
        var manifestRegressionPaths = manifestRows
            .Where(row => row.SourceType == "regression")
            .Select(row => row.Path)
            .Order(StringComparer.Ordinal)
            .ToArray();

        manifestRegressionPaths.Should().Equal(regressionWorkbookPaths);
    }

    [Fact]
    public void NewestStatusReport_StatesCurrentCorpusManifestCount()
    {
        var manifestRows = ReadManifestRows();
        var docsDirectory = Path.GetDirectoryName(FindWorkspaceFile("docs", "XLSX_CORPUS_REPORT.md"))!;
        var newestStatusReport = Directory.GetFiles(docsDirectory, "PROJECT_STATUS_REPORT_*.md")
            .Order(StringComparer.Ordinal)
            .Last();
        var report = File.ReadAllText(newestStatusReport);

        report.Should().ContainAny(
            $"expanded to {manifestRows.Count} manifest rows",
            $"Manifest baseline is {manifestRows.Count} rows",
            $"XLSX corpus manifest rows | {manifestRows.Count}");
        report.Should().Contain($"{manifestRows.Count} workbook manifest rows");
        report.Should().Contain($"Expand the {manifestRows.Count}-row corpus baseline");
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

    private static IReadOnlyList<string> ExpectedWarningsFor(ManifestRow row)
    {
        var tags = row.FeatureTags.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var warnings = new List<string>();

        if (tags.Contains("unsupported-chart-family"))
            warnings.Add("unsupported chart package disclosed");
        if (tags.Contains("embedded-objects"))
            warnings.Add("unsupported embedded object disclosed");
        if (tags.Contains("threaded-comments"))
            warnings.Add("unsupported threaded comment disclosed");
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

    private static string FindWorkspaceDirectory(params string[] relativeParts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(new[] { directory.FullName }.Concat(relativeParts).ToArray());
            if (Directory.Exists(candidate))
                return candidate;

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException($"Could not locate workspace directory: {Path.Combine(relativeParts)}");
    }

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
