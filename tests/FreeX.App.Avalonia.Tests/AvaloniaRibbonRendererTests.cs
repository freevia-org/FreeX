using System.Linq;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.VisualTree;
using FreeX.App.Avalonia.Ribbon;
using FreeX.App.Presentation.DrawingUI;
using Free.Shared.Ribbon;
using Free.Shared.Ribbon.Avalonia;
using Free.Shared.Ribbon.Icons;
using FreeX.Ribbon.Definitions;
using AvaloniaPath = Avalonia.Controls.Shapes.Path;
using SelectionPaneObjectKind = FreeX.Core.Model.SelectionPaneObjectKind;

[assembly: AvaloniaTestApplication(typeof(FreeX.App.Avalonia.Tests.RibbonHeadlessApp))]
[assembly: AvaloniaTestIsolation(AvaloniaTestIsolationLevel.PerTest)]
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]

namespace FreeX.App.Avalonia.Tests;

[Collection("AvaloniaHeadless")]
public sealed class AvaloniaRibbonRendererTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    private static Task RunOnUiThread(Action action) =>
        Session.Dispatch(action, CancellationToken.None);

    private static RibbonTab BuildHomeTab()
    {
        var definition = new RibbonDefinitionBuilder()
            .Tab("home", "Home", "H", home =>
            {
                home.Group("clipboard", "Clipboard", "C", 100, g =>
                {
                    g.SplitButton("paste", "Paste", new RibbonMenu(new[]
                    {
                        new RibbonMenuItem("Paste", "paste"),
                        RibbonMenuItem.Separator(),
                        new RibbonMenuItem("Paste Special", "pasteSpecial"),
                    }), c => c with { PreferredLayout = RibbonCommandLayoutKind.Large });
                    g.Button("cut", "Cut");
                    g.Button("copy", "Copy");
                });
                home.Group("font", "Font", "F", 90, g =>
                {
                    g.ComboBox("fontName", "Font", c => c with { Items = new[] { "Calibri", "Arial" } });
                    g.Toggle("bold", "Bold");
                    g.Separator();
                    g.Dropdown("fill", "Fill", new RibbonMenu(new[]
                    {
                        new RibbonMenuItem("Yellow", "fillYellow"),
                    }));
                });
            })
            .Build();

        return definition.FindTab("home")!;
    }

    private sealed class StatefulCommand(RibbonCommandState state) : IRibbonStatefulCommand
    {
        public void Execute(RibbonCommandContext context)
        {
        }

        public RibbonCommandState GetState() => state;
    }

    private sealed class MutableStatefulCommand(RibbonCommandState state, Action? execute = null) : IRibbonStatefulCommand
    {
        public RibbonCommandState State { get; set; } = state;

        public void Execute(RibbonCommandContext context) => execute?.Invoke();

        public RibbonCommandState GetState() => State;
    }

    private sealed class NoOpCommand : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
        }
    }

    private sealed class RecordingCommand(Action execute) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context) => execute();
    }

    [Fact]
    public Task BuildTabContent_ProducesNonEmptyVisualTree() => RunOnUiThread(() =>
    {
        var tab = BuildHomeTab();
        var registry = new RibbonCommandRegistry();
        var content = AvaloniaRibbonRenderer.BuildTabContent(tab, registry);

        var window = new Window { Width = 1200, Height = 200, Content = content };
        window.Show();
        window.Measure(new Size(1200, 200));
        window.Arrange(new Rect(0, 0, 1200, 200));

        var descendants = content.GetLogicalDescendants().ToList();
        Assert.NotEmpty(descendants);

        // One group header label per group (2 groups).
        var headers = descendants.OfType<TextBlock>().Select(t => t.Text).ToList();
        Assert.Contains("Clipboard", headers);
        Assert.Contains("Font", headers);

        // Buttons (Paste/Cut/Copy/Fill dropdown) and a toggle (Bold) were created.
        Assert.True(descendants.OfType<Button>().Count() >= 4);
        Assert.Contains(descendants, d => d is ToggleButton);

        // The combo box rendered with its items.
        var combo = descendants.OfType<ComboBox>().Single();
        Assert.Equal(2, combo.Items.Count);

        // The content actually took up space once arranged.
        Assert.True(content.Bounds.Width > 0);
        Assert.True(content.Bounds.Height > 0);
    });

    [Fact]
    public Task BuildTabContent_GroupCount_MatchesDefinition() => RunOnUiThread(() =>
    {
        var tab = BuildHomeTab();
        var content = AvaloniaRibbonRenderer.BuildTabContent(tab, registry: null);

        var groupGrids = content.GetLogicalDescendants()
            .OfType<Grid>()
            .Where(g => g.Tag is string)
            .Select(g => (string)g.Tag!)
            .ToList();

        Assert.Equal(new[] { "clipboard", "font" }, groupGrids);
    });

    [Fact]
    public Task BuildTabContent_NarrowWidth_CollapsesRibbonGroups() => RunOnUiThread(() =>
    {
        var tab = BuildHomeTab();
        var content = AvaloniaRibbonRenderer.BuildTabContent(tab, registry: null);

        var window = new Window { Width = 180, Height = 200, Content = content };
        window.Show();
        window.Measure(new Size(180, 200));
        window.Arrange(new Rect(0, 0, 180, 200));

        var collapsedButtons = content.GetLogicalDescendants()
            .OfType<Button>()
            .Where(b => b.Classes.Contains("free-ribbon-collapsed-group"))
            .ToList();

        Assert.NotEmpty(collapsedButtons);
        Assert.All(collapsedButtons, b => Assert.IsType<MenuFlyout>(b.Flyout));
        Assert.True(content.Bounds.Width <= 180);
    });

    [Fact]
    public Task CollapsedGroup_FlattensSplitButtonPrimaryAndAdditionalActions() => RunOnUiThread(() =>
    {
        var executed = false;
        var registry = new RibbonCommandRegistry();
        registry.Register("paste", new RecordingCommand(() => executed = true));
        var content = AvaloniaRibbonRenderer.BuildTabContent(BuildHomeTab(), registry);
        var window = new Window { Width = 180, Height = 200, Content = content };
        window.Show();
        window.Measure(new Size(180, 200));
        window.Arrange(new Rect(0, 0, 180, 200));

        var clipboardButton = content.GetLogicalDescendants()
            .OfType<Button>()
            .Single(button => Equals(button.Tag, "collapsed:clipboard"));
        var flyout = Assert.IsType<MenuFlyout>(clipboardButton.Flyout);
        var items = flyout.Items.OfType<MenuItem>().ToList();

        Assert.Contains(items, item => Equals(item.Header, "Paste") && Equals(item.Tag, "paste") && item.Items.Count == 0);
        Assert.Contains(items, item => Equals(item.Header, "Paste Special") && Equals(item.Tag, "pasteSpecial"));
        Assert.DoesNotContain(items, item => Equals(item.Header, "Paste") && item.Items.Count > 0);

        items.Single(item => Equals(item.Tag, "paste"))
            .RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
        Assert.True(executed);
    });

    [Fact]
    public Task BuildTabContent_WideWidth_KeepsRibbonGroupsExpanded() => RunOnUiThread(() =>
    {
        var tab = BuildHomeTab();
        var content = AvaloniaRibbonRenderer.BuildTabContent(tab, registry: null);

        var window = new Window { Width = 1200, Height = 200, Content = content };
        window.Show();
        window.Measure(new Size(1200, 200));
        window.Arrange(new Rect(0, 0, 1200, 200));

        var collapsedButtons = content.GetLogicalDescendants()
            .OfType<Button>()
            .Where(b => b.Classes.Contains("free-ribbon-collapsed-group"))
            .ToList();

        Assert.Empty(collapsedButtons);
    });

    [Fact]
    public Task BuildTabContent_CollapseSet_MatchesSharedPolicyForMeasuredWidths() => RunOnUiThread(() =>
    {
        var tab = BuildHomeTab();
        var content = AvaloniaRibbonRenderer.BuildTabContent(tab, registry: null);

        var window = new Window { Width = 1200, Height = 200, Content = content };
        window.Show();
        Layout(window, 1200);

        var panel = FindAdaptivePanel(content);
        var hosts = panel.Children.OfType<Control>().Where(IsAdaptiveGroupHost).ToList();
        var fixedChromeWidth = GetFixedChromeWidth(panel);
        var groups = hosts
            .Select(host =>
            {
                var group = GetHostGroup(host);
                return new RibbonAdaptiveCollapseGroup(
                    group.Id,
                    GetHostDouble(host, "FullWidth"),
                    GetHostCollapsedWidth(host),
                    GetHostInt(host, "Priority"));
            })
            .ToList();

        var targetWidth = PickPartialCollapseWidth(groups, fixedChromeWidth);
        var expected = RibbonAdaptiveCollapsePolicy.Plan(targetWidth, groups, fixedChromeWidth);
        Assert.Contains(expected, decision => decision.IsCollapsed);
        Assert.Contains(expected, decision => !decision.IsCollapsed);

        Layout(window, targetWidth);

        Assert.Equal(
            expected.Select(decision => decision.IsCollapsed),
            hosts.Select(host => GetHostBool(host, "Collapsed")));
    });

    [Fact]
    public Task Dropdown_ButtonHasFlyout_BuiltFromMenu() => RunOnUiThread(() =>
    {
        var tab = BuildHomeTab();
        var content = AvaloniaRibbonRenderer.BuildTabContent(tab, new RibbonCommandRegistry());

        var flyoutButton = content.GetLogicalDescendants()
            .OfType<Button>()
            .FirstOrDefault(b => b.Flyout is MenuFlyout);

        Assert.NotNull(flyoutButton);
        var flyout = (MenuFlyout)flyoutButton!.Flyout!;
        Assert.NotEmpty(flyout.Items);
        // Paste menu: Paste, Separator, Paste Special.
        Assert.Contains(flyout.Items, i => i is MenuItem);
        Assert.Contains(flyout.Items, i => i is Separator);
    });

    private static void Layout(Window window, double width)
    {
        window.Width = width;
        window.Measure(new Size(width, 200));
        window.Arrange(new Rect(0, 0, width, 200));
    }

    private static Panel FindAdaptivePanel(Control root) =>
        root.GetVisualDescendants()
            .OfType<Panel>()
            .Single(panel => panel.GetType().Name == "AvaloniaRibbonAdaptivePanel");

    private static bool IsAdaptiveGroupHost(Control control) =>
        control.GetType().Name == "AvaloniaRibbonGroupHost";

    private static double GetFixedChromeWidth(Panel panel)
    {
        var spacing = (double)panel.GetType()
            .GetField("GroupSpacing", BindingFlags.NonPublic | BindingFlags.Static)!
            .GetRawConstantValue()!;
        return panel.Children
            .Where(child => !IsAdaptiveGroupHost(child))
            .Sum(child => child.DesiredSize.Width) +
            spacing * Math.Max(0, panel.Children.Count - 1);
    }

    private static RibbonGroup GetHostGroup(Control host) =>
        (RibbonGroup)host.GetType()
            .GetField("_group", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(host)!;

    private static double GetHostDouble(Control host, string propertyName) =>
        (double)host.GetType()
            .GetProperty(propertyName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(host)!;

    private static int GetHostInt(Control host, string propertyName) =>
        (int)host.GetType()
            .GetProperty(propertyName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(host)!;

    private static bool GetHostBool(Control host, string propertyName) =>
        (bool)host.GetType()
            .GetProperty(propertyName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(host)!;

    private static double GetHostCollapsedWidth(Control host) =>
        Convert.ToDouble(host.GetType()
            .GetField("CollapsedWidth", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!
            .GetRawConstantValue());

    private static double PickPartialCollapseWidth(
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
    public Task BuildRibbon_ProducesTabPerVisibleTab() => RunOnUiThread(() =>
    {
        var definition = AvaloniaRibbonComposition.BuildDefinition();
        var registry = AvaloniaRibbonComposition.BuildRegistry(() => null, _ => { });
        var ribbon = AvaloniaRibbonRenderer.BuildRibbon(definition, registry);

        var tabControl = Assert.IsType<TabControl>(ribbon);
        Assert.Equal(definition.VisibleTabs.Count() + 1, tabControl.Items.Count);
        Assert.Equal("FileTab", ((TabItem)tabControl.Items[0]!).Tag);
        Assert.Equal(1, tabControl.SelectedIndex);
        Assert.All(tabControl.Items, item => Assert.IsType<TabItem>(item));
    });

    [Fact]
    public Task FileTab_InvokesBackstageWithoutChangingTheSelectedContentTab() => RunOnUiThread(() =>
    {
        var definition = AvaloniaRibbonComposition.BuildDefinition();
        var registry = AvaloniaRibbonComposition.BuildRegistry(() => null, _ => { });
        var opened = 0;
        var ribbon = AvaloniaRibbonRenderer.BuildRibbon(
            definition,
            registry,
            onFileTabSelected: () => opened++);
        var tabControl = Assert.IsType<TabControl>(ribbon);

        tabControl.SelectedIndex = 0;

        Assert.Equal(1, opened);
        Assert.Equal(1, tabControl.SelectedIndex);
        Assert.Equal("HomeTab", ((TabItem)tabControl.SelectedItem!).Tag);
    });

    [Fact]
    public Task TopLevelKeyTips_ShowAndActivateTheMatchingTab() => RunOnUiThread(() =>
    {
        var definition = new RibbonDefinitionBuilder()
            .Tab("home", "Home", "H", tab => tab.Group("g", "G", "G", 1, group => group.Button("b", "B")))
            .Tab("insert", "Insert", "N", tab => tab.Group("g2", "G2", "G", 1, group => group.Button("b2", "B2")))
            .Build();
        var ribbon = AvaloniaRibbonRenderer.BuildRibbon(definition);
        var tabControl = Assert.IsType<TabControl>(ribbon);

        AvaloniaRibbonRenderer.SetTopLevelKeyTipsVisible(ribbon, true);
        var badges = ribbon.GetLogicalDescendants()
            .OfType<Border>()
            .Where(border => Equals(border.Tag, "RibbonKeyTipBadge"))
            .ToArray();

        Assert.Equal(3, badges.Length);
        Assert.All(badges, badge => Assert.True(badge.IsVisible));
        Assert.True(AvaloniaRibbonRenderer.TryActivateTopLevelKeyTip(ribbon, "N"));
        Assert.Equal("insert", ((TabItem)tabControl.SelectedItem!).Tag);
    });

    [Fact]
    public Task KeyTipPath_OpensRenderedMenuAndNestedSubmenu() => RunOnUiThread(() =>
    {
        var definition = new RibbonDefinitionBuilder()
            .Tab("home", "Home", "H", tab => tab.Group("group", "Group", "G", 1, group =>
                group.Dropdown("borders", "Borders", new RibbonMenu(new[]
                {
                    new RibbonMenuItem("Line Style", new RibbonCommandId(), "S", Children: new[]
                    {
                        new RibbonMenuItem("Dashed", "dashed", "D")
                    })
                }), control => control with { KeyTip = "B" })))
            .Build();
        var executed = 0;
        var registry = new RibbonCommandRegistry();
        registry.Register("borders", new NoOpCommand());
        registry.Register("dashed", new RecordingCommand(() => executed++));
        var ribbon = AvaloniaRibbonRenderer.BuildRibbon(definition, registry);
        var window = new Window { Width = 640, Height = 180, Content = ribbon };
        window.Show();
        window.Measure(new Size(640, 180));
        window.Arrange(new Rect(0, 0, 640, 180));

        Assert.True(AvaloniaRibbonRenderer.TryActivateKeyTip(ribbon, "HB"));
        var button = ribbon.GetLogicalDescendants().OfType<Button>()
            .First(candidate => Equals(candidate.Tag, "borders"));
        var flyout = Assert.IsType<MenuFlyout>(button.Flyout);
        Assert.True(flyout.IsOpen);

        Assert.True(AvaloniaRibbonRenderer.TryActivateKeyTip(ribbon, "HBS"));
        var lineStyle = flyout.Items.OfType<MenuItem>().Single();
        Assert.True(lineStyle.IsSubMenuOpen);
        Assert.Equal(0, executed);

        Assert.True(AvaloniaRibbonRenderer.TryActivateKeyTip(ribbon, "HBSD"));
        Assert.Equal(1, executed);
        AvaloniaRibbonRenderer.CloseKeyTipFlyouts(ribbon);
        window.Close();
    });

    [Fact]
    public Task KeyTipPath_DisabledRenderedCommandDoesNotOpenOrExecute() => RunOnUiThread(() =>
    {
        var definition = new RibbonDefinitionBuilder()
            .Tab("home", "Home", "H", tab => tab.Group("group", "Group", "G", 1, group =>
                group.Dropdown("locked", "Locked", new RibbonMenu(new[]
                {
                    new RibbonMenuItem("Action", "action", "A")
                }), control => control with { KeyTip = "L" })))
            .Build();
        var executed = 0;
        var registry = new RibbonCommandRegistry();
        registry.Register("locked", new StatefulCommand(new RibbonCommandState(IsEnabled: false)));
        registry.Register("action", new RecordingCommand(() => executed++));
        var ribbon = AvaloniaRibbonRenderer.BuildRibbon(definition, registry);
        var window = new Window { Width = 640, Height = 180, Content = ribbon };
        window.Show();
        window.Measure(new Size(640, 180));
        window.Arrange(new Rect(0, 0, 640, 180));

        Assert.False(AvaloniaRibbonRenderer.TryActivateKeyTip(ribbon, "HL"));
        var button = ribbon.GetLogicalDescendants().OfType<Button>()
            .First(candidate => Equals(candidate.Tag, "locked"));
        Assert.False(button.Flyout is MenuFlyout { IsOpen: true });
        Assert.Equal(0, executed);
        window.Close();
    });

    [Fact]
    public Task ContextualTabKeyTip_UsesRenderedActivationKey() => RunOnUiThread(() =>
    {
        var definition = new RibbonDefinitionBuilder()
            .Tab("home", "Home", "H", tab => tab.Group("group", "Group", "G", 1, group => group.Button("home", "Home")))
            .ContextualTab("ChartDesignTab", "Chart Design", new RibbonTabContext("chart.selected", "Chart Design", RibbonContextColor.Green, KeyTip: "JC"), tab =>
                tab.Group("chartGroup", "Chart", "C", 1, group => group.Button("chart", "Chart")))
            .Build();
        var source = new FakeContextSource();
        var ribbon = AvaloniaRibbonRenderer.BuildRibbon(definition, contextSource: source);
        var window = new Window { Width = 640, Height = 180, Content = ribbon };
        window.Show();
        source.Raise(RibbonContextState.None.With("chart.selected"));

        Assert.True(AvaloniaRibbonRenderer.TryActivateTopLevelKeyTip(ribbon, "JC"));
        var tabControl = Assert.IsType<TabControl>(ribbon);
        Assert.Equal("ChartDesignTab", ((TabItem)tabControl.SelectedItem!).Tag);
        window.Close();
    });

    [Fact]
    public Task BuildRibbon_UsesCompactTabAndComboStyles() => RunOnUiThread(() =>
    {
        var definition = AvaloniaRibbonComposition.BuildDefinition();
        var registry = AvaloniaRibbonComposition.BuildRegistry(() => null, _ => { });
        var ribbon = AvaloniaRibbonRenderer.BuildRibbon(definition, registry);

        var tabControl = Assert.IsType<TabControl>(ribbon);
        Assert.Contains(tabControl.Styles, style => style is Style concrete && HasSetter(concrete, Layoutable.HeightProperty, 28d));
        Assert.Contains(tabControl.Styles, style => style is Style concrete && HasSetter(concrete, TemplatedControl.PaddingProperty, new Thickness(0)));
        Assert.Contains(tabControl.Styles, style => style is Style concrete && HasSetter(concrete, TemplatedControl.TemplateProperty));
        Assert.Contains(tabControl.Styles, style => style is Style concrete && HasSetter(concrete, TemplatedControl.BorderThicknessProperty, new Thickness(0)));
        Assert.Contains(tabControl.Styles, style => style is Style concrete && HasSetter(concrete, Border.BorderThicknessProperty, new Thickness(0)));
        Assert.DoesNotContain(tabControl.Styles, style => style is Style concrete && HasSetter(concrete, TemplatedControl.BorderThicknessProperty, new Thickness(0, 0, 0, 3)));
        Assert.Contains(tabControl.Styles, style => style is Style concrete && HasSetter(concrete, Layoutable.MaxHeightProperty, RibbonVisualMetrics.SmallRowHeight));
        Assert.Contains(tabControl.Styles, style => style is Style concrete && HasSetter(concrete, Layoutable.HeightProperty, 16d));
        Assert.Contains(tabControl.Styles, style => style is Style concrete && HasSetter(concrete, Layoutable.MaxHeightProperty, 16d));
        Assert.Contains(tabControl.Styles, style => style is Style concrete && HasSetter(concrete, TemplatedControl.TemplateProperty));
    });

    [Fact]
    public Task BuildRibbon_DisabledButtonsKeepFlatTransparentChrome() => RunOnUiThread(() =>
    {
        var definition = AvaloniaRibbonComposition.BuildDefinition();
        var registry = AvaloniaRibbonComposition.BuildRegistry(() => null, _ => { });
        var ribbon = AvaloniaRibbonRenderer.BuildRibbon(definition, registry);

        var tabControl = Assert.IsType<TabControl>(ribbon);
        Assert.Contains(tabControl.Styles, style => style is Style concrete
            && HasSetter(concrete, Visual.OpacityProperty, 0.45d)
            && HasSetter(concrete, TemplatedControl.BackgroundProperty, Brushes.Transparent)
            && HasSetter(concrete, TemplatedControl.BorderBrushProperty, Brushes.Transparent));
        Assert.Contains(tabControl.Styles, style => style is Style concrete
            && HasSetter(concrete, Border.BackgroundProperty, Brushes.Transparent)
            && HasSetter(concrete, Border.BorderBrushProperty, Brushes.Transparent));
    });

    [Fact]
    public Task DropdownChevron_UsesWindowsChevronPath() => RunOnUiThread(() =>
    {
        var content = AvaloniaRibbonRenderer.BuildTabContent(BuildHomeTab(), new RibbonCommandRegistry());
        var window = new Window { Width = 1200, Height = 200, Content = content };
        window.Show();
        window.Measure(new Size(1200, 200));
        window.Arrange(new Rect(0, 0, 1200, 200));

        var chevrons = content.GetLogicalDescendants()
            .OfType<Viewbox>()
            .Where(viewbox => viewbox.Width == 10 &&
                              viewbox.Height == 8 &&
                              viewbox.Child is AvaloniaPath path &&
                              path.StrokeThickness == 1.45 &&
                              path.StrokeLineCap == PenLineCap.Round &&
                              path.StrokeJoin == PenLineJoin.Round)
            .ToList();

        Assert.True(chevrons.Count >= 2);
    });

    [Fact]
    public Task RibbonCheckBox_UsesCompactWindowsSizedTemplate() => RunOnUiThread(() =>
    {
        var definition = new RibbonDefinitionBuilder()
            .Tab("view", "View", "V", view =>
            {
                view.Group("show", "Show", "S", 100, group =>
                {
                    group.CheckBox("gridlines", "Gridlines");
                });
            })
            .Build();
        var content = AvaloniaRibbonRenderer.BuildTabContent(definition.FindTab("view")!, new RibbonCommandRegistry());

        var checkBox = content.GetLogicalDescendants().OfType<CheckBox>().Single();

        Assert.Equal(16, checkBox.Height);
        Assert.Equal(16, checkBox.MinHeight);
        Assert.Equal(16, checkBox.MaxHeight);
        Assert.NotNull(checkBox.Template);
    });

    [Fact]
    public Task ComboBox_UsesCompactHeightWithoutClippingPadding() => RunOnUiThread(() =>
    {
        var content = AvaloniaRibbonRenderer.BuildTabContent(BuildHomeTab(), registry: null);

        var combo = content.GetLogicalDescendants().OfType<ComboBox>().Single();

        Assert.Equal(RibbonVisualMetrics.SmallRowHeight, combo.Height);
        Assert.Equal(RibbonVisualMetrics.SmallRowHeight, combo.MinHeight);
        Assert.Equal(RibbonVisualMetrics.SmallRowHeight, combo.MaxHeight);
        Assert.Equal(new Thickness(6, 0, 18, 0), combo.Padding);
        Assert.Equal(new Thickness(2, 0, 2, 0), combo.Margin);
        Assert.Equal(new Thickness(1), combo.BorderThickness);
        Assert.False(combo.ClipToBounds);
        Assert.Equal(VerticalAlignment.Center, combo.VerticalContentAlignment);
    });

    [Fact]
    public Task BuildRibbon_ContextSource_ShowsAndHidesContextualTabs() => RunOnUiThread(() =>
    {
        var definition = new RibbonDefinitionBuilder()
            .Tab("home", "Home", "H", t => t.Group("g", "G", "G", 1, g => g.Button("b", "B")))
            .ContextualTab("chart", "Chart Design", new RibbonTabContext("chart.selected", "Chart Design", RibbonContextColor.Green),
                t => t.Group("cg", "CG", "C", 1, g => g.Button("cb", "CB")))
            .Build();

        var source = new FakeContextSource();
        var ribbon = AvaloniaRibbonRenderer.BuildRibbon(definition, registry: null, source);
        var tabControl = Assert.IsType<TabControl>(ribbon);

        static IEnumerable<string> TabIds(TabControl tc) =>
            tc.Items.OfType<TabItem>().Select(i => (string)i.Tag!);

        // File stays in front of the static tabs, but Home remains selected.
        Assert.Equal(new[] { "FileTab", "home" }, TabIds(tabControl).ToArray());
        Assert.Equal(1, tabControl.SelectedIndex);

        // Activating the chart context inserts the contextual tab.
        source.Raise(RibbonContextState.None.With("chart.selected"));
        Assert.Equal(new[] { "FileTab", "home", "chart" }, TabIds(tabControl).ToArray());

        // Selecting it, then clearing context, removes it and falls back to the first tab.
        tabControl.SelectedIndex = 2;
        source.Raise(RibbonContextState.None);
        Assert.Equal(new[] { "FileTab", "home" }, TabIds(tabControl).ToArray());
        Assert.Equal(1, tabControl.SelectedIndex);
    });

    [Fact]
    public Task BuildRibbon_SameContextRefresh_RebuildsContextualCommandState() => RunOnUiThread(() =>
    {
        var definition = new RibbonDefinitionBuilder()
            .Tab("home", "Home", "H", t => t.Group("g", "G", "G", 1, g => g.Button("home", "Home")))
            .ContextualTab(
                "drawing",
                "Drawing Format",
                new RibbonTabContext("drawing.selected", "Drawing Format", RibbonContextColor.Purple),
                t => t.Group("format", "Format", "F", 1, g => g.Button("direction", "Text Direction")))
            .Build();
        var command = new MutableStatefulCommand(new RibbonCommandState(IsEnabled: false));
        var registry = new RibbonCommandRegistry();
        registry.Register("direction", command);
        registry.Register("home", new NoOpCommand());
        var source = new FakeContextSource();
        source.Raise(RibbonContextState.None.With("drawing.selected"));
        var tabControl = Assert.IsType<TabControl>(
            AvaloniaRibbonRenderer.BuildRibbon(definition, registry, source));
        tabControl.SelectedItem = tabControl.Items
            .OfType<TabItem>()
            .Single(item => Equals(item.Tag, "drawing"));

        static Button DirectionButton(TabControl tabs) =>
            ((Control)tabs.Items
                .OfType<TabItem>()
                .Single(item => Equals(item.Tag, "drawing"))
                .Content!)
            .GetLogicalDescendants()
            .OfType<Button>()
            .Single(button => Equals(button.Tag, "direction"));

        Assert.False(DirectionButton(tabControl).IsEnabled);
        command.State = new RibbonCommandState(IsEnabled: true);

        source.Raise(RibbonContextState.None.With("drawing.selected"));

        Assert.True(DirectionButton(tabControl).IsEnabled);
        Assert.Equal("drawing", ((TabItem)tabControl.SelectedItem!).Tag);
    });

    [Fact]
    public Task BuildRibbon_ContextualTabsAppearBeforeHelpInWindowsOrder() => RunOnUiThread(() =>
    {
        var definition = new RibbonDefinitionBuilder()
            .Tab("HomeTab", "Home", "H", t => t.Group("g", "G", "G", 1, g => g.Button("b", "B")))
            .Tab("HelpTab", "Help", "Y", t => t.Group("hg", "HG", "Y", 1, g => g.Button("hb", "HB")))
            .ContextualTab("ChartFormatTab", "Chart Format", new RibbonTabContext("chart.selected", "Chart Format", RibbonContextColor.Green, DisplayOrder: 3),
                t => t.Group("cfg", "CFG", "F", 1, g => g.Button("cfb", "CFB")))
            .ContextualTab("ChartDesignTab", "Chart Design", new RibbonTabContext("chart.selected", "Chart Design", RibbonContextColor.Green, DisplayOrder: 2),
                t => t.Group("cdg", "CDG", "C", 1, g => g.Button("cdb", "CDB")))
            .Build();

        var source = new FakeContextSource();
        source.Raise(RibbonContextState.None.With("chart.selected"));
        var ribbon = AvaloniaRibbonRenderer.BuildRibbon(definition, registry: null, source);
        var tabControl = Assert.IsType<TabControl>(ribbon);

        var ids = tabControl.Items.OfType<TabItem>().Select(i => (string)i.Tag!).ToArray();
        Assert.Equal(new[] { "FileTab", "HomeTab", "ChartDesignTab", "ChartFormatTab", "HelpTab" }, ids);
    });

    [Fact]
    public void ParityCaptureToolContextOverride_IgnoresNormalContextRefreshUntilCleared()
    {
        var source = new AvaloniaRibbonContextSource();

        source.OnTableActive(true);
        source.SetParityCaptureContext("pivot.active");
        source.OnTableActive(false);

        Assert.True(source.Current.IsActive("pivot.active"));
        Assert.False(source.Current.IsActive(DrawingObjectContextualRibbonPlanner.TableContextKey));

        source.SetParityCaptureContext(null);

        Assert.False(source.Current.IsActive("pivot.active"));
        Assert.True(source.Current.IsActive(DrawingObjectContextualRibbonPlanner.TableContextKey));
    }

    [Fact]
    public void DrawingObjectContext_TextBoxActivatesShapeFormatContext()
    {
        var source = new AvaloniaRibbonContextSource();

        source.OnDrawingObjectSelected(SelectionPaneObjectKind.TextBox);

        Assert.True(source.Current.IsActive(DrawingObjectContextualRibbonPlanner.ShapeContextKey));
        Assert.False(source.Current.IsActive(DrawingObjectContextualRibbonPlanner.PictureContextKey));
    }

    private sealed class FakeContextSource : IRibbonContextSource
    {
        public RibbonContextState Current { get; private set; } = RibbonContextState.None;
        public event EventHandler? ContextChanged;
        public void Raise(RibbonContextState state)
        {
            Current = state;
            ContextChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private static bool HasSetter(Style style, AvaloniaProperty property, object expectedValue) =>
        style.Setters
            .OfType<Setter>()
            .Any(setter => setter.Property == property && Equals(setter.Value, expectedValue));

    private static bool HasSetter(Style style, AvaloniaProperty property) =>
        style.Setters
            .OfType<Setter>()
            .Any(setter => setter.Property == property);

    [Fact]
    public Task UnregisteredCommand_DisablesPrimaryActionsButKeepsMenusReachable() => RunOnUiThread(() =>
    {
        var tab = BuildHomeTab();
        // Match WPF: command-only actions are disabled, while controls with real menu
        // items remain reachable even when their primary command is unregistered.
        var content = AvaloniaRibbonRenderer.BuildTabContent(tab, new RibbonCommandRegistry());

        var buttons = content.GetLogicalDescendants().OfType<Button>().ToList();
        Assert.NotEmpty(buttons);
        Assert.All(
            buttons.Where(button => button.Flyout is null),
            button => Assert.False(button.IsEnabled));
        Assert.All(
            buttons.Where(button => button.Flyout is MenuFlyout),
            button => Assert.True(button.IsEnabled));
    });

    [Fact]
    public Task StatefulCommand_DisabledState_RendersButtonAndToggleDisabled() => RunOnUiThread(() =>
    {
        var registry = new RibbonCommandRegistry();
        registry.Register("cut", new StatefulCommand(new RibbonCommandState(IsEnabled: false)));
        registry.Register("bold", new StatefulCommand(new RibbonCommandState(IsEnabled: false, IsChecked: true)));

        var content = AvaloniaRibbonRenderer.BuildTabContent(BuildHomeTab(), registry);

        var cut = content.GetLogicalDescendants()
            .OfType<Button>()
            .Single(b => Equals(b.Tag, "cut"));
        var bold = content.GetLogicalDescendants()
            .OfType<ToggleButton>()
            .Single(b => Equals(b.Tag, "bold"));

        Assert.False(cut.IsEnabled);
        Assert.False(bold.IsEnabled);
        Assert.True(bold.IsChecked);
    });

    [Fact]
    public Task StatefulCheckBox_InitialCheckedState_RendersBeforeRefresh() => RunOnUiThread(() =>
    {
        var definition = new RibbonDefinitionBuilder()
            .Tab("view", "View", "V", view =>
            {
                view.Group("show", "Show", "S", 100, group =>
                {
                    group.CheckBox("gridlines", "Gridlines");
                });
            })
            .Build();
        var registry = new RibbonCommandRegistry();
        registry.Register("gridlines", new StatefulCommand(new RibbonCommandState(IsChecked: true)));

        var content = AvaloniaRibbonRenderer.BuildTabContent(definition.FindTab("view")!, registry);

        var checkBox = content.GetLogicalDescendants().OfType<CheckBox>().Single();
        Assert.True(checkBox.IsChecked);
        Assert.True(checkBox.IsEnabled);
    });

    [Fact]
    public Task StatefulCheckBox_SynchronizationDoesNotReexecuteTheCommand()
    {
        return RunOnUiThread(() =>
        {
            var definition = new RibbonDefinitionBuilder()
                .Tab("view", "View", "V", view =>
                {
                    view.Group("show", "Show", "S", 100, group =>
                    {
                        group.CheckBox("headings", "Headings");
                    });
                })
                .Build();
            var executions = 0;
            MutableStatefulCommand? command = null;
            command = new MutableStatefulCommand(
                new RibbonCommandState(IsChecked: false),
                () =>
                {
                    executions++;
                    command!.State = command.State with { IsChecked = !command.State.IsChecked };
                });
            var registry = new RibbonCommandRegistry();
            registry.Register("headings", command);
            Control? content = null;
            content = AvaloniaRibbonRenderer.BuildTabContent(
                definition.FindTab("view")!,
                registry,
                afterExecute: () => AvaloniaRibbonRenderer.SyncToggleStates(content!, registry));
            var window = new Window { Width = 320, Height = 160, Content = content };
            window.Show();

            var checkBox = content.GetLogicalDescendants().OfType<CheckBox>().Single();
            command.State = new RibbonCommandState(IsChecked: true);
            AvaloniaRibbonRenderer.SyncToggleStates(content, registry);

            Assert.True(checkBox.IsChecked);
            Assert.Equal(0, executions);

            checkBox.IsChecked = false;
            Assert.Equal(1, executions);
            window.Close();
        });
    }

    [Fact]
    public Task StaticDrawTab_RendersAllBackedFormatCommandsEnabled() => RunOnUiThread(() =>
    {
        var definition = AvaloniaRibbonComposition.BuildDefinition();
        var registry = AvaloniaRibbonComposition.BuildRegistry(() => null, _ => { });

        var content = AvaloniaRibbonRenderer.BuildTabContent(definition.FindTab("DrawTab")!, registry);

        var bringForward = content.GetLogicalDescendants().OfType<Button>().Single(button => Equals(button.Tag, "Bring Forward"));
        var sendBackward = content.GetLogicalDescendants().OfType<Button>().Single(button => Equals(button.Tag, "Send Backward"));
        var selectionPane = content.GetLogicalDescendants().OfType<Button>().Single(button => Equals(button.Tag, FreeXRibbonCommandIds.DrawingSelectionPane));
        var rotate = content.GetLogicalDescendants().OfType<Button>().Single(button => Equals(button.Tag, "Rotate Object"));
        var objectSize = content.GetLogicalDescendants().OfType<Button>().Single(button => Equals(button.Tag, "Object Size"));
        var fill = content.GetLogicalDescendants().OfType<Button>().Single(button => Equals(button.Tag, "Shape Fill"));
        var outline = content.GetLogicalDescendants().OfType<Button>().Single(button => Equals(button.Tag, "Object Outline"));
        var crop = content.GetLogicalDescendants().OfType<Button>().Single(button => Equals(button.Tag, "Crop Picture"));
        var gradient = content.GetLogicalDescendants().OfType<Button>().Single(button => Equals(button.Tag, "Shape Gradient"));
        var effects = content.GetLogicalDescendants().OfType<Button>().Single(button => Equals(button.Tag, "Shape Effects"));

        Assert.True(bringForward.IsEnabled);
        Assert.True(sendBackward.IsEnabled);
        Assert.True(selectionPane.IsEnabled);
        Assert.True(rotate.IsEnabled);
        Assert.True(objectSize.IsEnabled);
        Assert.True(fill.IsEnabled);
        Assert.True(outline.IsEnabled);
        Assert.True(crop.IsEnabled);
        Assert.True(gradient.IsEnabled);
        Assert.True(effects.IsEnabled);
    });

    [Fact]
    public Task IconBuild_ProducesVectorShapes_NotTextGlyphs() => RunOnUiThread(() =>
    {
        // Save is a multi-line vector icon: it must render as native Avalonia shapes, proving the
        // Avalonia renderer draws the shared neutral geometry rather than a single TextBlock glyph.
        var icon = AvaloniaRibbonIcons.Build(RibbonCommandIconKind.Save, 24);

        var viewbox = Assert.IsType<Viewbox>(icon);
        var visuals = viewbox.GetVisualDescendants().ToList();

        // The Save geometry is made entirely of lines.
        Assert.Contains(visuals, v => v is Line);
        // And it is definitely not a lone text glyph.
        Assert.DoesNotContain(visuals, v => v is TextBlock);
    });

    [Fact]
    public Task IconBuild_ShapeIconHasPathAndEllipse() => RunOnUiThread(() =>
    {
        // Protect (shield) mixes a stroked path and a check path; Cut mixes lines and ellipses.
        var shield = AvaloniaRibbonIcons.Build(RibbonCommandIconKind.Protect, 24);
        Assert.Contains(((Viewbox)shield).GetVisualDescendants(), v => v is AvaloniaPath);

        var cut = AvaloniaRibbonIcons.Build(RibbonCommandIconKind.Cut, 24);
        var cutVisuals = ((Viewbox)cut).GetVisualDescendants().ToList();
        Assert.Contains(cutVisuals, v => v is Line);
        Assert.Contains(cutVisuals, v => v is Ellipse);
    });

    [Fact]
    public Task IconBuild_EveryKind_ProducesVisualsAndScales() => RunOnUiThread(() =>
    {
        foreach (var kind in Enum.GetValues<RibbonCommandIconKind>())
        {
            var icon = AvaloniaRibbonIcons.Build(kind, 22);
            var viewbox = Assert.IsType<Viewbox>(icon);
            Assert.Equal(22, viewbox.Width);

            var canvas = Assert.IsType<Canvas>(viewbox.Child);
            Assert.NotEmpty(canvas.Children);
        }
    });

    [Fact]
    public Task IconBuild_Accent_ColorsTheShape() => RunOnUiThread(() =>
    {
        // A non-None accent should color the geometry with the accent color, ignoring the foreground.
        var accented = new RibbonCommandIcon(RibbonCommandIconKind.Save, RibbonCommandIconAccent.Warning);
        var icon = AvaloniaRibbonIcons.Build(accented, 24, Brushes.Black);

        var expected = RibbonIconAccents.Resolve(RibbonCommandIconAccent.Warning)!.Value;
        var lines = ((Viewbox)icon).GetVisualDescendants().OfType<Line>().ToList();
        Assert.NotEmpty(lines);

        var stroke = Assert.IsType<SolidColorBrush>(lines[0].Stroke);
        Assert.Equal(Color.FromArgb(expected.A, expected.R, expected.G, expected.B), stroke.Color);
    });

    [Fact]
    public Task LargeControl_EmbedsIconShapes() => RunOnUiThread(() =>
    {
        var tab = BuildHomeTab();
        var content = AvaloniaRibbonRenderer.BuildTabContent(tab, new RibbonCommandRegistry());

        var window = new Window { Width = 1200, Height = 200, Content = content };
        window.Show();
        window.Measure(new Size(1200, 200));
        window.Arrange(new Rect(0, 0, 1200, 200));

        // The rendered ribbon should contain real icon shapes (from the neutral geometry), not just text.
        var shapes = content.GetVisualDescendants().OfType<Shape>().ToList();
        Assert.NotEmpty(shapes);
    });

    [Theory]
    [InlineData("Bold")]
    [InlineData("Italic")]
    [InlineData("Underline")]
    [InlineData("Strikethrough")]
    [InlineData("Font Color")]
    [InlineData("Accounting Number Format")]
    [InlineData("AutoSum")]
    [InlineData("Find & Select")]
    [InlineData("Sort & Filter")]
    [InlineData("Paste")]
    [InlineData("Format Painter")]
    [InlineData("Conditional Formatting")]
    [InlineData("Selection Pane")]
    [InlineData("Remove Duplicates#RemoveDuplicatesBtn_Click")]
        [InlineData("Advanced")]
        [InlineData("Page Setup dialog")]
        [InlineData("View Gridlines")]
        [InlineData("View Headings")]
        [InlineData("Shape Gradient")]
        [InlineData("Clear#ClearFilterButton_Click")]
        [InlineData("Sort A to Z#SortAscButton_Click")]
        [InlineData("Sort Z to A#SortDescButton_Click")]
        [InlineData("Insert Link")]
        [InlineData("Header & Footer")]
        [InlineData("Pictures")]
        [InlineData("Scale Width")]
        [InlineData("Scale Height")]
        [InlineData("Scale Percent")]
        [InlineData("Refresh")]
        [InlineData("Select")]
        [InlineData("Add Watch")]
        [InlineData("Delete Watch")]
        [InlineData("Allow Edit Ranges")]
        [InlineData("Date and Time")]
        [InlineData("Lookup and Reference")]
        [InlineData("Math and Trig")]
        public Task IconBuild_KnownCommand_LoadsSharedSvg(string commandName) => RunOnUiThread(() =>
    {
        // A command with a matching CommandIconsSvg/<slug>.svg must render the SAME shared SVG the WPF
        // host loads, parsed natively into an Avalonia Image backed by a DrawingImage (no external SVG
        // library), not the fallback kind glyph.
        var icon = AvaloniaRibbonIcons.Build(RibbonCommandIconKind.Generic, 32, commandName);

        var image = Assert.IsType<Image>(icon);
        var drawingImage = Assert.IsType<DrawingImage>(image.Source);
        var group = Assert.IsType<DrawingGroup>(drawingImage.Drawing);
        Assert.NotEmpty(group.Children);
        Assert.Equal(32, image.Width);
    });

    [Fact]
    public Task IconBuild_UnknownCommand_FallsBackToKindGlyph() => RunOnUiThread(() =>
    {
        // A command name with no SVG file falls back to the neutral kind glyph (a Viewbox over a Canvas),
        // exactly like the WPF host.
        var icon = AvaloniaRibbonIcons.Build(RibbonCommandIconKind.Save, 24, "No Such Command Xyzzy");

        var viewbox = Assert.IsType<Viewbox>(icon);
        Assert.IsType<Canvas>(viewbox.Child);
    });
}
