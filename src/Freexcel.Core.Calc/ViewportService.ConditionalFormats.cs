using Freexcel.Core.Model;

namespace Freexcel.Core.Calc;

public sealed partial class ViewportService
{
    private static CfEvaluationContext BuildConditionalFormatContext(Sheet sheet) =>
        ViewportConditionalFormatEvaluator.BuildContext(sheet);

    private static CellStyle? EvaluateConditionalFormats(
        Sheet sheet,
        CellAddress addr,
        ScalarValue value,
        Workbook workbook,
        CfEvaluationContext cfContext) =>
        ViewportConditionalFormatEvaluator.Evaluate(sheet, addr, value, workbook, cfContext, MatchesFormula);

    private static CellStyle MergeStyles(CellStyle? baseStyle, CellStyle cfStyle) =>
        ViewportConditionalFormatEvaluator.MergeStyles(baseStyle, cfStyle);

    private static bool TryGetDouble(ScalarValue value, out double result) =>
        ViewportConditionalFormatEvaluator.TryGetDouble(value, out result);

    private static bool TryParseDouble(string? text, out double result) =>
        ViewportConditionalFormatEvaluator.TryParseDouble(text, out result);
}
