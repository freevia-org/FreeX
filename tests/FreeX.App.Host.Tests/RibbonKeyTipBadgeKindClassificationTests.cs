using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using FluentAssertions;
using FreeX.App.Host;

namespace FreeX.App.Host.Tests;

public sealed class RibbonKeyTipBadgeKindClassificationTests
{
    [Fact]
    public void GetKeyTipBadgeKind_ClassifiesContextMenuButtonsAsDropdownCommands()
    {
        StaTestRunner.Run(() =>
        {
            var button = new Button
            {
                ContextMenu = new ContextMenu()
            };

            GetKeyTipBadgeKind(button).Should().Be(RibbonKeyTipBadgeKind.DropdownCommand);
        });
    }

    [Fact]
    public void GetKeyTipBadgeKind_ClassifiesMetadataDropdownButtonsAsDropdownCommands()
    {
        StaTestRunner.Run(() =>
        {
            var button = new Button();
            RibbonMetadata.SetDropdownMenuButton(button, true);

            GetKeyTipBadgeKind(button).Should().Be(RibbonKeyTipBadgeKind.DropdownCommand);
        });
    }

    [Fact]
    public void GetKeyTipBadgeKind_KeepsPlainButtonsAsCommands()
    {
        StaTestRunner.Run(() =>
        {
            GetKeyTipBadgeKind(new Button()).Should().Be(RibbonKeyTipBadgeKind.Command);
        });
    }

    [Fact]
    public void GetKeyTipBadgeKind_KeepsCollapsedGroupsAheadOfDropdownCommands()
    {
        StaTestRunner.Run(() =>
        {
            var button = new Button
            {
                ContextMenu = new ContextMenu()
            };
            RibbonMetadata.SetRole(button, RibbonMetadataRole.CollapsedGroupButton);

            GetKeyTipBadgeKind(button).Should().Be(RibbonKeyTipBadgeKind.CollapsedGroup);
        });
    }

    private static RibbonKeyTipBadgeKind GetKeyTipBadgeKind(FrameworkElement element)
    {
        var method = typeof(MainWindow).GetMethod(
            "GetKeyTipBadgeKind",
            BindingFlags.Static | BindingFlags.NonPublic);

        method.Should().NotBeNull();
        return (RibbonKeyTipBadgeKind)method!.Invoke(null, [element])!;
    }
}
