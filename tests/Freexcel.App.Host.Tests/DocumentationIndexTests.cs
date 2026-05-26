using System.IO;
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
        readme.Should().Contain("[FIDELITY_CONTRACT.md](FIDELITY_CONTRACT.md)");
        readme.Should().Contain("[XLSX_CORPUS_REPORT.md](XLSX_CORPUS_REPORT.md)");
        readme.Should().Contain("[XLSX_TEST_CORPUS_PLAN.md](XLSX_TEST_CORPUS_PLAN.md)");
        File.Exists(Path.Combine(docsDirectory, "COMMAND_INVENTORY.json")).Should().BeTrue();
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
        report.Should().Contain("[FIDELITY_CONTRACT.md](FIDELITY_CONTRACT.md)");
        report.Should().Contain("[XLSX_CORPUS_REPORT.md](XLSX_CORPUS_REPORT.md)");
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
            "COMMAND_SURFACE_PARITY.md",
            "MENU_TOOLBAR_PARITY.md",
            "FIDELITY_CONTRACT.md",
            "XLSX_CORPUS_REPORT.md",
            "XLSX_TEST_CORPUS_PLAN.md"
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
                || target.StartsWith("#", StringComparison.Ordinal)
                || target.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var targetWithoutFragment = target.Split('#', 2)[0];
            var resolvedPath = Path.GetFullPath(
                Path.Combine(docsDirectory, targetWithoutFragment.Replace('/', Path.DirectorySeparatorChar)));

            (File.Exists(resolvedPath) || Directory.Exists(resolvedPath)).Should().BeTrue(
                "{0} links to {1}",
                Path.GetFileName(sourcePath),
                target);
        }
    }

    [GeneratedRegex(@"\[PROJECT_STATUS_REPORT_\d{4}-\d{2}-\d{2}\.md\]\(PROJECT_STATUS_REPORT_\d{4}-\d{2}-\d{2}\.md\)")]
    private static partial Regex ProjectStatusReportLink();

    [GeneratedRegex(@"(?<!!)\[[^\]]+\]\((?<target>[^)]+)\)")]
    private static partial Regex MarkdownLink();

    [GeneratedRegex(@"\| Existing UI evidence screenshots \| (?<count>\d+) \|")]
    private static partial Regex UiEvidenceScreenshotCount();

    [GeneratedRegex(@"\| XAML click-wired controls \| (?<count>\d+) \|")]
    private static partial Regex UiCatalogXamlClickWiredCount();

    [GeneratedRegex(@"Click=""[^""]+""")]
    private static partial Regex XamlClickHandler();
}
