using FreeX.Core.Model;

namespace FreeX.App.Presentation.Charts;

/// <summary>A single point of a trendline fit in data space (not pixels).</summary>
public readonly record struct TrendPoint(double X, double Y);

/// <summary>
/// Portable, UI-framework-free trendline regression. Given the source data points (in data space)
/// it produces the fitted trendline points for each supported fit (linear, exponential, logarithmic,
/// power, moving-average, polynomial). This mirrors the source (desktop) renderer's trendline math
/// exactly so both desktop hosts draw identical overlays; the layout engine then maps the resulting
/// data-space points into pixel space.
/// </summary>
public static class TrendlineCalculator
{
    /// <summary>
    /// Computes the trendline points for <paramref name="type"/> from <paramref name="points"/>.
    /// Returns an empty list when the fit is undefined (too few usable points, singular system).
    /// </summary>
    public static IReadOnlyList<TrendPoint> Calculate(
        ChartTrendlineType type,
        IReadOnlyList<TrendPoint> points,
        int period,
        int order) =>
        type switch
        {
            ChartTrendlineType.Exponential => CalculateExponential(points),
            ChartTrendlineType.Logarithmic => CalculateLogarithmic(points),
            ChartTrendlineType.Power => CalculatePower(points),
            ChartTrendlineType.MovingAverage => CalculateMovingAverage(points, period),
            ChartTrendlineType.Polynomial => CalculatePolynomial(points, order),
            _ => CalculateLinear(points),
        };

    public static bool TryCalculateRSquared(
        IReadOnlyList<TrendPoint> sourcePoints,
        IReadOnlyList<TrendPoint> trendPoints,
        out double rSquared,
        bool logTransformY = false)
    {
        rSquared = 0;
        var count = 0;
        var sumActual = 0.0;
        var sumActualSquared = 0.0;
        var residual = 0.0;
        foreach (var point in sourcePoints)
        {
            if (!TryInterpolateTrendY(trendPoints, point.X, out var predicted))
                continue;

            // Excel reports exponential/power trendline R-squared on the linearized
            // (log-Y) regression, not the original-scale residuals.
            var actualY = point.Y;
            if (logTransformY)
            {
                if (actualY <= 0 || predicted <= 0)
                    continue;
                actualY = Math.Log(actualY);
                predicted = Math.Log(predicted);
            }

            count++;
            sumActual += actualY;
            sumActualSquared += actualY * actualY;
            residual += Math.Pow(actualY - predicted, 2);
        }

        if (count < 2)
            return false;

        var total = sumActualSquared - (sumActual * sumActual / count);
        if (Math.Abs(total) < double.Epsilon)
            return false;

        rSquared = 1 - (residual / total);
        return !double.IsNaN(rSquared) && !double.IsInfinity(rSquared);
    }

    private static bool TryInterpolateTrendY(IReadOnlyList<TrendPoint> trendPoints, double x, out double y)
    {
        y = 0;
        if (trendPoints.Count == 0 || x < trendPoints[0].X || x > trendPoints[^1].X)
            return false;

        for (var i = 1; i < trendPoints.Count; i++)
        {
            var left = trendPoints[i - 1];
            var right = trendPoints[i];
            if (x > right.X)
                continue;

            var dx = right.X - left.X;
            if (Math.Abs(dx) < double.Epsilon)
            {
                y = right.Y;
                return true;
            }

            var t = (x - left.X) / dx;
            y = left.Y + ((right.Y - left.Y) * t);
            return true;
        }

        return false;
    }

    private static IReadOnlyList<TrendPoint> CalculateLinear(IReadOnlyList<TrendPoint> points)
    {
        var n = points.Count;
        var sumX = 0.0;
        var sumY = 0.0;
        var sumXY = 0.0;
        var sumXX = 0.0;
        var minX = double.PositiveInfinity;
        var maxX = double.NegativeInfinity;
        for (var i = 0; i < points.Count; i++)
        {
            var point = points[i];
            sumX += point.X;
            sumY += point.Y;
            sumXY += point.X * point.Y;
            sumXX += point.X * point.X;
            minX = Math.Min(minX, point.X);
            maxX = Math.Max(maxX, point.X);
        }

        var denominator = (n * sumXX) - (sumX * sumX);
        if (n < 2 || Math.Abs(denominator) < double.Epsilon)
            return [];

        var slope = ((n * sumXY) - (sumX * sumY)) / denominator;
        var intercept = (sumY - (slope * sumX)) / n;
        return [new TrendPoint(minX, intercept + slope * minX), new TrendPoint(maxX, intercept + slope * maxX)];
    }

