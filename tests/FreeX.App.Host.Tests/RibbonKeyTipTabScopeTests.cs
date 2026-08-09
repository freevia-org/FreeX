using FluentAssertions;

namespace FreeX.App.Host.Tests;

/// <summary>
/// Guards against the same de-duplication/resolution scope mismatch found in FreeP's ribbon
/// catalog: control KeyTips are resolved per-TAB at runtime (Excel-style, a control can be
/// reached directly after the tab KeyTip without first entering its group -
/// see <c>RibbonTabs_DoNotReuseCommandKeyTipsWithinTheSameTab</c> for the legacy XAML ribbon),
/// so uniqueness must be asserted across every group's controls in a tab, not scoped to a
/// single group.
/// </summary>
public sealed class RibbonKeyTipTabScopeTests
{
    [Fact]
    public void ControlKeyTips_AreUniqueWithinEachTab_AcrossAllGroups()
    {
        var definition = FreeXRibbon.Build();

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
}
