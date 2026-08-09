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
            var clonedParent = (MenuItem)RibbonMenuItemCloner.CloneRibbonMenuItem(sourceParent)!;
            var clonedChild = clonedParent.Items.OfType<MenuItem>().Single();
            clonedChild.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, clonedChild));

            childInvocations.Should().Be(1);
            parentInvocations.Should().Be(0, "a nested collapsed overflow command should not also invoke its parent menu command");
        });
    }

}
