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
            var tiled = arrangeAll!.Items.OfType<MenuItem>()
                .First(item => string.Equals(item.Header?.ToString(), "Tiled", StringComparison.Ordinal));

            tiled.IsCheckable.Should().BeTrue();
            tiled.InputGestureText.Should().Be("T");

            arrangeAll.RaiseEvent(new RoutedEventArgs(MenuItem.SubmenuOpenedEvent, arrangeAll));

            tiled.IsChecked.Should().BeTrue("the clone should run the source menu's Opened state refresh before display");
            arrangeAll.Items.OfType<MenuItem>()
                .Where(item => !ReferenceEquals(item, tiled))
                .Should().OnlyContain(item => item.IsChecked == false);
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
            using var harness = MainWindowHarness.Create();

            harness.SetRibbonWidth(220);
            var sourceButton = harness.VisibleOrCollapsedRibbonButton("Find & Select");
            var menu = harness.CollapsedMenu("Editing");
            var item = harness.CollapsedMenuItem("Editing", "Find & Select");

            sourceButton.Should().NotBeNull(harness.DebugRibbonChildren);
            item.Should().NotBeNull(harness.DebugRibbonChildren);

            sourceButton!.IsEnabled = false;
            menu!.RaiseEvent(new RoutedEventArgs(ContextMenu.OpenedEvent, menu));

            item!.IsEnabled.Should().BeFalse("collapsed overflow commands should use the current enabled state of their source ribbon controls");
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
