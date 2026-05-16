using System.Windows.Controls;
using FluentAssertions;
using Freexcel.App.Host;

namespace Freexcel.App.Host.Tests;

public sealed class RibbonKeyTipRoutingTests
{
    [Fact]
    public void ResolveKeyTipElement_PrefersExactCommandOverLongerPrefix()
    {
        RunSta(() =>
        {
            var shortButton = CreateButton("Q");
            var longButton = CreateButton("QI");

            var resolved = RibbonKeyTipRouting.ResolveKeyTipElement([shortButton, longButton], "Q");

            resolved.Should().BeSameAs(shortButton, "an exact visible command keytip should not be shadowed by an unrelated longer command keytip");
        });
    }

    [Fact]
    public void ResolveKeyTipElement_ResolvesLongerExactMatch()
    {
        RunSta(() =>
        {
            var shortButton = CreateButton("Q");
            var longButton = CreateButton("QI");

            var resolved = RibbonKeyTipRouting.ResolveKeyTipElement([shortButton, longButton], "QI");

            resolved.Should().BeSameAs(longButton);
        });
    }

    [Fact]
    public void ResolveKeyTipElement_RejectsDuplicateExactMatches()
    {
        RunSta(() =>
        {
            var first = CreateButton("B");
            var second = CreateButton("B");

            var resolved = RibbonKeyTipRouting.ResolveKeyTipElement([first, second], "B");

            resolved.Should().BeNull("duplicate keytips cannot route deterministically");
        });
    }

    [Fact]
    public void HasKeyTipPrefix_MatchesCaseInsensitively()
    {
        RunSta(() =>
        {
            var button = CreateButton("FX");

            RibbonKeyTipRouting.HasKeyTipPrefix([button], "f").Should().BeTrue();
        });
    }

    [Fact]
    public void ResolveMenuItem_WaitsWhenNestedLongerPrefixExists()
    {
        RunSta(() =>
        {
            var parent = CreateMenuItem("H");
            parent.Items.Add(CreateMenuItem("HG"));

            var resolved = RibbonKeyTipRouting.ResolveMenuItem([parent], "H");

            resolved.Should().BeNull("parent menu keytips should open submenus before invoking shorter ambiguous choices");
        });
    }

    [Fact]
    public void ResolveMenuItem_ResolvesNestedLeafAfterParentPrefix()
    {
        RunSta(() =>
        {
            var parent = CreateMenuItem("H");
            var child = CreateMenuItem("HG");
            parent.Items.Add(child);

            var resolved = RibbonKeyTipRouting.ResolveMenuItem([parent], "HG");

            resolved.Should().BeSameAs(child);
        });
    }

    [Fact]
    public void HasMenuItemKeyTipPrefix_SearchesNestedMenuItems()
    {
        RunSta(() =>
        {
            var parent = CreateMenuItem("T");
            parent.Items.Add(CreateMenuItem("TA"));

            RibbonKeyTipRouting.HasMenuItemKeyTipPrefix([parent], "TA").Should().BeTrue();
        });
    }

    private static Button CreateButton(string keyTip)
    {
        var button = new Button();
        RibbonTooltip.SetKeyTip(button, keyTip);
        return button;
    }

    private static MenuItem CreateMenuItem(string keyTip)
    {
        var menuItem = new MenuItem();
        RibbonTooltip.SetKeyTip(menuItem, keyTip);
        return menuItem;
    }

    private static void RunSta(Action action)
    {
        Exception? exception = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                exception = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (exception is not null)
            throw exception;
    }
}
