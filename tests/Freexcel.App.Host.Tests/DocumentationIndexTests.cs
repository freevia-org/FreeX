using System.IO;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;

namespace Freexcel.App.Host.Tests;

public sealed partial class DocumentationIndexTests
{
    [Fact]
    public void DocsReadme_LinksNewestStatusReportAndCurrentPlanningSources()
    {
        var docsDirectory = Path.GetDirectoryName(WorkspaceFileLocator.Find("docs", "README.md"))!;
        var readme = File.ReadAllText(Path.Combine(docsDirectory, "README.md"));
        var newestStatusReport = Directory.GetFiles(docsDirectory, "PROJECT_STATUS_REPORT_*.md")
            .Select(path => Path.GetFileName(path)!)
            .Order(StringComparer.Ordinal)
            .Last();

        readme.Should().Contain($"[{newestStatusReport}]({newestStatusReport})");
        readme.Should().Contain("[OUTSTANDING_BUILD.md](OUTSTANDING_BUILD.md)");
        readme.Should().Contain("[NEXT_PHASES_PLAN.md](NEXT_PHASES_PLAN.md)");
        readme.Should().Contain("[COMMAND_SURFACE_PARITY.md](COMMAND_SURFACE_PARITY.md)");
        readme.Should().Contain("[MENU_TOOLBAR_PARITY.md](MENU_TOOLBAR_PARITY.md)");
        readme.Should().Contain("[SHORTCUT_PARITY_MATRIX.md](SHORTCUT_PARITY_MATRIX.md)");
        readme.Should().Contain("[FUNCTION_PARITY.md](FUNCTION_PARITY.md)");
        readme.Should().Contain("[FIDELITY_CONTRACT.md](FIDELITY_CONTRACT.md)");
        readme.Should().Contain("[XLSX_CORPUS_REPORT.md](XLSX_CORPUS_REPORT.md)");
        readme.Should().Contain("[XLSX_TEST_CORPUS_PLAN.md](XLSX_TEST_CORPUS_PLAN.md)");
        readme.Should().Contain("[CODE_REVIEW_COMPREHENSIVE_2026-05-28.md](CODE_REVIEW_COMPREHENSIVE_2026-05-28.md)");
        readme.Should().Contain("[TESTER_RELEASE_CHECKLIST.md](TESTER_RELEASE_CHECKLIST.md)");
        readme.Should().Contain("[CODE_REVIEW.md](CODE_REVIEW.md)");
        readme.Should().Contain("[DECISIONS/008-code-review-hardening-2026-05-28.md](DECISIONS/008-code-review-hardening-2026-05-28.md)");
        readme.Should().Contain("[PERF_BASELINE.md](PERF_BASELINE.md)");
        File.Exists(Path.Combine(docsDirectory, "COMMAND_INVENTORY.json")).Should().BeTrue();
        File.Exists(Path.Combine(docsDirectory, "CODE_REVIEW_COMPREHENSIVE_2026-05-28.md")).Should().BeTrue();
        File.Exists(Path.Combine(docsDirectory, "CODE_REVIEW.md")).Should().BeTrue();
        File.Exists(Path.Combine(docsDirectory, "DECISIONS", "008-code-review-hardening-2026-05-28.md")).Should().BeTrue();
        ProjectStatusReportLink().Matches(readme).Should().NotBeEmpty();
    }

