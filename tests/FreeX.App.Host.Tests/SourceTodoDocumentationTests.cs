using System.IO;
using System.Text.RegularExpressions;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed partial class SourceTodoDocumentationTests
{
    [Fact]
    public void SourceText_DoesNotContainMojibake()
    {
        var repoDirectory = FindRepoDirectory();
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
        var repoDirectory = FindRepoDirectory();
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
        var repoDirectory = FindRepoDirectory();
        var sourceDirectory = Path.Combine(repoDirectory, "src");
        var invalidMarkers = Directory
            .EnumerateFiles(sourceDirectory, "*.cs", SearchOption.AllDirectories)
            .Where(IsTrackedSourceFile)
            .SelectMany(path => FindInvalidMarkers(repoDirectory, path))
            .ToList();

        invalidMarkers.Should().BeEmpty(
            "source TODO/FIXME/HACK/XXX markers must use '// TODO(owner): note (ref: docs/file.md#anchor)' so deferred work remains traceable");
    }

    private static string FindRepoDirectory()
    {
        var hostDirectory = Path.GetDirectoryName(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"))!;
        var sourceDirectory = Directory.GetParent(hostDirectory)!.FullName;
        return Directory.GetParent(sourceDirectory)!.FullName;
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

            if (!DocumentedDeferredWorkMarkerRegex().IsMatch(line.Line))
                yield return $"{relativePath}:{line.Number}: {line.Line.Trim()}";
        }
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

    [GeneratedRegex(@"//\s*(TODO|FIXME|HACK|XXX)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DeferredWorkMarkerRegex();

    [GeneratedRegex(@"//\s*(TODO|FIXME|HACK|XXX)\([A-Za-z0-9_.-]+\):\s+\S.+\s+\(ref:\s+docs/[A-Za-z0-9_./-]+\.md#[^)]+\)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DocumentedDeferredWorkMarkerRegex();

    [GeneratedRegex("[\\uFFFD]|(?:\\u00C3|\\u00C2|\\u00E2)[\\u0080-\\u00BF\\u201A-\\u201E\\u20AC\\u2122\\u0152\\u0161\\u017D\\u017E\\u02C6\\u2030\\u2039\\u203A\\u2018-\\u201D]+", RegexOptions.CultureInvariant)]
    private static partial Regex MojibakeRegex();
}
