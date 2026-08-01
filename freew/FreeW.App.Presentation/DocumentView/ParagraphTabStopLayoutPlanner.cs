using FreeW.Core.Model;

namespace FreeW.App.Presentation.DocumentView;

public sealed record ParagraphTabStopResolution(
    double StopPositionDip,
    TabStopAlignment Alignment,
    TabLeader Leader,
    bool IsExplicit);

public sealed record ParagraphTabStopPlacementPlan(
    double StopPositionDip,
    double SegmentStartDip,
    double AdvanceDip,
    TabStopAlignment Alignment,
    TabLeader Leader,
    bool IsExplicit)
{
    public bool HasLeader => Leader != TabLeader.None && AdvanceDip > ParagraphTabStopLayoutPlanner.SameStopToleranceDip;
}

public static class ParagraphTabStopLayoutPlanner
{
    public const double SameStopToleranceDip = 0.5;
    private const double SameStopTolerancePt = 0.01;
    public const double MinimumAdvanceDip = 1.0;

    public static ParagraphTabStopResolution ResolveNextStop(
        double penPositionDip,
        IEnumerable<TabStop> tabStops,
        double defaultTabStopPt,
        double dipPerPoint)
    {
        var safeDipPerPoint = Math.Max(0.01, dipPerPoint);
        foreach (var stop in ResolveEffectiveStops(tabStops))
        {
            var stopDip = stop.PositionPt * safeDipPerPoint;
            if (stopDip > penPositionDip + SameStopToleranceDip)
                return new ParagraphTabStopResolution(stopDip, stop.Alignment, stop.Leader, IsExplicit: true);
        }

        var interval = Math.Max(MinimumAdvanceDip, defaultTabStopPt * safeDipPerPoint);
        var next = (Math.Floor(penPositionDip / interval) + 1) * interval;
        return new ParagraphTabStopResolution(next, TabStopAlignment.Left, TabLeader.None, IsExplicit: false);
    }

    private static IReadOnlyList<TabStop> ResolveEffectiveStops(IEnumerable<TabStop> tabStops)
    {
        var effective = new List<TabStop>();
        foreach (var stop in tabStops)
        {
            effective.RemoveAll(candidate =>
                Math.Abs(candidate.PositionPt - stop.PositionPt) <= SameStopTolerancePt);
            if (!stop.IsClear)
                effective.Add(stop);
        }

        effective.Sort((left, right) => left.PositionPt.CompareTo(right.PositionPt));
        return effective;
    }

    public static ParagraphTabStopPlacementPlan BuildPlacementPlan(
        double penPositionDip,
        double followingSegmentWidthDip,
        IEnumerable<TabStop> tabStops,
        double defaultTabStopPt,
        double dipPerPoint,
        double? decimalAlignmentOffsetDip = null)
    {
        var resolution = ResolveNextStop(penPositionDip, tabStops, defaultTabStopPt, dipPerPoint);
        var segmentWidth = Math.Max(0, followingSegmentWidthDip);
        var decimalOffset = decimalAlignmentOffsetDip is { } offset
            ? Math.Clamp(offset, 0, segmentWidth)
            : segmentWidth;
        var desiredSegmentStart = resolution.Alignment switch
        {
            TabStopAlignment.Center => resolution.StopPositionDip - segmentWidth / 2,
            TabStopAlignment.Right => resolution.StopPositionDip - segmentWidth,
            TabStopAlignment.Decimal => resolution.StopPositionDip - decimalOffset,
            _ => resolution.StopPositionDip
        };
        var segmentStart = Math.Max(penPositionDip + MinimumAdvanceDip, desiredSegmentStart);
        return new ParagraphTabStopPlacementPlan(
            resolution.StopPositionDip,
            segmentStart,
            segmentStart - penPositionDip,
            resolution.Alignment,
            resolution.Leader,
            resolution.IsExplicit);
    }

    public static double ComputeTabAdvance(
        double penPositionDip,
        IEnumerable<TabStop> tabStops,
        double defaultTabStopPt,
        double dipPerPoint)
    {
        var stop = ResolveNextStop(penPositionDip, tabStops, defaultTabStopPt, dipPerPoint);
        return Math.Max(MinimumAdvanceDip, stop.StopPositionDip - penPositionDip);
    }
}
