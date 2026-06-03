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

    [Fact]
    public void ViewZoomCommandKeyTips_ResetAndFitSelection()
    {
        RunSta(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.OpenRibbonMenu(Key.W, Key.Q);
            harness.HandleKeyTip(Key.D2);
            harness.StatusZoomText.Should().Be("200%");

            harness.EnterKeyTipScope("TopLevel");
            harness.HandleKeyTip(Key.W);
            harness.HandleKeyTip(Key.Z);

            harness.KeyTipScope.Should().Be("Commands", "Z is a visible prefix for 100% and Zoom to Selection");

            harness.HandleKeyTip(Key.D1);

            harness.StatusZoomText.Should().Be("100%");
            harness.KeyTipScope.Should().Be("None");
            harness.OverlayBadgeTexts.Should().BeEmpty();

            harness.SelectRange(1, 1, 12, 6);
            var expectedFitPercent = harness.ExpectedZoomSelectionPercent;

            harness.EnterKeyTipScope("TopLevel");
            harness.HandleKeyTip(Key.W);
            harness.HandleKeyTip(Key.Z);
            harness.HandleKeyTip(Key.S);

            harness.StatusZoomText.Should().Be($"{expectedFitPercent}%");
            harness.KeyTipScope.Should().Be("None");
            harness.OverlayBadgeTexts.Should().BeEmpty();

            harness.OpenRibbonMenu(Key.H, Key.B);
            harness.HandleKeyTip(Key.S);

            harness.KeyTipScope.Should().Be("Menu");
            harness.ActiveMenuItemSubmenuIsOpen("Line Style").Should().BeTrue();
            harness.ActiveMenuItemGestureText("Dashed").Should().Be("D");

            harness.HandleKeyTip(Key.D);

            harness.KeyTipScope.Should().Be("None");
            harness.OverlayBadgeTexts.Should().BeEmpty();
        });
    }

    [Fact]
    public void ZoomCustomDialogCancel_ReturnsFocusToWorksheet()
    {
        RunSta(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.OpenCustomZoomDialogAndCancel();

            harness.FocusedElementIsWorksheet.Should().BeTrue();
        });
    }
}
