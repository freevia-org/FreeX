using System.Text;
using FreeX.Core.Model;

namespace FreeX.Core.Formula;

public sealed partial class FormulaEvaluator
{
    private bool TryEvaluateTextjoinDirectRanges(
        FunctionCallNode node,
        IEvalContext context,
        out ScalarValue result)
    {
        result = BlankValue.Instance;
        if (node.Arguments.Count < 3)
            return false;

        var delimiterState = TryEvaluateFastScalarControl(node.Arguments[0], context, out var delimiterValue);
        if (delimiterState == DirectRangeFastPathState.Unsupported)
            return false;
        if (delimiterState == DirectRangeFastPathState.Error)
        {
            result = delimiterValue;
            return true;
        }

        if (delimiterValue is ErrorValue delimiterError)
        {
            result = delimiterError;
            return true;
        }

        var ignoreState = TryEvaluateFastScalarControl(node.Arguments[1], context, out var ignoreValue);
        if (ignoreState == DirectRangeFastPathState.Unsupported)
            return false;
        if (ignoreState == DirectRangeFastPathState.Error)
        {
            result = ignoreValue;
            return true;
        }

        if (ignoreValue is ErrorValue ignoreError)
        {
            result = ignoreError;
            return true;
        }

        bool ignoreEmpty;
        try
        {
            ignoreEmpty = BuiltInFunctions.ToBool(ignoreValue);
        }
        catch (FormulaEvalException ex)
        {
            result = ErrorFromCode(ex.ErrorCode);
            return true;
        }

        var delimiter = BuiltInFunctions.ToText(delimiterValue);
        if (!CanEvaluateTextjoinDirectTextArguments(node, context))
            return false;

        var builderCapacity = EstimateTextjoinBuilderCapacity(node, context, delimiter);
        var builder = builderCapacity > 0 ? new StringBuilder(builderCapacity) : new StringBuilder();
        var hasPart = false;

        for (var index = 2; index < node.Arguments.Count; index++)
        {
            var argument = node.Arguments[index];
            var rangeState = TryCreateDirectRangeArgument(argument, context, out var range, out var rangeResult);
            if (rangeState == DirectRangeFastPathState.Error)
            {
                result = rangeResult;
                return true;
            }

            if (rangeState == DirectRangeFastPathState.Success)
            {
                var error = AppendTextjoinDirectRange(context, range, delimiter, ignoreEmpty, builder, ref hasPart);
                if (error is not null)
                {
                    result = error;
                    return true;
                }

                continue;
            }

            var scalarState = TryEvaluateFastScalarControl(argument, context, out var scalarValue);
            if (scalarState == DirectRangeFastPathState.Unsupported)
                return false;
            if (scalarState == DirectRangeFastPathState.Error)
            {
                result = scalarValue;
                return true;
            }

            if (scalarValue is ErrorValue scalarError)
            {
                result = scalarError;
                return true;
            }

            AppendTextjoinPart(BuiltInFunctions.ToText(scalarValue), delimiter, ignoreEmpty, builder, ref hasPart);
        }

        var text = builder.ToString();
        result = BuiltInFunctions.ExceedsExcelTextLimit(text) ? ErrorValue.Value : new TextValue(text);
        return true;
    }

    private bool CanEvaluateTextjoinDirectTextArguments(FunctionCallNode node, IEvalContext context)
    {
        for (var index = 2; index < node.Arguments.Count; index++)
        {
            var rangeState = TryCreateDirectRangeArgument(node.Arguments[index], context, out _, out _);
            if (rangeState is DirectRangeFastPathState.Success or DirectRangeFastPathState.Error)
                continue;

            var scalarState = TryEvaluateFastScalarControl(node.Arguments[index], context, out _);
            if (scalarState == DirectRangeFastPathState.Unsupported)
                return false;
        }

        return true;
    }

    private static ErrorValue? AppendTextjoinDirectRange(
        IEvalContext context,
        DirectRangeArgument range,
        string delimiter,
        bool ignoreEmpty,
        StringBuilder builder,
        ref bool hasPart)
    {
        if (context is SheetEvalContext sheetContext)
        {
            var sheet = sheetContext.ResolveSheetForFastRange(range.SheetName);
            return sheet is null
                ? ErrorValue.Ref
                : AppendTextjoinSheetRange(sheet, range, delimiter, ignoreEmpty, builder, ref hasPart);
        }

        for (var row = range.StartRow; row <= range.EndRow; row++)
        {
            for (var col = range.StartCol; col <= range.EndCol; col++)
            {
                var value = GetFastRangeCellValue(context, range, row, col);
                if (value is ErrorValue error)
                    return error;

                AppendTextjoinPart(BuiltInFunctions.ToText(value), delimiter, ignoreEmpty, builder, ref hasPart);
            }
        }

        return null;
    }

    private static ErrorValue? AppendTextjoinSheetRange(
        Sheet sheet,
        DirectRangeArgument range,
        string delimiter,
        bool ignoreEmpty,
        StringBuilder builder,
        ref bool hasPart)
    {
        for (var row = range.StartRow; row <= range.EndRow; row++)
        {
            for (var col = range.StartCol; col <= range.EndCol; col++)
            {
                var value = sheet.GetValue(row, col);
                if (value is ErrorValue error)
                    return error;

                AppendTextjoinPart(BuiltInFunctions.ToText(value), delimiter, ignoreEmpty, builder, ref hasPart);
            }
        }

        return null;
    }

    private static void AppendTextjoinPart(
        string text,
        string delimiter,
        bool ignoreEmpty,
        StringBuilder builder,
        ref bool hasPart)
    {
        if (ignoreEmpty && text.Length == 0)
            return;

        if (hasPart)
            builder.Append(delimiter);

        builder.Append(text);
        hasPart = true;
    }

    private static int EstimateTextjoinBuilderCapacity(
        FunctionCallNode node,
        IEvalContext context,
        string delimiter)
    {
        long partCount = 0;
        for (var index = 2; index < node.Arguments.Count; index++)
        {
            var rangeState = TryCreateDirectRangeArgument(node.Arguments[index], context, out var range, out _);
            if (rangeState == DirectRangeFastPathState.Success)
            {
                partCount += FormulaSafetyLimits.GetRangeCellCount(
                    range.StartRow,
                    range.StartCol,
                    range.EndRow,
                    range.EndCol);
            }
            else
            {
                partCount++;
            }
        }

        if (partCount <= 16)
            return 0;

        var delimiterCharacters = delimiter.Length * Math.Max(0, partCount - 1);
        var estimatedCharacters = Math.Min(32767, partCount + delimiterCharacters);
        return (int)estimatedCharacters;
    }
}
