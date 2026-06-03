using FreeX.Core.Model;

namespace FreeX.Core.Formula;

public sealed partial class FormulaEvaluator
{
    private bool TryEvaluateConditionalAggregateDirectRanges(
        string functionName,
        FunctionCallNode node,
        IEvalContext context,
        out ScalarValue result)
    {
        return functionName switch
        {
            "COUNTIF" => TryEvaluateCountIfDirectRange(node, context, out result),
            "SUMIF" => TryEvaluateSumAverageIfDirectRange(node, context, average: false, out result),
            "AVERAGEIF" => TryEvaluateSumAverageIfDirectRange(node, context, average: true, out result),
            "COUNTIFS" => TryEvaluateCountIfsDirectRanges(node, context, out result),
            "SUMIFS" => TryEvaluateSumAverageIfsDirectRanges(node, context, average: false, out result),
            "AVERAGEIFS" => TryEvaluateSumAverageIfsDirectRanges(node, context, average: true, out result),
            _ => Unsupported(out result)
        };
    }

    private bool TryEvaluateCountIfDirectRange(
        FunctionCallNode node,
        IEvalContext context,
        out ScalarValue result)
    {
        result = BlankValue.Instance;
        if (node.Arguments.Count != 2)
            return false;

        var rangeState = TryCreateConditionalRangeArgument(node.Arguments[0], context, out var range, out result);
        if (rangeState == DirectRangeFastPathState.Unsupported)
            return false;
        if (rangeState == DirectRangeFastPathState.Error)
            return true;

        var criteriaState = TryCreateConditionalCriteria(node.Arguments[1], context, out var criteria, out result);
        if (criteriaState == DirectRangeFastPathState.Unsupported)
            return false;
        if (criteriaState == DirectRangeFastPathState.Error)
            return true;

        long count = 0;
        var rowCount = DirectRangeRowCount(range);
        var colCount = DirectRangeColCount(range);
        for (uint rowOffset = 0; rowOffset < rowCount; rowOffset++)
        {
            for (uint colOffset = 0; colOffset < colCount; colOffset++)
            {
                if (criteria.Matches(GetDirectConditionalCell(context, range, rowOffset, colOffset)))
                    count++;
            }
        }

        result = NumberValueFor(count);
        return true;
    }

    private bool TryEvaluateSumAverageIfDirectRange(
        FunctionCallNode node,
        IEvalContext context,
        bool average,
        out ScalarValue result)
    {
        result = BlankValue.Instance;
        if (node.Arguments.Count is < 2 or > 3)
            return false;

        var criteriaRangeState = TryCreateConditionalRangeArgument(node.Arguments[0], context, out var criteriaRange, out result);
        if (criteriaRangeState == DirectRangeFastPathState.Unsupported)
            return false;
        if (criteriaRangeState == DirectRangeFastPathState.Error)
            return true;

        var criteriaState = TryCreateConditionalCriteria(node.Arguments[1], context, out var criteria, out result);
        if (criteriaState == DirectRangeFastPathState.Unsupported)
            return false;
        if (criteriaState == DirectRangeFastPathState.Error)
            return true;

        var valueRange = criteriaRange;
        if (node.Arguments.Count == 3)
        {
            var valueRangeState = TryCreateConditionalRangeArgument(node.Arguments[2], context, out valueRange, out result);
            if (valueRangeState == DirectRangeFastPathState.Unsupported)
                return false;
            if (valueRangeState == DirectRangeFastPathState.Error)
                return true;
        }

        double total = 0;
        long count = 0;
        var rowCount = DirectRangeRowCount(criteriaRange);
        var colCount = DirectRangeColCount(criteriaRange);
        for (uint rowOffset = 0; rowOffset < rowCount; rowOffset++)
        {
            for (uint colOffset = 0; colOffset < colCount; colOffset++)
            {
                if (!criteria.Matches(GetDirectConditionalCell(context, criteriaRange, rowOffset, colOffset)))
                    continue;

                var value = GetDirectConditionalCell(context, valueRange, rowOffset, colOffset);
                if (TryDirectRangeNumber(value, out var number, out var error))
                {
                    total += number;
                    count++;
                }
                else if (error is not null)
                {
                    result = error;
                    return true;
                }
            }
        }

        result = average
            ? count == 0 ? ErrorValue.DivByZero : FastNumberResult(total / count)
            : FastNumberResult(total);
        return true;
    }

