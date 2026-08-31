using System.Windows;
using FluentAssertions;
using FreeX.App.Host;

namespace FreeX.App.Host.Tests;

public sealed class RibbonKeyTipOverlayCollisionResolverTests
{
    [Fact]
    public void Resolve_KeepsUncrowdedBadgesAtTheirPreferredPositions()
    {
        var placements = new[]
        {
            new RibbonKeyTipBadgePlacement(new Point(20, 40), new Size(20, 16)),
            new RibbonKeyTipBadgePlacement(new Point(80, 40), new Size(20, 16)),
        };

        var points = RibbonKeyTipOverlayCollisionResolver.Resolve(placements, new Size(160, 100));

        points.Should().Equal(new Point(20, 40), new Point(80, 40));
    }

    [Fact]
    public void Resolve_SeparatesOverlappingBadgesWithoutMovingTheFirstAnchor()
    {
        var placements = new[]
        {
            new RibbonKeyTipBadgePlacement(new Point(100, 40), new Size(24, 16)),
            new RibbonKeyTipBadgePlacement(new Point(110, 40), new Size(24, 16)),
        };

        var points = RibbonKeyTipOverlayCollisionResolver.Resolve(placements, new Size(200, 100));

        points[0].Should().Be(new Point(100, 40));
        new Rect(points[0], placements[0].BadgeSize)
            .IntersectsWith(new Rect(points[1], placements[1].BadgeSize))
            .Should()
            .BeFalse("adjacent top-level tabs and QAT controls need individually readable labels");
    }

    [Fact]
    public void Resolve_ClampsShiftedBadgesInsideTheOverlay()
    {
        var placements = new[]
        {
            new RibbonKeyTipBadgePlacement(new Point(22, 10), new Size(20, 16)),
            new RibbonKeyTipBadgePlacement(new Point(22, 10), new Size(20, 16)),
        };

        var overlay = new Size(42, 40);
        var points = RibbonKeyTipOverlayCollisionResolver.Resolve(placements, overlay);

        points.Should().OnlyContain(point =>
            point.X >= 0 && point.Y >= 0 &&
            point.X + 20 <= overlay.Width && point.Y + 16 <= overlay.Height);
        new Rect(points[0], placements[0].BadgeSize)
            .IntersectsWith(new Rect(points[1], placements[1].BadgeSize))
            .Should()
            .BeFalse();
    }
}
