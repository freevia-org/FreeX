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
    public void PlaceBadge_CentersBadgeNearTopOfElementWhenThereIsRoom()
    {
        var elementBounds = new Rect(100, 40, 80, 24);
        var overlaySize = new Size(1280, 720);
        var badgeSize = new Size(24, 16);

        var point = RibbonKeyTipOverlayPlacement.PlaceBadge(elementBounds, overlaySize, badgeSize);

        point.X.Should().Be(128);
        point.Y.Should().Be(56);
    }
}
