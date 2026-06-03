using FreeX.Core.Model;

namespace FreeX.Core.Formula;

public sealed partial class FormulaEvaluator
{
    private ScalarValue EvaluateFunction(FunctionCallNode node, IEvalContext context)
    {
        var functionName = node.FunctionName;

        // LET-scoped lambda bindings: a name like "double" resolves to a LambdaValue
        // before any built-in lookup, allowing user-defined functions to shadow nothing.
        var lambdaBinding = context.TryResolveLambdaBinding(functionName);
        if (lambdaBinding is LambdaValue lv)
            return InvokeLambdaWithArgs(lv, node.Arguments, context);
        if (lambdaBinding is ErrorValue bindingError)
            return bindingError;
        if (lambdaBinding is not null)
            return ErrorValue.Value;

        // LET and LAMBDA are AST-aware special forms not in the built-in registry.
        if (functionName is "LET" or "LAMBDA")
            return EvaluateAstAware(node, context);

        if (!BuiltInFunctions.TryGet(functionName, out var entry))
            return ErrorValue.Name;

        // Short-circuit functions evaluate arguments lazily to avoid propagating errors from untaken branches.
        if (functionName is "IF" or "IFERROR" or "IFNA" or "CHOOSE" or "IFS" or "SWITCH")
            return EvaluateShortCircuit(node, context);

        // AST-aware functions: must inspect the raw argument nodes before evaluation.
        if (functionName is "ISREF" or "ISFORMULA" or "FORMULATEXT" or "OFFSET" or "CELL")
            return EvaluateAstAware(node, context);

        var (func, minArgs, maxArgs) = entry;

        if (functionName == "INDEX" &&
            TryEvaluateIndexDirectRange(node, context, out var directIndexResult))
            return directIndexResult;

        if (TryEvaluateReferenceDimensionFunction(functionName, node, context, out var dimensionResult))
            return dimensionResult;

        if (functionName == "XNPV" &&
            TryEvaluateXnpvDirectRanges(node, context, out var directXnpvResult))
            return directXnpvResult;

        bool isStructured = IsStructuredRangeFunction(functionName);
        bool isAggregate = IsAggregateFunction(functionName);
        bool isDirectTextCoercingAggregate = IsDirectTextCoercingAggregate(functionName);
        bool preservesReferenceProvenance = IsReferenceProvenanceAggregate(functionName);
        bool isSingleCellReferenceRangeFunction = IsSingleCellReferenceRangeFunction(functionName);

        if (node.Arguments.Count >= minArgs &&
            (isAggregate || node.Arguments.Count <= maxArgs) &&
            TryEvaluateRangeOnlyFastAggregate(functionName, node.Arguments, context, out var fastAggregate))
        {
            return fastAggregate;
        }

        // Expand range arguments into individual values for aggregate functions,
        // or wrap as RangeValue for structured functions that need 2-D access.
        var expandedArgs = new List<ScalarValue>(node.Arguments.Count);
        for (var argIndex = 0; argIndex < node.Arguments.Count; argIndex++)
        {
            var arg = node.Arguments[argIndex];
            if (TryAsRangeRef(arg, out var range))
            {
                if (range.SheetName is not null && !context.SheetExists(range.SheetName))
                {
                    expandedArgs.Add(ErrorValue.Ref);
                    continue;
                }

                // Full-column/full-row references nominally span the whole grid and would exceed the
                // materialization cap (returning #REF!). Excel only ever reads the populated extent,
                // so clamp the open end to the sheet's used range — both the streamed (GetRangeValues)
                // and structured (BuildRangeValue) branches below then operate on a bounded range.
                range = ClampOpenEndedRangeToUsed(range, context);

                if (isStructured)
                {
                    // Build a 2-D RangeValue for structured functions
                    expandedArgs.Add(BuildRangeValueOrError(range, context));
                }
                else
                {
                    IReadOnlyList<ScalarValue> values = range.SheetName is not null
                        ? context.GetRangeValues(range.SheetName,
                            range.Start.Row, range.Start.ColumnNumber,
                            range.End.Row, range.End.ColumnNumber)
                        : context.GetRangeValues(
                            range.Start.Row, range.Start.ColumnNumber,
                            range.End.Row, range.End.ColumnNumber);
                    AddRangeValues(expandedArgs, values, preservesReferenceProvenance);
                }
            }
            else if (arg is StringNode directText && isDirectTextCoercingAggregate)
            {
                expandedArgs.Add(new DirectTextLiteralValue(directText.Value));
            }
            else if (arg is CellRefNode structuredCell && IsConditionalAggregateRangeArgument(functionName, argIndex))
            {
                if (structuredCell.SheetName is not null && !context.SheetExists(structuredCell.SheetName))
                {
                    expandedArgs.Add(ErrorValue.Ref);
                    continue;
                }

                expandedArgs.Add(BuildRangeValueOrError(new RangeRefNode(structuredCell, structuredCell, structuredCell.SheetName), context));
            }
            else if (arg is CellRefNode aggregateCell && IsSingleCellReferenceProvenanceArgument(functionName, argIndex, preservesReferenceProvenance))
            {
                if (aggregateCell.SheetName is not null && !context.SheetExists(aggregateCell.SheetName))
                {
                    expandedArgs.Add(ErrorValue.Ref);
                    continue;
                }

                var value = aggregateCell.SheetName is not null
                    ? context.GetCellValue(aggregateCell.SheetName, aggregateCell.Row, aggregateCell.ColumnNumber)
                    : context.GetCellValue(aggregateCell.Row, aggregateCell.ColumnNumber);
                expandedArgs.Add(new ReferencedScalarValue(value));
            }
            else if (arg is CellRefNode cell && isSingleCellReferenceRangeFunction)
            {
                if (cell.SheetName is not null && !context.SheetExists(cell.SheetName))
                {
                    expandedArgs.Add(ErrorValue.Ref);
                    continue;
                }

                expandedArgs.Add(BuildRangeValueOrError(new RangeRefNode(cell, cell, cell.SheetName), context));
            }
            else if (arg is NamedRangeNode named)
            {
                // Check LET/LAMBDA bindings first — these shadow workbook named ranges.
                var lambdaBound = context.TryResolveLambdaBinding(named.Name);
                if (lambdaBound is not null)
                {
                    if (isStructured && lambdaBound is RangeValue)
                        expandedArgs.Add(lambdaBound);
                    else if (!isStructured && lambdaBound is RangeValue flatRv)
                        AddRangeValues(expandedArgs, flatRv.Flatten(), preservesReferenceProvenance);
                    else
                        expandedArgs.Add(lambdaBound);
                }
                else
                {
                    var resolvedRange = context.TryResolveNamedRange(named.Name);
                    if (resolvedRange is null)
                    {
                        expandedArgs.Add(ErrorValue.Name);
                    }
                    else
                    {
                        var r = resolvedRange.Value;
                        if (isStructured)
                        {
                            expandedArgs.Add(BuildRangeValueOrError(r, context));
                        }
                        else
                        {
                            // Resolve the sheet name when the named range lives on a different sheet
                            var sheetName = context.TryGetSheetName(r.Start.Sheet);
                            IReadOnlyList<ScalarValue> values = sheetName is not null
                                ? context.GetRangeValues(sheetName,
                                    r.Start.Row, r.Start.Col,
                                    r.End.Row, r.End.Col)
                                : context.GetRangeValues(
                                    r.Start.Row, r.Start.Col,
                                    r.End.Row, r.End.Col);
                            AddRangeValues(expandedArgs, values, preservesReferenceProvenance);
                        }
                    }
                }
            }
            else
            {
                var value = EvaluateNode(arg, context);
                if (!isStructured && isAggregate && value is RangeValue rangeValue)
                    AddRangeValues(expandedArgs, rangeValue.Flatten(), preservesReferenceProvenance);
                else
                    expandedArgs.Add(value);
            }
        }

        foreach (var expandedArg in expandedArgs)
        {
            if (expandedArg is RangeMaterializationErrorValue rangeError)
                return rangeError.Error;
        }

        // Always enforce minimum arg count for every function, including aggregates.
        if (node.Arguments.Count < minArgs)
            return ErrorValue.Value;
        // Enforce maximum only for non-aggregate functions (aggregates accept unbounded ranges).
        if (!isAggregate && node.Arguments.Count > maxArgs)
            return ErrorValue.Value;

        try
        {
            return func(expandedArgs, context);
        }
        catch (FormulaEvalException ex)
        {
            return ErrorFromCode(ex.ErrorCode);
        }
        catch (OverflowException)
        {
            return ErrorValue.Num;
        }
        catch (ArgumentOutOfRangeException)
        {
            return ErrorValue.Num;
        }
        catch (IndexOutOfRangeException)
        {
            return ErrorValue.Ref;
        }
    }

    private static ErrorValue ErrorFromCode(string code) => code.ToUpperInvariant() switch
    {
        "#DIV/0!" => ErrorValue.DivByZero,
        "#VALUE!" => ErrorValue.Value,
        "#REF!" => ErrorValue.Ref,
        "#NAME?" => ErrorValue.Name,
        "#NULL!" => ErrorValue.Null,
        "#N/A" => ErrorValue.NA,
        "#NUM!" => ErrorValue.Num,
        "#SPILL!" => ErrorValue.Spill,
        "#CALC!" => ErrorValue.Calc,
        _ => ErrorValue.Value
    };


    private ScalarValue EvaluateAstAware(FunctionCallNode node, IEvalContext context)
    {
        return node.FunctionName switch
        {
            "ISREF"        => EvaluateIsRef(node, context),
            "ISFORMULA"    => EvaluateIsFormula(node, context),
            "FORMULATEXT"  => EvaluateFormulaText(node, context),
            "CELL"         => EvaluateCellInfo(node, context),
            "OFFSET"       => EvaluateOffset(node, context),
            "LET"          => EvaluateLet(node, context),
            "LAMBDA"       => EvaluateLambda(node, context),
            _              => ErrorValue.Value
        };
    }

}
