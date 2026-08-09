using Free.Shared.Ribbon;
using FreeP.App.Compositor;

namespace FreeP.Ribbon.Definitions.Tests;

public sealed class FreePKeyTipInventoryTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EveryVisibleTabGroupAndActionableControlHasAnUnambiguousKeyTip(bool avalonia)
    {
        var definition = FreePRibbon.Build(
            avalonia ? FreePRibbonCapabilities.Avalonia : FreePRibbonCapabilities.Wpf);

        definition.Tabs.Select(tab => tab.KeyTip).Should().NotContainNulls();
        definition.Tabs.Select(tab => tab.KeyTip!).Should().OnlyHaveUniqueItems();

        foreach (var tab in definition.Tabs)
        {
            tab.KeyTip.Should().NotBeNullOrWhiteSpace($"tab {tab.Id} must be keyboard reachable");
            tab.Groups.Select(group => group.KeyTip).Should().NotContainNulls();
            tab.Groups.Select(group => group.KeyTip!).Should().OnlyHaveUniqueItems();

            // Control KeyTips are resolved per-TAB at runtime (a control can be reached
            // directly after the tab KeyTip, without first entering its group), so
            // uniqueness must be asserted across every group's controls in the tab, not
            // scoped to a single group - otherwise two controls in different groups of
            // the same tab could share a badge with only one of them reachable.
            var tabActionable = tab.Groups
                .SelectMany(group => group.Controls
                    .Where(control => control is not RibbonSeparator and not RibbonRowBreak and not RibbonLabel)
                    .Select(control => (group, control)))
                .ToArray();
            tabActionable.Select(entry => entry.control.KeyTip!).Should().OnlyHaveUniqueItems(
                $"control KeyTips in tab {tab.Id} must be unambiguous across every group");

            foreach (var group in tab.Groups)
            {
                group.KeyTip.Should().NotBeNullOrWhiteSpace(
                    $"group {tab.Id}/{group.Id} must be keyboard reachable");
                var actionable = group.Controls
                    .Where(control => control is not RibbonSeparator and not RibbonRowBreak and not RibbonLabel)
                    .ToArray();
                actionable.Should().OnlyContain(
                    control => !string.IsNullOrWhiteSpace(control.KeyTip),
                    $"every actionable control in {tab.Id}/{group.Id} must have a KeyTip");

                foreach (var control in actionable)
                    AssertMenuKeyTips(control, $"{tab.Id}/{group.Id}/{control.CommandId.Value}");
            }
        }
    }

    [Fact]
    public void SharedCommandKeyTipsMatchExceptForDeclaredProfileOverrides()
    {
        var wpf = FlattenControls(FreePRibbon.Build(FreePRibbonCapabilities.Wpf));
        var avalonia = FlattenControls(FreePRibbon.Build(FreePRibbonCapabilities.Avalonia));
        var declaredOverrides = new HashSet<string>(StringComparer.Ordinal)
        {
            "freep.new-slide",
            // "New Slide" (Slides group) uses "N" on WPF but "I" on Avalonia (see freep.new-slide
            // above). On WPF that "N" collides with the Paragraph group's "Numbering" control,
            // which also declares "N" - the tab-wide keytip de-duplication in FreePRibbon
            // (EnsureUnambiguousKeyTips) resolves the collision by suffixing the later-seen
            // control to "N2" on WPF only, since Avalonia's differing "New Slide" keytip never
            // collides with it there.
            PresentationListGalleryPlanner.NumberingCommandId,
        };

        foreach (var commandId in wpf.Keys.Intersect(avalonia.Keys, StringComparer.Ordinal))
        {
            if (declaredOverrides.Contains(commandId))
            {
                wpf[commandId].Should().NotBe(avalonia[commandId]);
                continue;
            }

            avalonia[commandId].Should().Be(wpf[commandId],
                $"shared command {commandId} must keep the same KeyTip");
        }
    }

    [Fact]
    public void ComboBoxKeyTipsMatchCanonicalWpfInventoryInBothProfiles()
    {
        var expected = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["freep.font-family"] = "FON",
            ["freep.font-size"] = "SIZ",
            ["freep.font-color"] = "FC",
            ["freep.text-autofit"] = "TA",
            ["freep.text-columns"] = "TC",
            ["freep.text-column-spacing"] = "CS",
            ["freep.transition.duration"] = "DUR",
            ["freep.transition.advance-after"] = "AFT",
            ["freep.anim.trigger"] = "STA",
            ["freep.anim.duration"] = "DUR",
            ["freep.anim.delay"] = "DEL",
        };
        var wpf = FlattenControls(FreePRibbon.Build(FreePRibbonCapabilities.Wpf));
        var avalonia = FlattenControls(FreePRibbon.Build(FreePRibbonCapabilities.Avalonia));

        foreach (var (commandId, keyTip) in expected)
        {
            wpf[commandId].Should().Be(keyTip, $"WPF combo {commandId} owns the canonical KeyTip");
            avalonia[commandId].Should().Be(wpf[commandId],
                $"Avalonia combo {commandId} must reuse the WPF KeyTip");
        }
    }

    private static Dictionary<string, string?> FlattenControls(RibbonDefinition definition) =>
        definition.Tabs
            .SelectMany(tab => tab.Groups)
            .SelectMany(group => group.Controls)
            .Where(control => !string.IsNullOrWhiteSpace(control.CommandId.Value))
            .ToDictionary(control => control.CommandId.Value, control => control.KeyTip, StringComparer.Ordinal);

    private static void AssertMenuKeyTips(RibbonControl control, string scope)
    {
        var menu = control switch
        {
            RibbonSplitButton split => split.Menu,
            RibbonDropdown dropdown => dropdown.Menu,
            _ => null,
        };
        if (menu is null)
            return;

        AssertMenuItems(menu.Items, scope);
    }

    private static void AssertMenuItems(IEnumerable<RibbonMenuItem> items, string scope)
    {
        var actionable = items.Where(item => item.CommandId is not null).ToArray();
        actionable.Should().OnlyContain(
            item => !string.IsNullOrWhiteSpace(item.KeyTip),
            $"every actionable menu item in {scope} must have a KeyTip");
        actionable.Select(item => item.KeyTip!).Should().OnlyHaveUniqueItems();

        foreach (var item in items)
            AssertMenuItems(item.Children, $"{scope}/{item.Header}");
    }
}
