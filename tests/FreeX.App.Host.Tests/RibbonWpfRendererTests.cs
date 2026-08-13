using System.Collections.Generic;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;
using FluentAssertions;
using Free.Shared.Ribbon;
using Free.Shared.Ribbon.Wpf;

namespace FreeX.App.Host.Tests;

public class RibbonWpfRendererTests
{
    private sealed class RecordingCommand : IRibbonCommand
    {
        public int Invocations { get; private set; }
        public void Execute(RibbonCommandContext context) => Invocations++;
    }

    [Fact]
    public void AdaptivePanel_CollapsesLowestPriorityGroupsFirst_WhenNarrow()
    {
        var definition = new RibbonDefinitionBuilder()
            .Tab("t", "T", "T", tab => tab
                .Group("g1", "Alpha", "1", priority: 100, g => g
                    .Large("a1", "A1", RibbonCommandIconKind.Paste).Large("a2", "A2", RibbonCommandIconKind.Copy))
                .Group("g2", "Beta", "2", priority: 50, g => g
                    .Large("b1", "B1", RibbonCommandIconKind.Cut).Large("b2", "B2", RibbonCommandIconKind.Font))
                .Group("g3", "Gamma", "3", priority: 10, g => g
                    .Large("c1", "C1", RibbonCommandIconKind.Filter).Large("c2", "C2", RibbonCommandIconKind.Sort)))
            .Build();

        StaTestRunner.Run(() =>
        {
            var host = BuildHost();
            host.Child = RibbonWpfRenderer.BuildTabContent(definition.FindTab("t")!, host);

            // Wide: everything fits, nothing collapses.
            Layout(host, 2000);
            var hosts = Descendants(host).OfType<RibbonGroupHost>().ToList();
            hosts.Should().HaveCount(3);
            hosts.Should().OnlyContain(h => !h.Collapsed);

            // Invariant at every width: collapsed groups are always lower-priority than the
            // groups still shown (lowest-priority groups collapse first). Verify at a width
            // that produces a mix, scanning down from wide to narrow.
            var sawMix = false;
            for (var width = 1100.0; width >= 150; width -= 60)
            {
                Layout(host, width);
                var collapsed = hosts.Where(h => h.Collapsed).ToList();
                var shown = hosts.Where(h => !h.Collapsed).ToList();
                if (collapsed.Count == 0 || shown.Count == 0)
                    continue;
                sawMix = true;
                collapsed.Max(h => h.Priority).Should().BeLessThan(shown.Min(h => h.Priority),
                    $"lowest-priority groups collapse first (width {width})");
            }

            sawMix.Should().BeTrue("some width should collapse part of the ribbon");
        });
    }

    [Fact]
    public void AdaptivePanel_CollapseSet_MatchesSharedPolicyForMeasuredWidths()
    {
        var definition = new RibbonDefinitionBuilder()
            .Tab("t", "T", "T", tab => tab
                .Group("g1", "Alpha", "1", priority: 100, g => g
                    .Large("a1", "A1", RibbonCommandIconKind.Paste).Large("a2", "A2", RibbonCommandIconKind.Copy))
                .Group("g2", "Beta", "2", priority: 50, g => g
                    .Large("b1", "B1", RibbonCommandIconKind.Cut).Large("b2", "B2", RibbonCommandIconKind.Font))
                .Group("g3", "Gamma", "3", priority: 10, g => g
                    .Large("c1", "C1", RibbonCommandIconKind.Filter).Large("c2", "C2", RibbonCommandIconKind.Sort)))
            .Build();

        StaTestRunner.Run(() =>
        {
            var host = BuildHost();
            host.Child = RibbonWpfRenderer.BuildTabContent(definition.FindTab("t")!, host);

            Layout(host, 2000);
            var panel = Descendants(host).OfType<RibbonAdaptivePanel>().Single();
            var hosts = panel.Children.OfType<RibbonGroupHost>().ToList();
            var fixedChromeWidth = GetFixedChromeWidth(panel);
            var groups = hosts
                .Select(ribbonGroup => new RibbonAdaptiveCollapseGroup(
                    ribbonGroup.GroupName,
                    ribbonGroup.FullWidth,
                    RibbonGroupHost.CollapsedWidth,
                    ribbonGroup.Priority))
                .ToList();

            var targetFitWidth = PickPartialCollapseFitWidth(groups, fixedChromeWidth);
            var expected = RibbonAdaptiveCollapsePolicy.Plan(targetFitWidth, groups, fixedChromeWidth);
            expected.Any(decision => decision.IsCollapsed).Should().BeTrue();
            expected.Any(decision => !decision.IsCollapsed).Should().BeTrue();

            Layout(host, targetFitWidth + 4);

            hosts.Select(ribbonGroup => ribbonGroup.Collapsed)
                .Should()
                .Equal(expected.Select(decision => decision.IsCollapsed));
        });
    }

