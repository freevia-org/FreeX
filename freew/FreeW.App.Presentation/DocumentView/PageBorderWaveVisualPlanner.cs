namespace FreeW.App.Presentation.DocumentView;

public sealed record PageBorderWaveSegment(
    double X1Dip,
    double Y1Dip,
    double X2Dip,
    double Y2Dip);

public static class PageBorderWaveVisualPlanner
{
    public const double RepeatDip = 8.0;
    public const double SegmentLengthDip = 3.0;
    public const double PhaseDip = 4.0;
    public const double StrokeWidthDip = 1.0;
    public const double StrokeOpacity = 166.0 / 255.0;

    public static IReadOnlyList<PageBorderWaveSegment> BuildFrame(
        double widthDip,
        double heightDip,
        double edgeInsetDip)
    {
        var width = Math.Max(0, widthDip);
        var height = Math.Max(0, heightDip);
        var inset = Math.Max(0, edgeInsetDip);
        if (width <= 2 * inset || height <= 2 * inset)
            return [];

        var segments = new List<PageBorderWaveSegment>();
        var horizontalEnd = width - inset;
        for (var start = inset + PhaseDip; start + SegmentLengthDip < horizontalEnd; start += RepeatDip)
        {
            segments.Add(new PageBorderWaveSegment(
                start,
                inset,
                start + SegmentLengthDip,
                inset + SegmentLengthDip));
            segments.Add(new PageBorderWaveSegment(
                start,
                height - inset - SegmentLengthDip - 1,
                start + SegmentLengthDip,
                height - inset - 1));
        }

        var verticalEnd = height - inset;
        for (var start = inset + PhaseDip; start + SegmentLengthDip < verticalEnd; start += RepeatDip)
        {
            segments.Add(new PageBorderWaveSegment(
                inset + SegmentLengthDip,
                start,
                inset,
                start + SegmentLengthDip));
            segments.Add(new PageBorderWaveSegment(
                width - inset - 1,
                start,
                width - inset - SegmentLengthDip - 1,
                start + SegmentLengthDip));
        }

        return segments;
    }
}