    private static IReadOnlyList<TrendPoint> CalculateExponential(IReadOnlyList<TrendPoint> points)
    {
        var n = 0;
        var sumX = 0.0;
        var sumLogY = 0.0;
        var sumXLogY = 0.0;
        var sumXX = 0.0;
        var minX = double.PositiveInfinity;
        var maxX = double.NegativeInfinity;
        for (var i = 0; i < points.Count; i++)
        {
            var point = points[i];
            if (point.Y <= 0)
                continue;

            var logY = Math.Log(point.Y);
            n++;
            sumX += point.X;
            sumLogY += logY;
            sumXLogY += point.X * logY;
            sumXX += point.X * point.X;
            minX = Math.Min(minX, point.X);
            maxX = Math.Max(maxX, point.X);
        }

        if (n < 2)
            return [];

        var denominator = (n * sumXX) - (sumX * sumX);
        if (Math.Abs(denominator) < double.Epsilon)
            return [];

        var b = ((n * sumXLogY) - (sumX * sumLogY)) / denominator;
        var logA = (sumLogY - (b * sumX)) / n;
        var a = Math.Exp(logA);
        // Sample the fitted curve across the range (not just the two endpoints) so it
        // renders as a smooth curve instead of a straight chord, matching the source renderer.
        return SampleCurve(minX, maxX, points.Count, x => a * Math.Exp(b * x));
    }

    private static IReadOnlyList<TrendPoint> CalculateLogarithmic(IReadOnlyList<TrendPoint> points)
    {
        var n = 0;
        var sumLogX = 0.0;
        var sumY = 0.0;
        var sumLogXY = 0.0;
        var sumLogXLogX = 0.0;
        var minX = double.PositiveInfinity;
        var maxX = double.NegativeInfinity;
        for (var i = 0; i < points.Count; i++)
        {
            var point = points[i];
            if (point.X <= 0)
                continue;

            var logX = Math.Log(point.X);
            n++;
            sumLogX += logX;
            sumY += point.Y;
            sumLogXY += logX * point.Y;
            sumLogXLogX += logX * logX;
            minX = Math.Min(minX, point.X);
            maxX = Math.Max(maxX, point.X);
        }

        if (n < 2)
            return [];

        var denominator = (n * sumLogXLogX) - (sumLogX * sumLogX);
        if (Math.Abs(denominator) < double.Epsilon)
            return [];

        var slope = ((n * sumLogXY) - (sumLogX * sumY)) / denominator;
        var intercept = (sumY - (slope * sumLogX)) / n;
        return SampleCurve(minX, maxX, points.Count, x => intercept + slope * Math.Log(x));
    }

    private static IReadOnlyList<TrendPoint> CalculatePower(IReadOnlyList<TrendPoint> points)
    {
        var n = 0;
        var sumLogX = 0.0;
        var sumLogY = 0.0;
        var sumLogXLogY = 0.0;
        var sumLogXLogX = 0.0;
        var minX = double.PositiveInfinity;
        var maxX = double.NegativeInfinity;
        for (var i = 0; i < points.Count; i++)
        {
            var point = points[i];
            if (point.X <= 0 || point.Y <= 0)
                continue;

            var logX = Math.Log(point.X);
            var logY = Math.Log(point.Y);
            n++;
            sumLogX += logX;
            sumLogY += logY;
            sumLogXLogY += logX * logY;
            sumLogXLogX += logX * logX;
            minX = Math.Min(minX, point.X);
            maxX = Math.Max(maxX, point.X);
        }

        if (n < 2)
            return [];

        var denominator = (n * sumLogXLogX) - (sumLogX * sumLogX);
        if (Math.Abs(denominator) < double.Epsilon)
            return [];

        var b = ((n * sumLogXLogY) - (sumLogX * sumLogY)) / denominator;
        var logA = (sumLogY - (b * sumLogX)) / n;
        var a = Math.Exp(logA);
        return SampleCurve(minX, maxX, points.Count, x => a * Math.Pow(x, b));
    }

    private static IReadOnlyList<TrendPoint> CalculateMovingAverage(IReadOnlyList<TrendPoint> points, int period)
    {
        var windowSize = Math.Max(2, period);
        if (points.Count < windowSize)
            return [];

        var trendPoints = new List<TrendPoint>(points.Count - windowSize + 1);
        var runningTotal = 0.0;
        for (var i = windowSize - 1; i < points.Count; i++)
        {
            if (i == windowSize - 1)
            {
                for (var windowIndex = 0; windowIndex < windowSize; windowIndex++)
                    runningTotal += points[windowIndex].Y;
            }
            else
            {
                runningTotal += points[i].Y;
                runningTotal -= points[i - windowSize].Y;
            }

            trendPoints.Add(new TrendPoint(points[i].X, runningTotal / windowSize));
        }

        return trendPoints;
    }

