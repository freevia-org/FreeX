using System.Linq;
using System.Reflection;
using Free.Shared.Ribbon;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public class RibbonNativeRegistryTests
{
    [Fact]
    public void EveryGeneratedHandler_ResolvesToAMainWindowMethod()
    {
        var type = typeof(MainWindow);
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

        var missing = FreeXRibbonHandlerMap.Handlers
            .Where(kv => type.GetMethod(kv.Value, flags) is null)
            .Select(kv => $"{kv.Key} -> {kv.Value}")
            .OrderBy(x => x)
            .ToList();

        missing.Should().BeEmpty("every generated ribbon handler must bind to a real MainWindow method");
    }

    [Fact]
    public void HandlerMap_CoversCoreHomeCommands()
    {
        FreeXRibbonHandlerMap.Handlers.Keys.Should().Contain(new[]
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
            .Where(id => !FreeXRibbonHandlerMap.Handlers.ContainsKey(id))
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
