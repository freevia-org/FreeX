using FreeX.Core.Formula;
using FreeX.Core.Model;

namespace FreeX.Core.Calc;

public sealed partial class ViewportService
{
    private static readonly FormulaEvaluator _cfEvaluator = new();

    // Internal (not private): passed as the matchesFormula delegate to
    // ViewportConditionalFormatEvaluator.MatchesRuleCondition by ConditionalFormatRenderEvaluator
    // (FreeX.App.Presentation, granted access via InternalsVisibleTo) so print preview and PDF
    // export evaluate Formula-type conditional-format rules with the same logic the screen
    // renderer uses.
    internal static bool MatchesFormula(
        ConditionalFormat cf,
        Sheet sheet,
        CellAddress addr,
        Workbook workbook,
        CfEvaluationContext cfContext)
    {
        if (string.IsNullOrWhiteSpace(cf.FormulaText)) return false;
        if (!cfContext.Formulas.TryGetValue(cf, out var formulaCache)) return false;

        // A Formula-type CF rule (e.g. "=RAND()>0.5") is re-evaluated for the same cell on every
        // GetViewport call, including pure render passes (scroll/resize) that touch no content.
        // Without caching, a volatile function inside the rule would re-randomize on every render
        // instead of only on a genuine recalc. cfContext.FormulaResults lives on the CfEvaluationContext,
        // which itself is only rebuilt when Sheet.ContentVersion or the rule set actually changes, so
        // caching here reuses the last evaluated result across render-only requests and naturally
        // refreshes on the next real recalc/edit.
        if (cfContext.FormulaResults.TryGet(cf, addr, out var cachedResult))
            return cachedResult;

        var evaluated = EvaluateFormulaUncached(cf, sheet, addr, workbook, formulaCache);
        cfContext.FormulaResults.Set(cf, addr, evaluated);
        return evaluated;
    }

    private static bool EvaluateFormulaUncached(
        ConditionalFormat cf,
        Sheet sheet,
        CellAddress addr,
        Workbook workbook,
        CfFormulaCache formulaCache)
    {
        try
        {
            // Shift relative references from the CF range's top-left to the current cell.
            int dr = (int)addr.Row - (int)cf.AppliesTo.Start.Row;
            int dc = (int)addr.Col - (int)cf.AppliesTo.Start.Col;
            if (formulaCache.SimpleComparison is { } simpleComparison)
                return MatchesSimpleComparison(simpleComparison, sheet, workbook, dr, dc);
            if (formulaCache.SimpleAnd is { } simpleAnd)
                return MatchesSimpleAnd(simpleAnd, sheet, workbook, dr, dc);

            var ast = ViewportConditionalFormatEvaluator.GetShiftedConditionalFormatFormula(
                formulaCache.Ast,
                cf.AppliesTo.Start,
                addr,
                formulaCache.HasRelativeReferences);

            var result = _cfEvaluator.Evaluate(ast, sheet, workbook, addr);
            return MatchesConditionalFormulaResult(result);
        }
        catch
        {
            return false;
        }
    }

    private static bool MatchesConditionalFormulaResult(ScalarValue result)
    {
        while (result is RangeValue { RowCount: 1, ColCount: 1 } singleCell)
            result = singleCell.Cells[0, 0];

        return result switch
        {
            BoolValue boolean => boolean.Value,
            NumberValue number => number.Value != 0,
            DateTimeValue date => date.Value != 0,
            _ => false
        };
    }

    private static bool MatchesSimpleComparison(
        CfSimpleFormulaComparison comparison,
        Sheet sheet,
        Workbook workbook,
        int dr,
        int dc)
    {
        if (!TryResolveSimpleOperand(comparison.Left, sheet, workbook, dr, dc, out var left) ||
            !TryResolveSimpleOperand(comparison.Right, sheet, workbook, dr, dc, out var right))
            return false;

        if (left is ErrorValue || right is ErrorValue)
            return false;

        var cmp = CompareSimpleValues(left, right);
        return comparison.Operator switch
        {
            BinaryOperator.Equal => cmp == 0,
            BinaryOperator.NotEqual => cmp != 0,
            BinaryOperator.LessThan => cmp < 0,
            BinaryOperator.GreaterThan => cmp > 0,
            BinaryOperator.LessOrEqual => cmp <= 0,
            BinaryOperator.GreaterOrEqual => cmp >= 0,
            _ => false
        };
    }

