using FreeX.Core.Formula;
using FreeX.Core.Model;

namespace FreeX.Core.Calc;

internal static partial class ViewportConditionalFormatEvaluator
{
    private static Dictionary<ConditionalFormat, CfFormulaCache> PrecomputeFormulaCaches(Sheet sheet)
    {
        Dictionary<ConditionalFormat, CfFormulaCache>? result = null;
        foreach (var cf in sheet.ConditionalFormats)
        {
            if (cf.RuleType != CfRuleType.Formula || string.IsNullOrWhiteSpace(cf.FormulaText))
                continue;

            try
            {
                var ast = ParseFormulaText(cf.FormulaText);
                var simpleComparison = TryCreateSimpleComparison(ast, out var comparison)
                    ? comparison
                    : (CfSimpleFormulaComparison?)null;
                var simpleAnd = simpleComparison is null && TryCreateSimpleAnd(ast, out var and)
                    ? and
                    : null;

                (result ??= new Dictionary<ConditionalFormat, CfFormulaCache>(ReferenceEqualityComparer.Instance))[cf] = new CfFormulaCache(
                    ast,
                    FormulaAstReferenceShifter.HasRelativeReferences(ast),
                    simpleComparison,
                    simpleAnd);
            }
            catch
            {
                // Preserve formula CF error handling: invalid formulas do not match.
            }
        }

        return result ?? EmptyFormulas;
    }

    private static bool TryCreateSimpleComparison(FormulaNode ast, out CfSimpleFormulaComparison comparison)
    {
        comparison = default;
        if (ast is not BinaryOpNode binary || !IsComparisonOperator(binary.Operator))
            return false;

        if (!TryCreateSimpleOperand(binary.Left, out var left) ||
            !TryCreateSimpleOperand(binary.Right, out var right))
            return false;

        comparison = new CfSimpleFormulaComparison(left, binary.Operator, right);
        return true;
    }

    private static bool TryCreateSimpleAnd(FormulaNode ast, out CfSimpleFormulaAnd and)
    {
        and = default!;
        if (ast is not FunctionCallNode { Arguments.Count: > 0 } function ||
            !string.Equals(function.FunctionName, "AND", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var comparisons = new CfSimpleFormulaComparison[function.Arguments.Count];
        for (var i = 0; i < function.Arguments.Count; i++)
        {
            if (!TryCreateSimpleComparison(function.Arguments[i], out comparisons[i]))
                return false;
        }

        and = new CfSimpleFormulaAnd(comparisons);
        return true;
    }

    private static bool IsComparisonOperator(BinaryOperator op) =>
        op is BinaryOperator.Equal
            or BinaryOperator.NotEqual
            or BinaryOperator.LessThan
            or BinaryOperator.GreaterThan
            or BinaryOperator.LessOrEqual
            or BinaryOperator.GreaterOrEqual;

    private static bool TryCreateSimpleOperand(FormulaNode node, out CfFormulaScalarOperand operand)
    {
        operand = default;
        switch (node)
        {
            case CellRefNode cell:
                operand = new CfFormulaScalarOperand(
                    CfFormulaScalarOperandKind.Reference,
                    null,
                    cell.Row,
                    cell.ColumnNumber,
                    cell.IsRowAbsolute,
                    cell.IsColAbsolute,
                    cell.SheetName);
                return true;
            case NumberNode number:
                operand = LiteralOperand(new NumberValue(number.Value));
                return true;
            case StringNode text:
                operand = LiteralOperand(new TextValue(text.Value));
                return true;
            case BooleanNode boolean:
                operand = LiteralOperand(new BoolValue(boolean.Value));
                return true;
            case ErrorNode error:
                operand = LiteralOperand(error.Error);
                return true;
            default:
                return false;
        }
    }

    private static CfFormulaScalarOperand LiteralOperand(ScalarValue value) =>
        new(CfFormulaScalarOperandKind.Literal, value, 0, 0, true, true, null);

    internal static FormulaNode GetShiftedConditionalFormatFormula(
        FormulaNode ast,
        CellAddress anchorCell,
        CellAddress currentCell,
        bool? hasRelativeReferences = null) =>
        FormulaAstReferenceShifter.ShiftForCell(
            ast,
            anchorCell,
            currentCell,
            hasRelativeReferences);

    internal static uint? ShiftRow(uint row, bool isAbsolute, int delta) =>
        FormulaAstReferenceShifter.ShiftRow(row, isAbsolute, delta);

    internal static uint? ShiftColumn(uint column, bool isAbsolute, int delta) =>
        FormulaAstReferenceShifter.ShiftColumn(column, isAbsolute, delta);
}
