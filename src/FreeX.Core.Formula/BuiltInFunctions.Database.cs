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
    private static bool DbRowMatchesCriteriaRow(RangeValue database, int dataRow, RangeValue criteria, int critRow)
    {
        for (int cc = 0; cc < criteria.ColCount; cc++)
        {
            var critHeader = criteria.Cells[0, cc];
            if (critHeader is BlankValue) continue;

            var critCell = criteria.Cells[critRow, cc];
            if (critCell is BlankValue) continue;
            if (critCell is TextValue tv && tv.Value.Length == 0) continue;

            var headerText = ToText(critHeader);
            int dbCol = FindDbHeaderCol(database, headerText);
            if (dbCol < 0) return false;

            var cellValue = database.Cells[dataRow, dbCol];
            if (!MatchesCriteria(cellValue, critCell)) return false;
        }
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
        RangeValue database, ScalarValue fieldArg, RangeValue criteria)
    {
        if (database.RowCount < 2) return (new List<ScalarValue>(), null, 0);

        int? fieldCol = ResolveDatabaseField(database, fieldArg);
        if (fieldCol is null) return (new List<ScalarValue>(), ErrorValue.Value, 0);

        var matches = new List<ScalarValue>();
        ErrorValue? firstError = null;
        int matchCount = 0;
        for (int r = 1; r < database.RowCount; r++)
        {
            bool rowMatches = false;
            // OR across criteria rows
            for (int cr = 1; cr < criteria.RowCount; cr++)
            {
                if (DbRowMatchesCriteriaRow(database, r, criteria, cr))
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
        RangeValue database, ScalarValue fieldArg, RangeValue criteria)
    {
        var (matches, err, _) = DatabaseExtract(database, fieldArg, criteria);
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
        Func<List<double>, ScalarValue> aggregate)
    {
        if (!TryDbArgs(args, out var db, out var f, out var cr, out var err)) return err!;
        var (nums, e) = DatabaseExtractNumeric(db, f, cr);
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
        => EvaluateDatabaseNumericAggregate(args, nums => NumberResult(nums.Sum()));

    private static ScalarValue DAverage(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
        => EvaluateDatabaseNumericAggregate(args, nums =>
        {
            if (nums.Count == 0) return ErrorValue.DivByZero;
            return NumberResult(nums.Average());
        });

    private static ScalarValue DCount(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (!TryDbArgs(args, out var db, out var f, out var cr, out var err)) return err!;
        // Mirrors plain COUNT: ignore an error in a matched field cell rather than
        // propagating it -- only numeric matches are counted.
        var (matches, _, _) = DatabaseExtract(db, f, cr);
        int count = 0;
        foreach (var v in matches)
            if (TryCellNumber(v, out _)) count++;
        return new NumberValue(count);
    }

    private static ScalarValue DCountA(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (!TryDbArgs(args, out var db, out var f, out var cr, out var err)) return err!;
        // Mirrors plain COUNTA: an error in a matched field cell still counts as a
        // non-blank present value rather than being propagated.
        var (matches, _, _) = DatabaseExtract(db, f, cr);
        int count = 0;
        foreach (var v in matches)
            if (v is not BlankValue && !(v is TextValue tv && tv.Value.Length == 0)) count++;
        return new NumberValue(count);
    }

    private static ScalarValue DGet(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (!TryDbArgs(args, out var db, out var f, out var cr, out var err)) return err!;
        var (matches, e, matchCount) = DatabaseExtract(db, f, cr);
        // Excel's documented "more than one record satisfies the criteria" #NUM! rule takes
        // priority over a matched row's field error — check the total match count first.
        if (matchCount > 1) return ErrorValue.Num;
        if (e is not null) return e;
        if (matches.Count == 0) return ErrorValue.Value;
        if (matches.Count > 1) return ErrorValue.Num;
        return matches[0];
    }

    private static ScalarValue DMax(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
        => EvaluateDatabaseNumericAggregate(args, nums =>
        {
            if (nums.Count == 0) return NumberResult(0);
            return NumberResult(nums.Max());
        });

    private static ScalarValue DMin(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
        => EvaluateDatabaseNumericAggregate(args, nums =>
        {
            if (nums.Count == 0) return NumberResult(0);
            return NumberResult(nums.Min());
        });

    private static ScalarValue DProduct(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
        => EvaluateDatabaseNumericAggregate(args, nums =>
        {
            if (nums.Count == 0) return NumberResult(0);
            double prod = 1;
            foreach (var x in nums) prod *= x;
            return NumberResult(prod);
        });

    private static ScalarValue DStdev(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
        => EvaluateDatabaseNumericAggregate(args, nums => DatabaseStdDev(nums, sample: true));

    private static ScalarValue DStdevP(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
        => EvaluateDatabaseNumericAggregate(args, nums => DatabaseStdDev(nums, sample: false));

    private static ScalarValue DVar(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
        => EvaluateDatabaseNumericAggregate(args, nums => DatabaseVariance(nums, sample: true));

    private static ScalarValue DVarP(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
        => EvaluateDatabaseNumericAggregate(args, nums => DatabaseVariance(nums, sample: false));

}