    private bool TryEvaluateCountIfsDirectRanges(
        FunctionCallNode node,
        IEvalContext context,
        out ScalarValue result)
    {
        result = BlankValue.Instance;
        if (node.Arguments.Count < 2 || node.Arguments.Count % 2 != 0)
            return false;

        var pairCount = node.Arguments.Count / 2;
        var pairState = TryCreateDirectConditionalCriteriaPairs(
            node.Arguments,
            firstCriteriaRangeIndex: 0,
            pairCount,
            requiredShape: null,
            context,
            out var pairs,
            out result);
        if (pairState == DirectRangeFastPathState.Unsupported)
            return false;
        if (pairState == DirectRangeFastPathState.Error)
            return true;

        long count = 0;
        var shape = pairs[0].Range;
        var rowCount = DirectRangeRowCount(shape);
        var colCount = DirectRangeColCount(shape);
        for (uint rowOffset = 0; rowOffset < rowCount; rowOffset++)
        {
            for (uint colOffset = 0; colOffset < colCount; colOffset++)
            {
                if (DirectConditionalCriteriaMatchAll(context, pairs, rowOffset, colOffset))
                    count++;
            }
        }

        result = NumberValueFor(count);
        return true;
    }

    private bool TryEvaluateSumAverageIfsDirectRanges(
        FunctionCallNode node,
        IEvalContext context,
        bool average,
        out ScalarValue result)
    {
        result = BlankValue.Instance;
        if (node.Arguments.Count < 3 || (node.Arguments.Count - 1) % 2 != 0)
            return false;

        var valueRangeState = TryCreateConditionalRangeArgument(node.Arguments[0], context, out var valueRange, out result);
        if (valueRangeState == DirectRangeFastPathState.Unsupported)
            return false;
        if (valueRangeState == DirectRangeFastPathState.Error)
            return true;

        var pairCount = (node.Arguments.Count - 1) / 2;
        var pairState = TryCreateDirectConditionalCriteriaPairs(
            node.Arguments,
            firstCriteriaRangeIndex: 1,
            pairCount,
            valueRange,
            context,
            out var pairs,
            out result);
        if (pairState == DirectRangeFastPathState.Unsupported)
            return false;
        if (pairState == DirectRangeFastPathState.Error)
            return true;

        double total = 0;
        long count = 0;
        var rowCount = DirectRangeRowCount(valueRange);
        var colCount = DirectRangeColCount(valueRange);
        for (uint rowOffset = 0; rowOffset < rowCount; rowOffset++)
        {
            for (uint colOffset = 0; colOffset < colCount; colOffset++)
            {
                if (!DirectConditionalCriteriaMatchAll(context, pairs, rowOffset, colOffset))
                    continue;

                var value = GetDirectConditionalCell(context, valueRange, rowOffset, colOffset);
                if (TryDirectRangeNumber(value, out var number, out var error))
                {
                    total += number;
                    count++;
                }
                else if (error is not null)
                {
                    result = error;
                    return true;
                }
            }
        }

        result = average
            ? count == 0 ? ErrorValue.DivByZero : FastNumberResult(total / count)
            : FastNumberResult(total);
        return true;
    }

