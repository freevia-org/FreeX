using Freexcel.Core.Model;
using OxyPlot;

namespace Freexcel.App.UI;

public static class ChartTrendlineCalculator
{
    public static IReadOnlyList<DataPoint> Calculate(
        ChartTrendlineType type,
        IReadOnlyList<DataPoint> points,
        int period,
        int order) =>
        type switch
        {
            ChartTrendlineType.Exponential => CalculateExponentialTrendline(points),
            ChartTrendlineType.Logarithmic => CalculateLogarithmicTrendline(points),
            ChartTrendlineType.Power => CalculatePowerTrendline(points),
            ChartTrendlineType.MovingAverage => CalculateMovingAverageTrendline(points, period),
            ChartTrendlineType.Polynomial => CalculatePolynomialTrendline(points, order),
            _ => CalculateLinearTrendline(points)
        };

    public static bool TryCalculateRSquared(
        IReadOnlyList<DataPoint> sourcePoints,
        IReadOnlyList<DataPoint> trendPoints,
        out double rSquared)
    {
        rSquared = 0;
        var matches = new List<(double Actual, double Predicted)>();
        foreach (var point in sourcePoints)
        {
            if (TryInterpolateTrendY(trendPoints, point.X, out var predicted))
                matches.Add((point.Y, predicted));
        }

        if (matches.Count < 2)
            return false;

        var mean = matches.Average(match => match.Actual);
        var total = matches.Sum(match => Math.Pow(match.Actual - mean, 2));
        if (Math.Abs(total) < double.Epsilon)
            return false;

        var residual = matches.Sum(match => Math.Pow(match.Actual - match.Predicted, 2));
        rSquared = 1 - (residual / total);
        return !double.IsNaN(rSquared) && !double.IsInfinity(rSquared);
    }

    private static bool TryInterpolateTrendY(IReadOnlyList<DataPoint> trendPoints, double x, out double y)
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

    private static IReadOnlyList<DataPoint> CalculateLinearTrendline(IReadOnlyList<DataPoint> points)
    {
        var n = points.Count;
        var sumX = points.Sum(point => point.X);
        var sumY = points.Sum(point => point.Y);
        var sumXY = points.Sum(point => point.X * point.Y);
        var sumXX = points.Sum(point => point.X * point.X);
        var denominator = (n * sumXX) - (sumX * sumX);
        if (Math.Abs(denominator) < double.Epsilon)
            return [];

        var slope = ((n * sumXY) - (sumX * sumY)) / denominator;
        var intercept = (sumY - (slope * sumX)) / n;
        var minX = points.Min(point => point.X);
        var maxX = points.Max(point => point.X);
        return [new DataPoint(minX, intercept + slope * minX), new DataPoint(maxX, intercept + slope * maxX)];
    }

    private static IReadOnlyList<DataPoint> CalculateExponentialTrendline(IReadOnlyList<DataPoint> points)
    {
        var positivePoints = points.Where(point => point.Y > 0).ToList();
        if (positivePoints.Count < 2)
            return [];

        var n = positivePoints.Count;
        var sumX = positivePoints.Sum(point => point.X);
        var sumLogY = positivePoints.Sum(point => Math.Log(point.Y));
        var sumXLogY = positivePoints.Sum(point => point.X * Math.Log(point.Y));
        var sumXX = positivePoints.Sum(point => point.X * point.X);
        var denominator = (n * sumXX) - (sumX * sumX);
        if (Math.Abs(denominator) < double.Epsilon)
            return [];

        var b = ((n * sumXLogY) - (sumX * sumLogY)) / denominator;
        var logA = (sumLogY - (b * sumX)) / n;
        var a = Math.Exp(logA);
        var minX = positivePoints.Min(point => point.X);
        var maxX = positivePoints.Max(point => point.X);
        return [new DataPoint(minX, a * Math.Exp(b * minX)), new DataPoint(maxX, a * Math.Exp(b * maxX))];
    }

