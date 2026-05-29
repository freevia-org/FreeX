using System.IO;
using System.Text.RegularExpressions;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed partial class DocumentationEncodingTests
{
    [Fact]
    public void CurrentUserFacingDocs_DoNotContainMojibake()
    {
        var docsDirectory = Path.GetDirectoryName(WorkspaceFileLocator.Find("docs", "OUTSTANDING_BUILD.md"))!;
        var repoDirectory = Directory.GetParent(docsDirectory)!.FullName;

        var invalidLines = CurrentDocumentationFiles
            .SelectMany(file => FindMojibake(repoDirectory, Path.Combine(docsDirectory, file)))
            .ToList();

        invalidLines.Should().BeEmpty("current user-facing docs should stay readable after UTF-8/Windows-1252 round trips");
    }

    [Theory]
    [InlineData("Code Review Hardening \u00E2\u20AC\u201D 2026-05-28")]
    [InlineData("PRs (#33\u00E2\u20AC\u201C#44)")]
    [InlineData("quote \u00E2\u20AC\u0153text\u00E2\u20AC\u009D")]
    public void MojibakeDetector_CatchesCommonWindows1252Sequences(string text)
    {
        MojibakeRegex().IsMatch(text).Should().BeTrue();
    }

    private static IEnumerable<string> FindMojibake(string repoDirectory, string path)
    {
        var relativePath = Path.GetRelativePath(repoDirectory, path).Replace('\\', '/');
        var lines = File.ReadLines(path).Select((line, index) => new { Line = line, Number = index + 1 });

        foreach (var line in lines)
        {
            if (MojibakeRegex().IsMatch(line.Line))
                yield return $"{relativePath}:{line.Number}: {line.Line.Trim()}";
        }
    }

    private static readonly string[] CurrentDocumentationFiles =
    [
        "README.md",
        "OUTSTANDING_BUILD.md",
        "NEXT_PHASES_PLAN.md",
        "PROJECT_STATUS_REPORT_2026-05-28.md",
        "TEST_DISTRIBUTION_PLAN.md",
        "TROUBLESHOOTING.md",
        "USER_GUIDE.md",
    ];

    [GeneratedRegex("[\\uFFFD]|(?:\\u00C3|\\u00C2|\\u00E2)[\\u0080-\\u00BF\\u201A-\\u201E\\u20AC\\u2122\\u0152\\u0161\\u017D\\u017E\\u02C6\\u2030\\u2039\\u203A\\u2018-\\u201D]+", RegexOptions.CultureInvariant)]
    private static partial Regex MojibakeRegex();
}
