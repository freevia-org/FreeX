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
            .Select(Path.GetFileName)
            .Order(StringComparer.Ordinal)
            .Last();

        readme.Should().Contain($"[{newestStatusReport}]({newestStatusReport})");
        readme.Should().Contain("[PROJECT_BUILD_HISTORY_METRICS_2026-05-25.md](PROJECT_BUILD_HISTORY_METRICS_2026-05-25.md)");
        readme.Should().Contain("[OUTSTANDING_BUILD.md](OUTSTANDING_BUILD.md)");
        readme.Should().Contain("[NEXT_PHASES_PLAN.md](NEXT_PHASES_PLAN.md)");
        readme.Should().Contain("[COMMAND_SURFACE_PARITY.md](COMMAND_SURFACE_PARITY.md)");
        readme.Should().Contain("[SHORTCUT_PARITY_MATRIX.md](SHORTCUT_PARITY_MATRIX.md)");
        readme.Should().Contain("[FIDELITY_CONTRACT.md](FIDELITY_CONTRACT.md)");
        readme.Should().Contain("[XLSX_CORPUS_REPORT.md](XLSX_CORPUS_REPORT.md)");
        ProjectStatusReportLink().Matches(readme).Should().HaveCount(4);
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

    [GeneratedRegex(@"\[PROJECT_STATUS_REPORT_\d{4}-\d{2}-\d{2}\.md\]\(PROJECT_STATUS_REPORT_\d{4}-\d{2}-\d{2}\.md\)")]
    private static partial Regex ProjectStatusReportLink();
}
