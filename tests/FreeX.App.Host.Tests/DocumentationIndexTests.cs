using System.IO;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed partial class DocumentationIndexTests
{








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
