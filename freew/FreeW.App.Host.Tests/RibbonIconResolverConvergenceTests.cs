using Free.Shared.Ribbon;
using FreeW.App.Host;
using FreeW.Ribbon.Definitions;

namespace FreeW.App.Host.Tests;

public sealed class RibbonIconResolverConvergenceTests
{
    [Theory]
    [InlineData("freew.custom-margins", RibbonCommandIconKind.Margins)]
    [InlineData("freew.more-paper-sizes", RibbonCommandIconKind.OnePage)]
    [InlineData("freew.columns-one", RibbonCommandIconKind.TextColumns)]
    [InlineData("freew.columns-more", RibbonCommandIconKind.TextColumns)]
    [InlineData("freew.hyphenation-auto", RibbonCommandIconKind.Hyphenation)]
    public void Migrated_menu_icons_are_owned_by_the_definition(
        string commandId,
        RibbonCommandIconKind expectedKind)
    {
        var item = EnumerateMenuItems(FreeWRibbon.Build(FreeWRibbonCapabilities.Wpf))
            .Single(candidate => candidate.CommandId?.Value == commandId);

        item.Icon.Should().Be(new RibbonCommandIcon(expectedKind));
        FreeWRibbonIcons.Resolve(commandId).Should().BeNull();
    }

    [Fact]
    public void Wpf_definition_icons_and_host_fallbacks_converge()
    {
        var controls = FreeWRibbon.Build(FreeWRibbonCapabilities.Wpf)
            .Tabs.SelectMany(tab => tab.Groups)
            .SelectMany(group => group.Controls)
            .Where(control => !string.IsNullOrWhiteSpace(control.CommandId.Value))
            .GroupBy(control => control.CommandId.Value, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.OrdinalIgnoreCase);

        var duplicateMetadata = FreeWRibbonIcons.Fallbacks
            .Where(fallback => controls.TryGetValue(fallback.Key, out var definitions) &&
                definitions.Any(control => control.Icon is { Kind: not RibbonCommandIconKind.Generic }))
            .Select(fallback => fallback.Key)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        duplicateMetadata.Should().BeEmpty(
            $"non-generic WPF definition icons must bypass the host fallback map: {string.Join(", ", duplicateMetadata)}");

        foreach (var fallback in FreeWRibbonIcons.Fallbacks)
        {
            fallback.Value.Should().NotBe(RibbonCommandIconKind.Generic, fallback.Key);
            FreeWRibbonIcons.Resolve(fallback.Key).Should().Be(fallback.Value, fallback.Key);
        }

        foreach (var commandId in controls
            .Where(pair => pair.Value.Any(control => control.Icon is { Kind: not RibbonCommandIconKind.Generic }))
            .Select(pair => pair.Key))
        {
            FreeWRibbonIcons.Resolve(commandId).Should().BeNull(
                $"{commandId} owns its icon in the WPF ribbon definition");
        }
    }

    private static IEnumerable<RibbonMenuItem> EnumerateMenuItems(RibbonDefinition definition) =>
        definition.Tabs
            .SelectMany(tab => tab.Groups)
            .SelectMany(group => group.Controls)
            .SelectMany(control => control switch
            {
                RibbonDropdown dropdown => EnumerateMenuItems(dropdown.Menu.Items),
                RibbonSplitButton splitButton => EnumerateMenuItems(splitButton.Menu.Items),
                _ => []
            });

    private static IEnumerable<RibbonMenuItem> EnumerateMenuItems(IEnumerable<RibbonMenuItem> items) =>
        items.SelectMany(item => new[] { item }.Concat(EnumerateMenuItems(item.Children)));
}