    private static void Layout(FrameworkElement host, double width)
    {
        host.Width = width;
        host.Measure(new Size(width, 130));
        host.Arrange(new Rect(0, 0, width, 130));
        host.UpdateLayout();
    }

    private static double GetFixedChromeWidth(RibbonAdaptivePanel panel)
    {
        var children = panel.Children.Cast<UIElement>().ToList();
        var spacing = (double)typeof(RibbonAdaptivePanel)
            .GetField("GroupSpacing", BindingFlags.NonPublic | BindingFlags.Static)!
            .GetRawConstantValue()!;
        return children
            .Where(child => child is not RibbonGroupHost)
            .Sum(child => child.DesiredSize.Width) +
            spacing * Math.Max(0, children.Count - 1);
    }

    private static double PickPartialCollapseFitWidth(
        IReadOnlyList<RibbonAdaptiveCollapseGroup> groups,
        double fixedChromeWidth)
    {
        var total = groups.Sum(group => group.FullWidth) + fixedChromeWidth;
        var firstSavings = groups
            .OrderBy(group => group.Priority)
            .Select(group => group.FullWidth - group.CollapsedWidth)
            .First(savings => savings > 0.5);
        return total - firstSavings / 2;
    }

    [Fact]
    public void GeneratedTabs_HaveDropdownsWithPopulatedMenus()
    {
        var definition = FreeXRibbon.Build();
        var dropdownsWithMenus = definition.Tabs
            .SelectMany(t => t.Groups)
            .SelectMany(g => g.Controls)
            .OfType<RibbonDropdown>()
            .Count(d => d.Menu.Items.Count > 0);

        dropdownsWithMenus.Should().BeGreaterThan(10);
    }

    [Fact]
    public void RenderedDropdown_OpensMenu_AndItemInvokesCommand()
    {
        var registry = new RibbonCommandRegistry();
        var shadow = new RecordingCommand();
        registry.Register("Shadow", shadow);

        var definition = new RibbonDefinitionBuilder()
            .Tab("t", "T", "T", tab => tab
                .Group("g", "G", "G", 1, g => g
                    .Medium("Shape Effects", "Shape Effects", RibbonCommandIconKind.RibbonShape, "FX",
                        menu: m => m.Item("Shadow", "Shadow", "S").Separator().Item("Glow", "Glow", "G"))))
            .Build();

        StaTestRunner.Run(() =>
        {
            var host = BuildHost();
            host.Child = RibbonWpfRenderer.BuildTabContent(definition.FindTab("t")!, host, registry);
            host.Measure(new Size(600, 130));
            host.Arrange(new Rect(0, 0, 600, 130));
            host.UpdateLayout();

            var button = (ButtonBase)FindByCommandName(host, "Shape Effects")!;
            button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

            var menu = button.ContextMenu;
            menu.Should().NotBeNull();
            menu!.IsOpen.Should().BeTrue();
            var shadowItem = menu.Items.OfType<MenuItem>().First(mi => Equals(mi.Header, "Shadow"));
            shadowItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        });

        shadow.Invocations.Should().Be(1);
    }

    [Fact]
    public void ContextualTabs_RefreshFullWidthsForAdaptiveCollapse()
    {
        var definition = new RibbonDefinitionBuilder()
            .Tab("main", "Main", "M", tab => tab
                .Group("mainGroup", "Main", "M", priority: 100, group => group
                    .Large("MainCommand", "Main Command", RibbonCommandIconKind.Paste)))
            .ContextualTab(
                "contextual",
                "Contextual",
                new RibbonTabContext("context.active", "Contextual", RibbonContextColor.Green),
                tab => tab
                    .Group("contextGroup", "Context", "C", priority: 100, group => group
                        .Large("ContextCommand", "Context Command", RibbonCommandIconKind.ChartColumn)))
            .Build();

        StaTestRunner.Run(() =>
        {
            var host = BuildHost();

            host.Child = RibbonWpfRenderer.BuildTabContent(definition.FindTab("main")!, host);
            Descendants(host)
                .OfType<RibbonAdaptivePanel>()
                .Single()
                .RefreshFullWidthsFromFullContent
                .Should()
                .BeFalse("main tabs keep the existing cached-width adaptive behavior");

            host.Child = RibbonWpfRenderer.BuildTabContent(definition.FindTab("contextual")!, host);
            Descendants(host)
                .OfType<RibbonAdaptivePanel>()
                .Single()
                .RefreshFullWidthsFromFullContent
                .Should()
                .BeTrue("contextual tabs need refreshed full-width budgets so narrow widths collapse instead of clipping");
        });
    }

