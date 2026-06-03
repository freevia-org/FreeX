using System.Windows.Input;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed partial class MainWindowRibbonKeyTipTests
{
    [Fact]
    public void CrossTabMenuKeyTips_RouteThroughStaticRibbonMenus()
    {
        RunSta(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.OpenRibbonMenu(Key.P, Key.B, Key.K);
            harness.SelectedRibbonTabHeader.Should().Be("Page Layout");
            harness.KeyTipScope.Should().Be("Menu");
            harness.ActiveMenuItemGestureText("Insert Page Break").Should().Be("I");
            harness.ActiveMenuItemGestureText("Remove Page Break").Should().Be("R");
            harness.HandleKeyTip(Key.Escape);

            harness.OpenRibbonMenu(Key.M, Key.E, Key.C);
            harness.SelectedRibbonTabHeader.Should().Be("Formulas");
            harness.KeyTipScope.Should().Be("Menu");
            harness.ActiveMenuItemGestureText("Error Checking...").Should().Be("E");
            harness.ActiveMenuItemGestureText("Error Checking Options...").Should().Be("O");
            harness.HandleKeyTip(Key.Escape);

            harness.OpenRibbonMenu(Key.W, Key.F, Key.P);
            harness.SelectedRibbonTabHeader.Should().Be("View");
            harness.KeyTipScope.Should().Be("Menu");
            harness.ActiveMenuItemGestureText("Freeze Panes").Should().Be("F");
            harness.ActiveMenuItemGestureText("Unfreeze Panes").Should().Be("U");
            harness.HandleKeyTip(Key.Escape);

            harness.OpenRibbonMenu(Key.W, Key.Q);
            harness.ActiveMenuItemGestureText("100%").Should().Be("1");
            harness.ActiveMenuItemGestureText("Custom...").Should().Be("C");
            harness.HandleKeyTip(Key.Escape);

            harness.OpenRibbonMenu(Key.W, Key.A);
            harness.ActiveMenuItemGestureText("Tiled").Should().Be("T");
            harness.ActiveMenuItemGestureText("Cascade").Should().Be("C");
        });
    }
}
