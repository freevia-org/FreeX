using FreeX.Core.Model;

namespace FreeX.Core.Formula;

public static partial class BuiltInFunctions
{
    // ════════════════════════════════════════════════════════════════════════
    // Phase A1 – Database functions
    // DSUM, DAVERAGE, DCOUNT, DCOUNTA, DGET, DMAX, DMIN, DPRODUCT, DSTDEV, DSTDEVP, DVAR, DVARP
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>Resolve field arg to 0-based column index in database (or null if not found).</summary>
    private static int? ResolveDatabaseField(RangeValue database, ScalarValue field)
    {
        if (TryCellNumber(field, out double colIdx))
        {
            int idx = (int)colIdx;
            if (idx < 1 || idx > database.ColCount) return null;
            return idx - 1;
        }
        if (field is TextValue or DirectTextLiteralValue)
        {
            var name = ToText(field);
            for (int c = 0; c < database.ColCount; c++)
            {
                var header = database.Cells[0, c];
                if (string.Equals(ToText(header), name, StringComparison.OrdinalIgnoreCase))
                    return c;
            }
        }
        return null;
    }

    /// <summary>Find database column index matching the given header text (case-insensitive).</summary>
    private static int FindDbHeaderCol(RangeValue database, string headerText)
    {
        for (int c = 0; c < database.ColCount; c++)
        {
            var h = database.Cells[0, c];
            string hText = h is TextValue or DirectTextLiteralValue ? ToText(h) : ToText(h);
            if (string.Equals(hText, headerText, StringComparison.OrdinalIgnoreCase))
                return c;
        }
        return -1;
    }

    /// <summary>Returns true if a single data row matches a single criteria row (AND across columns).</summary>
    private static bool DbRowMatchesCriteriaRow(RangeValue database, int dataRow, RangeValue criteria, int critRow, IEvalContext ctx)
    {
        for (int cc = 0; cc < criteria.ColCount; cc++)
        {
            var critHeader = criteria.Cells[0, cc];
            bool blankHeader = critHeader is BlankValue;
            int dbCol = blankHeader ? -1 : FindDbHeaderCol(database, ToText(critHeader));

            if (blankHeader || dbCol < 0)
            {
                // Excel's documented "computed criteria" convention: a criteria column whose
                // header is blank, or is a label that doesn't match any database column name,
                // has no field of its own to compare against. Its criteria cell instead holds a
                // formula that is re-evaluated per candidate database row (relative references
                // shifted to that row) and the row matches only when the formula is truthy. A
                // criteria cell that isn't itself a live formula contributes no condition and is
                // ignored, mirroring the "blank criterion under a mapped header" convention below.
                if (!TryEvaluateComputedCriterion(criteria, critRow, cc, database, dataRow, ctx, out bool computedMatch))
                    continue;
                if (!computedMatch) return false;
                continue;
            }

            var critCell = criteria.Cells[critRow, cc];
            if (critCell is BlankValue) continue;
            if (critCell is TextValue tv && tv.Value.Length == 0) continue;

            var cellValue = database.Cells[dataRow, dbCol];
            // Excel's database/Advanced-Filter criteria treat a bare (non-wildcard, non-numeric,
            // non-operator) text criterion as a "begins with" match (e.g. "Dav" matches "Davolio"),
            // unlike COUNTIF/SUMIF's plain-text criteria which require exact equality.
            if (!MatchesCriteria(cellValue, critCell, textPrefixMatch: true)) return false;
        }
        return true;
    }

