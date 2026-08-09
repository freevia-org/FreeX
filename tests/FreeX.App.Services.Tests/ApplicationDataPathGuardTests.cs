using System.Text.RegularExpressions;
using FluentAssertions;

namespace FreeX.App.Services.Tests;

public sealed class ApplicationDataPathGuardTests
{
    private static readonly Regex ApplicationDataSpecialFolderPattern =
        new(@"\bEnvironment\s*\.\s*SpecialFolder\s*\.\s*ApplicationData\b", RegexOptions.Compiled);

    [Fact]
    public void AppServicesSources_DoNotUseApplicationDataSpecialFolderOutsidePathProvider()
    {
        var projectPath = RepositoryFileLocator.Find("src", "FreeX.App.Services", "FreeX.App.Services.csproj");
        var servicesRoot = Path.GetDirectoryName(projectPath)!;
        var repositoryRoot = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx");
        var approvedProviderPath = Path.Combine(servicesRoot, "ApplicationDataPathProvider.cs");
        var hasApprovedProvider = File.Exists(approvedProviderPath);

        var violations = Directory.EnumerateFiles(servicesRoot, "*.cs", SearchOption.AllDirectories)
            .Where(IsSourceFile)
            .Where(path => !IsApprovedProvider(path, approvedProviderPath, hasApprovedProvider))
            .SelectMany(path => FindViolations(path, repositoryRoot))
            .OrderBy(violation => violation.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(violation => violation.LineNumber)
            .ToArray();

        violations.Should().BeEmpty(
            "FreeX.App.Services should route application data paths through ApplicationDataPathProvider so macOS uses ~/Library/Application Support");
    }

    private static bool IsSourceFile(string path)
    {
        var segments = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return !segments.Contains("bin", StringComparer.OrdinalIgnoreCase)
            && !segments.Contains("obj", StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsApprovedProvider(string path, string approvedProviderPath, bool hasApprovedProvider) =>
        hasApprovedProvider
        && string.Equals(
            Path.GetFullPath(path),
            Path.GetFullPath(approvedProviderPath),
            StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<SourceViolation> FindViolations(string path, string repositoryRoot)
    {
        var lineNumber = 0;
        foreach (var line in File.ReadLines(path))
        {
            lineNumber++;

            if (ApplicationDataSpecialFolderPattern.IsMatch(line))
                yield return new SourceViolation(
                    Path.GetRelativePath(repositoryRoot, path),
                    lineNumber,
                    line.Trim());
        }
    }

    private readonly record struct SourceViolation(
        string RelativePath,
        int LineNumber,
        string SourceLine)
    {
        public override string ToString() =>
            $"{RelativePath}:{LineNumber}: direct Environment.SpecialFolder.ApplicationData use: {SourceLine}";
    }
}
