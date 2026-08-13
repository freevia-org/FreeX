using FreeX.Core.Model;

namespace FreeX.App.Presentation.Sparklines;

public readonly record struct SparklineAxisScale(
    double? Minimum,
    double? Maximum,
    double? MaximumAbsolute);

/// <summary>
/// Immutable group-scale lookup built once for a sparkline render pass.
/// </summary>
public sealed class SparklineAxisScalePlan
{
    private readonly IReadOnlyDictionary<int, double> _groupMinimums;
    private readonly IReadOnlyDictionary<int, double> _groupMaximums;
    private readonly IReadOnlyDictionary<int, double> _groupMaximumAbsolutes;

    internal SparklineAxisScalePlan(
        IReadOnlyDictionary<int, double> groupMinimums,
        IReadOnlyDictionary<int, double> groupMaximums,
        IReadOnlyDictionary<int, double> groupMaximumAbsolutes)
    {
        _groupMinimums = groupMinimums;
        _groupMaximums = groupMaximums;
        _groupMaximumAbsolutes = groupMaximumAbsolutes;
    }

    public SparklineAxisScale Resolve(SparklineModel sparkline)
    {
        ArgumentNullException.ThrowIfNull(sparkline);

        var minimum = sparkline.MinAxisType switch
        {
            SparklineAxisScaling.Custom => sparkline.ManualMin,
            SparklineAxisScaling.Group =>
                _groupMinimums.TryGetValue(sparkline.GroupId, out var value) && value != double.MaxValue
                    ? value
                    : null,
            _ => null,
        };

        var maximum = sparkline.MaxAxisType switch
        {
            SparklineAxisScaling.Custom => sparkline.ManualMax,
            SparklineAxisScaling.Group =>
                _groupMaximums.TryGetValue(sparkline.GroupId, out var value) && value != double.MinValue
                    ? value
                    : null,
            _ => null,
        };

        double? customMaximumAbsolute = null;
        if (sparkline.MaxAxisType == SparklineAxisScaling.Custom && sparkline.ManualMax.HasValue)
            customMaximumAbsolute = Math.Abs(sparkline.ManualMax.Value);
        if (sparkline.MinAxisType == SparklineAxisScaling.Custom && sparkline.ManualMin.HasValue)
        {
            var absoluteMinimum = Math.Abs(sparkline.ManualMin.Value);
            customMaximumAbsolute = customMaximumAbsolute.HasValue
                ? Math.Max(customMaximumAbsolute.Value, absoluteMinimum)
                : absoluteMinimum;
        }

        double? groupMaximumAbsolute = null;
        if (sparkline.MinAxisType == SparklineAxisScaling.Group ||
            sparkline.MaxAxisType == SparklineAxisScaling.Group)
        {
            groupMaximumAbsolute = _groupMaximumAbsolutes.TryGetValue(sparkline.GroupId, out var value)
                ? value
                : null;
        }

        var maximumAbsolute = customMaximumAbsolute.HasValue && groupMaximumAbsolute.HasValue
            ? Math.Max(customMaximumAbsolute.Value, groupMaximumAbsolute.Value)
            : customMaximumAbsolute ?? groupMaximumAbsolute;

        return new SparklineAxisScale(minimum, maximum, maximumAbsolute);
    }
}

/// <summary>
/// Computes renderer-neutral individual, group, and custom sparkline axis bounds.
/// </summary>
public static class SparklineAxisScalePlanner
{
    public static SparklineAxisScalePlan Build(
        IEnumerable<SparklineModel> sparklines,
        IReadOnlyDictionary<Guid, IReadOnlyList<double>> sparklineValues)
    {
        ArgumentNullException.ThrowIfNull(sparklines);
        ArgumentNullException.ThrowIfNull(sparklineValues);

        var groupMinimums = new Dictionary<int, double>();
        var groupMaximums = new Dictionary<int, double>();
        var groupMaximumAbsolutes = new Dictionary<int, double>();

        foreach (var sparkline in sparklines)
        {
            if ((sparkline.MinAxisType != SparklineAxisScaling.Group &&
                 sparkline.MaxAxisType != SparklineAxisScaling.Group) ||
                !sparklineValues.TryGetValue(sparkline.Id, out var values) ||
                values.Count == 0)
            {
                continue;
            }

            if (!groupMinimums.ContainsKey(sparkline.GroupId))
            {
                groupMinimums[sparkline.GroupId] = double.MaxValue;
                groupMaximums[sparkline.GroupId] = double.MinValue;
                groupMaximumAbsolutes[sparkline.GroupId] = 0;
            }

            foreach (var value in values)
            {
                if (!double.IsFinite(value))
                    continue;

                if (value < groupMinimums[sparkline.GroupId])
                    groupMinimums[sparkline.GroupId] = value;
                if (value > groupMaximums[sparkline.GroupId])
                    groupMaximums[sparkline.GroupId] = value;

                var absolute = Math.Abs(value);
                if (absolute > groupMaximumAbsolutes[sparkline.GroupId])
                    groupMaximumAbsolutes[sparkline.GroupId] = absolute;
            }
        }

        return new SparklineAxisScalePlan(groupMinimums, groupMaximums, groupMaximumAbsolutes);
    }
}