    /// <summary>
    /// Evaluates a computed (blank/non-column-header) database criteria cell against a candidate
    /// database row, mirroring Advanced Filter's <c>ComputedCriteriaCheck</c>: the formula authored
    /// at the criteria cell is re-evaluated with its relative references shifted to the candidate
    /// row, exactly as if it had been entered there. Returns false (nothing to evaluate) when the
    /// criteria/database ranges aren't backed by real worksheet cells (e.g. synthesized arrays) or
    /// the criteria cell holds no formula — Excel's computed criteria only exists as an authored
    /// formula in a real cell, so there is no condition to apply otherwise.
    /// </summary>
    private static bool TryEvaluateComputedCriterion(
        RangeValue criteria, int critRow, int cc,
        RangeValue database, int dataRow,
        IEvalContext ctx, out bool matches)
    {
        matches = false;
        if (!criteria.IsSheetReference || !database.IsSheetReference) return false;

        var sheet = criteria.SheetName is { } sheetName
            ? ctx.CurrentWorkbook?.GetSheet(sheetName)
            : ctx.CurrentSheet;
        if (sheet is null) return false;

        uint formulaRow = criteria.StartRow + (uint)critRow;
        uint formulaCol = criteria.StartCol + (uint)cc;
        var cell = sheet.GetCell(formulaRow, formulaCol);
        if (cell?.FormulaText is not { Length: > 0 } formulaText) return false;

        uint targetRow = database.StartRow + (uint)dataRow;
        // Excel's documented "computed criteria" convention anchors the shift on the database's
        // own first data row (database.StartRow + 1), NOT on the criteria formula's own physical
        // row: the authored formula is expected to reference that first data row directly (e.g.
        // "=B6>200" when the list's first data row is row 6), and every other candidate row is
        // evaluated by shifting relative references by (targetRow - firstDataRow) -- independent
        // of where the criteria cell itself sits in its (usually disjoint) criteria region.
        // Shared with AdvancedFilterPlanBuilder.ComputedCriteriaCheck via
        // ComputedCriteriaEvaluator so the two can't drift apart again. A computed criterion
        // that errors is a real (non-matching) evaluation, not an "ignore this column" case --
        // ComputedCriteriaEvaluator.Evaluate already treats it that way.
        uint firstDataRow = database.StartRow + 1;
        matches = ComputedCriteriaEvaluator.Evaluate(sheet, formulaText, firstDataRow, formulaCol, targetRow, ctx.CurrentWorkbook);
        return true;
    }

    /// <summary>Extract values from the field column for all matching rows.</summary>
    /// <remarks>
    /// <paramref name="matchCount"/> is the total number of database rows satisfying the
    /// criteria, independent of whether the matched row's field cell is itself an error —
    /// DGET needs this to apply Excel's "more than one record matches" #NUM! rule even when
    /// a matching row's field value errors before every match has been scanned.
    /// </remarks>
    private static (List<ScalarValue> Matches, ErrorValue? Error, int MatchCount) DatabaseExtract(
        RangeValue database, ScalarValue fieldArg, RangeValue criteria, IEvalContext ctx)
    {
        // Resolve the field argument before checking for data rows: an unresolvable field
        // name/index is a #VALUE! error even when the database has no data rows to scan
        // (matches DCOUNT/DCOUNTA's explicit ResolveDatabaseField check, which runs
        // unconditional on RowCount).
        int? fieldCol = ResolveDatabaseField(database, fieldArg);
        if (fieldCol is null) return (new List<ScalarValue>(), ErrorValue.Value, 0);

        if (database.RowCount < 2) return (new List<ScalarValue>(), null, 0);

        var matches = new List<ScalarValue>();
        ErrorValue? firstError = null;
        int matchCount = 0;
        for (int r = 1; r < database.RowCount; r++)
        {
            bool rowMatches = false;
            // OR across criteria rows
            for (int cr = 1; cr < criteria.RowCount; cr++)
            {
                if (DbRowMatchesCriteriaRow(database, r, criteria, cr, ctx))
                {
                    rowMatches = true;
                    break;
                }
            }
            if (rowMatches)
            {
                matchCount++;
                var cell = database.Cells[r, fieldCol.Value];
                if (cell is ErrorValue ev)
                    firstError ??= ev;
                matches.Add(cell);
            }
        }
        return (matches, firstError, matchCount);
    }

    private static (List<double> Nums, ErrorValue? Error) DatabaseExtractNumeric(
        RangeValue database, ScalarValue fieldArg, RangeValue criteria, IEvalContext ctx)
    {
        var (matches, err, _) = DatabaseExtract(database, fieldArg, criteria, ctx);
        if (err is not null) return (new List<double>(), err);
        var nums = new List<double>();
        foreach (var v in matches)
            if (TryCellNumber(v, out double d)) nums.Add(d);
        return (nums, null);
    }