    private static IReadOnlyList<TrendPoint> CalculatePolynomial(IReadOnlyList<TrendPoint> points, int order)
    {
        var degree = Math.Clamp(order, 2, 6);
        if (points.Count <= degree)
            return [];

        var coefficients = SolvePolynomialLeastSquares(points, degree);
        if (coefficients is null)
            return [];

        var minX = double.PositiveInfinity;
        var maxX = double.NegativeInfinity;
        for (var i = 0; i < points.Count; i++)
        {
            var x = points[i].X;
            minX = Math.Min(minX, x);
            maxX = Math.Max(maxX, x);
        }

        var samples = Math.Max(16, points.Count * 4);
        var trendPoints = new List<TrendPoint>(samples);
        for (var i = 0; i < samples; i++)
        {
            var x = samples == 1 ? minX : minX + ((maxX - minX) * i / (samples - 1));
            trendPoints.Add(new TrendPoint(x, EvaluatePolynomial(coefficients, x)));
        }

        return trendPoints;
    }

    private static double[]? SolvePolynomialLeastSquares(IReadOnlyList<TrendPoint> points, int degree)
    {
        var size = degree + 1;
        var matrix = new double[size, size];
        var vector = new double[size];
        var xPowerSums = new double[(degree * 2) + 1];
        var yXPowerSums = new double[size];

        for (var pointIndex = 0; pointIndex < points.Count; pointIndex++)
        {
            var point = points[pointIndex];
            var xPower = 1.0;
            for (var power = 0; power < xPowerSums.Length; power++)
            {
                xPowerSums[power] += xPower;
                if (power < yXPowerSums.Length)
                    yXPowerSums[power] += point.Y * xPower;
                xPower *= point.X;
            }
        }

        for (var row = 0; row < size; row++)
        {
            for (var col = 0; col < size; col++)
                matrix[row, col] = xPowerSums[row + col];

            vector[row] = yXPowerSums[row];
        }

        return SolveLinearSystem(matrix, vector);
    }

    private static double EvaluatePolynomial(IReadOnlyList<double> coefficients, double x)
    {
        var y = 0.0;
        var power = 1.0;
        foreach (var coefficient in coefficients)
        {
            y += coefficient * power;
            power *= x;
        }

        return y;
    }

    // Samples a fitted curve uniformly across [minX, maxX] so curved trendlines
    // (exponential, logarithmic, power) render as smooth curves like the source renderer,
    // instead of a single straight segment between the two endpoints. The first and last
    // samples land exactly on minX/maxX so equation/R-squared recovery is unaffected.
    private static IReadOnlyList<TrendPoint> SampleCurve(
        double minX,
        double maxX,
        int sourcePointCount,
        Func<double, double> curve)
    {
        var sampleCount = Math.Max(16, sourcePointCount * 4);
        var trendPoints = new List<TrendPoint>(sampleCount);
        for (var i = 0; i < sampleCount; i++)
        {
            var x = minX + ((maxX - minX) * i / (sampleCount - 1));
            trendPoints.Add(new TrendPoint(x, curve(x)));
        }

        return trendPoints;
    }

    private static double[]? SolveLinearSystem(double[,] matrix, double[] vector)
    {
        var size = vector.Length;
        for (var pivot = 0; pivot < size; pivot++)
        {
            var pivotRow = pivot;
            for (var row = pivot + 1; row < size; row++)
            {
                if (Math.Abs(matrix[row, pivot]) > Math.Abs(matrix[pivotRow, pivot]))
                    pivotRow = row;
            }

            if (Math.Abs(matrix[pivotRow, pivot]) < 1e-10)
                return null;

            if (pivotRow != pivot)
            {
                for (var col = pivot; col < size; col++)
                    (matrix[pivot, col], matrix[pivotRow, col]) = (matrix[pivotRow, col], matrix[pivot, col]);
                (vector[pivot], vector[pivotRow]) = (vector[pivotRow], vector[pivot]);
            }

            var divisor = matrix[pivot, pivot];
            for (var col = pivot; col < size; col++)
                matrix[pivot, col] /= divisor;
            vector[pivot] /= divisor;

            for (var row = 0; row < size; row++)
            {
                if (row == pivot)
                    continue;

                var factor = matrix[row, pivot];
                for (var col = pivot; col < size; col++)
                    matrix[row, col] -= factor * matrix[pivot, col];
                vector[row] -= factor * vector[pivot];
            }
        }

        return vector;
    }
}
