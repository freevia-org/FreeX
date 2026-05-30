using System.Globalization;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public static class PivotValueFilterInputParser
{
    public static bool TryCreateFilter(
        PivotValueFilterKind kind,
        bool usesCount,
        string valueText,
        string value2Text,
        int sourceFieldIndex,
        out PivotValueFilterModel filter,
        out string? error)
    {
        filter = default!;
        error = null;

        if (usesCount)
        {
            if (!int.TryParse(valueText.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var count) || count <= 0)
            {
                error = UiText.Get("PivotValueFilter_PositiveItemCountMessage");
                return false;
            }

            filter = new PivotValueFilterModel(0, kind, Count: count, SourceFieldIndex: sourceFieldIndex);
            return true;
        }

        if (kind is PivotValueFilterKind.AboveAverage or PivotValueFilterKind.BelowAverage)
        {
            filter = new PivotValueFilterModel(0, kind, SourceFieldIndex: sourceFieldIndex);
            return true;
        }

        if (!TryParseFiniteDouble(valueText, out var value))
        {
            error = UiText.Get("PivotValueFilter_NumericComparisonMessage");
            return false;
        }

        double? value2 = null;
        if (kind is PivotValueFilterKind.Between or PivotValueFilterKind.NotBetween)
        {
            if (!TryParseFiniteDouble(value2Text, out var parsedValue2))
            {
                error = UiText.Get("PivotValueFilter_NumericEndingComparisonMessage");
                return false;
            }

            value2 = parsedValue2;
        }

        filter = new PivotValueFilterModel(
            0,
            kind,
            ComparisonValue: value,
            ComparisonValue2: value2,
            SourceFieldIndex: sourceFieldIndex);
        return true;
    }

    private static bool TryParseFiniteDouble(string input, out double value) =>
        NumericInputParser.TryParseFiniteDouble(input, CultureInfo.InvariantCulture, out value);
}