    [Fact]
    public void NewestStatusReport_NamesCurrentPlanningSources()
    {
        var docsDirectory = Path.GetDirectoryName(WorkspaceFileLocator.Find("docs", "README.md"))!;
        var newestStatusReport = Directory.GetFiles(docsDirectory, "PROJECT_STATUS_REPORT_*.md")
            .Order(StringComparer.Ordinal)
            .Last();
        var report = File.ReadAllText(newestStatusReport);

        report.Should().Contain("[OUTSTANDING_BUILD.md](OUTSTANDING_BUILD.md)");
        report.Should().Contain("[NEXT_PHASES_PLAN.md](NEXT_PHASES_PLAN.md)");
        report.Should().Contain("[COMMAND_SURFACE_PARITY.md](COMMAND_SURFACE_PARITY.md)");
        report.Should().Contain("[MENU_TOOLBAR_PARITY.md](MENU_TOOLBAR_PARITY.md)");
        report.Should().Contain("[SHORTCUT_PARITY_MATRIX.md](SHORTCUT_PARITY_MATRIX.md)");
        report.Should().Contain("[FUNCTION_PARITY.md](FUNCTION_PARITY.md)");
        report.Should().Contain("[FIDELITY_CONTRACT.md](FIDELITY_CONTRACT.md)");
        report.Should().Contain("[XLSX_CORPUS_REPORT.md](XLSX_CORPUS_REPORT.md)");
        report.Should().Contain("[TEST_DISTRIBUTION_PLAN.md](TEST_DISTRIBUTION_PLAN.md)");
        report.Should().Contain("[PERF_BASELINE.md](PERF_BASELINE.md)");
    }

    [Fact]
    public void NewestStatusReport_UsesBranchNeutralMainlineMetadata()
    {
        var docsDirectory = Path.GetDirectoryName(WorkspaceFileLocator.Find("docs", "README.md"))!;
        var newestStatusReport = Directory.GetFiles(docsDirectory, "PROJECT_STATUS_REPORT_*.md")
            .Order(StringComparer.Ordinal)
            .Last();
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
        var docsDirectory = Path.GetDirectoryName(WorkspaceFileLocator.Find("docs", "README.md"))!;
        var repositoryRoot = Directory.GetParent(docsDirectory)!.FullName;
        var newestStatusReport = Directory.GetFiles(docsDirectory, "PROJECT_STATUS_REPORT_*.md")
            .Order(StringComparer.Ordinal)
            .Last();
        var report = File.ReadAllText(newestStatusReport);
        using var progressDocument = JsonDocument.Parse(File.ReadAllText(Path.Combine(repositoryRoot, "release", "progress.json")));
        var overallCompletion = progressDocument.RootElement.GetProperty("overallCompletion").GetInt32();
        var expectedReleaseStream = GetExpectedTesterReleaseStream(overallCompletion);

        report.Should().Contain("[release/progress.json](../release/progress.json)");
        report.Should().Contain($"overallCompletion: {overallCompletion}");
        report.Should().Contain($"Overall completion estimate is now **{overallCompletion}%**");
        report.Should().Contain($"`{expectedReleaseStream}` stream");
    }

    [Fact]
    public void ReleaseFacingDocs_UseTesterReleaseStreamFromProgressMetadata()
    {
        var docsDirectory = Path.GetDirectoryName(WorkspaceFileLocator.Find("docs", "README.md"))!;
        var repositoryRoot = Directory.GetParent(docsDirectory)!.FullName;
        using var progressDocument = JsonDocument.Parse(File.ReadAllText(Path.Combine(repositoryRoot, "release", "progress.json")));
        var expectedReleaseStream = GetExpectedTesterReleaseStream(progressDocument.RootElement.GetProperty("overallCompletion").GetInt32());

        var releaseFacingDocs = new[]
        {
            "OUTSTANDING_BUILD.md",
            "TEST_DISTRIBUTION_PLAN.md",
            Path.GetFileName(Directory.GetFiles(docsDirectory, "PROJECT_STATUS_REPORT_*.md").Order(StringComparer.Ordinal).Last())
        };

        foreach (var doc in releaseFacingDocs)
        {
            var source = File.ReadAllText(Path.Combine(docsDirectory, doc));

            source.Should().Contain(
                expectedReleaseStream,
                "{0} should describe the same tester stream that release/progress.json drives",
                doc);
        }
    }

