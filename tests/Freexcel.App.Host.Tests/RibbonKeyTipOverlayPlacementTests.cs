using System.Windows;
using FluentAssertions;
using Freexcel.App.Host;

namespace Freexcel.App.Host.Tests;

public sealed class RibbonKeyTipOverlayPlacementTests
{
    [Fact]
    public void PlaceBadge_ClampsBadgeInsideOverlayBounds()
    {
        var elementBounds = new Rect(1268, 710, 24, 20);
        var overlaySize = new Size(1280, 720);
        var badgeSize = new Size(30, 16);

        var point = RibbonKeyTipOverlayPlacement.PlaceBadge(elementBounds, overlaySize, badgeSize);

        point.X.Should().Be(1250);
        point.Y.Should().Be(704);
    }

    [Fact]
    public void PlaceBadge_WhenBadgeIsLargerThanOverlay_ClampsToOrigin()
    {
        var elementBounds = new Rect(8, 8, 12, 12);
        var overlaySize = new Size(20, 12);
        var badgeSize = new Size(30, 18);

        var point = RibbonKeyTipOverlayPlacement.PlaceBadge(elementBounds, overlaySize, badgeSize);

        point.X.Should().Be(0);
        point.Y.Should().Be(0);
    }

    [Fact]
    public void PlaceBadge_CentersBadgeNearTopOfElementWhenThereIsRoom()
    {
        var elementBounds = new Rect(100, 40, 80, 24);
        var overlaySize = new Size(1280, 720);
        var badgeSize = new Size(24, 16);

        var point = RibbonKeyTipOverlayPlacement.PlaceBadge(elementBounds, overlaySize, badgeSize);

        point.X.Should().Be(128);
        point.Y.Should().Be(56);
    }

    [Fact]
    public void PlaceBadge_AnchorsTallRibbonCommandsNearLowerCenter()
    {
        var elementBounds = new Rect(220, 140, 72, 46);
        var overlaySize = new Size(1280, 720);
        var badgeSize = new Size(26, 16);

        var point = RibbonKeyTipOverlayPlacement.PlaceBadge(elementBounds, overlaySize, badgeSize);

        point.X.Should().Be(243);
        point.Y.Should().Be(178);
    }

    [Fact]
    public void PlaceBadge_SnapsFractionalCoordinatesToWholePixels()
    {
        var elementBounds = new Rect(100.5, 40.25, 81, 24.5);
        var overlaySize = new Size(1280, 720);
        var badgeSize = new Size(25, 15.5);

        var point = RibbonKeyTipOverlayPlacement.PlaceBadge(elementBounds, overlaySize, badgeSize);

        point.X.Should().Be(129);
        point.Y.Should().Be(57);
    }
}
