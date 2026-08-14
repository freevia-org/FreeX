using Free.Shared.Ribbon;

namespace FreeW.Ribbon.Definitions.Tests;

/// <summary>
/// Guards against the same de-duplication/resolution scope mismatch found in FreeP's ribbon
/// catalog: control KeyTips are resolved per-TAB at runtime (Excel-style, a control can be
/// reached directly after the tab KeyTip without first entering its group), so uniqueness
/// must be asserted across every group's controls in a tab, not scoped to a single group.
/// </summary>
public sealed class FreeWRibbonKeyTipTabScopeTests
{
    private static readonly HashSet<string> PortableProfileTabIds =
    [
        "mailings",
        "help",
        "developer",
        "header-footer-design",
    ];

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ControlKeyTips_AreUniqueWithinEachTab_AcrossAllGroups(bool avalonia)
    {
        var definition = FreeWRibbon.Build(
            avalonia ? FreeWRibbonCapabilities.Avalonia : FreeWRibbonCapabilities.Wpf);

        foreach (var tab in definition.Tabs)
        {
            var tabControls = tab.Groups
                .SelectMany(group => group.Controls
                    .Where(control => control is not RibbonSeparator and not RibbonRowBreak and not RibbonLabel)
                    .Select(control => (group.Id, control)))
                .Where(entry => !string.IsNullOrWhiteSpace(entry.control.KeyTip))
                .ToArray();

            var duplicates = tabControls
                .GroupBy(entry => entry.control.KeyTip!, StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1)
                .Select(g => $"{g.Key} ({string.Join(", ", g.Select(e => $"{e.Id}/{e.control.CommandId.Value}"))})")
                .ToArray();

            duplicates.Should().BeEmpty(
                $"control KeyTips in tab '{tab.Id}' must be unambiguous across every group " +
                "(runtime resolution operates per-tab, not per-group)");
        }
    }

    [Fact]
    public void WpfAuthoredKeyTipsAreSharedWithAvaloniaForPortableProfileTabs()
    {
        var wpf = BuildKeyTipMap(FreeWRibbon.Build(FreeWRibbonCapabilities.Wpf));
        var avalonia = BuildKeyTipMap(FreeWRibbon.Build(FreeWRibbonCapabilities.Avalonia));

        foreach (var (path, wpfKeyTip) in wpf.Where(entry => !string.IsNullOrWhiteSpace(entry.Value)))
        {
            avalonia.Should().ContainKey(path, $"Avalonia must expose the WPF keyboard route at {path}");
            avalonia[path].Should().Be(wpfKeyTip, $"{path} is a shared command surface");
        }
    }

    private static Dictionary<string, string?> BuildKeyTipMap(RibbonDefinition definition)
    {
        var result = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var tab in definition.Tabs.Where(tab => PortableProfileTabIds.Contains(tab.Id)))
        {
            foreach (var group in tab.Groups)
            {
                var groupPath = $"{tab.Id}/group/{group.Id}";
                result.Add(groupPath, group.KeyTip);
                foreach (var control in group.Controls)
                {
                    if (string.IsNullOrWhiteSpace(control.CommandId.Value))
                        continue;

                    var controlPath = $"{tab.Id}/{group.Id}/control/{control.CommandId.Value}";
                    result.Add(controlPath, control.KeyTip);
                    switch (control)
                    {
                        case RibbonDropdown dropdown:
                            AddMenuKeyTips(result, controlPath, dropdown.Menu.Items);
                            break;
                        case RibbonSplitButton splitButton:
                            AddMenuKeyTips(result, controlPath, splitButton.Menu.Items);
                            break;
                    }
                }
            }
        }

        return result;
    }

    private static void AddMenuKeyTips(
        Dictionary<string, string?> result,
        string parentPath,
        IReadOnlyList<RibbonMenuItem> items)
    {
        foreach (var item in items)
        {
            if (item.CommandId is { } commandId)
            {
                var itemPath = $"{parentPath}/menu/{commandId.Value}";
                result.Add(itemPath, item.KeyTip);
                AddMenuKeyTips(result, itemPath, item.Children);
            }
        }
    }
}
