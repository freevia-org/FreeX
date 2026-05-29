using FreeX.Core.Model;

namespace FreeX.Core.Commands;

internal static class ConsolidationRules
{
    public static void AddUnique(List<string> labels, string label)
    {
        if (!labels.Contains(label, StringComparer.OrdinalIgnoreCase))
            labels.Add(label);
    }

    public static string RowPositionLabel(uint offset) => $"Row {offset + 1}";

    public static string ColumnPositionLabel(uint offset) => $"Column {offset + 1}";

    public static string LabelText(ScalarValue value) =>
        value switch
        {
            TextValue text => text.Value.Trim(),
            NumberValue number => number.Value.ToString("G15", System.Globalization.CultureInfo.CurrentCulture),
            DateTimeValue date => date.Value.ToString("d", System.Globalization.CultureInfo.CurrentCulture),
            BoolValue boolean => boolean.Value ? "TRUE" : "FALSE",
            ErrorValue error => error.Code,
            _ => ""
        };

    public static double Aggregate(IReadOnlyList<double> values, int nonEmptyCount, ConsolidateFunction function) =>
        function switch
        {
            ConsolidateFunction.Count => nonEmptyCount,
            ConsolidateFunction.Average => values.Count == 0 ? 0 : values.Average(),
            ConsolidateFunction.Max => values.Count == 0 ? 0 : values.Max(),
            ConsolidateFunction.Min => values.Count == 0 ? 0 : values.Min(),
            ConsolidateFunction.Product => values.Count == 0 ? 0 : values.Aggregate(1.0, (product, value) => product * value),
            ConsolidateFunction.CountNumbers => values.Count,
            ConsolidateFunction.StdDev => StandardDeviation(values, sample: true),
            ConsolidateFunction.StdDevp => StandardDeviation(values, sample: false),
            ConsolidateFunction.Var => Variance(values, sample: true),
            ConsolidateFunction.Varp => Variance(values, sample: false),
            _ => values.Sum()
        };

    public static string CreateSourceLinkFormula(
        Workbook workbook,
        IReadOnlyList<CellAddress> sourceAddresses,
        SheetId destinationSheetId,
        ConsolidateFunction function)
    {
        var functionName = function switch
        {
            ConsolidateFunction.Count => "COUNTA",
            ConsolidateFunction.CountNumbers => "COUNT",
            ConsolidateFunction.StdDev => "STDEV",
            ConsolidateFunction.StdDevp => "STDEVP",
            ConsolidateFunction.Var => "VAR",
            ConsolidateFunction.Varp => "VARP",
            _ => function.ToString().ToUpperInvariant()
        };

        var arguments = sourceAddresses
            .Select(address => FormatFormulaReference(workbook, address, destinationSheetId));
        return $"{functionName}({string.Join(",", arguments)})";
    }

    private static string FormatFormulaReference(Workbook workbook, CellAddress address, SheetId destinationSheetId)
    {
        var reference = CellAddress.NumberToColumnName(address.Col) + address.Row;
        if (address.Sheet == destinationSheetId)
            return reference;

        var sheetName = workbook.GetSheet(address.Sheet)?.Name ?? "Sheet";
        return $"{QuoteSheetName(sheetName)}!{reference}";
    }

    private static string QuoteSheetName(string sheetName)
    {
        var escaped = sheetName.Replace("'", "''", StringComparison.Ordinal);
        return sheetName.Any(ch => !char.IsLetterOrDigit(ch) && ch != '_')
            ? $"'{escaped}'"
            : escaped;
    }

    private static double StandardDeviation(IReadOnlyList<double> values, bool sample) =>
        Math.Sqrt(Variance(values, sample));

    private static double Variance(IReadOnlyList<double> values, bool sample)
    {
        var denominator = sample ? values.Count - 1 : values.Count;
        if (denominator <= 0)
            return 0;

        var average = values.Average();
        return values.Sum(value => Math.Pow(value - average, 2)) / denominator;
    }
}
