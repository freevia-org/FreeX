using System.Text.RegularExpressions;
using FluentAssertions;

namespace FreeX.App.Services.Tests;

public sealed class AppServicesPortabilityGuardTests
{
    private static readonly HashSet<string> SourceExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cs",
        ".csproj",
        ".props",
        ".targets"
    };

    private static readonly (string Description, Regex Pattern)[] ForbiddenPatterns =
    [
        ("System.Windows/WPF namespace", new(@"\bSystem\.Windows\b", RegexOptions.Compiled)),
        ("Microsoft.Win32 dependency", new(@"\bMicrosoft\.Win32\b", RegexOptions.Compiled)),
        ("WinRT Windows namespace", new(@"\bWindows\.", RegexOptions.Compiled)),
        ("FreeX.App.Host dependency", new(@"\bFreeX\.App\.Host\b", RegexOptions.Compiled)),
        ("FreeX.App.UI dependency", new(@"\bFreeX\.App\.UI\b", RegexOptions.Compiled)),
        ("WPF project marker", new(@"\bUseWPF\b|\bUseWpf\b", RegexOptions.Compiled)),
        ("Windows desktop SDK", new(@"\bMicrosoft\.NET\.Sdk\.WindowsDesktop\b", RegexOptions.Compiled)),
        ("Windows-targeted framework", new(@"\bnet\d+(?:\.\d+)?-windows\b", RegexOptions.Compiled | RegexOptions.IgnoreCase)),
        ("Windows Forms project marker", new(@"\bUseWindowsForms\b", RegexOptions.Compiled)),
        ("AppKit namespace", new(@"\bAppKit\b", RegexOptions.Compiled)),
        ("Foundation namespace", new(@"\bFoundation\b", RegexOptions.Compiled)),
        ("ObjCRuntime namespace", new(@"\bObjCRuntime\b", RegexOptions.Compiled)),
        ("NSUrl native type", new(@"\bNSUrl\b", RegexOptions.Compiled)),
        ("NSData native type", new(@"\bNSData\b", RegexOptions.Compiled)),
        ("NSError native type", new(@"\bNSError\b", RegexOptions.Compiled))
    ];

    [Fact]
    public void AppServicesSources_DoNotReferenceWindowsOnlyDesktopDependencies()
    {
        var projectPath = RepositoryFileLocator.Find("src", "FreeX.App.Services", "FreeX.App.Services.csproj");
        var servicesRoot = Path.GetDirectoryName(projectPath)!;
        var repositoryRoot = Path.GetFullPath(Path.Combine(servicesRoot, "..", ".."));

        var violations = Directory.EnumerateFiles(servicesRoot, "*", SearchOption.AllDirectories)
            .Where(IsPortableSourceFile)
            .SelectMany(path => FindViolations(path, repositoryRoot))
            .OrderBy(violation => violation.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(violation => violation.LineNumber)
            .ThenBy(violation => violation.Description, StringComparer.Ordinal)
            .ToArray();

        violations.Should().BeEmpty(
            "FreeX.App.Services must stay portable for the Avalonia/macOS port; keep WPF and Windows-only dependencies in host/UI projects");
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
