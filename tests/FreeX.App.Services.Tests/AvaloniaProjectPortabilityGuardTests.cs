using System.Text.RegularExpressions;
using FluentAssertions;

namespace FreeX.App.Services.Tests;

public sealed class AvaloniaProjectPortabilityGuardTests
{
    private const RegexOptions DefaultRegexOptions = RegexOptions.Compiled | RegexOptions.CultureInvariant;

    private static readonly HashSet<string> SourceExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cs",
        ".csproj",
        ".props",
        ".targets"
    };

    private static readonly (string Description, Regex Pattern)[] ForbiddenPatterns =
    [
        ("System.Windows namespace", new(@"(?<![\w.])System\.Windows(?:\.[A-Za-z_]\w*)?(?![\w.])", DefaultRegexOptions)),
        ("Microsoft.Win32 namespace", new(@"(?<![\w.])Microsoft\.Win32(?:\.[A-Za-z_]\w*)?(?![\w.])", DefaultRegexOptions)),
        ("WinRT Windows namespace", new(@"(?<![\w.])Windows\.[A-Za-z_]\w*(?:\.[A-Za-z_]\w*)*(?![\w.])", DefaultRegexOptions)),
        ("FreeX.App.Host dependency", new(@"(?<![\w.])FreeX\.App\.Host(?![\w.])", DefaultRegexOptions)),
        ("FreeX.App.UI dependency", new(@"(?<![\w.])FreeX\.App\.UI(?![\w.])", DefaultRegexOptions)),
        ("UseWPF project marker", new(@"(?<![\w])UseWPF(?![\w])", DefaultRegexOptions | RegexOptions.IgnoreCase)),
        ("WindowsDesktop SDK", new(@"(?<![\w.])Microsoft\.NET\.Sdk\.WindowsDesktop(?![\w.])", DefaultRegexOptions)),
        ("Windows-targeted framework", new(@"(?<![\w.-])net\d+(?:\.\d+)?-windows(?:\d+(?:\.\d+)*)?(?![\w.-])", DefaultRegexOptions | RegexOptions.IgnoreCase)),
        ("Windows Forms project marker", new(@"(?<![\w])UseWindowsForms(?![\w])", DefaultRegexOptions | RegexOptions.IgnoreCase)),
        ("WindowsDesktop framework reference", new(@"(?<![\w.])Microsoft\.WindowsDesktop\.App(?:\.(?:WPF|WindowsForms))?(?![\w.])", DefaultRegexOptions))
    ];

    [Fact]
    public void AvaloniaProjectSources_DoNotReferenceWindowsDesktopDependencies()
    {
        var projectPath = RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "FreeX.App.Avalonia.csproj");
        var avaloniaRoot = Path.GetDirectoryName(projectPath)!;
        var repositoryRoot = Path.GetFullPath(Path.Combine(avaloniaRoot, "..", ".."));

        var violations = Directory.EnumerateFiles(avaloniaRoot, "*", SearchOption.AllDirectories)
            .Where(IsPortableSourceFile)
            .SelectMany(path => FindViolations(path, repositoryRoot))
            .OrderBy(violation => violation.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(violation => violation.LineNumber)
            .ThenBy(violation => violation.Description, StringComparer.Ordinal)
            .ToArray();

        violations.Should().BeEmpty(
            "FreeX.App.Avalonia is the macOS-capable host and must not acquire WPF, WindowsDesktop, Windows Forms, or Windows-only host/UI dependencies");
    }

    [Fact]
    public void ForbiddenPatterns_DoNotMatchPlainWindowsOnlyProse()
    {
        const string prose = "This note says Windows-only behavior without declaring a Windows desktop dependency.";

        var matches = ForbiddenPatterns
            .Where(pattern => pattern.Pattern.IsMatch(prose))
            .Select(pattern => pattern.Description)
            .ToArray();

        matches.Should().BeEmpty("plain prose is not a dependency marker");
    }

    private static bool IsPortableSourceFile(string path)
    {
        if (!SourceExtensions.Contains(Path.GetExtension(path)))
            return false;

        var segments = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return !segments.Contains("bin", StringComparer.OrdinalIgnoreCase)
            && !segments.Contains("obj", StringComparer.OrdinalIgnoreCase);
    }

    private static IEnumerable<SourceViolation> FindViolations(string path, string repositoryRoot)
    {
        var lineNumber = 0;
        foreach (var line in File.ReadLines(path))
        {
            lineNumber++;

            foreach (var (description, pattern) in ForbiddenPatterns)
            {
                if (pattern.IsMatch(line))
                    yield return new SourceViolation(
                        Path.GetRelativePath(repositoryRoot, path),
                        lineNumber,
                        description,
                        line.Trim());
            }
        }
    }

    private readonly record struct SourceViolation(
        string RelativePath,
        int LineNumber,
        string Description,
        string SourceLine)
    {
        public override string ToString() => $"{RelativePath}:{LineNumber}: {Description}: {SourceLine}";
    }
}
