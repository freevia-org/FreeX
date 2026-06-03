using System.Windows.Input;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed partial class MainWindowRibbonKeyTipTests
{
    [Fact]
    public void ViewZoomMenuKeyTip_AppliesPresetAndExitsKeyTipMode()
    {
        RunSta(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.StatusZoomText.Should().Be("100%");

            harness.OpenRibbonMenu(Key.W, Key.Q);
            harness.ActiveMenuItemGestureText("200%").Should().Be("2");

            harness.HandleKeyTip(Key.D2);

            harness.StatusZoomText.Should().Be("200%");
            harness.KeyTipScope.Should().Be("None");
            harness.ActiveMenuIsOpen.Should().BeFalse();
            harness.OverlayBadgeTexts.Should().BeEmpty("invoking a zoom preset should close menu keytip mode like Excel");
        });
    }

}