    [Fact]
    public void NewestStatusReport_RepositoryMetricsMatchTrackedSources()
    {
        var docsDirectory = Path.GetDirectoryName(WorkspaceFileLocator.Find("docs", "README.md"))!;
        var repositoryRoot = Directory.GetParent(docsDirectory)!.FullName;
        var newestStatusReport = Directory.GetFiles(docsDirectory, "PROJECT_STATUS_REPORT_*.md")
            .Order(StringComparer.Ordinal)
            .Last();
        var report = File.ReadAllText(newestStatusReport);
        var metrics = ReadMetricTable(report);
        var trackedFiles = RunGitLines(repositoryRoot, "ls-files");
        var sourceFiles = trackedFiles.Where(path => path.StartsWith("src/", StringComparison.Ordinal) && path.EndsWith(".cs", StringComparison.Ordinal)).ToArray();
        var testFiles = trackedFiles.Where(path => path.StartsWith("tests/", StringComparison.Ordinal) && path.EndsWith(".cs", StringComparison.Ordinal)).ToArray();
        var docsFiles = trackedFiles.Where(path => path.StartsWith("docs/", StringComparison.Ordinal) && path.EndsWith(".md", StringComparison.Ordinal)).ToArray();

        metrics["Tracked files"].Should().Be(trackedFiles.Count);
        metrics["C# source files under `src/`"].Should().Be(sourceFiles.Length);
        metrics["C# test files under `tests/`"].Should().Be(testFiles.Length);
        metrics["Markdown docs under `docs/`"].Should().Be(docsFiles.Length);
    }

    [Fact]
    public void NewestStatusReport_KeyOpenItemsMatchOutstandingBuildHighestPriorityItems()
    {
        var docsDirectory = Path.GetDirectoryName(WorkspaceFileLocator.Find("docs", "README.md"))!;
        var newestStatusReport = Directory.GetFiles(docsDirectory, "PROJECT_STATUS_REPORT_*.md")
            .Order(StringComparer.Ordinal)
            .Last();
        var outstandingBuild = File.ReadAllLines(Path.Combine(docsDirectory, "OUTSTANDING_BUILD.md"));
        var report = File.ReadAllLines(newestStatusReport);

        ReadNumberedBoldItems(outstandingBuild, "## Highest Priority Outstanding Work")
            .Take(5)
            .Should()
            .Equal(ReadNumberedBoldItems(report, "## Remaining Outstanding Work"));
    }

    [Fact]
    public void CurrentPlanningDocs_ConditionalFormattingRemainingScopeStaysAligned()
    {
        var docsDirectory = Path.GetDirectoryName(WorkspaceFileLocator.Find("docs", "README.md"))!;
        var newestStatusReport = Directory.GetFiles(docsDirectory, "PROJECT_STATUS_REPORT_*.md")
            .Order(StringComparer.Ordinal)
            .Last();
        var outstandingBuild = File.ReadAllText(Path.Combine(docsDirectory, "OUTSTANDING_BUILD.md"));
        var nextPhasesPlan = File.ReadAllText(Path.Combine(docsDirectory, "NEXT_PHASES_PLAN.md"));
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
        var docsDirectory = Path.GetDirectoryName(WorkspaceFileLocator.Find("docs", "README.md"))!;
        var readme = File.ReadAllText(Path.Combine(docsDirectory, "README.md"));

        readme.Should().Contain("[USER_GUIDE.md](USER_GUIDE.md)");
        readme.Should().Contain("[TROUBLESHOOTING.md](TROUBLESHOOTING.md)");
        new FileInfo(Path.Combine(docsDirectory, "USER_GUIDE.md")).Length.Should().BeGreaterThan(0);
        new FileInfo(Path.Combine(docsDirectory, "TROUBLESHOOTING.md")).Length.Should().BeGreaterThan(0);
    }

    [Fact]
    public void UiTestCatalog_EvidenceScreenshotCountMatchesArtifacts()
    {
        var docsDirectory = Path.GetDirectoryName(WorkspaceFileLocator.Find("docs", "README.md"))!;
        var catalog = File.ReadAllText(Path.Combine(docsDirectory, "UI_TEST_CATALOG.md"));
        var screenshotCount = Directory.GetFiles(Path.Combine(docsDirectory, "ui-test-artifacts"), "*.png").Length;
        var declaredCount = int.Parse(UiEvidenceScreenshotCount().Match(catalog).Groups["count"].Value);

        declaredCount.Should().Be(screenshotCount);
    }

