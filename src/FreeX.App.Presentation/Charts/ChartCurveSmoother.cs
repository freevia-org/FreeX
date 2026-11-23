using Free.Shared.Drawing;

namespace FreeX.App.Presentation.Charts;

/// <summary>
/// Turns the points of a smooth line / smooth scatter series into the dense polyline the hosts stroke.
/// Excel smooths with a cubic spline through the data points; a Catmull-Rom spline (converted to cubic
/// Beziers with the usual 1/6 tangent scale) passes through every point the same way and is sampled
/// finely enough that both hosts draw an identical curve from plain line segments.
/// </summary>
public static class ChartCurveSmoother
{
    private const int StepsPerSegment = 16;

    /// <summary>
    /// Smooths <paramref name="points"/>. A point with a non-finite Y is a gap: the curve is built per
    /// run of finite points, runs are separated by a single NaN-Y point, and a run of fewer than three
    /// points is returned as is (nothing to smooth).
    /// </summary>
    public static IReadOnlyList<LayoutPoint> Smooth(IReadOnlyList<SeriesPoint> points)
    {
        ArgumentNullException.ThrowIfNull(points);

        var result = new List<LayoutPoint>();
        var run = new List<LayoutPoint>();
        foreach (var point in points)
        {
            if (double.IsFinite(point.Position.X) && double.IsFinite(point.Position.Y))
            {
                run.Add(point.Position);
                continue;
            }

            Flush(run, result);
            if (result.Count > 0 && double.IsFinite(result[^1].Y))
                result.Add(new LayoutPoint(point.Position.X, double.NaN));
        }

        Flush(run, result);
        return result;
    }

    private static void Flush(List<LayoutPoint> run, List<LayoutPoint> result)
    {
        if (run.Count == 0)
            return;

        if (run.Count < 3)
        {
            result.AddRange(run);
        }
        else
        {
            result.Add(run[0]);
            for (var i = 0; i < run.Count - 1; i++)
            {
                var p0 = run[Math.Max(i - 1, 0)];
                var p1 = run[i];
                var p2 = run[i + 1];
                var p3 = run[Math.Min(i + 2, run.Count - 1)];
                var c1 = new LayoutPoint(p1.X + (p2.X - p0.X) / 6.0, p1.Y + (p2.Y - p0.Y) / 6.0);
                var c2 = new LayoutPoint(p2.X - (p3.X - p1.X) / 6.0, p2.Y - (p3.Y - p1.Y) / 6.0);
                for (var step = 1; step <= StepsPerSegment; step++)
                {
                    var t = step / (double)StepsPerSegment;
                    var u = 1 - t;
                    var x = u * u * u * p1.X + 3 * u * u * t * c1.X + 3 * u * t * t * c2.X + t * t * t * p2.X;
                    var y = u * u * u * p1.Y + 3 * u * u * t * c1.Y + 3 * u * t * t * c2.Y + t * t * t * p2.Y;
                    result.Add(new LayoutPoint(x, y));
                }
            }
        }

        run.Clear();
    }
}
