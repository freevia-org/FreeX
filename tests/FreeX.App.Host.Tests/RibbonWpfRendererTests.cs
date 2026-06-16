using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using FluentAssertions;
using Free.Shared.Ribbon;

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

    private static void Layout(FrameworkElement host, double width)
    {
        host.Width = width;
        host.Measure(new Size(width, 130));
        host.Arrange(new Rect(0, 0, width, 130));
        host.UpdateLayout();
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
    public void HomeDefinition_IsValid_AndHasAllSevenGroups()
    {
        var definition = HomeRibbonDefinition.Build();

        RibbonDefinitionValidator.Validate(definition).HasErrors.Should().BeFalse();
        definition.FindTab("HomeTab")!.Groups.Select(g => g.Header).Should().Equal(
            "Clipboard", "Font", "Alignment", "Number", "Styles", "Cells", "Editing");
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
