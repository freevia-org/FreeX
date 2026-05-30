using FreeX.Core.Model;
using OxyPlot;

namespace FreeX.App.UI;

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
        var count = 0;
        var sumActual = 0.0;
        var sumActualSquared = 0.0;
        var residual = 0.0;
        foreach (var point in sourcePoints)
        {
            if (!TryInterpolateTrendY(trendPoints, point.X, out var predicted))
                continue;

            count++;
            sumActual += point.Y;
            sumActualSquared += point.Y * point.Y;
            residual += Math.Pow(point.Y - predicted, 2);
        }

        if (count < 2)
            return false;

        var total = sumActualSquared - (sumActual * sumActual / count);
        if (Math.Abs(total) < double.Epsilon)
            return false;

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
        if (Math.Abs(denominator) < double.Epsilon)
            return [];

        var slope = ((n * sumXY) - (sumX * sumY)) / denominator;
        var intercept = (sumY - (slope * sumX)) / n;
        return [new DataPoint(minX, intercept + slope * minX), new DataPoint(maxX, intercept + slope * maxX)];
    }

    private static IReadOnlyList<DataPoint> CalculateExponentialTrendline(IReadOnlyList<DataPoint> points)
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
        return [new DataPoint(minX, a * Math.Exp(b * minX)), new DataPoint(maxX, a * Math.Exp(b * maxX))];
    }

    private static IReadOnlyList<DataPoint> CalculateLogarithmicTrendline(IReadOnlyList<DataPoint> points)
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
        return [
            new DataPoint(minX, intercept + slope * Math.Log(minX)),
            new DataPoint(maxX, intercept + slope * Math.Log(maxX))];
    }

    private static IReadOnlyList<DataPoint> CalculatePowerTrendline(IReadOnlyList<DataPoint> points)
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
        return [
            new DataPoint(minX, a * Math.Pow(minX, b)),
            new DataPoint(maxX, a * Math.Pow(maxX, b))];
    }

    private static IReadOnlyList<DataPoint> CalculateMovingAverageTrendline(IReadOnlyList<DataPoint> points, int period)
    {
        var windowSize = Math.Max(2, period);
        if (points.Count < windowSize)
            return [];

        var trendPoints = new List<DataPoint>(points.Count - windowSize + 1);
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

            trendPoints.Add(new DataPoint(points[i].X, runningTotal / windowSize));
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

        var minX = double.PositiveInfinity;
        var maxX = double.NegativeInfinity;
        for (var i = 0; i < points.Count; i++)
        {
            var x = points[i].X;
            minX = Math.Min(minX, x);
            maxX = Math.Max(maxX, x);
        }

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
