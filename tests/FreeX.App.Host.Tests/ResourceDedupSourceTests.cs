using System.IO;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class ResourceDedupSourceTests
{
    [Fact]
    public void MainWindowResources_UsesThemeResourcesForTitleBarBrush()
    {
        var source = DialogSourceTestSupport.ReadHostSources("Resources\\MainWindowResources.xaml");

        source.Should().Contain("Source=\"ThemeResources.xaml\"");
        source.Should().NotContain("<SolidColorBrush x:Key=\"FreeXTitleBarBrush\"");
    }

    [Fact]
    public void CanonicalWatchIcons_ArePackagedForBothOutputAndPublish()
    {
        var root = FindRepositoryRoot();
        var iconDirectory = Path.Combine(root, "src", "FreeX.Ribbon.Definitions", "Resources", "CommandIconsSvg");
        var project = File.ReadAllText(Path.Combine(root, "src", "FreeX.Ribbon.Definitions", "FreeX.Ribbon.Definitions.csproj"));

        foreach (var fileName in new[] { "watch-add.svg", "watch-delete.svg" })
            File.Exists(Path.Combine(iconDirectory, fileName)).Should().BeTrue();

        foreach (var alias in new[] { "add-watch.svg", "delete-watch.svg" })
            File.Exists(Path.Combine(iconDirectory, alias)).Should().BeFalse();

        project.Should().Contain("<CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>");
        project.Should().Contain("<CopyToPublishDirectory>PreserveNewest</CopyToPublishDirectory>");
        project.Should().Contain("Resources\\CommandIconsSvg\\**\\*.svg");
    }

    [Fact]
    public void CommandIconAssets_RemoveOnlyTheFourExactDuplicateShortNames()
    {
        var root = FindRepositoryRoot();
        var iconDirectory = Path.Combine(root, "src", "FreeX.Ribbon.Definitions", "Resources", "CommandIconsSvg");
        var expectedCanonicalHashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["allow-users-to-edit-ranges.svg"] = "747FE620B2E4FAAB2B4A62E4F26547CF863FBD5B564948E033EFDE921EC3E8AB",
            ["date-time.svg"] = "1360D5FC601F63527D52860D91D7C85CEA203804A75189D4D444AF8382DDE5DE",
            ["lookup-reference.svg"] = "0B5B879CC43EB9FACB0C0B4B073E0523346E03C44E027D5F7529EB5DE2C2EB55",
            ["math-trig.svg"] = "FA1C7A155D2A81CBD256111BE154D973834B7779786BC5EEF452D0CA8DFC716F"
        };
        var removedShortNames = new[]
        {
            "allow-edit-ranges.svg", "date-and-time.svg", "lookup-and-reference.svg", "math-and-trig.svg"
        };

        foreach (var (fileName, expectedHash) in expectedCanonicalHashes)
        {
            var path = Path.Combine(iconDirectory, fileName);
            File.Exists(path).Should().BeTrue(path);
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path)))
                .Should().Be(expectedHash);
        }

        foreach (var fileName in removedShortNames)
            File.Exists(Path.Combine(iconDirectory, fileName)).Should().BeFalse(fileName);

        var duplicateGroups = Directory
            .EnumerateFiles(iconDirectory, "*.svg")
            .GroupBy(path => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path))))
            .Where(group => group.Count() > 1)
            .Select(group => string.Join(", ", group.Select(Path.GetFileName).Order(StringComparer.OrdinalIgnoreCase)))
            .ToList();

        duplicateGroups.Should().BeEmpty("command SVG payloads must have one source filename");
    }

    private static string FindRepositoryRoot() =>
        TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx");
}