    private static bool TryDbArgs(
        IReadOnlyList<ScalarValue> args,
        out RangeValue database,
        out ScalarValue field,
        out RangeValue criteria,
        out ScalarValue? error)
    {
        database = null!;
        field = null!;
        criteria = null!;
        error = null;
        if (args[0] is ErrorValue e0) { error = e0; return false; }
        if (args[1] is ErrorValue e1) { error = e1; return false; }
        if (args[2] is ErrorValue e2) { error = e2; return false; }
        if (args[0] is not RangeValue db) { error = ErrorValue.Value; return false; }
        if (args[2] is not RangeValue cr) { error = ErrorValue.Value; return false; }
        database = db;
        field = args[1];
        criteria = cr;
        return true;
    }

    private static ScalarValue EvaluateDatabaseNumericAggregate(
        IReadOnlyList<ScalarValue> args,
        IEvalContext ctx,
        Func<List<double>, ScalarValue> aggregate)
    {
        if (!TryDbArgs(args, out var db, out var f, out var cr, out var err)) return err!;
        var (nums, e) = DatabaseExtractNumeric(db, f, cr, ctx);
        if (e is not null) return e;
        return aggregate(nums);
    }

    private static bool TryDatabaseVariance(List<double> nums, bool sample, out double variance)
    {
        variance = 0;
        if (nums.Count < (sample ? 2 : 1)) return false;

        double mean = nums.Average();
        double sumSquares = nums.Sum(x => (x - mean) * (x - mean));
        variance = sumSquares / (sample ? nums.Count - 1 : nums.Count);
        return true;
    }

    private static ScalarValue DatabaseVariance(List<double> nums, bool sample)
        => TryDatabaseVariance(nums, sample, out double variance)
            ? NumberResult(variance)
            : ErrorValue.DivByZero;

    private static ScalarValue DatabaseStdDev(List<double> nums, bool sample)
        => TryDatabaseVariance(nums, sample, out double variance)
            ? NumberResult(Math.Sqrt(variance))
            : ErrorValue.DivByZero;

