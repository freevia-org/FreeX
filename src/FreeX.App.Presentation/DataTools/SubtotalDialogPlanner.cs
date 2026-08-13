using System.Globalization;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.DataTools;

public enum SubtotalDialogPlanAction
{
    Apply,
    RemoveAll
}

public sealed record SubtotalDialogColumnChoice(uint Offset, string Header, bool IsSelected)
{
    public override string ToString() => Header;
}

public sealed record SubtotalDialogFunctionChoice(string Label, string FunctionText)
{
    public override string ToString() => Label;
}

public sealed record SubtotalDialogPlannerText(
    string ColumnLabelFormat,
    string FunctionSum,
    string FunctionCount,
    string FunctionAverage,
    string FunctionMax,
    string FunctionMin,
    string FunctionProduct,
    string FunctionCountNumbers,
    string FunctionStdDev,
    string FunctionStdDevp,
    string FunctionVar,
    string FunctionVarp)
{
    public static SubtotalDialogPlannerText From(Func<string, string> getText)
    {
        ArgumentNullException.ThrowIfNull(getText);

        return new(
            getText("Subtotal_ColumnLabel"),
            getText("Subtotal_FunctionSum"),
            getText("Subtotal_FunctionCount"),
            getText("Subtotal_FunctionAverage"),
            getText("Subtotal_FunctionMax"),
            getText("Subtotal_FunctionMin"),
            getText("Subtotal_FunctionProduct"),
            getText("Subtotal_FunctionCountNumbers"),
            getText("Subtotal_FunctionStdDev"),
            getText("Subtotal_FunctionStdDevp"),
            getText("Subtotal_FunctionVar"),
            getText("Subtotal_FunctionVarp"));
    }

    public static SubtotalDialogPlannerText Default { get; } = new(
        "Column {0}",
        "Sum",
        "Count",
        "Average",
        "Max",
        "Min",
        "Product",
        "Count Numbers",
        "StdDev",
        "StdDevp",
        "Var",
        "Varp");

    public string FormatColumnLabel(object value) =>
        string.Format(CultureInfo.CurrentCulture, ColumnLabelFormat, value);
}

public sealed record SubtotalDialogPlanResult(
    uint GroupColumnOffset,
    IReadOnlyList<uint> SubtotalColumnOffsets,
    int FunctionNumber,
    bool ReplaceCurrentSubtotals,
    bool PageBreakBetweenGroups,
    bool SummaryBelowData,
    SubtotalDialogPlanAction Action = SubtotalDialogPlanAction.Apply)
{
    public SubtotalInputOptions ToInputOptions() =>
        new(
            GroupColumnOffset,
            SubtotalColumnOffsets,
            FunctionNumber,
            ReplaceCurrentSubtotals,
            PageBreakBetweenGroups,
            SummaryBelowData);
}

public static class SubtotalDialogPlanner
{
    public const string DefaultFunctionText = "Sum";

    public static IReadOnlyList<SubtotalDialogColumnChoice> NormalizeColumnChoices(
        IEnumerable<SubtotalDialogColumnChoice>? columns,
        SubtotalDialogPlannerText? text = null)
    {
        var normalized = columns?.ToArray() ?? [];
        if (normalized.Length > 0)
            return normalized;

        var resolvedText = ResolveText(text);
        return
        [
            new SubtotalDialogColumnChoice(0, resolvedText.FormatColumnLabel(1), false),
            new SubtotalDialogColumnChoice(1, resolvedText.FormatColumnLabel(2), true)
        ];
    }

    public static IReadOnlyList<SubtotalDialogColumnChoice> BuildColumnChoices(
        Sheet sheet,
        GridRange range,
        SubtotalDialogPlannerText? text = null)
    {
        ArgumentNullException.ThrowIfNull(sheet);

        return BuildColumnChoices(
            range,
            absoluteColumn => SpreadsheetDisplayFormatter.FormatCellValue(
                sheet.GetCell(range.Start.Row, absoluteColumn)?.Value),
            text);
    }

