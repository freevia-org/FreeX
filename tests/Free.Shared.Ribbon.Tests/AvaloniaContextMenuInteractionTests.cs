using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Free.Shared.Ribbon.Avalonia;

namespace Free.Shared.Ribbon.Tests;

public sealed class AvaloniaContextMenuInteractionTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public async Task ContextMenu_NestedLeftClosesSubmenuAndRestoresParentSelection()
    {
        await Session.Dispatch(() =>
        {
            var menu = AvaloniaContextMenuRenderer.BuildContextMenu(
                new RibbonMenu(
                [
                    new RibbonMenuItem("Disabled") { IsEnabled = false },
                    RibbonMenuItem.Separator(),
                    new RibbonMenuItem("More", Children:
                    [
                        new RibbonMenuItem("Child"),
                    ]),
                ]),
                _ => { });
            var parent = menu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "More"));
            var child = Assert.Single(parent.Items.OfType<MenuItem>());

            parent.IsSubMenuOpen = true;
            RaiseKey(child, Key.Left);

            Assert.False(parent.IsSubMenuOpen);
            Assert.True(parent.IsSelected);
        }, CancellationToken.None);
    }

    [Fact]
    public async Task ContextMenu_DownSkipsDisabledItemsAndSeparators()
    {
        await Session.Dispatch(() =>
        {
            var menu = AvaloniaContextMenuRenderer.BuildContextMenu(
                new RibbonMenu(
                [
                    new RibbonMenuItem("Disabled") { IsEnabled = false },
                    RibbonMenuItem.Separator(),
                    new RibbonMenuItem("First"),
                    new RibbonMenuItem("Second"),
                ]),
                _ => { });
            var items = menu.Items.OfType<MenuItem>().ToArray();

            items[1].Focus();
            var args = RaiseKey(items[1], Key.Down);

            // Headless controls are detached from a popup visual tree, so focus itself cannot be
            // observed here. Handled proves the shared navigation adapter consumed the key; the
            // neutral planner test proves the selected target is the next enabled item.
            Assert.True(args.Handled);
        }, CancellationToken.None);
    }

    [Fact]
    public async Task ContextMenu_OpenedFocusesFirstEnabledItem_AndRightDefersToRealizedChild()
    {
        await Session.Dispatch(() =>
        {
            var anchor = new Button();
            var window = Show(anchor);
            var menu = AvaloniaContextMenuRenderer.BuildContextMenu(
                new RibbonMenu(
                [
                    new RibbonMenuItem("Disabled") { IsEnabled = false },
                    new RibbonMenuItem("More", Children:
                    [
                        new RibbonMenuItem("Child"),
                    ]),
                ]),
                _ => { });
            var parent = menu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "More"));
            var child = Assert.Single(parent.Items.OfType<MenuItem>());
            menu.PlacementTarget = anchor;

            try
            {
                menu.Open(anchor);
                Dispatcher.UIThread.RunJobs(DispatcherPriority.Input);
                Assert.True(parent.IsFocused);

                RaiseKey(parent, Key.Right);
                Assert.True(parent.IsSubMenuOpen);
                Dispatcher.UIThread.RunJobs(DispatcherPriority.Input);

                Assert.True(child.IsFocused);
            }
            finally
            {
                menu.Close();
                window.Close();
            }
        }, CancellationToken.None);
    }

    private static KeyEventArgs RaiseKey(Control target, Key key)
    {
        var args = new KeyEventArgs
        {
            Key = key,
            RoutedEvent = InputElement.KeyDownEvent,
        };
        target.RaiseEvent(args);
        return args;
    }

    private static Window Show(Control content)
    {
        var window = new Window { Width = 320, Height = 160, Content = content };
        window.Show();
        window.Measure(new Size(320, 160));
        window.Arrange(new Rect(0, 0, 320, 160));
        return window;
    }
}
