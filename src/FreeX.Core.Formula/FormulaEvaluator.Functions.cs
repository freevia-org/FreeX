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

        // LET, LAMBDA, SINGLE, and ANCHORARRAY are AST-aware special forms not in the built-in registry.
        if (functionName is "LET" or "LAMBDA" or "SINGLE" or "ANCHORARRAY")
            return EvaluateAstAware(node, context);

        if (!BuiltInFunctions.TryGet(functionName, out var entry))
        {
            // Not a built-in and not a LET-scoped binding: check for a workbook/sheet-defined
            // Name Manager name whose RefersTo is a LAMBDA (Excel's "custom function via Name
            // Manager" pattern, e.g. FACT -> =LAMBDA(n, IF(n<=1,1,n*FACT(n-1)))). Only a
            // formula-backed name that actually resolves to a callable LambdaValue can be
            // invoked with call syntax; anything else (a plain range/value name, or no such
            // name at all) still yields #NAME? like Excel.
            if (TryEvaluateNamedFormula(functionName, context, out var namedFormulaValue) &&
                namedFormulaValue is LambdaValue namedLambda)
                return InvokeLambdaWithArgs(namedLambda, node.Arguments, context);

            return ErrorValue.Name;
        }

        // Short-circuit functions evaluate arguments lazily to avoid propagating errors from untaken branches.
        if (functionName is "IF" or "IFERROR" or "IFNA" or "CHOOSE" or "IFS" or "SWITCH")
            return EvaluateShortCircuit(node, context);

        // AST-aware functions: must inspect the raw argument nodes before evaluation.
        if (functionName is "ISREF" or "ISFORMULA" or "FORMULATEXT" or "OFFSET" or "CELL")
            return EvaluateAstAware(node, context);

        var (func, minArgs, maxArgs) = entry;

        bool isStructured = IsStructuredRangeFunction(functionName);
        bool isAggregate = IsAggregateFunction(functionName);
        bool isSheetSpanAggregate = IsSheetSpanAggregateFunction(functionName);
        bool isDirectTextCoercingAggregate = IsDirectTextCoercingAggregate(functionName);
        bool preservesReferenceProvenance = IsReferenceProvenanceAggregate(functionName);
        bool isSingleCellReferenceRangeFunction = IsSingleCellReferenceRangeFunction(functionName);

        // Enforce ordinary function arity before evaluating or expanding range arguments.
        // Aggregate functions (SUM, AVERAGE, MEDIAN, AND, OR, CONCAT(ENATE), etc.) are variadic
        // but not unbounded: Excel caps every function, including these, at 255 syntax
        // arguments -- already their registered maxArgs -- so the cap is enforced uniformly
        // here rather than exempting isAggregate (R126-aggregate-arg-cap). Note this counts
        // literal comma-separated argument slots, not expanded cell values, so =SUM(A1:A10000)
        // is unaffected -- only e.g. =SUM(1,2,3,...,256 literals) is rejected, matching Excel.
        if (node.Arguments.Count < minArgs)
            return ErrorValue.Value;
        if (node.Arguments.Count > maxArgs)
            return ErrorValue.Value;

        if (functionName == "TEXTJOIN" &&
            TryEvaluateTextjoinDirectRanges(node, context, out var directTextjoinResult))
            return directTextjoinResult;

        if (functionName == "INDEX" &&
            TryEvaluateIndexDirectRange(node, context, out var directIndexResult))
            return directIndexResult;

        if (TryEvaluateReferenceDimensionFunction(functionName, node, context, out var dimensionResult))
            return dimensionResult;

        if (functionName == "MATCH" &&
            TryEvaluateMatchDirectRange(node, context, out var directMatchResult))
            return directMatchResult;

        if (functionName == "XMATCH" &&
            TryEvaluateXmatchDirectRange(node, context, out var directXmatchResult))
            return directXmatchResult;

        if ((functionName == "VLOOKUP" || functionName == "HLOOKUP") &&
            TryEvaluateLegacyLookupDirectTable(node, context, horizontal: functionName == "HLOOKUP", out var directLegacyLookupResult))
            return directLegacyLookupResult;

        if (functionName == "XLOOKUP" &&
            TryEvaluateXlookupDirectRanges(node, context, out var directXlookupResult))
            return directXlookupResult;

        if (functionName == "LOOKUP" &&
            TryEvaluateLookupDirectRanges(node, context, out var directLookupResult))
            return directLookupResult;

        if (IsDirectSelectionFunction(functionName) &&
            TryEvaluateStatisticalSelectionDirectRange(functionName, node, context, out var directSelectionResult))
            return directSelectionResult;

        if (functionName == "NPV" &&
            TryEvaluateNpvDirectRanges(node, context, out var directNpvResult))
            return directNpvResult;

        if (functionName == "XNPV" &&
            TryEvaluateXnpvDirectRanges(node, context, out var directXnpvResult))
            return directXnpvResult;

        if (functionName == "IRR" &&
            TryEvaluateIrrDirectRange(node, context, out var directIrrResult))
            return directIrrResult;

        if (functionName == "SUBTOTAL" &&
            TryEvaluateSubtotalOffsetArrayArg(node, context, out var subtotalOffsetArrayResult))
            return subtotalOffsetArrayResult;

        if (functionName == "SUBTOTAL" &&
            TryEvaluateSubtotalDirectRanges(node, context, out var directSubtotalResult))
            return directSubtotalResult;

        if (functionName == "AGGREGATE" &&
            TryEvaluateAggregateDirectRanges(node, context, out var directAggregateResult))
            return directAggregateResult;

        if (TryEvaluateConditionalAggregateDirectRanges(functionName, node, context, out var directConditionalAggregateResult))
            return directConditionalAggregateResult;

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

            // A 3-D sheet-span reference (e.g. Sheet1:Sheet3!A1 or Sheet1:Sheet3!A1:B5) is only
            // valid Excel syntax as a direct argument to an aggregate function (SUM, AVERAGE,
            // COUNT, ...), where it expands across every sheet from the start to the end sheet
            // inclusive, in workbook tab order. TryAsRangeRef deliberately refuses span-shaped
            // RangeRefNodes (see its comment) so every other code path — structured functions,
            // fast paths, plain evaluation — naturally treats a span as "not a plain range" and
            // ultimately surfaces #VALUE!; this is the one place that actually expands it.
            if (arg is RangeRefNode { EndSheetName: not null } spanRange)
            {
                if (isStructured || !isSheetSpanAggregate)
                {
                    // SHEETS(Sheet1:Sheet3!A1) is valid Excel and must return the number of sheets
                    // spanned (3), not #VALUE!. SheetsFunc/SheetSpanCount (see
                    // BuiltInFunctions.Lookup.Reference.cs) already handle a RangeValue whose
                    // SheetName encodes a "Start:End" span; encode one here so it reaches them
                    // instead of falling into the generic error path below. Only SHEETS is
                    // special-cased — every other non-aggregate/structured function still errors.
                    if (functionName == "SHEETS")
                    {
                        expandedArgs.Add(new RangeValue(new ScalarValue[1, 1] { { BlankValue.Instance } },
                            spanRange.Start.Row, spanRange.Start.ColumnNumber)
                        { SheetName = $"{spanRange.SheetName}:{spanRange.EndSheetName}" });
                        continue;
                    }

                    expandedArgs.Add(ErrorValue.Value);
                    continue;
                }

                var spanResult = TryExpandSheetSpanAggregateRange(spanRange, context, expandedArgs, preservesReferenceProvenance);
                if (spanResult is not null)
                    return spanResult;

                continue;
            }

            if (TryResolveReferenceRange(arg, context, out var range, out var referenceRangeError))
            {
                if (referenceRangeError is { } refErr)
                {
                    expandedArgs.Add(new RangeMaterializationErrorValue(refErr));
                    continue;
                }

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
            else if (isAggregate &&
                     !isStructured &&
                     functionName != "COUNTBLANK" &&
                     arg is FunctionCallNode { FunctionName: "INDIRECT" } indirect &&
                     TryExpandLiteralIndirectAggregateRange(
                         indirect,
                         context,
                         expandedArgs,
                         preservesReferenceProvenance,
                         out var indirectError))
            {
                if (indirectError is not null)
                    return indirectError;
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
            else if (arg is CellRefNode cell && isSingleCellReferenceRangeFunction &&
                     IsSingleCellReferenceRangeDataArgument(functionName, argIndex))
            {
                if (cell.SheetName is not null && !context.SheetExists(cell.SheetName))
                {
                    expandedArgs.Add(ErrorValue.Ref);
                    continue;
                }

                expandedArgs.Add(BuildRangeValueOrError(new RangeRefNode(cell, cell, cell.SheetName), context));
            }
            else if (functionName == "TEXTSPLIT" && argIndex == 5 && arg is OmittedArgumentNode)
            {
                // TEXTSPLIT's pad_with must default to #N/A only when the argument slot itself is
                // genuinely omitted -- not when an explicit argument merely evaluates to blank
                // (e.g. a reference to an empty cell, which should pad with "" instead). The
                // generic EvaluateNode(OmittedArgumentNode) fallback below returns the same
                // BlankValue.Instance singleton a blank-cell reference produces, so the two cases
                // must be told apart here, before evaluation, the same way OFFSET/INDEX inspect
                // the raw argument node for OmittedArgumentNode ahead of evaluating it.
                expandedArgs.Add(TextSplitOmittedPadArgumentValue.Instance);
            }
            else if (functionName == "TEXTSPLIT" && (argIndex == 1 || argIndex == 2) && arg is OmittedArgumentNode)
            {
                // TEXTSPLIT's col_delimiter/row_delimiter must be told apart from an explicit
                // blank-cell-reference delimiter: TEXTSPLIT(A1,,";") (double-comma -- the argument
                // slot itself omitted) means "don't split on that axis", but TEXTSPLIT(A1,B1,";")
                // with B1 blank means "split on the empty-string delimiter", which Excel rejects
                // with #VALUE! (mirroring the literal ="" case, since Excel coerces the blank
                // reference to "" for this text argument). The generic EvaluateNode(OmittedArgumentNode)
                // fallback below returns the same BlankValue.Instance singleton a blank-cell reference
                // produces, so the two cases must be told apart here, before evaluation -- mirroring
                // the TEXTSPLIT pad_with special case above.
                expandedArgs.Add(TextSplitOmittedDelimiterArgumentValue.Instance);
            }
            // R102: this branch used to ALSO cover FIND/SEARCH's start_num, LEFT/RIGHT's num_chars,
            // and SUBSTITUTE's instance_num (plus the FINDB/SEARCHB/LEFTB/RIGHTB byte-count
            // variants) via a shared IsOmittedOptionalOrdinalArgument(functionName, argIndex)
            // predicate. That was WRONG for those five function families specifically: per
            // Parser.ParseArgumentList, OmittedArgumentNode is produced ONLY for an argument slot
            // that is explicitly PRESENT but empty (a trailing comma, e.g. LEFT("abc",) -- there is
            // no LATER argument for that comma to help reach, since num_chars/start_num/instance_num
            // is each of those five functions' ONLY (and therefore last) optional argument). A
            // genuinely OMITTED trailing argument (e.g. LEFT("abc")) never produces a node at all;
            // the args list for that call is simply shorter, which every one of these functions
            // already detects via its own `args.Count > N` check. So forcing that explicitly-blank
            // last argument into the SAME "genuinely omitted, use the smart default" treatment as a
            // fully-absent argument was backwards from Excel, which treats a present-but-blank
            // numeric argument like any other blank-cell numeric coercion (coerces to 0) when there
            // is no later argument the blank could have been skipping to reach -- exactly the
            // omitted-vs-blank distinction VLOOKUP's range_lookup makes, just the other direction
            // (omitted->TRUE, blank->FALSE there; omitted->default, blank->0 here). E.g. real
            // Excel's LEFT("abc",) is "" (num_chars coerced to 0), not "a" (the num_chars-omitted
            // default of 1); FIND("a","abc",) is #VALUE! (start_num coerced to 0, which every
            // FIND/SEARCH/SUBSTITUTE domain check already rejects as < 1), not the omitted default
            // (which succeeds from the start of the text / replaces every occurrence). That part of
            // the branch was removed for those five, letting their OmittedArgumentNode fall through
            // to the generic EvaluateNode(OmittedArgumentNode) => BlankValue.Instance case below --
            // the SAME value a blank-cell reference already produces -- so their existing
            // `args.Count > N` genuinely-omitted check and BlankValue-coerces-to-0 domain check
            // (both already present, see e.g. BuiltInFunctions.TextCore.Slice.cs's Left/Right) now
            // naturally see the correct case without any further change.
            //
            // TEXTBEFORE/TEXTAFTER's instance_num (argIndex 2) is DELIBERATELY KEPT here, unlike its
            // five siblings above: unlike them, instance_num is a MIDDLE optional argument --
            // match_mode, match_end and if_not_found all follow it -- so a real, extremely common
            // Excel idiom is skipping instance_num via a blank comma specifically to reach one of
            // those LATER arguments while still wanting instance_num's ordinary default (e.g.
            // TEXTBEFORE(text,delim,,1) to set match_mode=1 without caring about instance_num, or
            // TEXTBEFORE("Socrates"," ",,,1) -- a real Microsoft documentation example -- to set
            // match_end=1). Excel's own designers evidently did not want every such call to hit the
            // instanceNum==0 domain error (BuiltInFunctions.TextSplit.cs's TryTextBeforeAfterOptions)
            // just because the user skipped past instance_num to specify a later argument -- unlike
            // VLOOKUP's range_lookup (the LAST argument, no later argument to reach) or the five
            // siblings above (each argument here is likewise its function's ONLY optional argument,
            // with nothing after it), where there is no such ergonomic pressure and Excel's ordinary
            // blank-coerces-to-0 rule applies untouched. This is confirmed by this codebase's own
            // pre-existing test suite (ExcelParityModernTextTests' documented-Excel-results and
            // case-insensitive-search theories), which exercises exactly this "blank instance_num to
            // reach match_mode/match_end" pattern and expects the DEFAULT (not an error) -- left
            // passing, unchanged, by keeping this branch scoped down to just these two entries.
            else if (arg is OmittedArgumentNode && functionName is "TEXTBEFORE" or "TEXTAFTER" && argIndex == 2)
            {
                expandedArgs.Add(OmittedOptionalOrdinalArgumentValue.Instance);
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
                    else if (!isStructured && isAggregate && lambdaBound is UnionValue lambdaUnion)
                        AddRangeValues(expandedArgs, FlattenUnionAreas(lambdaUnion), preservesReferenceProvenance);
                    else if (isStructured && lambdaBound is UnionValue lambdaStructuredUnion &&
                             IsUnionMaterializableRangeFunction(functionName))
                        expandedArgs.Add(MaterializeUnionRangeValue(lambdaStructuredUnion));
                    else
                        expandedArgs.Add(lambdaBound);
                }
                else
                {
                    // Excel scope precedence: a sheet-scoped name (either kind) always wins over a
                    // same-named workbook-global name, so a sheet-scoped named FORMULA must be
                    // preferred over a workbook-global named RANGE here too — matching the bare-name
                    // resolution in EvaluateNamedRange/EvaluateArrayOperand (see IsSheetScopedName).
                    FreeX.Core.Model.GridRange? resolvedRange;
                    bool preferScopedFormula = false;
                    if (IsSheetScopedName(named.Name, context, out var sheetScopedIsFormula) && sheetScopedIsFormula)
                    {
                        preferScopedFormula = true;
                        resolvedRange = null;
                    }
                    else
                    {
                        resolvedRange = context.TryResolveNamedRange(named.Name);
                    }

                    if (!preferScopedFormula && resolvedRange is not null)
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
                    else if (isSheetSpanAggregate && !isStructured &&
                             TryExpandNamedFormulaSheetSpanAggregateRange(
                                 named.Name, context, expandedArgs, preservesReferenceProvenance, out var spanError))
                    {
                        // A defined name whose RefersTo is a bare 3-D sheet-span (e.g.
                        // Sheet1:Sheet3!A1): expand across the spanned sheets just like a literal
                        // span argument, rather than falling through to TryEvaluateNamedFormula
                        // (which has no aggregate-argument context and would surface #VALUE!).
                        if (spanError is not null)
                            return spanError;
                    }
                    else if (TryEvaluateNamedFormula(named.Name, context, out var namedFormulaArg))
                    {
                        // Named formula: the evaluated result may be a scalar, RangeValue (array),
                        // or -- when the name's RefersTo is itself a union, e.g. a name "U" whose
                        // RefersTo is "=(Sheet1!A1:A2,Sheet1!B1:B2)" -- a UnionValue; unwrap the
                        // same way a literal parenthesized union argument is unwrapped below.
                        if (!isStructured && isAggregate && namedFormulaArg is RangeValue namedRv)
                            AddRangeValues(expandedArgs, namedRv.Flatten(), preservesReferenceProvenance);
                        else if (!isStructured && isAggregate && namedFormulaArg is UnionValue namedUnion)
                            AddRangeValues(expandedArgs, FlattenUnionAreas(namedUnion), preservesReferenceProvenance);
                        else if (isStructured && namedFormulaArg is UnionValue namedStructuredUnion &&
                                 IsUnionMaterializableRangeFunction(functionName))
                            expandedArgs.Add(MaterializeUnionRangeValue(namedStructuredUnion));
                        else
                            expandedArgs.Add(namedFormulaArg);
                    }
                    else
                    {
                        expandedArgs.Add(ErrorValue.Name);
                    }
                }
            }
            else
            {
                var value = EvaluateNode(arg, context);
                if (!isStructured && isAggregate && value is RangeValue rangeValue)
                    AddRangeValues(expandedArgs, rangeValue.Flatten(), preservesReferenceProvenance);
                else if (!isStructured && isAggregate && value is UnionValue union)
                    // R93-AREAS-union-value-model gap close: a parenthesized union argument (e.g.
                    // the "(A1:A2,B1:B2)" in MIN((A1:A2,B1:B2))) evaluates to a UnionValue here --
                    // unwrap it into its per-area flattened cell values at this single choke
                    // point, exactly like a plain RangeValue argument above, so every aggregate
                    // function (SUM/AVERAGE/MIN/MAX/COUNT/COUNTA/PRODUCT/MEDIAN/STDEV/VAR/...)
                    // gets union support for free instead of each needing its own inline unwrap.
                    // FlattenUnionAreas concatenates each area's cells in order without
                    // deduplicating, so a cell covered by two overlapping areas is naturally
                    // counted/summed/collected twice -- matching Excel's own double-counting of
                    // overlapping union areas (e.g. SUM((A1:A2,A1:A2)) double-counts A1 and A2).
                    AddRangeValues(expandedArgs, FlattenUnionAreas(union), preservesReferenceProvenance);
                else if (isStructured && value is UnionValue structuredUnion &&
                         IsUnionMaterializableRangeFunction(functionName))
                    // R94-formula-union-selection-range: LARGE/SMALL/RANK/RANK.EQ/RANK.AVG/
                    // PERCENTILE(.INC/.EXC)/QUARTILE(.INC/.EXC)/TRIMMEAN/PERCENTRANK(.INC/.EXC)/
                    // COUNTBLANK are StructuredRangeFunctions (isAggregate is false), so the
                    // aggregate-flatten branches above never run for them and a UnionValue
                    // argument would otherwise reach the function body as an opaque scalar (see
                    // IsUnionMaterializableRangeFunction's doc comment). Unlike the aggregate
                    // case, these functions expect a single RangeValue argument at a fixed
                    // position (not a variadic scalar list), so materialize the union into one
                    // synthetic RangeValue here instead of spreading it across expandedArgs.
                    expandedArgs.Add(MaterializeUnionRangeValue(structuredUnion));
                else
                    expandedArgs.Add(value);
            }
        }

        if (IsErrorInspectingFunction(functionName))
        {
            // The IS*/N/TYPE/ERROR.TYPE family's CONTRACT is to inspect an erroring argument, not
            // propagate it (e.g. =ISERROR(A1:A2 C1:C2) with disjoint ranges must return TRUE, not
            // re-surface the #NULL! that TryResolveReferenceRange wrapped as a
            // RangeMaterializationErrorValue). Unwrap to the inner ErrorValue so the function body
            // (which already special-cases ErrorValue args) can see and report on it, instead of
            // short-circuiting the whole call below like every other function must.
            for (var i = 0; i < expandedArgs.Count; i++)
            {
                if (expandedArgs[i] is RangeMaterializationErrorValue errorInspectRangeError)
                    expandedArgs[i] = errorInspectRangeError.Error;
            }
        }
        else
        {
            foreach (var expandedArg in expandedArgs)
            {
                if (expandedArg is RangeMaterializationErrorValue rangeError)
                    return rangeError.Error;
            }
        }

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

    /// <summary>
    /// Expands a 3-D sheet-span aggregate argument (e.g. Sheet1:Sheet3!A1 or Sheet1:Sheet3!A1:B5)
    /// into <paramref name="expandedArgs"/>: one value (or block of values, for the range form) per
    /// sheet from <paramref name="spanRange"/>'s start sheet to its end sheet inclusive, in workbook
    /// tab order. A reversed span (end sheet appears before start sheet in tab order) covers the
    /// same sheets as the forward span, matching Excel's normalization. Returns null on success
    /// (values were appended); returns a non-null ScalarValue when the whole function call must
    /// short-circuit to that error (missing workbook/sheet -> #REF!).
    /// </summary>
    private static ScalarValue? TryExpandSheetSpanAggregateRange(
        RangeRefNode spanRange,
        IEvalContext context,
        List<ScalarValue> expandedArgs,
        bool preservesReferenceProvenance)
    {
        var workbook = context.CurrentWorkbook;
        if (workbook is null)
            return ErrorValue.Ref;

        var startSheetIndex = FindSheetIndex(workbook, spanRange.SheetName!);
        var endSheetIndex = FindSheetIndex(workbook, spanRange.EndSheetName!);
        if (startSheetIndex < 0 || endSheetIndex < 0)
        {
            // An external-workbook 3-D sheet-span (e.g. '[1]Sheet1:Sheet3'!A1) carries its external
            // marker only on the start sheet name (Excel's own on-disk shape -- see
            // FormulaSerializer.WriteSheetSpanName), so it never resolves through FindSheetIndex
            // above, which only searches THIS workbook's own local Sheets. Mirror the single-sheet
            // external path (SheetEvalContext.GetCellValue/GetRangeValues in
            // FormulaEvaluator.Contexts.cs): when the start sheet name is a resolvable external
            // reference, throw FormulaParseException instead of surfacing #REF!, so RecalcEngine's
            // external-workbook-reference guard (IsLikelyExternalWorkbookReferenceFormula) preserves
            // the cell's last-known cached value instead of clobbering it. A span whose start sheet
            // is NOT a resolvable external reference is a genuine broken/unknown local span and still
            // surfaces #REF! below.
            if (ExternalSheetReferenceResolver.TryResolve(workbook, spanRange.SheetName!) is not null)
            {
                throw new FormulaParseException(
                    $"External reference '{spanRange.SheetName}:{spanRange.EndSheetName}' spans " +
                    "sheets this workbook cannot resolve locally; preserving the last-known loaded " +
                    "value instead of recomputing to #REF!.");
            }

            return ErrorValue.Ref;
        }

        var firstIndex = Math.Min(startSheetIndex, endSheetIndex);
        var lastIndex = Math.Max(startSheetIndex, endSheetIndex);

        for (var sheetIndex = firstIndex; sheetIndex <= lastIndex; sheetIndex++)
        {
            var sheetName = workbook.Sheets[sheetIndex].Name;

            // A full-column/full-row span (e.g. Sheet1:Sheet3!A:A) nominally spans the whole grid
            // on every sheet it covers and would exceed the materialization cap per sheet, just
            // like the non-span full-column path above (Functions.cs ~line 160). Clamp per sheet
            // since each spanned sheet's used range can differ.
            var perSheet = ClampOpenEndedRangeToUsed(spanRange with { SheetName = sheetName, EndSheetName = null }, context);
            var values = context.GetRangeValues(
                sheetName,
                perSheet.Start.Row, perSheet.Start.ColumnNumber,
                perSheet.End.Row, perSheet.End.ColumnNumber);
            AddRangeValues(expandedArgs, values, preservesReferenceProvenance);
        }

        return null;
    }

    /// <summary>
    /// A defined name whose RefersTo is a bare 3-D sheet-span (e.g. Name -> Sheet1:Sheet3!A1) is
    /// valid Excel syntax only as a direct argument to an aggregate function, exactly like a
    /// literal span written inline (see <see cref="TryExpandSheetSpanAggregateRange"/> above).
    /// The generic named-formula path (TryEvaluateNamedFormula -> EvaluateNamedFormulaAst ->
    /// EvaluateArrayOperand -> BuildRangeValueOrError) has no notion of "called from an aggregate
    /// argument position" and always surfaces #VALUE! for a span, so this parses the name's
    /// RefersTo text directly and, when it is nothing but a bare span reference, reuses the same
    /// span-expansion machinery instead of falling through to that generic path.
    /// Returns false (do nothing) when the name isn't a formula, doesn't parse, or its RefersTo
    /// isn't a bare 3-D span — callers should fall back to the normal named-formula evaluation.
    /// </summary>
    private static bool TryExpandNamedFormulaSheetSpanAggregateRange(
        string name,
        IEvalContext context,
        List<ScalarValue> expandedArgs,
        bool preservesReferenceProvenance,
        out ScalarValue? error)
    {
        error = null;

        var formulaText = context.TryGetNamedFormulaText(name);
        if (formulaText is null)
            return false;

        FormulaNode ast;
        try
        {
            ast = GetOrParseFormula(formulaText);
            ast = ApplyRelativeNameAnchor(ast, context);
        }
        catch (FormulaParseException)
        {
            return false;
        }

        if (ast is not RangeRefNode { EndSheetName: not null } spanRange)
            return false;

        error = TryExpandSheetSpanAggregateRange(spanRange, context, expandedArgs, preservesReferenceProvenance);
        return true;
    }

    private static int FindSheetIndex(FreeX.Core.Model.Workbook workbook, string sheetName)
    {
        var sheets = workbook.Sheets;
        for (var i = 0; i < sheets.Count; i++)
        {
            if (string.Equals(sheets[i].Name, sheetName, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return -1;
    }

    private static bool TryExpandLiteralIndirectAggregateRange(
        FunctionCallNode indirect,
        IEvalContext context,
        List<ScalarValue> expandedArgs,
        bool preservesReferenceProvenance,
        out ErrorValue? error)
    {
        error = null;
        if (!TryBuildLiteralIndirectArguments(indirect, out var indirectArgs, out error))
            return error is not null;

        if (!BuiltInFunctions.TryResolveIndirectRangeReference(indirectArgs, context, out var indirectRange, out var indirectError))
        {
            error = indirectError as ErrorValue;
            return error is not null;
        }

        var range = ToRangeRef(indirectRange);
        if (indirectRange.IsFullColumnRange || indirectRange.IsFullRowRange)
            range = ClampOpenEndedRangeToUsed(range, context);

        if (range.SheetName is not null && !context.SheetExists(range.SheetName))
        {
            error = ErrorValue.Ref;
            return true;
        }

        var values = range.SheetName is not null
            ? context.GetRangeValues(
                range.SheetName,
                range.Start.Row,
                range.Start.ColumnNumber,
                range.End.Row,
                range.End.ColumnNumber)
            : context.GetRangeValues(
                range.Start.Row,
                range.Start.ColumnNumber,
                range.End.Row,
                range.End.ColumnNumber);
        AddRangeValues(expandedArgs, values, preservesReferenceProvenance);
        return true;
    }

    private static RangeRefNode ToRangeRef(BuiltInFunctions.IndirectRangeReference range)
    {
        var startCol = Math.Min(range.StartCol, range.EndCol);
        var endCol = Math.Max(range.StartCol, range.EndCol);
        var start = new CellRefNode(
            FreeX.Core.Model.CellAddress.NumberToColumnName(startCol),
            Math.Min(range.StartRow, range.EndRow),
            SheetName: range.SheetName);
        var end = new CellRefNode(
            FreeX.Core.Model.CellAddress.NumberToColumnName(endCol),
            Math.Max(range.StartRow, range.EndRow),
            SheetName: range.SheetName);
        return new RangeRefNode(start, end, range.SheetName);
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
            "SINGLE"       => EvaluateSingle(node, context),
            "ANCHORARRAY"  => EvaluateAnchorArray(node, context),
            _              => ErrorValue.Value
        };
    }

    private ScalarValue EvaluateSingle(FunctionCallNode node, IEvalContext context)
    {
        if (node.Arguments.Count != 1)
            return ErrorValue.Value;

        var value = EvaluateArrayOperand(node.Arguments[0], context);
        return value is ErrorValue error
            ? error
            : ImplicitIntersectionOp(value, context);
    }

    /// <summary>
    /// ANCHORARRAY(ref) — the spill reference operator (#) — and ANCHORARRAY(ref, end) — the
    /// A1#:B5 shape, a spill range used as the start endpoint of a larger range.
    /// Given a reference to a cell that is a dynamic-array spill anchor, returns the full
    /// spill range as a RangeValue. When a second (end-cell) argument is present, the result is
    /// instead the union of the anchor's spill extent and the end cell — the smallest rectangle
    /// covering both, matching Excel's A1#:B5 semantics. If the anchor argument is not a spill
    /// anchor, returns #REF!. This is an AST-aware function because it needs the address of the
    /// cell, not its value.
    /// </summary>
    private ScalarValue EvaluateAnchorArray(FunctionCallNode node, IEvalContext context)
    {
        if (node.Arguments.Count is not (1 or 2))
            return ErrorValue.Value;

        if (!TryResolveAnchorAddress(node.Arguments[0], context, out var anchorRow, out var anchorCol, out var anchorSheet))
            return ErrorValue.Value;

        // Resolve the sheet containing the anchor.
        FreeX.Core.Model.Sheet? sheet;
        if (anchorSheet is not null)
        {
            sheet = context.CurrentWorkbook?.GetSheet(anchorSheet);
            if (sheet is null) return ErrorValue.Ref;
        }
        else
        {
            sheet = context.CurrentSheet;
            if (sheet is null) return ErrorValue.Ref;
        }

        // Look up the spill extent at the anchor address.
        var anchorAddr = new FreeX.Core.Model.CellAddress(sheet.Id, anchorRow, anchorCol);
        if (!sheet.TryGetSpillExtent(anchorAddr, out uint spillRows, out uint spillCols))
            return ErrorValue.Ref;  // not a spill anchor

        var startRow = anchorRow;
        var startCol = anchorCol;
        var endRow = anchorRow + spillRows - 1;
        var endCol = anchorCol + spillCols - 1;

        if (node.Arguments.Count == 2)
        {
            // A1#:B5 — union the anchor's spill extent with the given end cell so the result is
            // the smallest rectangle covering both, matching Excel. The end cell must be a plain
            // cell reference (no sheet-span or named-range endpoint is meaningful here); it is
            // always parsed unqualified relative to the anchor's own sheet.
            if (node.Arguments[1] is not CellRefNode endRef)
                return ErrorValue.Value;

            startRow = Math.Min(startRow, endRef.Row);
            startCol = Math.Min(startCol, endRef.ColumnNumber);
            endRow = Math.Max(endRow, endRef.Row);
            endCol = Math.Max(endCol, endRef.ColumnNumber);
        }

        var rows = endRow - startRow + 1;
        var cols = endCol - startCol + 1;

        var cells = new ScalarValue[(int)rows, (int)cols];
        for (int r = 0; r < (int)rows; r++)
            for (int c = 0; c < (int)cols; c++)
            {
                var row = startRow + (uint)r;
                var col = startCol + (uint)c;
                cells[r, c] = anchorSheet is not null
                    ? context.GetCellValue(anchorSheet, row, col)
                    : context.GetCellValue(row, col);
            }

        // ANCHORARRAY's result (A1#) is a genuine worksheet reference, not a materialized literal
        // array — SINGLE() and the `@` implicit-intersection operator must positionally intersect
        // it against the formula cell's own row/column (like any other reference), and
        // SUBTOTAL/AGGREGATE's hidden-row/nested-aggregate exclusion must recognize it as a
        // reference too. Every other reference-materializing site in this codebase
        // (BuildRangeValue, OFFSET, INDIRECT) sets IsSheetReference = true for the same reason;
        // omitting it here made SINGLE(A1#) always return the anchor's own top-left value instead
        // of intersecting by position, and made SUBTOTAL/AGGREGATE ignore-hidden/nested-exclusion
        // silently no-op for an A1# argument.
        return new RangeValue(cells, startRow, startCol) { SheetName = anchorSheet, IsSheetReference = true };
    }

    /// <summary>
    /// Resolves an ANCHORARRAY anchor argument (a plain cell reference, or a named range that
    /// itself points at a single cell) to the concrete cell address Excel would treat as the
    /// spill anchor. Returns false for any other shape (multi-cell named range, formula-valued
    /// name, etc.) — ANCHORARRAY only ever accepts a reference to one cell.
    /// </summary>
    private static bool TryResolveAnchorAddress(
        FormulaNode arg, IEvalContext context, out uint row, out uint col, out string? sheetName)
    {
        row = 0;
        col = 0;
        sheetName = null;

        switch (arg)
        {
            case CellRefNode cellRef:
                if (cellRef.SheetName is not null && !context.SheetExists(cellRef.SheetName))
                    return false;
                row = cellRef.Row;
                col = cellRef.ColumnNumber;
                sheetName = cellRef.SheetName;
                return true;

            case NamedRangeNode named:
                // Sheet-scope precedence: a sheet-scoped named FORMULA must shadow a same-named
                // workbook-global named RANGE here too (see ResolveNamedRangeNodeAsReference),
                // so ANCHORARRAY(Foo) anchors on the scoped formula's reference, not the global
                // range's.
                var reference = ResolveNamedRangeNodeAsReference(named, context);
                if (reference is not RangeValue resolved || resolved.RowCount != 1 || resolved.ColCount != 1)
                    return false;
                row = resolved.StartRow;
                col = resolved.StartCol;
                sheetName = resolved.SheetName;
                return true;

            default:
                // ANCHORARRAY only accepts a cell reference or single-cell named range argument.
                return false;
        }
    }

}

/// <summary>
/// R102: a genuinely-omitted-or-equivalent optional ordinal argument marker, now produced two ways:
/// (1) upstream, by the OmittedArgumentNode branch in FormulaEvaluator.Functions.cs's
/// argument-expansion loop, but ONLY for TEXTBEFORE/TEXTAFTER's instance_num (argIndex 2) -- kept
/// there deliberately because instance_num is a MIDDLE optional argument (match_mode/match_end/
/// if_not_found follow it), so a blank instance_num used purely to skip past it and reach one of
/// those later arguments must still mean "use the default", not "coerce to 0 and error"; and (2)
/// SUBSTITUTE's own internal "instance_num was never given at all" marker, substituted purely
/// locally in Substitute() (`args.Count > 3 ? args[3] : Instance`) for a genuinely-omitted 4th
/// argument, telling that apart from a PRESENT argument that merely evaluates to blank (a trailing
/// comma, e.g. SUBSTITUTE(a,b,c,), or a reference to an empty cell -- both of which Excel coerces
/// to numeric 0 instead of "replace all", since instance_num is SUBSTITUTE's only, and therefore
/// last, optional argument -- no later argument a blank could be skipping to reach). This type used
/// to also be produced upstream for FIND/SEARCH's start_num and LEFT/RIGHT's num_chars (plus their
/// FINDB/SEARCHB/LEFTB/RIGHTB byte-count variants) -- each of those is likewise its function's ONLY
/// optional argument, so (like SUBSTITUTE) there is no later argument to reach and Excel's ordinary
/// blank-coerces-to-0 numeric rule applies untouched; producing this sentinel for a mere
/// trailing-comma-empty argument there conflated it with genuine omission and gave the wrong Excel
/// result (e.g. real Excel's LEFT("abc",) is "" -- num_chars coerced to 0 -- not "a", the
/// num_chars-OMITTED default). That part of the upstream branch was removed; a trailing-comma-empty
/// argument for those five functions now evaluates like any other blank-cell reference
/// (BlankValue.Instance) via the generic EvaluateNode(OmittedArgumentNode) fallback, and each
/// function's existing BlankValue-coerces-to-0 domain check already does the right thing with no
/// further change needed.
/// </summary>
internal sealed record OmittedOptionalOrdinalArgumentValue : ScalarValue
{
    public static readonly OmittedOptionalOrdinalArgumentValue Instance = new();
}

/// <summary>
/// Sentinel substituted (by the OmittedArgumentNode branch in FormulaEvaluator.Functions.cs's
/// argument-expansion loop) for TEXTSPLIT's genuinely-omitted col_delimiter/row_delimiter argument
/// slot (e.g. the double-comma in TEXTSPLIT(A1,,";")), so BuiltInFunctions.TextSplit.cs's
/// TryCollectTextDelimiters can tell "the argument slot itself was omitted" (don't split on that
/// axis at all) apart from "an explicit argument evaluates to blank" (e.g. TEXTSPLIT(A1,B1,";")
/// with B1 a blank cell reference, which Excel coerces to an empty-string delimiter and rejects
/// with #VALUE!). Both cases would otherwise evaluate to the same BlankValue.Instance singleton.
/// Mirrors TextSplitOmittedPadArgumentValue and OmittedOptionalOrdinalArgumentValue, which solve
/// the identical problem for TEXTSPLIT's pad_with and TEXTBEFORE/TEXTAFTER's instance_num.
/// </summary>
internal sealed record TextSplitOmittedDelimiterArgumentValue : ScalarValue
{
    public static readonly TextSplitOmittedDelimiterArgumentValue Instance = new();
}