    private static ScalarValue DSum(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
        => EvaluateDatabaseNumericAggregate(args, ctx, nums => NumberResult(nums.Sum()));

    private static ScalarValue DAverage(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
        => EvaluateDatabaseNumericAggregate(args, ctx, nums =>
        {
            if (nums.Count == 0) return ErrorValue.DivByZero;
            return NumberResult(nums.Average());
        });

    /// <summary>
    /// Validates the 2-arg field-omitted form DCOUNT/DCOUNTA also accept: Excel documents that
    /// when the field argument is omitted, the function counts ALL records matching criteria,
    /// independent of any particular field's numeric/non-blank content.
    /// </summary>
    private static bool TryDbArgsFieldOmitted(
        IReadOnlyList<ScalarValue> args,
        out RangeValue database,
        out RangeValue criteria,
        out ScalarValue? error)
    {
        database = null!;
        criteria = null!;
        error = null;
        if (args[0] is ErrorValue e0) { error = e0; return false; }
        if (args[1] is ErrorValue e1) { error = e1; return false; }
        if (args[0] is not RangeValue db) { error = ErrorValue.Value; return false; }
        if (args[1] is not RangeValue cr) { error = ErrorValue.Value; return false; }
        database = db;
        criteria = cr;
        return true;
    }

    /// <summary>Counts database rows (excluding the header) that match at least one criteria row.</summary>
    private static int CountMatchingDatabaseRows(RangeValue database, RangeValue criteria, IEvalContext ctx)
    {
        if (database.RowCount < 2) return 0;
        int count = 0;
        for (int r = 1; r < database.RowCount; r++)
        {
            for (int cr = 1; cr < criteria.RowCount; cr++)
            {
                if (DbRowMatchesCriteriaRow(database, r, criteria, cr, ctx))
                {
                    count++;
                    break;
                }
            }
        }
        return count;
    }

    private static ScalarValue DCount(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        // Field omitted: =DCOUNT(database,criteria) counts every matching record, regardless
        // of whether any field in it is numeric.
        if (args.Count == 2)
        {
            if (!TryDbArgsFieldOmitted(args, out var db0, out var cr0, out var err0)) return err0!;
            return new NumberValue(CountMatchingDatabaseRows(db0, cr0, ctx));
        }
        if (!TryDbArgs(args, out var db, out var f, out var cr, out var err)) return err!;
        // A field that doesn't resolve to a database column is a #VALUE! error, matching
        // every other D-function (DSUM/DAVERAGE/etc. via DatabaseExtractNumeric). This must
        // be checked explicitly because, unlike those, DCount/DCountA below deliberately
        // ignore DatabaseExtract's per-matched-cell error (mirrors plain COUNT: ignore an
        // error in a matched field cell rather than propagating it -- only numeric matches
        // are counted) -- so the field-resolution failure can't be told apart from "no
        // matches" just by looking at DatabaseExtract's returned Error/matches.
        if (ResolveDatabaseField(db, f) is null) return ErrorValue.Value;
        var (matches, _, _) = DatabaseExtract(db, f, cr, ctx);
        int count = 0;
        foreach (var v in matches)
            if (TryCellNumber(v, out _)) count++;
        return new NumberValue(count);
    }

    private static ScalarValue DCountA(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        // Field omitted: =DCOUNTA(database,criteria) counts every matching record, same as
        // DCOUNT's field-omitted form (see CountMatchingDatabaseRows).
        if (args.Count == 2)
        {
            if (!TryDbArgsFieldOmitted(args, out var db0, out var cr0, out var err0)) return err0!;
            return new NumberValue(CountMatchingDatabaseRows(db0, cr0, ctx));
        }
        if (!TryDbArgs(args, out var db, out var f, out var cr, out var err)) return err!;
        // See DCount: an unresolvable field is #VALUE!, matching every sibling D-function.
        if (ResolveDatabaseField(db, f) is null) return ErrorValue.Value;
        // Mirrors plain COUNTA: an error in a matched field cell still counts as a
        // non-blank present value rather than being propagated.
        var (matches, _, _) = DatabaseExtract(db, f, cr, ctx);
        int count = 0;
        foreach (var v in matches)
            if (v is not BlankValue && !(v is TextValue tv && tv.Value.Length == 0)) count++;
        return new NumberValue(count);
    }

    private static ScalarValue DGet(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (!TryDbArgs(args, out var db, out var f, out var cr, out var err)) return err!;
        var (matches, e, matchCount) = DatabaseExtract(db, f, cr, ctx);
        // Excel's documented "more than one record satisfies the criteria" #NUM! rule takes
        // priority over a matched row's field error — check the total match count first.
        if (matchCount > 1) return ErrorValue.Num;
        if (e is not null) return e;
        if (matches.Count == 0) return ErrorValue.Value;
        if (matches.Count > 1) return ErrorValue.Num;
        return matches[0];
    }

    private static ScalarValue DMax(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
        => EvaluateDatabaseNumericAggregate(args, ctx, nums =>
        {
            if (nums.Count == 0) return NumberResult(0);
            return NumberResult(nums.Max());
        });

    private static ScalarValue DMin(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
        => EvaluateDatabaseNumericAggregate(args, ctx, nums =>
        {
            if (nums.Count == 0) return NumberResult(0);
            return NumberResult(nums.Min());
        });

    private static ScalarValue DProduct(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
        => EvaluateDatabaseNumericAggregate(args, ctx, nums =>
        {
            if (nums.Count == 0) return NumberResult(0);
            double prod = 1;
            foreach (var x in nums) prod *= x;
            return NumberResult(prod);
        });

    private static ScalarValue DStdev(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
        => EvaluateDatabaseNumericAggregate(args, ctx, nums => DatabaseStdDev(nums, sample: true));

    private static ScalarValue DStdevP(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
        => EvaluateDatabaseNumericAggregate(args, ctx, nums => DatabaseStdDev(nums, sample: false));

    private static ScalarValue DVar(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
        => EvaluateDatabaseNumericAggregate(args, ctx, nums => DatabaseVariance(nums, sample: true));

    private static ScalarValue DVarP(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
        => EvaluateDatabaseNumericAggregate(args, ctx, nums => DatabaseVariance(nums, sample: false));

}