    private static bool MatchesSimpleAnd(
        CfSimpleFormulaAnd simpleAnd,
        Sheet sheet,
        Workbook workbook,
        int dr,
        int dc)
    {
        var comparisons = simpleAnd.Comparisons;
        for (var i = 0; i < comparisons.Length; i++)
        {
            if (!MatchesSimpleComparison(comparisons[i], sheet, workbook, dr, dc))
                return false;
        }

        return true;
    }

    private static bool TryResolveSimpleOperand(
        CfFormulaScalarOperand operand,
        Sheet sheet,
        Workbook workbook,
        int dr,
        int dc,
        out ScalarValue value)
    {
        if (operand.Kind == CfFormulaScalarOperandKind.Literal)
        {
            value = operand.Literal ?? BlankValue.Instance;
            return true;
        }

        var row = ViewportConditionalFormatEvaluator.ShiftRow(operand.Row, operand.IsRowAbsolute, dr);
        var col = ViewportConditionalFormatEvaluator.ShiftColumn(operand.Col, operand.IsColAbsolute, dc);
        if (!row.HasValue || !col.HasValue)
        {
            value = ErrorValue.Ref;
            return false;
        }

        var targetSheet = operand.SheetName is null ? sheet : workbook.GetSheet(operand.SheetName);
        if (targetSheet is null)
        {
            value = ErrorValue.Ref;
            return false;
        }

        value = targetSheet.GetValue(row.Value, col.Value);
        return true;
    }

    private static int CompareSimpleValues(ScalarValue left, ScalarValue right)
    {
        // Blank coercion: Excel coerces a blank operand to match the other operand's type class
        // so that =A1=0, =A1="", and =A1=FALSE all return TRUE when A1 is empty. Mirrors
        // FormulaEvaluator.Operators.cs CompareValues so the CF fast path agrees with the
        // general evaluator's slow path for the same rule.
        var leftIsBlank = left is BlankValue;
        var rightIsBlank = right is BlankValue;
        if (leftIsBlank && !rightIsBlank)
            return CompareSimpleValues(CoerceSimpleBlankTo(right), right);
        if (rightIsBlank && !leftIsBlank)
            return CompareSimpleValues(left, CoerceSimpleBlankTo(left));
        // blank vs blank falls through — both remain BlankValue and TypeOrder gives 0==0.

        var leftIsNumber = left is NumberValue or DateTimeValue;
        var rightIsNumber = right is NumberValue or DateTimeValue;
        if (leftIsNumber && rightIsNumber)
            return GetNumber(left).CompareTo(GetNumber(right));

        if (left is TextValue leftText && right is TextValue rightText)
            return string.Compare(leftText.Value, rightText.Value, StringComparison.OrdinalIgnoreCase);

        if (left is BoolValue leftBool && right is BoolValue rightBool)
            return leftBool.Value.CompareTo(rightBool.Value);

        return SimpleValueTypeOrder(left).CompareTo(SimpleValueTypeOrder(right));
    }

    /// <summary>
    /// Returns the zero/empty/false value of the same type class as <paramref name="other"/>,
    /// used to coerce a blank operand before a comparison. Mirrors
    /// FormulaEvaluator.Operators.cs CoerceBlankTo.
    /// </summary>
    private static ScalarValue CoerceSimpleBlankTo(ScalarValue other) => other switch
    {
        NumberValue or DateTimeValue => new NumberValue(0),
        TextValue => new TextValue(string.Empty),
        BoolValue => new BoolValue(false),
        _ => BlankValue.Instance
    };

    private static double GetNumber(ScalarValue value) =>
        value is DateTimeValue date ? date.Value : ((NumberValue)value).Value;

    private static int SimpleValueTypeOrder(ScalarValue value) => value switch
    {
        BlankValue => 0,
        NumberValue or DateTimeValue => 1,
        TextValue => 2,
        BoolValue => 3,
        _ => 4
    };

}
