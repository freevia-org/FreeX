using System.IO;
using System.Text.RegularExpressions;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed partial class SourceTodoDocumentationTests
{
    [Fact]
    public void SourceText_DoesNotContainMojibake()
    {
        var repoDirectory = WorkspaceFileLocator.FindWorkspaceRoot();
        var sourceDirectory = Path.Combine(repoDirectory, "src");
        var invalidLines = Directory
            .EnumerateFiles(sourceDirectory, "*.*", SearchOption.AllDirectories)
            .Where(IsTrackedSourceFile)
            .Where(path => SourceTextExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
            .SelectMany(path => FindMojibake(repoDirectory, path))
            .ToList();

        invalidLines.Should().BeEmpty("source text shown to users and maintainers should not contain garbled UTF-8/Windows-1252 mojibake");
    }

    [Fact]
    public void DocumentationText_DoesNotContainMojibake()
    {
        var repoDirectory = WorkspaceFileLocator.FindWorkspaceRoot();
        var docsDirectory = Path.Combine(repoDirectory, "docs");
        var invalidLines = Directory
            .EnumerateFiles(docsDirectory, "*.md", SearchOption.AllDirectories)
            .SelectMany(path => FindMojibake(repoDirectory, path))
            .ToList();

        invalidLines.Should().BeEmpty("build, release, and parity documentation should stay readable after branch handoffs");
    }

    [Fact]
    public void SourceDeferredWorkMarkers_LinkToTrackingDocumentation()
    {
        var repoDirectory = WorkspaceFileLocator.FindWorkspaceRoot();
        var sourceDirectory = Path.Combine(repoDirectory, "src");
        var invalidMarkers = Directory
            .EnumerateFiles(sourceDirectory, "*.cs", SearchOption.AllDirectories)
            .Where(IsTrackedSourceFile)
            .SelectMany(path => FindInvalidMarkers(repoDirectory, path))
            .ToList();

        invalidMarkers.Should().BeEmpty(
            "source TODO/FIXME/HACK/XXX markers must use '// TODO(owner): note (ref: docs/file.md#anchor)' or an exact documented legacy mapping so deferred work remains traceable");
    }

    private static bool IsTrackedSourceFile(string path)
    {
        var segments = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return !segments.Contains("bin", StringComparer.OrdinalIgnoreCase) &&
            !segments.Contains("obj", StringComparer.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> FindInvalidMarkers(string repoDirectory, string path)
    {
        var relativePath = Path.GetRelativePath(repoDirectory, path).Replace('\\', '/');
        var lines = File.ReadLines(path).Select((line, index) => new { Line = line, Number = index + 1 });

        foreach (var line in lines)
        {
            if (!DeferredWorkMarkerRegex().IsMatch(line.Line))
                continue;

            if (DocumentedDeferredWorkMarkerRegex().IsMatch(line.Line) ||
                IsDocumentedLegacyMarker(repoDirectory, relativePath, line.Line))
            {
                continue;
            }

            yield return $"{relativePath}:{line.Number}: {line.Line.Trim()}";
        }
    }

    private static bool IsDocumentedLegacyMarker(string repoDirectory, string relativePath, string line)
    {
        var marker = LegacyDeferredWorkMarkers.SingleOrDefault(candidate =>
            candidate.SourcePath == relativePath &&
            line.Contains(candidate.Marker, StringComparison.Ordinal));
        if (marker is null)
            return false;

        var documentPath = Path.Combine(
            repoDirectory,
            marker.DocumentPath.Replace('/', Path.DirectorySeparatorChar));
        return File.Exists(documentPath) &&
            File.ReadAllText(documentPath).Contains(marker.Heading, StringComparison.Ordinal);
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

    private static readonly string[] SourceTextExtensions = [".cs", ".xaml", ".props", ".targets", ".resx"];

    private static readonly LegacyDeferredWorkMarker[] LegacyDeferredWorkMarkers =
    [
        new(
            "src/FreeX.Core.Formula/FormulaRewriter.cs",
            "// TODO(H28 3-D sheet-span refs, partially addressed — F1 defined-names-span-rowcol-shift):",
            "docs/planning/formula-deferred-work.md",
            "## H28 3-D Sheet-Span Structural Rewrites")
    ];

    [GeneratedRegex(@"//\s*(TODO|FIXME|HACK|XXX)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DeferredWorkMarkerRegex();

    [GeneratedRegex(@"//\s*(TODO|FIXME|HACK|XXX)\([A-Za-z0-9_.-]+\):\s+\S.+\s+\(ref:\s+docs/[A-Za-z0-9_./-]+\.md#[^)]+\)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DocumentedDeferredWorkMarkerRegex();

    [GeneratedRegex("[\\uFFFD]|(?:\\u00C3|\\u00C2|\\u00E2)[\\u0080-\\u00BF\\u201A-\\u201E\\u20AC\\u2122\\u0152\\u0161\\u017D\\u017E\\u02C6\\u2030\\u2039\\u203A\\u2018-\\u201D]+", RegexOptions.CultureInvariant)]
    private static partial Regex MojibakeRegex();

    private sealed record LegacyDeferredWorkMarker(
        string SourcePath,
        string Marker,
        string DocumentPath,
        string Heading);
}
