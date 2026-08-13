using System.IO;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed partial class DocumentationIndexTests
{
    [Fact]
    public void DocsReadme_LinksNewestStatusReportAndCurrentPlanningSources()
    {
        var docsDirectory = WorkspaceFileLocator.FindDocsDirectory();
        var readme = WorkspaceFileLocator.ReadAllText("docs", "README.md");
        var newestStatusReport = NewestStatusReportRelativePath(docsDirectory);

        readme.Should().Contain($"[{newestStatusReport}]({newestStatusReport})");
        readme.Should().Contain("[planning/outstanding-build.md](planning/outstanding-build.md)");
        readme.Should().Contain("[planning/next-phases.md](planning/next-phases.md)");
        readme.Should().Contain("[parity/command-surface.md](parity/command-surface.md)");
        readme.Should().Contain("[parity/menu-toolbar.md](parity/menu-toolbar.md)");
        readme.Should().Contain("[parity/shortcuts.md](parity/shortcuts.md)");
        readme.Should().Contain("[parity/functions.md](parity/functions.md)");
        readme.Should().Contain("[formats/fidelity-contract.md](formats/fidelity-contract.md)");
        readme.Should().Contain("[formats/xlsx-corpus-report.md](formats/xlsx-corpus-report.md)");
        readme.Should().Contain("[formats/xlsx-test-corpus-plan.md](formats/xlsx-test-corpus-plan.md)");
        readme.Should().Contain("[reviews/comprehensive-code-review-2026-05-28.md](reviews/comprehensive-code-review-2026-05-28.md)");
        readme.Should().Contain("[release/tester-release-checklist.md](release/tester-release-checklist.md)");
        readme.Should().Contain("[reviews/code-review-log.md](reviews/code-review-log.md)");
        readme.Should().Contain("[architecture/decisions/008-code-review-hardening-2026-05-28.md](architecture/decisions/008-code-review-hardening-2026-05-28.md)");
        readme.Should().Contain("[performance/baseline.md](performance/baseline.md)");
        readme.Should().Contain("[performance/backlog-2026-06-04.md](performance/backlog-2026-06-04.md)");
        File.Exists(Path.Combine(docsDirectory, "parity/command-inventory.json")).Should().BeTrue();
        File.Exists(Path.Combine(docsDirectory, "reviews/comprehensive-code-review-2026-05-28.md")).Should().BeTrue();
        File.Exists(Path.Combine(docsDirectory, "reviews/code-review-log.md")).Should().BeTrue();
        File.Exists(Path.Combine(docsDirectory, "architecture", "decisions", "008-code-review-hardening-2026-05-28.md")).Should().BeTrue();
        ProjectStatusReportLink().Matches(readme).Should().NotBeEmpty();
    }

    [Fact]
    public void DocsReadme_LinksEveryComprehensiveReviewReport()
    {
        var docsDirectory = WorkspaceFileLocator.FindDocsDirectory();
        var readme = WorkspaceFileLocator.ReadAllText("docs", "README.md");
        var reviewReports = Directory
            .GetFiles(Path.Combine(docsDirectory, "reviews"), "comprehensive-code-review-*.md")
            .Select(path => Path.GetRelativePath(docsDirectory, path).Replace(Path.DirectorySeparatorChar, '/'))
            .Order(StringComparer.Ordinal)
            .ToArray();

        reviewReports.Should().NotBeEmpty();
        foreach (var report in reviewReports)
            readme.Should().Contain($"[{report}]({report})");
    }

    [Fact]
    public void CodeReviewLog_LinksEveryComprehensiveReviewReport()
    {
        var docsDirectory = WorkspaceFileLocator.FindDocsDirectory();
        var log = WorkspaceFileLocator.ReadAllText("docs", "reviews", "code-review-log.md");
        var reviewReports = Directory
            .GetFiles(Path.Combine(docsDirectory, "reviews"), "comprehensive-code-review-*.md")
            .Select(path => Path.GetFileName(path)!)
            .Order(StringComparer.Ordinal)
            .ToArray();

        reviewReports.Should().NotBeEmpty();
        foreach (var report in reviewReports)
            Regex.IsMatch(log, $@"\[[^\]]*{Regex.Escape(report)}\]\([^)]*{Regex.Escape(report)}\)")
                .Should().BeTrue($"{report} should be linked from the code review log");
    }

    [Fact]
    public void NewestStatusReport_NamesCurrentPlanningSources()
    {
        var docsDirectory = WorkspaceFileLocator.FindDocsDirectory();
        var newestStatusReport = NewestStatusReportPath(docsDirectory);
        var report = File.ReadAllText(newestStatusReport);

        report.Should().Contain("[planning/outstanding-build.md](../planning/outstanding-build.md)");
        report.Should().Contain("[planning/next-phases.md](../planning/next-phases.md)");
        report.Should().Contain("[parity/command-surface.md](../parity/command-surface.md)");
        report.Should().Contain("[parity/menu-toolbar.md](../parity/menu-toolbar.md)");
        report.Should().Contain("[parity/shortcuts.md](../parity/shortcuts.md)");
        report.Should().Contain("[parity/functions.md](../parity/functions.md)");
        report.Should().Contain("[formats/fidelity-contract.md](../formats/fidelity-contract.md)");
        report.Should().Contain("[formats/xlsx-corpus-report.md](../formats/xlsx-corpus-report.md)");
        report.Should().Contain("[release/test-distribution.md](../release/test-distribution.md)");
        report.Should().Contain("[performance/baseline.md](../performance/baseline.md)");
    }

    [Fact]
    public void NewestStatusReport_UsesBranchNeutralMainlineMetadata()
    {
        var docsDirectory = WorkspaceFileLocator.FindDocsDirectory();
        var newestStatusReport = NewestStatusReportPath(docsDirectory);
        var report = File.ReadAllText(newestStatusReport);

        report.Should().Contain("Mainline observed: branch-neutral `origin/main` snapshot");
        report.Should().NotContain("codex/");
        report.Should().NotContain("Build-lane worktree");
        report.Should().NotContain("| Local branches |");
        report.Should().NotContain("| Registered worktrees |");
        report.Should().NotContain("| Source lines under `src/` |");
        report.Should().NotContain("| Test lines under `tests/` |");
        report.Should().NotContain("| Documentation lines under `docs/` |");
        report.Should().NotContain("| Test methods marked `[Fact]` / `[Theory]` |");
        report.Should().NotContain("registered worktrees remain");
    }

    [Fact]
    public void NewestStatusReport_ReleaseProgressMetadataMatchesJson()
    {
        var docsDirectory = WorkspaceFileLocator.FindDocsDirectory();
        var newestStatusReport = NewestStatusReportPath(docsDirectory);
        var report = File.ReadAllText(newestStatusReport);
        using var progressDocument = JsonDocument.Parse(WorkspaceFileLocator.ReadAllText("release", "progress.json"));
        var overallCompletion = progressDocument.RootElement.GetProperty("overallCompletion").GetInt32();
        var expectedReleaseStream = GetExpectedTesterReleaseStream(overallCompletion);

        report.Should().Contain("[release/progress.json](../../release/progress.json)");
        report.Should().Contain($"overallCompletion: {overallCompletion}");
        report.Should().Contain($"Overall completion estimate is now **{overallCompletion}%**");
        report.Should().Contain($"`{expectedReleaseStream}` stream");
    }

    [Fact]
    public void ReleaseFacingDocs_UseTesterReleaseStreamFromProgressMetadata()
    {
        var docsDirectory = WorkspaceFileLocator.FindDocsDirectory();
        using var progressDocument = JsonDocument.Parse(WorkspaceFileLocator.ReadAllText("release", "progress.json"));
        var expectedReleaseStream = GetExpectedTesterReleaseStream(progressDocument.RootElement.GetProperty("overallCompletion").GetInt32());
        var newestStatusReport = NewestStatusReportRelativePath(docsDirectory);

        var releaseFacingDocs = new[]
        {
            "planning/outstanding-build.md",
            "release/test-distribution.md",
            newestStatusReport
        };

        foreach (var doc in releaseFacingDocs)
        {
            var source = doc == newestStatusReport
                ? File.ReadAllText(Path.Combine(docsDirectory, doc))
                : WorkspaceFileLocator.ReadAllText("docs", doc);

            source.Should().Contain(
                expectedReleaseStream,
                "{0} should describe the same tester stream that release/progress.json drives",
                doc);
        }
    }

    [Fact]
    public void NewestStatusReport_RepositoryMetricsMatchTrackedSources()
    {
        var docsDirectory = WorkspaceFileLocator.FindDocsDirectory();
        var repositoryRoot = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx");
        var newestStatusReport = NewestStatusReportPath(docsDirectory);
        var report = File.ReadAllText(newestStatusReport);
        var metrics = ReadMetricTable(report);
        var gitResult = TestProcessRunner.Run("git", "ls-files", repositoryRoot);

        gitResult.ExitCode.Should().Be(0, gitResult.Error);
        IReadOnlyList<string> trackedFiles = gitResult.Output
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        var sourceFiles = trackedFiles.Where(path => path.StartsWith("src/", StringComparison.Ordinal) && path.EndsWith(".cs", StringComparison.Ordinal)).ToArray();
        var testFiles = trackedFiles.Where(path => path.StartsWith("tests/", StringComparison.Ordinal) && path.EndsWith(".cs", StringComparison.Ordinal)).ToArray();
        var docsFiles = trackedFiles.Where(path => path.StartsWith("docs/", StringComparison.Ordinal) && path.EndsWith(".md", StringComparison.Ordinal)).ToArray();

        // A dated status report is a point-in-time snapshot, not a live mirror of HEAD. With many
        // concurrent sessions merging to main, asserting byte-exact equality against `git ls-files`
        // flipped this test red on essentially every merge (the file census changes whenever any
        // session adds/removes a file) — see docs/reviews/comprehensive-code-review-2026-05-30.md s7.3.
        // Require each metric to be present, positive, and within a snapshot tolerance of live:
        // this still catches fabricated or grossly-stale counts but ignores routine per-merge churn.
        AssertMetricTracksLive(metrics, "Tracked files", trackedFiles.Count);
        AssertMetricTracksLive(metrics, "C# source files under `src/`", sourceFiles.Length);
        AssertMetricTracksLive(metrics, "C# test files under `tests/`", testFiles.Length);
        AssertMetricTracksLive(metrics, "Markdown docs under `docs/`", docsFiles.Length);
    }

    private static void AssertMetricTracksLive(IReadOnlyDictionary<string, int> metrics, string metric, int live)
    {
        metrics.Should().ContainKey(metric, "the newest status report must list the '{0}' metric", metric);
        var reported = metrics[metric];
        reported.Should().BeGreaterThan(0, "'{0}' should be a real, positive count", metric);

        // Tolerance comfortably exceeds routine per-merge churn (tens of files) while still catching
        // order-of-magnitude or fabricated values.
        var tolerance = Math.Max(50, live / 10);
        reported.Should().BeInRange(
            live - tolerance,
            live + tolerance,
            "'{0}' = {1} should stay within a snapshot tolerance (±{2}) of the live repository count {3}",
            metric,
            reported,
            tolerance,
            live);
    }

    [Fact]
    public void NewestStatusReport_KeyOpenItemsMatchOutstandingBuildHighestPriorityItems()
    {
        var docsDirectory = WorkspaceFileLocator.FindDocsDirectory();
        var newestStatusReport = NewestStatusReportPath(docsDirectory);
        var outstandingBuild = WorkspaceFileLocator.ReadAllLines("docs", "planning/outstanding-build.md");
        var report = File.ReadAllLines(newestStatusReport);

        ReadNumberedBoldItems(outstandingBuild, "## Highest Priority Outstanding Work")
            .Take(5)
            .Should()
            .Equal(ReadNumberedBoldItems(report, "## Remaining Outstanding Work"));
    }

    [Fact]
    public void CurrentPlanningDocs_ConditionalFormattingRemainingScopeStaysAligned()
    {
        var docsDirectory = WorkspaceFileLocator.FindDocsDirectory();
        var newestStatusReport = NewestStatusReportPath(docsDirectory);
        var outstandingBuild = WorkspaceFileLocator.ReadAllText("docs", "planning/outstanding-build.md");
        var nextPhasesPlan = WorkspaceFileLocator.ReadAllText("docs", "planning/next-phases.md");
        var report = File.ReadAllText(newestStatusReport);

        outstandingBuild.Should().Contain("Remaining: any deeper color-scale XLSX edge semantics.");
        nextPhasesPlan.Should().Contain("Remaining polish is any deeper color-scale XLSX edge semantics as new gaps are found.");
        report.Should().Contain("Phase 7D: Deeper color-scale XLSX edge semantics as new gaps are found");
        nextPhasesPlan.Should().NotContain("rule-manager dialog matching Excel's full priority/manage-rules UX");
        report.Should().NotContain("Remaining CF hardening beyond data bar/color scale advanced options");
    }

    [Fact]
    public void DocsReadme_LinksReleaseFacingUserDocs()
    {
        var docsDirectory = WorkspaceFileLocator.FindDocsDirectory();
        var readme = WorkspaceFileLocator.ReadAllText("docs", "README.md");

        readme.Should().Contain("[user/guide.md](user/guide.md)");
        readme.Should().Contain("[user/troubleshooting.md](user/troubleshooting.md)");
        new FileInfo(Path.Combine(docsDirectory, "user/guide.md")).Length.Should().BeGreaterThan(0);
        new FileInfo(Path.Combine(docsDirectory, "user/troubleshooting.md")).Length.Should().BeGreaterThan(0);
    }

    [Fact]
    public void UserTroubleshooting_DocumentsLegacyBinaryOpenOnlySupport()
    {
        var troubleshooting = WorkspaceFileLocator.ReadAllText("docs", "user/troubleshooting.md");

        troubleshooting.Should().Contain("legacy `.xls`, `.xlsb`, `.xlt`");

        // Macro-enabled workbooks/templates gained save support (XlsmFileAdapter/XltmFileAdapter
        // declare CanSave: true), so the open-only set is now only the legacy binaries plus dBASE
        // and PDF. The doc must not re-list .xlsm/.xltm/.xltx as open-only.
        troubleshooting.Should().Contain(
            "legacy binary workbooks and templates (`.xls`, `.xlsb`, `.xlt`), dBASE (`.dbf`), and PDF (`.pdf`) are open-only imports");
        troubleshooting.Should().Contain("(`.xlsm`, `.xltm`) and `.xltx` templates open **and** save");
        troubleshooting.Should().NotContain("It does not open `.xls`");
    }

    [Fact]
    public void UiTestCatalog_EvidenceScreenshotCountMatchesArtifacts()
    {
        var docsDirectory = WorkspaceFileLocator.FindDocsDirectory();
        var catalog = WorkspaceFileLocator.ReadAllText("docs", "testing/ui-test-catalog.md");
        var screenshotCount = Directory.GetFiles(Path.Combine(docsDirectory, "ui-test-artifacts"), "*.png").Length;
        var declaredCount = int.Parse(UiEvidenceScreenshotCount().Match(catalog).Groups["count"].Value);

        declaredCount.Should().Be(screenshotCount);
    }

    [Fact]
    public void UiTestCatalog_XamlClickWiredControlCountMatchesMainWindow()
    {
        var catalog = WorkspaceFileLocator.ReadAllText("docs", "testing/ui-test-catalog.md");
        var clickWiredCount = RibbonXamlCatalogSnapshotReader.ReadMainWindowSnapshot().ClickHandlerCount;
        var declaredCount = int.Parse(UiCatalogXamlClickWiredCount().Match(catalog).Groups["count"].Value);

        declaredCount.Should().Be(clickWiredCount);
    }

    [Fact]
    public void UiTestCatalog_UsesCanonicalBranchNeutralMetadata()
    {
        var catalog = WorkspaceFileLocator.ReadAllText("docs", "testing/ui-test-catalog.md");

        catalog.Should().Contain("Canonical path: `docs/testing/ui-test-catalog.md`");
        catalog.Should().NotContain("Last updated:");
        catalog.Should().NotContain("Branch:");
        catalog.Should().NotContain("Current catalog branch:");
    }

    [Fact]
    public void CurrentPlanningDocs_LocalMarkdownLinksResolve()
    {
        var docsDirectory = WorkspaceFileLocator.FindDocsDirectory();
        var newestStatusReport = NewestStatusReportRelativePath(docsDirectory);
        var currentDocs = new[]
        {
            "README.md",
            newestStatusReport,
            "planning/outstanding-build.md",
            "planning/next-phases.md",
            "testing/ui-test-catalog.md",
            "reviews/code-review-log.md",
            "parity/shortcuts.md",
            "parity/functions.md",
            "parity/command-surface.md",
            "parity/menu-toolbar.md",
            "formats/fidelity-contract.md",
            "formats/xlsx-corpus-report.md",
            "formats/xlsx-test-corpus-plan.md",
            "release/test-distribution.md",
            "release/tester-release-checklist.md",
            "performance/baseline.md",
            "performance/backlog-2026-06-04.md"
        };

        foreach (var doc in currentDocs)
            AssertLocalMarkdownLinksResolve(Path.Combine(docsDirectory, doc), docsDirectory);
    }

    private static void AssertLocalMarkdownLinksResolve(string sourcePath, string docsDirectory)
    {
        var source = File.ReadAllText(sourcePath);
        foreach (Match match in MarkdownLink().Matches(source))
        {
            var target = match.Groups["target"].Value;
            if (target.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                || target.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var targetParts = target.Split('#', 2);
            var targetWithoutFragment = targetParts[0];
            var resolvedPath = Path.GetFullPath(
                string.IsNullOrWhiteSpace(targetWithoutFragment)
                    ? sourcePath
                    : Path.Combine(
                        Path.GetDirectoryName(sourcePath)!,
                        targetWithoutFragment.Replace('/', Path.DirectorySeparatorChar)));

            (File.Exists(resolvedPath) || Directory.Exists(resolvedPath)).Should().BeTrue(
                "{0} links to {1}",
                Path.GetFileName(sourcePath),
                target);

            if (targetParts.Length == 2 && !string.IsNullOrWhiteSpace(targetParts[1]) && File.Exists(resolvedPath))
            {
                var anchors = ReadMarkdownHeadingAnchors(resolvedPath);
                var fragment = Uri.UnescapeDataString(targetParts[1]);

                anchors.Should().Contain(
                    fragment,
                    "{0} links to heading #{1} in {2}",
                    Path.GetFileName(sourcePath),
                    fragment,
                    targetWithoutFragment.Length == 0 ? Path.GetFileName(sourcePath) : targetWithoutFragment);
            }
        }
    }

    private static IReadOnlySet<string> ReadMarkdownHeadingAnchors(string path) =>
        File.ReadLines(path)
            .Where(line => line.StartsWith('#'))
            .Select(line => MarkdownHeading().Match(line))
            .Where(match => match.Success)
            .Select(match => ToMarkdownAnchor(match.Groups["heading"].Value))
            .ToHashSet(StringComparer.Ordinal);

    private static string ToMarkdownAnchor(string heading)
    {
        var builder = new StringBuilder(heading.Length);
        var previousWasHyphen = false;

        foreach (var character in heading.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(character);
                previousWasHyphen = false;
                continue;
            }

            if (character == ' ' || character == '-')
            {
                if (!previousWasHyphen && builder.Length > 0)
                    builder.Append('-');

                previousWasHyphen = true;
            }
        }

        if (builder.Length > 0 && builder[^1] == '-')
            builder.Length--;

        return builder.ToString();
    }

    private static IReadOnlyDictionary<string, int> ReadMetricTable(string report) =>
        report
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => MetricTableRow().Match(line))
            .Where(match => match.Success)
            .ToDictionary(
                match => match.Groups["metric"].Value,
                match => int.Parse(match.Groups["count"].Value.Replace(",", string.Empty), CultureInfo.InvariantCulture),
                StringComparer.Ordinal);

    private static int CountLines(string repositoryRoot, IEnumerable<string> relativePaths) =>
        relativePaths.Sum(file => File.ReadLines(Path.Combine(repositoryRoot, ToPlatformPath(file))).Count());

    private static string ToPlatformPath(string path) =>
        path.Replace('/', Path.DirectorySeparatorChar);

    private static string NewestStatusReportRelativePath(string docsDirectory) =>
        Directory.GetFiles(Path.Combine(docsDirectory, "history"), "status-*.md")
            .Select(path => Path.GetRelativePath(docsDirectory, path).Replace(Path.DirectorySeparatorChar, '/'))
            .Order(StringComparer.Ordinal)
            .Last();

    private static string NewestStatusReportPath(string docsDirectory) =>
        Path.Combine(docsDirectory, ToPlatformPath(NewestStatusReportRelativePath(docsDirectory)));

    private static string GetExpectedTesterReleaseStream(int overallCompletion)
    {
        var minor = overallCompletion >= 99 ? 9
            : overallCompletion >= 95 ? 8
            : overallCompletion >= 93 ? 7
            : overallCompletion >= 90 ? 6
            : 5;

        return $"v0.{minor}.<run>";
    }

    private static IReadOnlyList<string> ReadNumberedBoldItems(IReadOnlyList<string> lines, string sectionHeading)
    {
        var sectionStart = Array.IndexOf(lines.ToArray(), sectionHeading);
        sectionStart.Should().BeGreaterThanOrEqualTo(0);

        return lines
            .Skip(sectionStart + 1)
            .TakeWhile(line => !line.StartsWith("## ", StringComparison.Ordinal))
            .Select(line => NumberedBoldItem().Match(line))
            .Where(match => match.Success)
            .Select(match => match.Groups["title"].Value)
            .ToArray();
    }

    [GeneratedRegex(@"\[history/status-\d{4}-\d{2}-\d{2}\.md\]\(history/status-\d{4}-\d{2}-\d{2}\.md\)")]
    private static partial Regex ProjectStatusReportLink();

    [GeneratedRegex(@"(?<!!)\[[^\]]+\]\((?<target>[^)]+)\)")]
    private static partial Regex MarkdownLink();

    [GeneratedRegex(@"^#+\s+(?<heading>.+?)\s*#*$")]
    private static partial Regex MarkdownHeading();

    [GeneratedRegex(@"^\d+\. \*\*(?<title>[^*]+)\*\*")]
    private static partial Regex NumberedBoldItem();

    [GeneratedRegex(@"^\| (?<metric>[^|]+) \| (?<count>[\d,]+) \|$")]
    private static partial Regex MetricTableRow();

    [GeneratedRegex(@"\[(?:Fact|Theory)\]")]
    private static partial Regex FactOrTheoryAttribute();

    [GeneratedRegex(@"\| Existing UI evidence screenshots \| (?<count>\d+) \|")]
    private static partial Regex UiEvidenceScreenshotCount();

    [GeneratedRegex(@"\| XAML click-wired controls \| (?<count>\d+) \|")]
    private static partial Regex UiCatalogXamlClickWiredCount();

}
