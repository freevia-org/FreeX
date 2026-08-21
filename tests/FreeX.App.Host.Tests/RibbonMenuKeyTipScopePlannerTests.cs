using System.Windows.Controls;
using FluentAssertions;
using FreeX.App.Host;

namespace FreeX.App.Host.Tests;

public sealed class RibbonMenuKeyTipScopePlannerTests
{
    [Fact]
    public void ApplyScopedInputGestureText_StripsActiveParentPrefixFromPrefixedChildren()
    {
        RunSta(() =>
        {
            var parent = CreateMenuItem("H");
            var prefixedChild = CreateMenuItem("HG");
            var localChild = CreateMenuItem("L");
            parent.Items.Add(prefixedChild);
            parent.Items.Add(localChild);

            RibbonMenuKeyTipScopePlanner.ApplyScopedInputGestureText(parent);

            prefixedChild.InputGestureText.Should().Be("G");
            localChild.InputGestureText.Should().Be("L");
        });
    }

    [Fact]
    public void ApplyScopedInputGestureText_PreservesFullKeyTipsInRootMenuScope()
    {
        RunSta(() =>
        {
            var parent = CreateMenuItem("H");
            var child = CreateMenuItem("HG");
            parent.Items.Add(child);
            var menu = new ContextMenu();
            menu.Items.Add(parent);

            RibbonMenuKeyTipScopePlanner.ApplyScopedInputGestureText(menu);

            parent.InputGestureText.Should().Be("H");
            child.InputGestureText.Should().Be("HG");
        });
    }

    [Fact]
    public void ClearInputGestureText_HidesRootAndNestedMenuGestures()
    {
        RunSta(() =>
        {
            var parent = CreateMenuItem("H");
            var child = CreateMenuItem("HG");
            parent.Items.Add(child);
            var menu = new ContextMenu();
            menu.Items.Add(parent);

            RibbonMenuKeyTipScopePlanner.ApplyScopedInputGestureText(menu);
            RibbonMenuKeyTipScopePlanner.ClearInputGestureText(menu);

            parent.InputGestureText.Should().BeEmpty();
            child.InputGestureText.Should().BeEmpty();
        });
    }

    [Fact]
    public void GetScopedKeyTip_UsesNormalizedActiveParentPrefix()
    {
        RunSta(() =>
        {
            var parent = CreateMenuItem(" h ");
            var child = CreateMenuItem(" hg ");

            RibbonMenuKeyTipScopePlanner.GetScopedKeyTip(child, parent).Should().Be("G");
        });
    }

    private static MenuItem CreateMenuItem(string keyTip)
    {
        var menuItem = new MenuItem();
        RibbonTooltip.SetKeyTip(menuItem, keyTip);
        return menuItem;
    }

    private static void RunSta(Action action) => StaTestRunner.Run(action);
}