    public static IReadOnlyList<SubtotalDialogColumnChoice> BuildColumnChoices(
        GridRange range,
        Func<uint, string?> readHeader,
        SubtotalDialogPlannerText? text = null)
    {
        ArgumentNullException.ThrowIfNull(readHeader);

        var resolvedText = ResolveText(text);
        var choices = new List<SubtotalDialogColumnChoice>();
        for (uint offset = 0; offset < range.ColCount; offset++)
        {
            var absoluteColumn = range.Start.Col + offset;
            var header = readHeader(absoluteColumn);
            if (string.IsNullOrWhiteSpace(header))
                header = resolvedText.FormatColumnLabel(CellAddress.NumberToColumnName(absoluteColumn));

            choices.Add(new SubtotalDialogColumnChoice(offset, header, offset != 0));
        }

        return choices.Count == 0
            ? [new SubtotalDialogColumnChoice(0, resolvedText.FormatColumnLabel("A"), false)]
            : choices;
    }

    public static IReadOnlyList<SubtotalDialogFunctionChoice> CreateFunctionChoices(
        SubtotalDialogPlannerText? text = null)
    {
        var resolvedText = ResolveText(text);
        return
        [
            new(resolvedText.FunctionSum, "Sum"),
            new(resolvedText.FunctionCount, "Count"),
            new(resolvedText.FunctionAverage, "Average"),
            new(resolvedText.FunctionMax, "Max"),
            new(resolvedText.FunctionMin, "Min"),
            new(resolvedText.FunctionProduct, "Product"),
            new(resolvedText.FunctionCountNumbers, "CountA"),
            new(resolvedText.FunctionStdDev, "StdDev"),
            new(resolvedText.FunctionStdDevp, "StdDevp"),
            new(resolvedText.FunctionVar, "Var"),
            new(resolvedText.FunctionVarp, "Varp")
        ];
    }

    public static SubtotalDialogFunctionChoice? FindFunctionChoice(
        int functionNumber,
        SubtotalDialogPlannerText? text = null) =>
        CreateFunctionChoices(text).FirstOrDefault(choice =>
            SubtotalFunctionService.TryParse(choice.FunctionText, out var number) &&
            number == functionNumber);

    public static bool TryCreateResult(
        uint groupColumnOffset,
        IEnumerable<uint> subtotalColumnOffsets,
        string? functionText,
        bool replaceCurrentSubtotals,
        bool pageBreakBetweenGroups,
        bool summaryBelowData,
        out SubtotalDialogPlanResult result,
        out SubtotalDialogInputParseIssue issue)
    {
        if (SubtotalDialogInputParser.TryCreateResult(
                groupColumnOffset,
                subtotalColumnOffsets,
                functionText,
                replaceCurrentSubtotals,
                pageBreakBetweenGroups,
                summaryBelowData,
                out var parsed,
                out issue))
        {
            result = new SubtotalDialogPlanResult(
                parsed.GroupColumnOffset,
                parsed.SubtotalColumnOffsets,
                parsed.FunctionNumber,
                parsed.ReplaceCurrentSubtotals,
                parsed.PageBreakBetweenGroups,
                parsed.SummaryBelowData);
            return true;
        }

        result = default!;
        return false;
    }

    public static SubtotalDialogPlanResult CreateRemoveAllResult() =>
        new(
            GroupColumnOffset: 0,
            SubtotalColumnOffsets: [],
            FunctionNumber: 9,
            ReplaceCurrentSubtotals: false,
            PageBreakBetweenGroups: false,
            SummaryBelowData: true,
            Action: SubtotalDialogPlanAction.RemoveAll);

    private static SubtotalDialogPlannerText ResolveText(SubtotalDialogPlannerText? text) =>
        text ?? SubtotalDialogPlannerText.Default;
}
