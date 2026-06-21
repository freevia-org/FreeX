using FluentAssertions;
using FreeX.Core.Model;
using OxyPlot;

namespace FreeX.App.UI.Tests;

public sealed class ChartTrendlineCalculatorTests
{
    [Fact]
    public void Calculate_Linear_ReturnsRegressionEndpoints()
    {
        var trend = ChartTrendlineCalculator.Calculate(
            ChartTrendlineType.Linear,
            [new DataPoint(0, 1), new DataPoint(1, 3), new DataPoint(2, 5)],
            period: 2,
            order: 2);

        trend.Should().Equal(new DataPoint(0, 1), new DataPoint(2, 5));
    }

    [Fact]
    public void Calculate_Exponential_ReturnsSmoothCurveNotStraightChord()
    {
        // Points exactly on y = 2 * e^(0.5x).
        var source = new[]
        {
            new DataPoint(0, 2 * Math.Exp(0.0)),
            new DataPoint(1, 2 * Math.Exp(0.5)),
            new DataPoint(2, 2 * Math.Exp(1.0)),
            new DataPoint(3, 2 * Math.Exp(1.5)),
        };

        var trend = ChartTrendlineCalculator.Calculate(ChartTrendlineType.Exponential, source, period: 2, order: 2);

        // Many samples (a curve), not just the two endpoints (a chord)...
        trend.Count.Should().BeGreaterThan(2);
        // ...and every sample lies on the fitted exponential curve, not the straight chord.
        trend.Should().OnlyContain(p => Math.Abs(p.Y - 2 * Math.Exp(0.5 * p.X)) < 1e-6);
        trend[0].X.Should().BeApproximately(0, 1e-9);
        trend[^1].X.Should().BeApproximately(3, 1e-9);
    }

    [Fact]
    public void Calculate_Logarithmic_ReturnsSmoothCurveNotStraightChord()
    {
        // Points exactly on y = 3 + 2*ln(x).
        var source = new[]
        {
            new DataPoint(1, 3 + 2 * Math.Log(1)),
            new DataPoint(2, 3 + 2 * Math.Log(2)),
            new DataPoint(4, 3 + 2 * Math.Log(4)),
            new DataPoint(8, 3 + 2 * Math.Log(8)),
        };

        var trend = ChartTrendlineCalculator.Calculate(ChartTrendlineType.Logarithmic, source, period: 2, order: 2);

        trend.Count.Should().BeGreaterThan(2);
        trend.Should().OnlyContain(p => Math.Abs(p.Y - (3 + 2 * Math.Log(p.X))) < 1e-6);
    }

    [Fact]
    public void Calculate_Power_ReturnsSmoothCurveNotStraightChord()
    {
        // Points exactly on y = 1.5 * x^2.
        var source = new[]
        {
            new DataPoint(1, 1.5 * Math.Pow(1, 2)),
            new DataPoint(2, 1.5 * Math.Pow(2, 2)),
            new DataPoint(3, 1.5 * Math.Pow(3, 2)),
            new DataPoint(4, 1.5 * Math.Pow(4, 2)),
        };

        var trend = ChartTrendlineCalculator.Calculate(ChartTrendlineType.Power, source, period: 2, order: 2);

        trend.Count.Should().BeGreaterThan(2);
        trend.Should().OnlyContain(p => Math.Abs(p.Y - 1.5 * Math.Pow(p.X, 2)) < 1e-6);
    }

    [Fact]
    public void Calculate_MovingAverage_UsesRequestedWindow()
    {
        var trend = ChartTrendlineCalculator.Calculate(
            ChartTrendlineType.MovingAverage,
            [new DataPoint(0, 2), new DataPoint(1, 4), new DataPoint(2, 10), new DataPoint(3, 12)],
            period: 3,
            order: 2);

        trend.Should().Equal(new DataPoint(2, 16.0 / 3.0), new DataPoint(3, 26.0 / 3.0));
    }

