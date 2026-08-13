using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Free.Shared.Ribbon.Avalonia;

namespace Free.Shared.Ribbon.Tests;

public sealed class AvaloniaRibbonMenuStateTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public async Task DropdownMenu_PreservesDisabledParentsAndCheckedItems_ForKeyboardParity()
    {
        await Session.Dispatch(() =>
        {
            var executed = 0;
            var registry = new RibbonCommandRegistry();
            registry.Register("menu", new NoOpCommand());
            registry.Register("child", new RecordingCommand(() => executed++));
            registry.Register("checked", new NoOpCommand());
            var definition = new RibbonDefinitionBuilder()
                .Tab("home", "Home", "H", tab => tab.Group("group", "Group", "G", 1, group =>
                    group.Dropdown("menu", "Menu", new RibbonMenu(new[]
                    {
                        new RibbonMenuItem("Disabled", Children: new[]
                        {
                            new RibbonMenuItem("Child", "child", "C")
                        }) { IsEnabled = false },
                        new RibbonMenuItem(
                            "Checked",
                            "checked",
                            Icon: new RibbonCommandIcon(RibbonCommandIconKind.Warning)) { IsChecked = true }
                    }), control => control with { KeyTip = "M" })))
                .Build();
            var ribbon = AvaloniaRibbonRenderer.BuildRibbon(definition, registry);
            var window = Show(ribbon);
            try
            {
                Assert.True(AvaloniaRibbonRenderer.TryActivateKeyTip(ribbon, "HM"));
                var button = ribbon.GetLogicalDescendants().OfType<Button>()
                    .First(candidate => Equals(candidate.Tag, "menu") && candidate.Flyout is MenuFlyout);
                var flyout = Assert.IsType<MenuFlyout>(button.Flyout);
                var items = flyout.Items.OfType<MenuItem>().ToArray();

                Assert.False(items.Single(item => Equals(item.Header, "Disabled")).IsEnabled);
                Assert.False(AvaloniaRibbonRenderer.TryActivateKeyTip(ribbon, "HMD"));
                Assert.False(items.Single(item => Equals(item.Header, "Disabled")).IsSubMenuOpen);
                Assert.Equal(0, executed);

                var checkedItem = items.Single(item => Equals(item.Header, "Checked"));
                Assert.Equal(MenuItemToggleType.CheckBox, checkedItem.ToggleType);
                Assert.True(checkedItem.IsChecked);
                Assert.IsType<Viewbox>(checkedItem.Icon);
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task StateStore_AppliesExplicitInitialToggleState_AndLiveComboValue()
    {
        await Session.Dispatch(() =>
        {
            var store = new RibbonStateStore();
            store.SetState("bold", new RibbonCommandState(IsEnabled: false, IsChecked: true));
            store.SetValue("font", "Arial");

            var observedCheckedState = false;
            var registry = new RibbonCommandRegistry();
            registry.Register("bold", new ObservingCommand(() =>
                observedCheckedState = store.GetState("bold").IsChecked));
            registry.Register("font", new NoOpCommand());
            var content = AvaloniaRibbonRenderer.BuildTabContent(BuildHomeTab(), registry, stateStore: store);
            var window = Show(content);
            try
            {
                var bold = content.GetLogicalDescendants().OfType<ToggleButton>()
                    .Single(toggle => Equals(toggle.Tag, "bold"));
                var font = content.GetLogicalDescendants().OfType<ComboBox>().Single();

                Assert.True(bold.IsChecked);
                Assert.False(bold.IsEnabled);
                Assert.Equal("Arial", font.Text);

                store.SetChecked("bold", false);
                store.SetEnabled("bold", true);
                store.SetValue("font", "Consolas");

                Assert.False(bold.IsChecked);
                Assert.True(bold.IsEnabled);
                Assert.Equal("Consolas", font.Text);

                bold.IsChecked = true;
                bold.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.True(observedCheckedState);
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task StateStore_RebindsContextualTabs_AndDetachesRemovedContent()
    {
        await Session.Dispatch(() =>
        {
            var store = new RibbonStateStore();
            store.SetChecked("bold", true);
            var registry = new RibbonCommandRegistry();
            registry.Register("bold", new NoOpCommand());
            var source = new MutableContextSource();
            var contextTab = BuildHomeTab() with
            {
                Id = "chart",
                Header = "Chart",
                Context = new RibbonTabContext("chart.selected", "Chart", RibbonContextColor.Blue),
            };
            var definition = new RibbonDefinition(new[] { BuildHomeTab(), contextTab });
            var ribbon = AvaloniaRibbonRenderer.BuildRibbon(definition, registry, source, stateStore: store);
            var window = Show(ribbon);
            try
            {
                source.Set(RibbonContextState.None.With("chart.selected"));
                var tabControl = Assert.IsType<TabControl>(ribbon);
                var oldTab = tabControl.Items.OfType<TabItem>().Single(item => Equals(item.Tag, "chart"));
                var oldContent = Assert.IsAssignableFrom<Control>(oldTab.Content);
                var oldToggle = oldContent.GetLogicalDescendants().OfType<ToggleButton>()
                    .Single(toggle => Equals(toggle.Tag, "bold"));
                Assert.True(oldToggle.IsChecked);

                source.Set(RibbonContextState.None);
                store.SetChecked("bold", false);
                Assert.True(oldToggle.IsChecked);

                source.Set(RibbonContextState.None.With("chart.selected"));
                var newTab = tabControl.Items.OfType<TabItem>().Single(item => Equals(item.Tag, "chart"));
                var newContent = Assert.IsAssignableFrom<Control>(newTab.Content);
                var newToggle = newContent.GetLogicalDescendants().OfType<ToggleButton>()
                    .Single(toggle => Equals(toggle.Tag, "bold"));
                Assert.False(newToggle.IsChecked);
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    private static Window Show(Control content)
    {
        var window = new Window { Width = 420, Height = 160, Content = content };
        window.Show();
        window.Measure(new Size(420, 160));
        window.Arrange(new Rect(0, 0, 420, 160));
        return window;
    }

    private static RibbonTab BuildHomeTab() =>
        new RibbonDefinitionBuilder()
            .Tab("home", "Home", "H", home => home.Group("font", "Font", "F", 1, group =>
            {
                group.Toggle("bold", "Bold");
                group.ComboBox("font", "Font", combo => combo with { Items = new[] { "Calibri", "Arial" } });
            }))
            .Build()
            .FindTab("home")!;

    private sealed class MutableContextSource : IRibbonContextSource
    {
        public RibbonContextState Current { get; private set; } = RibbonContextState.None;
        public event EventHandler? ContextChanged;

        public void Set(RibbonContextState state)
        {
            Current = state;
            ContextChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private sealed class NoOpCommand : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
        }
    }

    private sealed class ObservingCommand(Action action) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context) => action();
    }

    private sealed class RecordingCommand(Action action) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context) => action();
    }
}
