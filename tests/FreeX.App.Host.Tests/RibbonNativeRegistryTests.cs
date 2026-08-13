using System.Linq;
using Free.Shared.Ribbon;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public class RibbonNativeRegistryTests
{
    [Fact]
    public void GeneratedHandlers_AreTypedDelegatesWithSemanticIds()
    {
        MainWindow.FreeXRibbonHandlers.Should().NotBeEmpty();
        MainWindow.FreeXRibbonHandlers.Values.Should().OnlyContain(binding => binding.Handler != null);
        MainWindow.FreeXRibbonHandlers.Keys.Should().OnlyContain(id =>
            !id.Contains('#', System.StringComparison.Ordinal) &&
            !id.Contains("_Click", System.StringComparison.Ordinal));
    }

    [Fact]
    public void HandlerMap_CoversCoreHomeCommands()
    {
        MainWindow.FreeXRibbonHandlers.Keys.Should().Contain(new[]
        {
            "Paste", "Cut", "Copy", "Bold", "Italic", "Underline"
        });
    }

    [Fact]
    public void HandlerMap_CoversEveryActionableDeclarativeRibbonCommand()
    {
        var definition = FreeXRibbon.Build();
        var comboIds = definition.Tabs
            .SelectMany(tab => tab.Groups)
            .SelectMany(group => group.Controls)
            .OfType<RibbonComboBox>()
            .Select(combo => combo.CommandId.Value)
            .ToHashSet(System.StringComparer.Ordinal);

        var missing = definition.Tabs
            .SelectMany(tab => tab.Groups)
            .SelectMany(group => group.Controls)
            .SelectMany(EnumerateCommandIds)
            .Where(id => !comboIds.Contains(id))
            .Distinct(System.StringComparer.Ordinal)
            .Where(id => !MainWindow.FreeXRibbonHandlers.ContainsKey(id))
            .OrderBy(id => id, System.StringComparer.Ordinal)
            .ToArray();

        missing.Should().BeEmpty(
            "every button and leaf menu item emitted by the declarative ribbon must resolve to a WPF handler");
    }

    private static IEnumerable<string> EnumerateCommandIds(RibbonControl control)
    {
        if (!string.IsNullOrEmpty(control.CommandId.Value))
            yield return control.CommandId.Value;

        var menu = control switch
        {
            RibbonSplitButton split => split.Menu,
            RibbonDropdown dropdown => dropdown.Menu,
            _ => null,
        };
        if (menu is null)
            yield break;

        foreach (var id in EnumerateMenuCommandIds(menu.Items))
            yield return id;
    }

    private static IEnumerable<string> EnumerateMenuCommandIds(IReadOnlyList<RibbonMenuItem> items)
    {
        foreach (var item in items)
        {
            if (item.CommandId is { } id && !string.IsNullOrEmpty(id.Value))
                yield return id.Value;

            foreach (var childId in EnumerateMenuCommandIds(item.Children))
                yield return childId;
        }
    }
}
