using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows.Controls;
using System.Windows.Media;
using Free.Shared.Ribbon;
using FreeW.App.Host;

namespace FreeW.App.Host.Tests;

public sealed class RibbonCommandIconAssetTests
{
    [Fact]
    public void FreeW_ribbon_commands_have_direct_svg_assets()
    {
        var iconDirectory = Path.Combine(AppContext.BaseDirectory, "Resources", "CommandIconsSvg");
        Directory.Exists(iconDirectory).Should().BeTrue("FreeW command SVG assets should be copied to the test output");

        var commandIds = WpfCommandIds()
            .Concat(AvaloniaCommandIds())
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
            .Where(item => !File.Exists(Path.Combine(iconDirectory, item.FileName)))
            .Select(item => $"{item.CommandId} -> {item.FileName}")
            .ToArray();

        missing.Should().BeEmpty("each visible FreeW command should resolve to app-local SVG artwork before shared fallback glyphs");
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

    private static IEnumerable<string> WpfCommandIds() =>
        CommandIds(FreeWRibbon.Build())
            .Select(id => id.Value)
            .Where(id => !string.IsNullOrWhiteSpace(id));

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

    private static IEnumerable<string> AvaloniaCommandIds()
    {
        var sourcePath = Path.Combine(
            FindRepositoryRoot(),
            "freew",
            "FreeW.App.Avalonia",
            "Ribbon",
            "FreeWRibbon.cs");

        var source = File.ReadAllText(sourcePath);
        return Regex
            .Matches(source, "\"(freew\\.[a-z0-9.-]+)\"")
            .Select(match => match.Groups[1].Value);
    }

    private static string ToCommandIconSlug(string commandId)
    {
        var method = typeof(RibbonIconFactory).GetMethod(
            "ToCommandIconSlug",
            BindingFlags.NonPublic | BindingFlags.Static);

        method.Should().NotBeNull();
        return (string)method!.Invoke(null, [commandId])!;
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "FreeW.slnx")))
                return directory.FullName;
        }

        throw new DirectoryNotFoundException("Could not locate repository root from test output directory.");
    }
}
