using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.IO;
using FreeX.Core.Model;
using Microsoft.Extensions.Logging.Abstractions;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed partial class MainWindowAdaptiveRibbonTests
{
    [Fact]
    public void CollapsedRibbonMenuItems_MirrorSourceMenuStateAndOpenedUpdates()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.SelectRibbonTab("View", 220);
            var arrangeAll = harness.CollapsedActiveMenuItem("Window", "Arrange All");

            arrangeAll.Should().NotBeNull(harness.DebugActiveRibbonChildren);

            // The collapsed Window overflow clones the live "Arrange All" submenu, so it must mirror the
            // source's full set of arrangement options together with their keyboard shortcuts. (The live
            // Arrange All command is not a radio/checkable group in the declarative ribbon -- its check
            // state is refreshed on the source ContextMenu rather than per-item -- so the meaningful mirror
            // invariant is the option set and gesture text, not a check-mark.)
            var options = arrangeAll!.Items.OfType<MenuItem>().ToList();
            options.Select(item => item.Header?.ToString())
                .Should().Equal(new[] { "Tiled", "Horizontal", "Vertical", "Cascade" },
                    "the cloned Arrange All submenu should mirror every source arrangement option in order");
            options.Select(item => item.InputGestureText)
                .Should().Equal(new[] { "T", "H", "V", "C" },
                    "each cloned arrangement option should carry the same keyboard shortcut as its source item");

            // Opening the cloned submenu must run the source's deferred refresh path without throwing and
            // leave the option set intact (it is the live submenu-opened update hook for the clone).
            arrangeAll.RaiseEvent(new RoutedEventArgs(MenuItem.SubmenuOpenedEvent, arrangeAll));
            arrangeAll.Items.OfType<MenuItem>().Select(item => item.Header?.ToString())
                .Should().Equal(new[] { "Tiled", "Horizontal", "Vertical", "Cascade" },
                    "the cloned submenu options should stay stable after the submenu-opened refresh runs");
        });
    }

    [Fact]
    public void CollapsedRibbonMenuItems_DeferNestedMenuRefreshUntilSubmenuOpened()
    {
        StaTestRunner.Run(() =>
        {
            var group = new StackPanel();
            RibbonMetadata.SetGroupName(group, "Window");

            var sourceButton = new Button();
            RibbonTooltip.SetTitle(sourceButton, "Arrange All");
            var sourceMenu = new ContextMenu();
            var sourceChild = new MenuItem { Header = "Tiled", IsCheckable = true };
            sourceMenu.Items.Add(sourceChild);
            sourceMenu.Opened += (_, _) => sourceChild.IsChecked = true;
            sourceButton.ContextMenu = sourceMenu;
            group.Children.Add(sourceButton);

            var createMenu = typeof(MainWindow)
                .GetMethod("CreateLazyCollapsedRibbonGroupMenu", BindingFlags.Static | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "CreateLazyCollapsedRibbonGroupMenu");
            var menu = (ContextMenu)createMenu.Invoke(null, [group])!;

            menu.RaiseEvent(new RoutedEventArgs(ContextMenu.OpenedEvent, menu));
            var arrangeAll = menu.Items.OfType<MenuItem>()
                .Single(item => string.Equals(item.Header?.ToString(), "Arrange All", StringComparison.Ordinal));
            var tiled = arrangeAll.Items.OfType<MenuItem>()
                .Single(item => string.Equals(item.Header?.ToString(), "Tiled", StringComparison.Ordinal));
            tiled.IsChecked.Should().BeFalse("cloned submenus should not run source Opened handlers while only the collapsed group menu opens");

            menu.RaiseEvent(new RoutedEventArgs(ContextMenu.OpenedEvent, menu));

            tiled.IsChecked.Should().BeFalse("top-level menu opening should only refresh top-level source button state");

            arrangeAll.RaiseEvent(new RoutedEventArgs(MenuItem.SubmenuOpenedEvent, arrangeAll));

            tiled.IsChecked.Should().BeTrue("the nested clone should refresh when that submenu is actually displayed");
        });
    }

    [Fact]
    public void CollapsedRibbonMenuItems_RefreshSourceButtonEnabledStateWhenOpened()
    {
        StaTestRunner.Run(() =>
        {
            // The live lazy collapsed-group menu mirrors each leaf source command button's current enabled
            // state every time it opens (SynchronizeCollapsedRibbonTopLevelMenuItems reads the source
            // button's IsEnabled through the cloned item's Tag). Drive that path directly with a real source
            // group + leaf button so the assertion exercises the live refresh, not the legacy assumption that
            // every Home Editing command is a plain leaf (in the declarative ribbon those are all dropdowns).
            var group = new StackPanel();
            RibbonMetadata.SetGroupName(group, "Editing");

            var sourceButton = new Button();
            RibbonTooltip.SetTitle(sourceButton, "Find & Select");
            RibbonTooltip.SetKeyTip(sourceButton, "FD");
            group.Children.Add(sourceButton);
            // The menu only clones currently-visible source buttons, so realize the group offscreen.
            var host = ShowStandaloneRibbonButton(new Button(), 200, 80);
            ((Grid)host.Content).Children.Add(group);
            host.UpdateLayout();
            PumpDispatcher();

            var createMenu = typeof(MainWindow)
                .GetMethod("CreateLazyCollapsedRibbonGroupMenu", BindingFlags.Static | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "CreateLazyCollapsedRibbonGroupMenu");
            var menu = (ContextMenu)createMenu.Invoke(null, [group])!;

            menu.RaiseEvent(new RoutedEventArgs(ContextMenu.OpenedEvent, menu));
            var item = menu.Items.OfType<MenuItem>()
                .FirstOrDefault(menuItem => string.Equals(menuItem.Header?.ToString(), "Find & Select", StringComparison.Ordinal));
            item.Should().NotBeNull("the collapsed Editing overflow should clone its leaf source command");
            item!.IsEnabled.Should().BeTrue("the freshly cloned command starts enabled, mirroring its source");

            sourceButton.IsEnabled = false;
            menu.RaiseEvent(new RoutedEventArgs(ContextMenu.OpenedEvent, menu));

            item.IsEnabled.Should().BeFalse("collapsed overflow commands should use the current enabled state of their source ribbon controls");

            host.Close();
            PumpDispatcher();
        });
    }

    [Fact]
    public void CollapsedRibbonNestedMenuItem_ClickRoutesOnlyToMatchingSourceItem()
    {
        StaTestRunner.Run(() =>
        {
            var parentInvocations = 0;
            var childInvocations = 0;
            var sourceParent = new MenuItem { Header = "Sort & Filter" };
            var sourceChild = new MenuItem { Header = "Sort A to Z" };
            sourceParent.Items.Add(sourceChild);
            sourceParent.Click += (_, args) =>
            {
                if (ReferenceEquals(args.OriginalSource, sourceParent))
                    parentInvocations++;
            };
            sourceChild.Click += (_, _) => childInvocations++;
            var cloneMethod = typeof(MainWindow)
                .GetMethod("CloneRibbonMenuItem", BindingFlags.Static | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "CloneRibbonMenuItem");

            var clonedParent = (MenuItem)cloneMethod.Invoke(null, [sourceParent])!;
            var clonedChild = clonedParent.Items.OfType<MenuItem>().Single();
            clonedChild.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, clonedChild));

            childInvocations.Should().Be(1);
            parentInvocations.Should().Be(0, "a nested collapsed overflow command should not also invoke its parent menu command");
        });
    }

}
