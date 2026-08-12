using System.Reflection;
using System.Xml.Linq;
using Free.Shared.Ribbon.Avalonia;
using Free.Shared.Ribbon.Icons;

namespace FreeW.App.Avalonia.Tests;

public sealed class RibbonCommandIconPackagingTests
{
    private static readonly string[] RemovedExactDuplicateSlugs =
    [
        "custom-paragraph-spacing", "customize-colors", "customize-fonts", "draftview",
        "image-brightness-minus40", "image-brightness-plus40", "image-saturation-0", "image-saturation-200",
        "image-transparency-25", "image-transparency-75", "shape-flip-horizontal", "shape-flip-vertical",
        "shape-position", "shape-rotate-left90", "shape-rotate-right90", "shape-rotate",
        "shape-wrap", "shape-wrap-behind", "shape-wrap-front", "shape-wrap-inline", "shape-wrap-square",
        "shape-wrap-tight", "shape-wrap-top-bottom", "index-insert", "index-mark", "insert-quickpart",
        "merge-rule-fill-in", "merge-rule-ref", "merge-rule-set", "merge-rule-skip-record-if",
        "multilevel-list", "multilevel-preset-0", "multilevel-preset-1", "multilevel-preset-2", "printlayout",
        "reset-style-set", "reviewingpane", "toc", "tof", "weblayout",
    ];

    [Fact]
    public void Canonical_command_icons_are_linked_for_Avalonia_output_and_publish()
    {
        var project = XDocument.Load(FindRepositoryFile("freew", "FreeW.App.Avalonia", "FreeW.App.Avalonia.csproj"));
        foreach (var include in new[]
        {
            @"..\..\src\FreeX.Ribbon.Definitions\Resources\CommandIconsSvg\**\*.svg",
            @"..\FreeW.Ribbon.Definitions\Resources\CommandIconsSvg\**\*.svg",
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

    [Fact]
    public void Exact_duplicate_aliases_resolve_through_Avalonia_loader_and_packaging()
    {
        var iconDirectory = Path.Combine(AppContext.BaseDirectory, "Resources", "CommandIconsSvg");
        Directory.Exists(iconDirectory).Should().BeTrue();

        foreach (var alias in RemovedExactDuplicateSlugs)
        {
            var canonical = RibbonCommandIconSlugAliases.GetCandidates(alias).First();
            var candidates = RibbonCommandIconPolicy.GetCommandIconSlugCandidates(alias).ToArray();

            candidates.First().Should().Be(canonical, alias);
            File.Exists(Path.Combine(iconDirectory, canonical + ".svg")).Should().BeTrue(alias);
            File.Exists(Path.Combine(iconDirectory, alias + ".svg")).Should().BeFalse(alias);
        }
    }

    [Fact]
    public void Chart_quick_layout_labels_resolve_to_packaged_Wpf_assets()
    {
        var iconDirectory = Path.Combine(AppContext.BaseDirectory, "Resources", "CommandIconsSvg");

        for (var id = 1; id <= 9; id++)
        {
            var labelSlug = $"layout-{id}";
            var canonical = $"chart-quick-layout-{id}";
            RibbonCommandIconPolicy.GetCommandIconSlugCandidates(labelSlug)
                .First().Should().Be(canonical);
            File.Exists(Path.Combine(iconDirectory, canonical + ".svg"))
                .Should().BeTrue($"Layout {id} must reuse WPF's chart quick-layout asset");
        }
    }

    private static string FindRepositoryFile(params string[] parts) =>
        TestWorkspaceFileLocator.Find(parts);
}