    [Fact]
    public void Calculate_MovingAverage_DelegatesToPresentationCalculator()
    {
        var source = AppUiSourceTestSupport.ReadAppUiSources("ChartTrendlineCalculator.cs");

        source.Should().Contain("TrendlineCalculator.Calculate");
        source.Should().NotContain("CalculateMovingAverageTrendline");
    }

    [Fact]
    public void Calculate_RegressionTrendlines_DelegatesToPresentationCalculator()
    {
        var source = AppUiSourceTestSupport.ReadAppUiSources("ChartTrendlineCalculator.cs");

        source.Should().Contain("ToTrendPoints");
        source.Should().NotContain("CalculateLinearTrendline");
        source.Should().NotContain("CalculateExponentialTrendline");
        source.Should().NotContain("CalculateLogarithmicTrendline");
        source.Should().NotContain("CalculatePowerTrendline");
    }

    [Fact]
    public void Calculate_PolynomialTrendline_DelegatesToPresentationCalculator()
    {
        var source = AppUiSourceTestSupport.ReadAppUiSources("ChartTrendlineCalculator.cs");

        source.Should().Contain("TrendlineCalculator.Calculate");
        source.Should().NotContain("SolvePolynomialLeastSquares");
    }

    [Fact]
    public void TryCalculateRSquared_DelegatesToPresentationCalculator()
    {
        var source = AppUiSourceTestSupport.ReadAppUiSources("ChartTrendlineCalculator.cs");

        source.Should().Contain("TrendlineCalculator.TryCalculateRSquared");
        source.Should().NotContain("TryInterpolateTrendY");
    }

    [Fact]
    public void TryCalculateRSquared_ReturnsOneForPerfectFit()
    {
        var source = new[] { new DataPoint(0, 1), new DataPoint(1, 3), new DataPoint(2, 5) };
        var trend = ChartTrendlineCalculator.Calculate(ChartTrendlineType.Linear, source, period: 2, order: 2);

        ChartTrendlineCalculator.TryCalculateRSquared(source, trend, out var rSquared).Should().BeTrue();
        rSquared.Should().BeApproximately(1.0, 0.000001);
    }

    [Fact]
    public void TryCalculateRSquared_Exponential_UsesLogSpaceFit()
    {
        // Points exactly on y = 2 * e^(0.5x): a perfect exponential fit. Excel reports the
        // R-squared of the linearized (ln y vs x) regression, which is 1.0 here.
        var source = new[]
        {
            new DataPoint(0, 2 * Math.Exp(0.0)),
            new DataPoint(1, 2 * Math.Exp(0.5)),
            new DataPoint(2, 2 * Math.Exp(1.0)),
            new DataPoint(3, 2 * Math.Exp(1.5)),
        };
        var trend = ChartTrendlineCalculator.Calculate(ChartTrendlineType.Exponential, source, period: 2, order: 2);

        ChartTrendlineCalculator.TryCalculateRSquared(source, trend, out var rSquared, logTransformY: true).Should().BeTrue();
        rSquared.Should().BeApproximately(1.0, 1e-6);
    }

    [Fact]
    public void TryCalculateRSquared_LogSpaceDiffersFromOriginalScaleForNoisyExponentialData()
    {
        // Noisy exponential-ish data: the log-space R-squared (Excel's) differs from the
        // original-scale R-squared, confirming the transform is actually applied.
        var source = new[]
        {
            new DataPoint(0, 2.0),
            new DataPoint(1, 3.0),
            new DataPoint(2, 6.5),
            new DataPoint(3, 8.0),
        };
        var trend = ChartTrendlineCalculator.Calculate(ChartTrendlineType.Exponential, source, period: 2, order: 2);

        ChartTrendlineCalculator.TryCalculateRSquared(source, trend, out var original, logTransformY: false).Should().BeTrue();
        ChartTrendlineCalculator.TryCalculateRSquared(source, trend, out var logSpace, logTransformY: true).Should().BeTrue();
        logSpace.Should().NotBeApproximately(original, 1e-3);
    }
}