    private DirectRangeFastPathState TryCreateDirectConditionalCriteriaPairs(
        IReadOnlyList<FormulaNode> arguments,
        int firstCriteriaRangeIndex,
        int pairCount,
        DirectRangeArgument? requiredShape,
        IEvalContext context,
        out DirectConditionalCriteriaPair[] pairs,
        out ScalarValue result)
    {
        pairs = new DirectConditionalCriteriaPair[pairCount];
        result = BlankValue.Instance;
        var shape = requiredShape.GetValueOrDefault();
        var hasShape = requiredShape.HasValue;

        for (var index = 0; index < pairCount; index++)
        {
            var rangeIndex = firstCriteriaRangeIndex + index * 2;
            var criteriaIndex = rangeIndex + 1;

            var rangeState = TryCreateConditionalRangeArgument(arguments[rangeIndex], context, out var range, out result);
            if (rangeState != DirectRangeFastPathState.Success)
                return rangeState;

            if (!hasShape)
            {
                shape = range;
                hasShape = true;
            }
            else if (!SameDirectRangeShape(shape, range))
            {
                result = ErrorValue.Value;
                return DirectRangeFastPathState.Error;
            }

            var criteriaState = TryCreateConditionalCriteria(arguments[criteriaIndex], context, out var criteria, out result);
            if (criteriaState != DirectRangeFastPathState.Success)
                return criteriaState;

            pairs[index] = new DirectConditionalCriteriaPair(range, criteria);
        }

        return DirectRangeFastPathState.Success;
    }

    private DirectRangeFastPathState TryCreateConditionalCriteria(
        FormulaNode node,
        IEvalContext context,
        out BuiltInFunctions.CriteriaMatcher criteria,
        out ScalarValue result)
    {
        criteria = default;
        var state = TryEvaluateFastScalarControl(node, context, out var criteriaValue);
        if (state == DirectRangeFastPathState.Unsupported)
        {
            result = BlankValue.Instance;
            return DirectRangeFastPathState.Unsupported;
        }

        if (criteriaValue is ErrorValue error)
        {
            result = error;
            return DirectRangeFastPathState.Error;
        }

        criteria = BuiltInFunctions.CompileCriteria(criteriaValue);
        result = BlankValue.Instance;
        return DirectRangeFastPathState.Success;
    }

    private static DirectRangeFastPathState TryCreateConditionalRangeArgument(
        FormulaNode node,
        IEvalContext context,
        out DirectRangeArgument range,
        out ScalarValue result)
    {
        if (node is CellRefNode cell)
        {
            if (cell.SheetName is not null && !context.SheetExists(cell.SheetName))
            {
                range = default;
                result = ErrorValue.Ref;
                return DirectRangeFastPathState.Error;
            }

            range = new DirectRangeArgument(
                cell.SheetName,
                cell.Row,
                cell.ColumnNumber,
                cell.Row,
                cell.ColumnNumber);
            result = BlankValue.Instance;
            return DirectRangeFastPathState.Success;
        }

        return TryCreateDirectRangeArgument(node, context, out range, out result);
    }

    private static bool DirectConditionalCriteriaMatchAll(
        IEvalContext context,
        DirectConditionalCriteriaPair[] pairs,
        uint rowOffset,
        uint colOffset)
    {
        for (var index = 0; index < pairs.Length; index++)
        {
            var pair = pairs[index];
            if (!pair.Criteria.Matches(GetDirectConditionalCell(context, pair.Range, rowOffset, colOffset)))
                return false;
        }

        return true;
    }

    private static ScalarValue GetDirectConditionalCell(
        IEvalContext context,
        DirectRangeArgument range,
        uint rowOffset,
        uint colOffset)
    {
        return GetFastRangeCellValue(
            context,
            range,
            range.StartRow + rowOffset,
            range.StartCol + colOffset);
    }

    private static uint DirectRangeRowCount(DirectRangeArgument range) =>
        range.EndRow - range.StartRow + 1;

    private static uint DirectRangeColCount(DirectRangeArgument range) =>
        range.EndCol - range.StartCol + 1;

    private static bool SameDirectRangeShape(DirectRangeArgument left, DirectRangeArgument right) =>
        DirectRangeRowCount(left) == DirectRangeRowCount(right) &&
        DirectRangeColCount(left) == DirectRangeColCount(right);

    private static bool Unsupported(out ScalarValue result)
    {
        result = BlankValue.Instance;
        return false;
    }

    private readonly record struct DirectConditionalCriteriaPair(
        DirectRangeArgument Range,
        BuiltInFunctions.CriteriaMatcher Criteria);
}
