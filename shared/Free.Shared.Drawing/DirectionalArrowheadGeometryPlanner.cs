namespace Free.Shared.Drawing;

/// <summary>Renderer-neutral points for a directional arrowhead.</summary>
public readonly record struct DirectionalArrowheadGeometry(
    bool IsVisible,
    LayoutPoint Tip,
    LayoutPoint Left,
    LayoutPoint Right);

/// <summary>Calculates the tip and wing points for an arrowhead aligned to a line segment.</summary>
public static class DirectionalArrowheadGeometryPlanner
{
    public static DirectionalArrowheadGeometry Calculate(
        LayoutPoint start,
        LayoutPoint end,
        double arrowLength,
        double arrowHalfWidth,
        double minimumShaftLength,
        bool drawAtMinimumLength)
    {
        var dx = end.X - start.X;
        var dy = end.Y - start.Y;
        var shaftLength = Math.Sqrt(dx * dx + dy * dy);
        if (shaftLength <= 0 ||
            shaftLength < minimumShaftLength ||
            (!drawAtMinimumLength && shaftLength == minimumShaftLength))
        {
            return default;
        }

        var unitX = dx / shaftLength;
        var unitY = dy / shaftLength;
        var baseX = end.X - unitX * arrowLength;
        var baseY = end.Y - unitY * arrowLength;
        var perpendicularX = -unitY;
        var perpendicularY = unitX;

        return new DirectionalArrowheadGeometry(
            IsVisible: true,
            Tip: end,
            Left: new LayoutPoint(
                baseX + perpendicularX * arrowHalfWidth,
                baseY + perpendicularY * arrowHalfWidth),
            Right: new LayoutPoint(
                baseX - perpendicularX * arrowHalfWidth,
                baseY - perpendicularY * arrowHalfWidth));
    }
}
