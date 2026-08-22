using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Free.Shared.Ribbon;
using Free.Shared.Ribbon.Wpf;

namespace FreeW.App.Host.Tests;

public sealed class RibbonShellBuilderTests
{
    [StaFact]
    public void Build_ComposesFileTabContentTabsAndRouter()
    {
        var backstageOpenCount = 0;

        var result = RibbonShellBuilder.Build(new RibbonShellBuildSpec(
            Definition: new RibbonDefinition([Tab("home", "Home"), Tab("insert", "Insert")]),
            Registry: new RibbonCommandRegistry(),
            StateStore: new RibbonStateStore(),
            FileTabHeader: "File",
            FileTabAccent: Color.FromRgb(0x0F, 0x6D, 0x8C),
            FileTabHover: Color.FromRgb(0x0B, 0x55, 0x6E),
            ShowBackstage: () => backstageOpenCount++));

        result.Root.Child.Should().BeSameAs(result.Tabs);
        result.FileTab.Header.Should().Be("File");
        result.Tabs.Items.Count.Should().Be(3);
        result.Tabs.SelectedIndex.Should().Be(1);

        result.Tabs.SelectedIndex = 2;
        result.Tabs.SelectedIndex = 0;

        result.FileTabRouter.LastContentTabIndex.Should().Be(2);
        result.Tabs.SelectedIndex.Should().Be(2);
        backstageOpenCount.Should().Be(1);
    }

    [StaFact]
    public void Build_MergesResourcesCustomizesTabsAndRegistersContextualTabs()
    {
        var dictionary = new ResourceDictionary();
        dictionary["FreeXAccentBrush"] = Brushes.Teal;
        var customizedTabs = new List<string>();

        var result = RibbonShellBuilder.Build(new RibbonShellBuildSpec(
            Definition: new RibbonDefinition([
                Tab("home", "Home"),
                Tab("picture", "Picture Format", new RibbonTabContext("picture.selected", "Picture Tools", RibbonContextColor.Purple))
            ]),
            Registry: new RibbonCommandRegistry(),
            StateStore: new RibbonStateStore(),
            FileTabHeader: "File",
            FileTabAccent: Color.FromRgb(0xB7, 0x47, 0x2A),
            FileTabHover: Color.FromRgb(0x8F, 0x37, 0x21),
            ShowBackstage: () => { })
        {
            EnableContextualTabs = true,
            ResourceDictionaries = [dictionary],
            CustomizeTabContent = (tab, content) =>
            {
                customizedTabs.Add(tab.Id);
                content.Tag = tab.Id;
            }
        });

        result.ContextualTabs.Should().NotBeNull();
        result.Tabs.Resources.MergedDictionaries.Should().Contain(dictionary);
        customizedTabs.Should().Equal("home", "picture");

        var contextualTab = result.Tabs.Items
            .OfType<TabItem>()
            .Single(item => Equals(item.Header, "Picture Format"));
        contextualTab.Visibility.Should().Be(Visibility.Collapsed);

        result.ContextualTabs!.Apply(RibbonContextState.None.With("picture.selected"));
        contextualTab.Visibility.Should().Be(Visibility.Visible);
    }

    [StaFact]
    public void BuildTabContent_ComboSelectionExecutesCommandWithSelectedValue()
    {
        string? selected = null;
        var registry = new RibbonCommandRegistry();
        registry.Register("theme", new CaptureValueCommand(value => selected = value));
        var tab = new RibbonTab(
            "design",
            "Design",
            KeyTip: null,
            Context: null,
            Groups:
            [
                new RibbonGroup(
                    "themes",
                    "Document Formatting",
                    KeyTip: null,
                    Priority: 0,
                    Controls: [new RibbonComboBox("theme", "Themes") { Items = ["Office", "Slate"] }],
                    RibbonGroupSizing.Default)
            ]);

        var content = RibbonWpfRenderer.BuildTabContent(tab, new Button(), registry, new RibbonStateStore());
        var combo = FindLogicalChild<ComboBox>(content)!;

        combo.SelectedIndex = 1;

        selected.Should().Be("Slate");
    }

    [StaFact]
    public void HomeFont_WpfRendererUsesThreeExplicitRowsToLimitExpandedWidth()
    {
        var home = FreeW.Ribbon.Definitions.FreeWRibbon
            .Build(FreeW.Ribbon.Definitions.FreeWRibbonCapabilities.Wpf)
            .FindTab("home")!;
        var content = RibbonWpfRenderer.BuildTabContent(home, new Button());
        var panel = FindLogicalChild<RibbonAdaptivePanel>(content)!;
        var font = panel.Children
            .OfType<RibbonGroupHost>()
            .Single(group => group.GroupName == "Font");

        var rows = FindLogicalChild<StackPanel>(font.GroupContent)!
            .Children
            .OfType<StackPanel>()
            .Single(stack => stack.Orientation == Orientation.Vertical);

        rows.Children
            .OfType<StackPanel>()
            .Should()
            .HaveCount(3, "the WPF Font group keeps its command set expanded across compact rows before adaptive collapse");
    }

    private sealed class CaptureValueCommand(Action<string?> capture) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context) => capture(context.SelectedValue);
    }

    private static T? FindLogicalChild<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T match)
            return match;

        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            if (FindLogicalChild<T>(child) is { } matchChild)
                return matchChild;
        }

        return null;
    }

    private static RibbonTab Tab(string id, string header, RibbonTabContext? context = null) =>
        new(
            id,
            header,
            KeyTip: null,
            context,
            [
                new RibbonGroup(
                    id + ".group",
                    header,
                    KeyTip: null,
                    Priority: 0,
                    Controls: [new RibbonButton(id + ".command", header)],
                    RibbonGroupSizing.Default)
            ]);
}
