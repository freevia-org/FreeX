using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
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
                        new RibbonMenuItem("Checked", "checked") { IsChecked = true }
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

    private sealed class NoOpCommand : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
        }
    }

    private sealed class RecordingCommand(Action action) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context) => action();
    }
}
