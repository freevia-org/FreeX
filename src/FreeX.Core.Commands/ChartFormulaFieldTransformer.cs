using FreeX.Core.Model;

namespace FreeX.Core.Commands;

internal static class ChartFormulaFieldTransformer
{
    internal static void Transform(ChartModel chart, Func<string?, string?> transform)
    {
        ArgumentNullException.ThrowIfNull(chart);
        ArgumentNullException.ThrowIfNull(transform);

        if (chart.VerbatimSeriesFormulas is { Count: > 0 } seriesFormulas)
        {
            for (var i = 0; i < seriesFormulas.Count; i++)
            {
                var entry = seriesFormulas[i];
                var value = transform(entry.ValFormula);
                var category = transform(entry.CatFormula);
                var title = transform(entry.TxFormula);
                var bubbleSize = transform(entry.BubbleSizeFormula);
                if (!Same(value, entry.ValFormula) ||
                    !Same(category, entry.CatFormula) ||
                    !Same(title, entry.TxFormula) ||
                    !Same(bubbleSize, entry.BubbleSizeFormula))
                {
                    seriesFormulas[i] = entry with
                    {
                        ValFormula = value,
                        CatFormula = category,
                        TxFormula = title,
                        BubbleSizeFormula = bubbleSize,
                    };
                }
            }
        }

        if (chart.SeriesRangeDataLabels is { Count: > 0 } dataLabels)
        {
            for (var i = 0; i < dataLabels.Count; i++)
            {
                var entry = dataLabels[i];
                var formula = transform(entry.Formula);
                if (!Same(formula, entry.Formula))
                    dataLabels[i] = entry with { Formula = formula };
            }
        }

        var plus = transform(chart.ErrorBarPlusRangeFormula);
        var minus = transform(chart.ErrorBarMinusRangeFormula);
        if (!Same(plus, chart.ErrorBarPlusRangeFormula))
            chart.ErrorBarPlusRangeFormula = plus;
        if (!Same(minus, chart.ErrorBarMinusRangeFormula))
            chart.ErrorBarMinusRangeFormula = minus;
    }

    private static bool Same(string? left, string? right) =>
        string.Equals(left, right, StringComparison.Ordinal);
}
