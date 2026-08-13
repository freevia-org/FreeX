using FreeX.Core.Model;

namespace FreeX.App.Presentation.ConditionalFormatting;

/// <summary>Presentation wrapper over the renderer-neutral conditional-format statistics.</summary>
public sealed class ConditionalFormatStatistics
{
    private readonly ConditionalFormatEvaluationStatistics _statistics;

    private ConditionalFormatStatistics(ConditionalFormatEvaluationStatistics statistics) =>
        _statistics = statistics;

    public int Count => _statistics.Count;
    public double Min => _statistics.Min;
    public double Max => _statistics.Max;
    public double Average => _statistics.Average;
    public double StdDev => _statistics.StdDev;
    public IReadOnlyList<double> SortedValues => _statistics.SortedValues ?? [];

    internal ConditionalFormatEvaluationStatistics EvaluationStatistics => _statistics;

    public static ConditionalFormatStatistics FromValues(IEnumerable<double> values) =>
        new(ConditionalFormatEvaluationMath.CalculateStatistics(values));

    public bool TryResolveThreshold(CfThresholdType type, string? value, out double resolved) =>
        ConditionalFormatEvaluationMath.TryResolveStaticThreshold(type, value, _statistics, out resolved);

    public static bool TryResolvePercentile(
        IReadOnlyList<double> sortedValues,
        double percentile,
        out double value) =>
        ConditionalFormatEvaluationMath.TryResolvePercentile(sortedValues, percentile, out value);

    internal static bool TryParseInvariant(string? text, out double result) =>
        ConditionalFormatEvaluationMath.TryParseInvariant(text, out result);
}
