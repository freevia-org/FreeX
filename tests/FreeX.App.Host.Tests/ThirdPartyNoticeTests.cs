using System.IO;
using System.Text.Json;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class ThirdPartyNoticeTests
{
    [Fact]
    public void ThirdPartyNotices_ListEveryRestoredNuGetPackage()
    {
        var notices = WorkspaceFileLocator.ReadAllText("THIRD_PARTY_NOTICES.md");
        var packages = FindRestoredPackages();

        packages.Should().NotBeEmpty();
        foreach (var package in packages)
        {
            notices.Should().Contain($"| {package.Name} | {package.Version} |");
        }
    }

    [Fact]
    public void ThirdPartyNotices_CallOutLicenseTextAndFluentAssertionsCommercialUse()
    {
        var notices = WorkspaceFileLocator.ReadAllText("THIRD_PARTY_NOTICES.md");
        var licenses = WorkspaceFileLocator.ReadAllText("THIRD_PARTY_LICENSES.md");

        notices.Should().Contain("[THIRD_PARTY_LICENSES.md](THIRD_PARTY_LICENSES.md)");
        notices.Should().Contain("FluentAssertions 8.9.0 is a test/development dependency only");
        notices.Should().MatchRegex(@"Versions 8 and later require a paid\s+license for commercial use");
        notices.Should().MatchRegex(@"replace this dependency,\s+use a suitably licensed version, or confirm the required license has been\s+obtained");
        notices.Should().Contain("Microsoft.NET.ILLink.Tasks `THIRD-PARTY-NOTICES.TXT`");
        notices.Should().Contain("Coverage: every restored package is listed below.");
        notices.Should().Contain("LGPL Runtime Distribution Requirements");
        notices.Should().Contain("permit replacement/relinking as required by the applicable");
        licenses.Should().Contain("Microsoft.NET.ILLink.Tasks Package Third-Party Notices");
        licenses.Should().Contain("Apache License");
        licenses.Should().Contain("SharpVectors.Wpf Package License File");
        licenses.Should().Contain("FluentAssertions Package License");
        licenses.Should().Contain("COMMUNITY LICENSE AGREEMENT");
        licenses.Should().Contain("(for Non-Commercial Use)");
        licenses.Should().Contain("requires a paid Commercial License");
    }

    private static IReadOnlyCollection<(string Name, string Version)> FindRestoredPackages()
    {
        var root = WorkspaceFileLocator.FindWorkspaceRoot();
        var packages = new SortedSet<(string Name, string Version)>();
        foreach (var assetsPath in Directory.EnumerateFiles(root, "project.assets.json", SearchOption.AllDirectories))
        {
            if (!IsUnder(root, assetsPath, "src") && !IsUnder(root, assetsPath, "tests"))
                continue;

            using var document = JsonDocument.Parse(File.ReadAllText(assetsPath));
            foreach (var library in document.RootElement.GetProperty("libraries").EnumerateObject())
            {
                if (!library.Value.TryGetProperty("type", out var type) ||
                    !string.Equals(type.GetString(), "package", StringComparison.OrdinalIgnoreCase))
                    continue;

                var parts = library.Name.Split('/', 2);
                if (parts.Length == 2)
                    packages.Add((parts[0], parts[1]));
            }
        }

        return packages;
    }

    private static bool IsUnder(string root, string path, string segment)
    {
        var relative = Path.GetRelativePath(root, path);
        return relative.StartsWith(segment + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
               relative.StartsWith(segment + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }
}
