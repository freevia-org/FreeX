using FluentAssertions;

namespace FreeX.Core.Model.Tests;

public sealed class CorePortabilityTests
{
    private static readonly string[] ForbiddenCoreTokens =
    [
        "using System.Windows",
        "System.Windows.",
        "using Microsoft.Win32",
        "Microsoft.Win32.",
        "Windows.ApplicationModel",
        "Windows.Storage",
        "System.Runtime.InteropServices.ComImport",
        "[ComImport",
        "[DllImport",
        "using System.Drawing",
        "System.Drawing.",
        "OxyPlot.Wpf",
        "PDFsharp-WPF",
        "SharpVectors.Wpf"
    ];

    [Fact]
    public void CoreProjects_RemainPortableAndFreeOfWpfWindowsDependencies()
    {
        var repoRoot = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx");
        var srcRoot = Path.Combine(repoRoot, "src");

        var matches = new List<string>();
        foreach (var coreDirectory in Directory.EnumerateDirectories(srcRoot, "FreeX.Core.*"))
        {
            foreach (var file in Directory.EnumerateFiles(coreDirectory, "*.csproj")
                         .Concat(Directory.EnumerateFiles(coreDirectory, "*.cs", SearchOption.AllDirectories)))
            {
                var relativePath = Path.GetRelativePath(repoRoot, file).Replace(Path.DirectorySeparatorChar, '/');
                if (relativePath.Contains("/bin/", StringComparison.Ordinal) ||
                    relativePath.Contains("/obj/", StringComparison.Ordinal))
                {
                    continue;
                }

                var source = File.ReadAllText(file);
                if (file.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
                {
                    AddIfContains(matches, relativePath, source, "<TargetFramework>net10.0-windows");
                    AddIfContains(matches, relativePath, source, "<UseWPF>true</UseWPF>");
                    AddIfContains(matches, relativePath, source, "PackageReference Include=\"OxyPlot.Wpf\"");
                    AddIfContains(matches, relativePath, source, "PackageReference Include=\"PDFsharp-WPF\"");
                    AddIfContains(matches, relativePath, source, "PackageReference Include=\"SharpVectors.Wpf\"");
                }

                foreach (var forbiddenToken in ForbiddenCoreTokens)
                {
                    AddIfContains(matches, relativePath, source, forbiddenToken);
                }
            }
        }

        matches.Should().BeEmpty("Core.* must stay buildable on macOS and other non-Windows platforms without Windows drawing APIs");
    }

    private static void AddIfContains(List<string> matches, string relativePath, string source, string token)
    {
        if (source.Contains(token, StringComparison.Ordinal))
            matches.Add($"{relativePath}: {token}");
    }
}