    [Fact]
    public void HomeDefinition_IsValid_AndHasAllSevenGroups()
    {
        var definition = HomeRibbonDefinition.Build();

        RibbonDefinitionValidator.Validate(definition).HasErrors.Should().BeFalse();
        definition.FindTab("HomeTab")!.Groups.Select(g => g.Header).Should().Equal(
            "Clipboard", "Font", "Alignment", "Number", "Styles", "Cells", "Editing");
    }

    [Fact]
    public void RenderedHomeTab_IncludesVisibleSectionDividers()
    {
        StaTestRunner.Run(() =>
        {
            var host = BuildHost();
            var tab = HomeRibbonDefinition.Build().FindTab("HomeTab")!;
            host.Child = RibbonWpfRenderer.BuildTabContent(tab, host);
            host.Measure(new Size(1880, 130));
            host.Arrange(new Rect(0, 0, 1880, 130));
            host.UpdateLayout();

            var dividers = Descendants(host)
                .OfType<Rectangle>()
                .Where(rect => rect.Width == 1 && rect.Margin == new Thickness(2, 5, 3, 18))
                .ToList();

            dividers.Should().HaveCountGreaterThan(0);
            dividers.Should().OnlyContain(rect =>
                rect.Fill != null &&
                rect.VerticalAlignment == VerticalAlignment.Stretch &&
                rect.Visibility == Visibility.Visible);
        });
    }

    [Fact]
    public void RenderedButton_InvokesRegisteredCommand_OnClick()
    {
        var registry = new RibbonCommandRegistry();
        var cut = new RecordingCommand();
        registry.Register("Cut", cut);

        StaTestRunner.Run(() =>
        {
            var host = BuildHost();
            var tab = HomeRibbonDefinition.Build().FindTab("HomeTab")!;
            host.Child = RibbonWpfRenderer.BuildTabContent(tab, host, registry);
            host.Measure(new Size(1880, 130));
            host.Arrange(new Rect(0, 0, 1880, 130));
            host.UpdateLayout();

            // Cut is a plain button -> clicking invokes its command.
            var cutButton = FindByCommandName(host, "Cut");
            cutButton.Should().NotBeNull();
            cutButton!.IsEnabled.Should().BeTrue();
            ((ButtonBase)cutButton).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

            // "Copy" has no registered command -> rendered disabled, never throws.
            FindByCommandName(host, "Copy")!.IsEnabled.Should().BeFalse();
        });

        cut.Invocations.Should().Be(1);
    }

    [Fact]
    public void WpfControlRibbonCommand_RaisesButtonClick()
    {
        var invocations = 0;

        StaTestRunner.Run(() =>
        {
            var button = new Button();
            button.Click += (_, _) => invocations++;

            new WpfControlRibbonCommand(button).Execute(RibbonCommandContext.Empty);
        });

        invocations.Should().Be(1);
    }

    [Fact]
    public void RenderedComboWithoutRegisteredCommand_RemainsEnabledForInputWiring()
    {
        var registry = new RibbonCommandRegistry();
        var definition = new RibbonDefinitionBuilder()
            .Tab("t", "T", "T", tab => tab
                .Group("g", "G", "G", 1, group => group
                    .ComboBox("Format", "Format", combo => combo with
                    {
                        KeyTip = "F",
                        Items = new[] { "General", "Number" }
                    })
                    .Medium("Copy", "Copy", RibbonCommandIconKind.Copy, "C")))
            .Build();

        StaTestRunner.Run(() =>
        {
            var host = BuildHost();
            host.Child = RibbonWpfRenderer.BuildTabContent(definition.FindTab("t")!, host, registry);
            host.Measure(new Size(600, 130));
            host.Arrange(new Rect(0, 0, 600, 130));
            host.UpdateLayout();

            FindByCommandName(host, "Format").Should().BeOfType<ComboBox>()
                .Which.IsEnabled.Should().BeTrue();
            FindByCommandName(host, "Copy")!.IsEnabled.Should().BeFalse();
        });
    }

    private static Border BuildHost()
    {
        if (Application.Current is null)
            _ = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };

        var host = new Border { Width = 1880 };
        host.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri(
                "pack://application:,,,/FreeX.App.Host;component/Resources/MainWindowResources.xaml")
        });
        return host;
    }

    private static Control? FindByCommandName(DependencyObject root, string commandName)
    {
        foreach (var child in Descendants(root))
        {
            if (child is Control control &&
                string.Equals(RibbonMetadata.GetCommandName(control), commandName, StringComparison.Ordinal))
            {
                return control;
            }
        }

        return null;
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var grandChild in Descendants(child))
                yield return grandChild;
        }
    }
}
