namespace FreeX.App.Presentation.PivotUI;

/// <summary>
/// A summary (aggregation) function that the values area supports, as a portable choice for the
/// value-field-settings UI. <see cref="FunctionCode"/> is the lowercase token stored on
/// <c>PivotDataFieldModel.SummaryFunction</c> and understood by the pivot refresh/aggregate logic;
/// <see cref="DisplayName"/> is the human-readable label a renderer shows in a list.
/// </summary>
public sealed record PivotAggregationFunction(string FunctionCode, string DisplayName);

/// <summary>
/// The portable catalog of aggregation functions offered by the value-field-settings UI, mirroring the
/// functions the pivot aggregate logic recognizes. Order matches the conventional presentation order.
/// </summary>
public static class PivotAggregationFunctions
{
    public static readonly PivotAggregationFunction Sum = new("sum", "Sum");
    public static readonly PivotAggregationFunction Count = new("count", "Count");
    public static readonly PivotAggregationFunction Average = new("average", "Average");
    public static readonly PivotAggregationFunction Max = new("max", "Max");
    public static readonly PivotAggregationFunction Min = new("min", "Min");
    public static readonly PivotAggregationFunction Product = new("product", "Product");
    public static readonly PivotAggregationFunction CountNumbers = new("countnums", "Count Numbers");
    public static readonly PivotAggregationFunction StdDev = new("stddev", "StdDev");
    public static readonly PivotAggregationFunction StdDevP = new("stddevp", "StdDevp");
    public static readonly PivotAggregationFunction Var = new("var", "Var");
    public static readonly PivotAggregationFunction VarP = new("varp", "Varp");

    /// <summary>All supported functions in conventional presentation order.</summary>
    public static IReadOnlyList<PivotAggregationFunction> All { get; } =
    [
        Sum, Count, Average, Max, Min, Product, CountNumbers, StdDev, StdDevP, Var, VarP
    ];

    /// <summary>
    /// Resolves a stored summary-function token to its catalog entry (case-insensitive, tolerating the
    /// "avg" alias). Returns null for an unrecognized token so the caller can decide on a fallback.
    /// </summary>
    public static PivotAggregationFunction? FromCode(string? functionCode)
    {
        if (string.IsNullOrWhiteSpace(functionCode))
            return null;

        var normalized = functionCode.Trim();
        if (string.Equals(normalized, "avg", StringComparison.OrdinalIgnoreCase))
            return Average;

        return All.FirstOrDefault(function =>
            string.Equals(function.FunctionCode, normalized, StringComparison.OrdinalIgnoreCase));
    }
}
