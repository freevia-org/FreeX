using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Free.Shared.Ribbon.Avalonia;

namespace Free.Shared.Ribbon.Tests;

public sealed class AvaloniaRibbonSplitButtonTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Theory]
    [InlineData(RibbonCommandLayoutKind.Large, 80d)]
    [InlineData(RibbonCommandLayoutKind.Medium, 20d)]
    [InlineData(RibbonCommandLayoutKind.Small, 14d)]
    public async Task SplitButton_SeparatesPrimaryAndDropdownActions(
        RibbonCommandLayoutKind layout,
        double dropdownWidth)
    {
        await Session.Dispatch(() =>
        {
            var primaryExecutions = 0;
            var menuExecutions = 0;
            var registry = new RibbonCommandRegistry();
            registry.Register("paste", new RecordingCommand(() => primaryExecutions++));
            registry.Register("pasteSpecial", new RecordingCommand(() => menuExecutions++));

            var content = AvaloniaRibbonRenderer.BuildTabContent(BuildSplitTab(layout), registry);
            var window = Show(content);
            try
            {
                var buttons = content.GetLogicalDescendants().OfType<Button>().ToArray();
                var primary = Assert.Single(buttons, button => Equals(button.Tag, "paste"));
                var dropdown = Assert.Single(buttons, button => Equals(button.Tag, "paste.Dropdown"));
                var flyout = Assert.IsType<MenuFlyout>(dropdown.Flyout);

                Assert.Equal(dropdownWidth, dropdown.Width);
                primary.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, primary));
                Assert.Equal(1, primaryExecutions);

                var item = Assert.Single(flyout.Items.OfType<MenuItem>());
                item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, item));
                Assert.Equal(1, menuExecutions);
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(RibbonCommandLayoutKind.Large)]
    [InlineData(RibbonCommandLayoutKind.Medium)]
    [InlineData(RibbonCommandLayoutKind.Small)]
    public async Task SplitButton_DropdownRemainsEnabledWhenPrimaryCommandIsUnavailable(RibbonCommandLayoutKind layout)
    {
        await Session.Dispatch(() =>
        {
            var registry = new RibbonCommandRegistry();
            registry.Register("pasteSpecial", new RecordingCommand(() => { }));

            var content = AvaloniaRibbonRenderer.BuildTabContent(BuildSplitTab(layout), registry);
            var window = Show(content);
            try
            {
                var dropdown = Assert.Single(
                    content.GetLogicalDescendants().OfType<Button>(),
                    button => Equals(button.Tag, "paste.Dropdown"));
                var primary = Assert.Single(
                    content.GetLogicalDescendants().OfType<Button>(),
                    button => Equals(button.Tag, "paste"));

                Assert.True(dropdown.IsEnabled);
                Assert.False(primary.IsEnabled);
                Assert.IsType<MenuFlyout>(dropdown.Flyout);
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task CollapsedGroup_AssignsDerivedKeyTipAndRoutesMenuKeyTip()
    {
        await Session.Dispatch(() =>
        {
            var executions = 0;
            var registry = new RibbonCommandRegistry();
            registry.Register("one", new RecordingCommand(() => executions++));
            registry.Register("two", new RecordingCommand(() => { }));

            var definition = new RibbonDefinitionBuilder()
                .Tab("home", "Home", "H", tab => tab.Group("group", "Group", "G", 1, group =>
                {
                    group.Button("one", "One", control => control with
                    {
                        KeyTip = "O",
                        PreferredLayout = RibbonCommandLayoutKind.Large,
                    });
                    group.Button("two", "Two", control => control with
                    {
                        KeyTip = "T",
                        PreferredLayout = RibbonCommandLayoutKind.Large,
                    });
                }))
                .Build();
            var ribbon = AvaloniaRibbonRenderer.BuildRibbon(definition, registry);
            var window = Show(ribbon, 180);
            try
            {
                var collapsed = ribbon.GetLogicalDescendants().OfType<Button>()
                    .First(button => button.Classes.Contains("freex-ribbon-collapsed-group"));

                Assert.True(AvaloniaRibbonRenderer.TryActivateKeyTip(ribbon, "HGR"));
                Assert.NotNull(collapsed.Flyout);
                Assert.True(collapsed.Flyout!.IsOpen);
                collapsed.Flyout.Hide();
                Assert.True(AvaloniaRibbonRenderer.TryActivateKeyTip(ribbon, "HGRO"));
                Assert.Equal(1, executions);
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(RibbonCommandLayoutKind.Medium)]
    [InlineData(RibbonCommandLayoutKind.Small)]
    public async Task SplitButton_KeyTipReachesDropdownMenu(RibbonCommandLayoutKind layout)
    {
        await Session.Dispatch(() =>
        {
            var menuExecutions = 0;
            var registry = new RibbonCommandRegistry();
            registry.Register("paste", new RecordingCommand(() => { }));
            registry.Register("pasteSpecial", new RecordingCommand(() => menuExecutions++));

            var ribbon = AvaloniaRibbonRenderer.BuildRibbon(BuildSplitDefinition(layout), registry);
            var window = Show(ribbon);
            try
            {
                Assert.True(AvaloniaRibbonRenderer.TryActivateKeyTip(ribbon, "HPS"));
                Assert.Equal(1, menuExecutions);
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    private static RibbonTab BuildSplitTab(RibbonCommandLayoutKind layout) =>
        BuildSplitDefinition(layout)
            .FindTab("home")!;

    private static RibbonDefinition BuildSplitDefinition(RibbonCommandLayoutKind layout) =>
        new RibbonDefinitionBuilder()
            .Tab("home", "Home", "H", tab => tab.Group("group", "Group", "G", 1, group =>
                group.SplitButton("paste", "Paste", new RibbonMenu(new[]
                {
                    new RibbonMenuItem("Paste Special", "pasteSpecial", "S")
                }), control => control with
                {
                    PreferredLayout = layout,
                    KeyTip = "P"
                })))
            .Build();

    private static Window Show(Control content, double width = 420)
    {
        var window = new Window { Width = width, Height = 160, Content = content };
        window.Show();
        window.Measure(new Size(width, 160));
        window.Arrange(new Rect(0, 0, width, 160));
        return window;
    }

    private sealed class RecordingCommand(Action action) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context) => action();
    }
}
