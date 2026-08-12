using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using System.Windows.Controls;
using System.Windows.Media;
using Free.Shared.Ribbon;
using Free.Shared.Ribbon.Icons;
using FreeW.App.Host;

namespace FreeW.App.Host.Tests;

public sealed class RibbonCommandIconAssetTests
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
    public void FreeW_ribbon_commands_have_direct_svg_assets()
    {
        var iconDirectory = Path.Combine(AppContext.BaseDirectory, "Resources", "CommandIconsSvg");
        Directory.Exists(iconDirectory).Should().BeTrue("FreeW command SVG assets should be copied to the test output");

        var commandIds = WpfCommandIds()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        commandIds.Should().NotBeEmpty();

        var missing = commandIds
            .Select(id => new
            {
                CommandId = id,
                FileName = ToCommandIconSlug(id) + ".svg"
            })
            .Where(item => !HasCommandIconAsset(iconDirectory, item.FileName[..^4]))
            .Select(item => $"{item.CommandId} -> {item.FileName}")
            .ToArray();

        missing.Should().BeEmpty($"each visible FreeW command should resolve through a canonical or product SVG before shared fallback glyphs. Missing: {string.Join("; ", missing)}");
    }

    [Fact]
    public void FreeW_command_svg_assets_are_not_empty_shells()
    {
        var iconDirectory = Path.Combine(AppContext.BaseDirectory, "Resources", "CommandIconsSvg");

        var emptyShells = Directory
            .EnumerateFiles(iconDirectory, "*.svg")
            .Where(path => File.ReadAllText(path).TrimEnd().EndsWith("/>", StringComparison.Ordinal))
            .Select(Path.GetFileName)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        emptyShells.Should().BeEmpty("ribbon icons should contain visible geometry");
    }

    [Fact]
    public void FreeW_command_slug_strips_app_prefix()
    {
        ToCommandIconSlug("freew.accept-all").Should().Be("accept-all");
        ToCommandIconSlug("freew.align-center").Should().Be("align-center");
        RibbonCommandIconPolicy.ToCommandIconSlug("  FREEW.Accept & Reject  ", "freew.")
            .Should().Be("accept-and-reject");
    }

    [Fact]
    public void FreeW_project_links_canonical_FreeX_assets_for_output_and_publish()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        var projectPath = Path.Combine(root, "freew", "FreeW.App.Host", "FreeW.App.Host.csproj");
        var project = XDocument.Load(projectPath);
        var canonicalIcons = project
            .Descendants("Content")
            .Single(item => (string?)item.Attribute("Include") ==
                @"..\..\src\FreeX.Ribbon.Definitions\Resources\CommandIconsSvg\**\*.svg");

        ((string?)canonicalIcons.Attribute("Link")).Should().Be(
            @"Resources\CommandIconsSvg\%(RecursiveDir)%(Filename)%(Extension)");
        ((string?)canonicalIcons.Attribute("CopyToOutputDirectory")).Should().Be("PreserveNewest");
        ((string?)canonicalIcons.Attribute("CopyToPublishDirectory")).Should().Be("PreserveNewest");

        foreach (var alias in new[]
        {
            "align-center.svg", "chart.svg", "datetime.svg", "font-dialog.svg", "highlight.svg", "zoom-dialog.svg",
            "reject-all.svg", "style-heading1.svg", "style-heading2.svg", "style-title.svg"
        })
            File.Exists(Path.Combine(root, "freew", "FreeW.Ribbon.Definitions", "Resources", "CommandIconsSvg", alias)).Should().BeFalse();
    }

    [Fact]
    public void FreeW_exact_duplicate_aliases_resolve_through_Wpf_loader_and_publish()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        var iconDirectory = Path.Combine(root, "freew", "FreeW.Ribbon.Definitions", "Resources", "CommandIconsSvg");
        var project = XDocument.Load(Path.Combine(root, "freew", "FreeW.App.Host", "FreeW.App.Host.csproj"));
        var localIcons = project
            .Descendants("Content")
            .Single(item => (string?)item.Attribute("Include") ==
                @"..\FreeW.Ribbon.Definitions\Resources\CommandIconsSvg\**\*.svg");

        ((string?)localIcons.Attribute("Link")).Should().Be(
            @"Resources\CommandIconsSvg\%(RecursiveDir)%(Filename)%(Extension)");
        ((string?)localIcons.Attribute("CopyToOutputDirectory")).Should().Be("PreserveNewest");
        ((string?)localIcons.Attribute("CopyToPublishDirectory")).Should().Be("PreserveNewest");

        var duplicateGroups = Directory
            .EnumerateFiles(iconDirectory, "*.svg")
            .GroupBy(path => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))), StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => string.Join(", ", group.Select(Path.GetFileName).Order(StringComparer.OrdinalIgnoreCase)))
            .ToArray();
        duplicateGroups.Should().BeEmpty("FreeW's app-local command SVG directory must contain one file per exact payload");

        var candidateMethod = typeof(RibbonIconFactory).GetMethod(
            "GetCommandIconSlugCandidates",
            BindingFlags.NonPublic | BindingFlags.Static);
        candidateMethod.Should().NotBeNull();

        foreach (var alias in RemovedExactDuplicateSlugs)
        {
            var canonical = RibbonCommandIconSlugAliases.GetCandidates(alias).First();
            var candidates = ((IEnumerable<string>)candidateMethod!.Invoke(null, [alias])!).ToArray();

            candidates.First().Should().Be(canonical, alias);
            File.Exists(Path.Combine(iconDirectory, canonical + ".svg")).Should().BeTrue(alias);
            File.Exists(Path.Combine(iconDirectory, alias + ".svg")).Should().BeFalse(alias);
        }
    }

    [Fact]
    public void FreeW_document_views_use_shared_run_clone_helper()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        foreach (var relativePath in new[]
        {
            Path.Combine("freew", "FreeW.App.Host", "Editing", "DocumentView.cs"),
            Path.Combine("freew", "FreeW.App.Avalonia", "Editing", "DocumentView.cs")
        })
        {
            var source = File.ReadAllText(Path.Combine(root, relativePath));

            source.Should().Contain("RevisionEditPlanner.CloneRunWithText");
            source.Should().NotContain("CloneRunWithText(ModelRun");
            source.Should().NotContain("CloneRunWithText(Run");
        }
    }

    [StaFact]
    public void Shared_renderer_prefers_FreeW_app_local_svg_artwork()
    {
        var previousElementResolver = Free.Shared.Ribbon.Wpf.RibbonIconFactory.CommandIconElementResolver;
        var previousKindResolver = Free.Shared.Ribbon.Wpf.RibbonIconFactory.CommandIconKindResolver;

        try
        {
            FreeWRibbonIcons.Install();

            var icon = Free.Shared.Ribbon.Wpf.RibbonIconFactory.CreateCommandIcon(
                "freew.accept-all",
                new RibbonCommandIcon(RibbonCommandIconKind.Generic),
                size: 32,
                Brushes.Black);

            icon.Should().BeOfType<Image>("FreeW's installed SVG loader should return app-local image artwork");
        }
        finally
        {
            Free.Shared.Ribbon.Wpf.RibbonIconFactory.CommandIconElementResolver = previousElementResolver;
            Free.Shared.Ribbon.Wpf.RibbonIconFactory.CommandIconKindResolver = previousKindResolver;
        }
    }

    [Fact]
    public void FreeW_icon_wrapper_keeps_svg_resolution_and_delegates_geometry_fallback()
    {
        var source = File.ReadAllText(Path.Combine(
            TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx"),
            "freew",
            "FreeW.App.Host",
            "Ribbon",
            "RibbonIconFactory.cs"));

        source.Should().Contain("SvgCommandIconLoader");
        source.Should().Contain("RibbonCommandIconSlugAliases.GetCandidates(slug)");
        source.Should().Contain("RibbonCommandIconPolicy.ToCommandIconSlug(text, \"freew.\")");
        source.Should().Contain("SharedRibbonIconFactory.CreateIcon(fallbackIcon, size, glyphBrush)");
        source.Should().NotContain("new System.Text.StringBuilder");
        source.Should().NotContain("RibbonIconDefinitions.Resolve(");
        source.Should().NotContain("DrawElement(");
        source.Should().NotContain("Geometry.Parse(");
        source.Should().NotContain("new Canvas");
    }

    private static IEnumerable<string> WpfCommandIds() =>
        CommandIds(FreeW.Ribbon.Definitions.FreeWRibbon.Build(FreeW.Ribbon.Definitions.FreeWRibbonCapabilities.Wpf))
            .Select(id => id.Value)
            .Where(id => !string.IsNullOrWhiteSpace(id));

    private static bool HasCommandIconAsset(string iconDirectory, string slug) =>
        RibbonCommandIconSlugAliases.GetCandidates(slug)
            .SelectMany(candidate => new[] { candidate, candidate + "-small", candidate + "-large" })
            .Select(candidate => Path.Combine(iconDirectory, candidate + ".svg"))
            .Any(File.Exists);

    private static IEnumerable<RibbonCommandId> CommandIds(RibbonDefinition definition)
    {
        foreach (var control in definition.Tabs.SelectMany(tab => tab.Groups).SelectMany(group => group.Controls))
        {
            if (!string.IsNullOrWhiteSpace(control.CommandId.Value))
                yield return control.CommandId;

            foreach (var id in MenuCommandIds(control))
                yield return id;
        }
    }

    private static IEnumerable<RibbonCommandId> MenuCommandIds(RibbonControl control) => control switch
    {
        RibbonDropdown dropdown => MenuCommandIds(dropdown.Menu.Items),
        RibbonSplitButton splitButton => MenuCommandIds(splitButton.Menu.Items),
        _ => Enumerable.Empty<RibbonCommandId>()
    };

    private static IEnumerable<RibbonCommandId> MenuCommandIds(IEnumerable<RibbonMenuItem> items)
    {
        foreach (var item in items)
        {
            if (item.CommandId is { } id && !string.IsNullOrWhiteSpace(id.Value))
                yield return id;

            foreach (var child in MenuCommandIds(item.Children))
                yield return child;
        }
    }

    private static string ToCommandIconSlug(string commandId)
    {
        var method = typeof(RibbonIconFactory).GetMethod(
            "ToCommandIconSlug",
            BindingFlags.NonPublic | BindingFlags.Static);

        method.Should().NotBeNull();
        return (string)method!.Invoke(null, [commandId])!;
    }

}
