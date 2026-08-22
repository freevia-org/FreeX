using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Themes.Fluent;
using Free.Shared.Ribbon.Avalonia;

[assembly: AvaloniaTestApplication(typeof(Free.Shared.Ribbon.Tests.RibbonHeadlessApp))]

namespace Free.Shared.Ribbon.Tests;

public sealed class RibbonHeadlessApp : Application
{
    public override void Initialize() => Styles.Add(new FluentTheme());

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<RibbonHeadlessApp>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = true });
}

public sealed class AvaloniaRibbonKeyTipBadgeTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public async Task MenuKeyTips_RenderRootAndNestedScopes_AndCleanupRecursively()
    {
        await Session.Dispatch(() =>
        {
            var registry = new RibbonCommandRegistry();
            registry.Register("menu", new NoOpCommand());
            registry.Register("child", new NoOpCommand());
            registry.Register("disabled", new DisabledCommand());
            var ribbon = AvaloniaRibbonRenderer.BuildRibbon(BuildDefinition(), registry);
            var window = Show(ribbon);
            try
            {
                AvaloniaRibbonRenderer.SetTopLevelKeyTipsVisible(ribbon, true);
                var menuButton = ribbon.GetLogicalDescendants().OfType<Button>()
                    .First(button => Equals(button.Tag, "menu") && button.Flyout is MenuFlyout);
                var flyout = Assert.IsType<MenuFlyout>(menuButton.Flyout);
                var rootItems = flyout.Items.OfType<MenuItem>().ToArray();

                // Host-specific key-tip loops may open the live flyout directly; the shared
                // registration must still reveal the root scope and track it for cleanup.
                flyout.ShowAt(menuButton);
                AssertBadgeVisible(rootItems.Single(item => Equals(item.Tag, "parent")));
                flyout.Hide();
                AvaloniaRibbonRenderer.SetTopLevelKeyTipsVisible(ribbon, false);
                Assert.All(rootItems, item => Assert.Null(item.Icon));

                AvaloniaRibbonRenderer.SetTopLevelKeyTipsVisible(ribbon, true);
                Assert.True(AvaloniaRibbonRenderer.TryActivateKeyTip(ribbon, "HM"));
                AssertBadgeVisible(rootItems.Single(item => Equals(item.Tag, "parent")));
                AssertBadgeVisible(rootItems.Single(item => Equals(item.Tag, "disabled")));
                var parent = rootItems.Single(item => Equals(item.Tag, "parent"));
                Assert.Null(parent.Items.OfType<MenuItem>().Single().Icon);

                Assert.True(AvaloniaRibbonRenderer.TryActivateKeyTip(ribbon, "HMP"));
                Assert.True(parent.IsSubMenuOpen);
                AssertBadgeVisible(parent.Items.OfType<MenuItem>().Single());

                // This is the shared cleanup path used by Escape/completion in the hosts.
                AvaloniaRibbonRenderer.SetTopLevelKeyTipsVisible(ribbon, false);
                Assert.All(rootItems, item => Assert.Null(item.Icon));
                Assert.Null(parent.Items.OfType<MenuItem>().Single().Icon);
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task DisabledMenuKeyTip_IsDisplayed_ButCannotActivate()
    {
        await Session.Dispatch(() =>
        {
            var registry = new RibbonCommandRegistry();
            registry.Register("menu", new NoOpCommand());
            registry.Register("disabled", new DisabledCommand());
            var ribbon = AvaloniaRibbonRenderer.BuildRibbon(BuildDefinition(), registry);
            var window = Show(ribbon);
            try
            {
                AvaloniaRibbonRenderer.SetTopLevelKeyTipsVisible(ribbon, true);
                Assert.True(AvaloniaRibbonRenderer.TryActivateKeyTip(ribbon, "HM"));
                var menuButton = ribbon.GetLogicalDescendants().OfType<Button>()
                    .First(button => Equals(button.Tag, "menu") && button.Flyout is MenuFlyout);
                var disabled = Assert.IsType<MenuFlyout>(menuButton.Flyout).Items.OfType<MenuItem>()
                    .Single(item => Equals(item.Tag, "disabled"));
                Assert.False(disabled.IsEnabled);
                AssertBadgeVisible(disabled);
                Assert.False(AvaloniaRibbonRenderer.TryActivateKeyTip(ribbon, "HMD"));
                Assert.Null(disabled.Icon);
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task MenuKeyTips_RestoreOriginalIconSlotWhenHidden()
    {
        await Session.Dispatch(() =>
        {
            var ribbon = AvaloniaRibbonRenderer.BuildRibbon(BuildDefinition(), new RibbonCommandRegistry());
            var window = Show(ribbon);
            try
            {
                var menuButton = ribbon.GetLogicalDescendants().OfType<Button>()
                    .First(button => Equals(button.Tag, "menu") && button.Flyout is MenuFlyout);
                var flyout = Assert.IsType<MenuFlyout>(menuButton.Flyout);
                var item = flyout.Items.OfType<MenuItem>().First(candidate => Equals(candidate.Tag, "parent"));
                var originalIcon = new Border { Width = 7, Height = 7 };
                item.Icon = originalIcon;

                // The shared renderer keeps its generated badge out of Header and restores the
                // pre-existing icon object when the scope is hidden again.
                AvaloniaRibbonRenderer.SetMenuKeyTipsVisible(flyout, false);
                AvaloniaRibbonRenderer.SetMenuKeyTipsVisible(flyout, true);
                AssertBadgeVisible(item);
                AvaloniaRibbonRenderer.SetMenuKeyTipsVisible(flyout, false);
                Assert.Same(originalIcon, item.Icon);
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task CollapsedGroupOverflow_RendersItsMenuKeyTips()
    {
        await Session.Dispatch(() =>
        {
            var definition = new RibbonDefinitionBuilder()
                .Tab("home", "Home", "H", tab => tab.Group("group", "Group", "G", 1, group =>
                {
                    group.Button("one", "One", control => control with { KeyTip = "O", PreferredLayout = RibbonCommandLayoutKind.Large });
                    group.Button("two", "Two", control => control with { KeyTip = "T", PreferredLayout = RibbonCommandLayoutKind.Large });
                }))
                .Build();
            var ribbon = AvaloniaRibbonRenderer.BuildRibbon(definition, new RibbonCommandRegistry());
            var window = Show(ribbon, 180);
            try
            {
                var collapsed = ribbon.GetLogicalDescendants().OfType<Button>()
                    .First(button => button.Classes.Contains("free-ribbon-collapsed-group"));
                var flyout = Assert.IsType<MenuFlyout>(collapsed.Flyout);

                flyout.ShowAt(collapsed);
                AvaloniaRibbonRenderer.SetMenuKeyTipsVisible(flyout, true);
                Assert.All(flyout.Items.OfType<MenuItem>(), AssertBadgeVisible);
                AvaloniaRibbonRenderer.SetMenuKeyTipsVisible(flyout, false);
                Assert.All(flyout.Items.OfType<MenuItem>(), item => Assert.Null(item.Icon));
                flyout.Hide();
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task ToggleKeyTip_RaisesTheCommandClickRoute()
    {
        await Session.Dispatch(() =>
        {
            var executed = 0;
            var registry = new RibbonCommandRegistry();
            registry.Register("toggle", new RecordingCommand(() => executed++));
            var definition = new RibbonDefinitionBuilder()
                .Tab("view", "View", "W", tab => tab.Group("window", "Window", "N", 1, group =>
                    group.IconToggle("toggle", "Split", RibbonCommandIconKind.Window, "SP")))
                .Build();
            var ribbon = AvaloniaRibbonRenderer.BuildRibbon(definition, registry);
            var window = Show(ribbon);
            try
            {
                var toggles = ribbon.GetLogicalDescendants().OfType<ToggleButton>()
                    .Where(candidate => candidate.Tag?.ToString() == "toggle")
                    .ToArray();
                toggles.Should().NotBeEmpty();
                toggles.Should().OnlyContain(candidate => candidate.IsChecked != true);
                Assert.True(AvaloniaRibbonRenderer.TryActivateKeyTip(ribbon, "WSP"));
                Assert.Equal(1, executed);
                toggles.Should().OnlyContain(candidate => candidate.IsChecked == true,
                    "all live replicas of the command should reflect the key-tip toggle state");
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    private static RibbonDefinition BuildDefinition() => new RibbonDefinitionBuilder()
        .Tab("home", "Home", "H", tab => tab.Group("group", "Group", "G", 1, group =>
            group.Dropdown("menu", "Menu", new RibbonMenu(new[]
            {
                new RibbonMenuItem("Parent", "parent", "P", Children: new[]
                {
                    new RibbonMenuItem("Child", "child", "C"),
                }),
                new RibbonMenuItem("Disabled", "disabled", "D"),
            }), control => control with { KeyTip = "M" })))
        .Build();

    private static Window Show(Control content, double width = 640)
    {
        var window = new Window { Width = width, Height = 220, Content = content };
        window.Show();
        window.Measure(new Size(width, 220));
        window.Arrange(new Rect(0, 0, width, 220));
        return window;
    }

    private static void AssertBadgeVisible(MenuItem item)
    {
        var badge = Assert.IsType<Border>(item.Icon);
        Assert.Equal("RibbonKeyTipBadge", badge.Tag);
        Assert.True(badge.IsVisible);
    }

    private sealed class NoOpCommand : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
        }
    }

    private sealed class DisabledCommand : IRibbonStatefulCommand
    {
        public void Execute(RibbonCommandContext context)
        {
        }

        public RibbonCommandState GetState() => new(IsEnabled: false);
    }

    private sealed class RecordingCommand(Action execute) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context) => execute();
    }
}