    [Fact]
    public void UiTestCatalog_XamlClickWiredControlCountMatchesMainWindow()
    {
        var docsDirectory = Path.GetDirectoryName(WorkspaceFileLocator.Find("docs", "README.md"))!;
        var catalog = File.ReadAllText(Path.Combine(docsDirectory, "UI_TEST_CATALOG.md"));
        var mainWindow = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml"));
        var clickWiredCount = XamlClickHandler().Matches(mainWindow).Count;
        var declaredCount = int.Parse(UiCatalogXamlClickWiredCount().Match(catalog).Groups["count"].Value);

        declaredCount.Should().Be(clickWiredCount);
    }

    [Fact]
    public void UiTestCatalog_UsesCanonicalBranchNeutralMetadata()
    {
        var docsDirectory = Path.GetDirectoryName(WorkspaceFileLocator.Find("docs", "README.md"))!;
        var catalog = File.ReadAllText(Path.Combine(docsDirectory, "UI_TEST_CATALOG.md"));

        catalog.Should().Contain("Canonical path: `docs/UI_TEST_CATALOG.md`");
        catalog.Should().NotContain("Last updated:");
        catalog.Should().NotContain("Branch:");
        catalog.Should().NotContain("Current catalog branch:");
    }

    [Fact]
    public void CurrentPlanningDocs_LocalMarkdownLinksResolve()
    {
        var docsDirectory = Path.GetDirectoryName(WorkspaceFileLocator.Find("docs", "README.md"))!;
        var newestStatusReport = Directory.GetFiles(docsDirectory, "PROJECT_STATUS_REPORT_*.md")
            .Select(path => Path.GetFileName(path)!)
            .Order(StringComparer.Ordinal)
            .Last();
        var currentDocs = new[]
        {
            "README.md",
            newestStatusReport,
            "OUTSTANDING_BUILD.md",
            "NEXT_PHASES_PLAN.md",
            "UI_TEST_CATALOG.md",
            "SHORTCUT_PARITY_MATRIX.md",
            "FUNCTION_PARITY.md",
            "COMMAND_SURFACE_PARITY.md",
            "MENU_TOOLBAR_PARITY.md",
            "FIDELITY_CONTRACT.md",
            "XLSX_CORPUS_REPORT.md",
            "XLSX_TEST_CORPUS_PLAN.md",
            "TEST_DISTRIBUTION_PLAN.md",
            "TESTER_RELEASE_CHECKLIST.md",
            "PERF_BASELINE.md"
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
                    : Path.Combine(docsDirectory, targetWithoutFragment.Replace('/', Path.DirectorySeparatorChar)));

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

    private static IReadOnlyList<string> RunGitLines(string workingDirectory, string arguments)
    {
        using var process = Process.Start(new ProcessStartInfo("git", arguments)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        }) ?? throw new InvalidOperationException("Could not start git.");

        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        process.ExitCode.Should().Be(0, error);
        return output
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .ToArray();
    }

    private static int CountLines(string repositoryRoot, IEnumerable<string> relativePaths) =>
        relativePaths.Sum(file => File.ReadLines(Path.Combine(repositoryRoot, ToPlatformPath(file))).Count());

    private static string ToPlatformPath(string path) =>
        path.Replace('/', Path.DirectorySeparatorChar);

    private static string GetExpectedTesterReleaseStream(int overallCompletion)
    {
        var minor = overallCompletion >= 99 ? 9
            : overallCompletion >= 96 ? 8
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

    [GeneratedRegex(@"\[PROJECT_STATUS_REPORT_\d{4}-\d{2}-\d{2}\.md\]\(PROJECT_STATUS_REPORT_\d{4}-\d{2}-\d{2}\.md\)")]
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

    [GeneratedRegex(@"Click=""[^""]+""")]
    private static partial Regex XamlClickHandler();
}
