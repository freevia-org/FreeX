using System.Xml.Linq;
namespace FreeW.App.Avalonia.Tests;

public sealed class RibbonCommandIconPackagingTests
{
    [Fact]
    public void Canonical_command_icons_are_linked_for_Avalonia_output_and_publish()
    {
        var project = XDocument.Load(FindRepositoryFile("freew", "FreeW.App.Avalonia", "FreeW.App.Avalonia.csproj"));
        foreach (var include in new[]
        {
            @"..\..\src\FreeX.Ribbon.Definitions\Resources\CommandIconsSvg\**\*.svg",
            @"..\FreeW.App.Host\Resources\CommandIconsSvg\**\*.svg",
        })
        {
            var canonicalIcons = project
                .Descendants("Content")
                .Single(item => (string?)item.Attribute("Include") == include);

            ((string?)canonicalIcons.Attribute("Link")).Should().Be(
                @"Resources\CommandIconsSvg\%(RecursiveDir)%(Filename)%(Extension)");
            ((string?)canonicalIcons.Attribute("CopyToOutputDirectory")).Should().Be("PreserveNewest");
            ((string?)canonicalIcons.Attribute("CopyToPublishDirectory")).Should().Be("PreserveNewest");
        }
    }

    [Fact]
    public void Deleted_command_aliases_have_canonical_targets_in_Avalonia_output()
    {
        var iconDirectory = Path.Combine(AppContext.BaseDirectory, "Resources", "CommandIconsSvg");
        Directory.Exists(iconDirectory).Should().BeTrue("canonical command SVGs should be copied to Avalonia test output");

        var missing = new[]
        {
            (Alias: "add-watch", Target: "watch-add"),
            (Alias: "delete-watch", Target: "watch-delete"),
            (Alias: "reject-all", Target: "reject-change"),
            (Alias: "style-heading1", Target: "heading-1"),
            (Alias: "style-heading2", Target: "heading-2"),
            (Alias: "style-title", Target: "title"),
        }
        .Where(pair => !File.Exists(Path.Combine(iconDirectory, pair.Target + ".svg")))
        .Select(pair => $"{pair.Alias} -> {pair.Target}.svg")
        .ToArray();

        missing.Should().BeEmpty("Avalonia must package the canonical SVG target for each deleted alias");
    }

    private static string FindRepositoryFile(params string[] parts)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            var candidate = Path.Combine(new[] { directory.FullName }.Concat(parts).ToArray());
            if (File.Exists(candidate))
                return candidate;
        }

        throw new FileNotFoundException($"Could not find repository file from {AppContext.BaseDirectory}.");
    }
}