    private static IReadOnlyList<DataPoint> CalculateLogarithmicTrendline(IReadOnlyList<DataPoint> points)
    {
        var positiveXPoints = points.Where(point => point.X > 0).ToList();
        if (positiveXPoints.Count < 2)
            return [];

        var n = positiveXPoints.Count;
        var sumLogX = positiveXPoints.Sum(point => Math.Log(point.X));
        var sumY = positiveXPoints.Sum(point => point.Y);
        var sumLogXY = positiveXPoints.Sum(point => Math.Log(point.X) * point.Y);
        var sumLogXLogX = positiveXPoints.Sum(point => Math.Log(point.X) * Math.Log(point.X));
        var denominator = (n * sumLogXLogX) - (sumLogX * sumLogX);
        if (Math.Abs(denominator) < double.Epsilon)
            return [];

        var slope = ((n * sumLogXY) - (sumLogX * sumY)) / denominator;
        var intercept = (sumY - (slope * sumLogX)) / n;
        var minX = positiveXPoints.Min(point => point.X);
        var maxX = positiveXPoints.Max(point => point.X);
        return [
            new DataPoint(minX, intercept + slope * Math.Log(minX)),
            new DataPoint(maxX, intercept + slope * Math.Log(maxX))];
    }

    private static IReadOnlyList<DataPoint> CalculatePowerTrendline(IReadOnlyList<DataPoint> points)
    {
        var positivePoints = points.Where(point => point.X > 0 && point.Y > 0).ToList();
        if (positivePoints.Count < 2)
            return [];

        var n = positivePoints.Count;
        var sumLogX = positivePoints.Sum(point => Math.Log(point.X));
        var sumLogY = positivePoints.Sum(point => Math.Log(point.Y));
        var sumLogXLogY = positivePoints.Sum(point => Math.Log(point.X) * Math.Log(point.Y));
        var sumLogXLogX = positivePoints.Sum(point => Math.Log(point.X) * Math.Log(point.X));
        var denominator = (n * sumLogXLogX) - (sumLogX * sumLogX);
        if (Math.Abs(denominator) < double.Epsilon)
            return [];

        var b = ((n * sumLogXLogY) - (sumLogX * sumLogY)) / denominator;
        var logA = (sumLogY - (b * sumLogX)) / n;
        var a = Math.Exp(logA);
        var minX = positivePoints.Min(point => point.X);
        var maxX = positivePoints.Max(point => point.X);
        return [
            new DataPoint(minX, a * Math.Pow(minX, b)),
            new DataPoint(maxX, a * Math.Pow(maxX, b))];
    }

    private static IReadOnlyList<DataPoint> CalculateMovingAverageTrendline(IReadOnlyList<DataPoint> points, int period)
    {
        var windowSize = Math.Max(2, period);
        if (points.Count < windowSize)
            return [];

        var trendPoints = new List<DataPoint>();
        for (var i = windowSize - 1; i < points.Count; i++)
        {
            var average = points.Skip(i - windowSize + 1).Take(windowSize).Average(point => point.Y);
            trendPoints.Add(new DataPoint(points[i].X, average));
        }

        return trendPoints;
    }

    private static IReadOnlyList<DataPoint> CalculatePolynomialTrendline(IReadOnlyList<DataPoint> points, int order)
    {
        var degree = Math.Clamp(order, 2, 6);
        if (points.Count <= degree)
            return [];

        var coefficients = SolvePolynomialLeastSquares(points, degree);
        if (coefficients is null)
            return [];

        var minX = points.Min(point => point.X);
        var maxX = points.Max(point => point.X);
        var samples = Math.Max(16, points.Count * 4);
        var trendPoints = new List<DataPoint>(samples);
        for (var i = 0; i < samples; i++)
        {
            var x = samples == 1 ? minX : minX + ((maxX - minX) * i / (samples - 1));
            trendPoints.Add(new DataPoint(x, EvaluatePolynomial(coefficients, x)));
        }

        return trendPoints;
    }

    private static double[]? SolvePolynomialLeastSquares(IReadOnlyList<DataPoint> points, int degree)
    {
        var size = degree + 1;
        var matrix = new double[size, size];
        var vector = new double[size];

        for (var row = 0; row < size; row++)
        {
            for (var col = 0; col < size; col++)
                matrix[row, col] = points.Sum(point => Math.Pow(point.X, row + col));

            vector[row] = points.Sum(point => point.Y * Math.Pow(point.X, row));
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
