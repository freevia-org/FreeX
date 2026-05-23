using Freexcel.Core.Model;

namespace Freexcel.Core.Formula;

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
                if (header is TextValue or DirectTextLiteralValue)
                {
                    if (string.Equals(ToText(header), name, StringComparison.OrdinalIgnoreCase))
                        return c;
                }
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
    private static (List<ScalarValue> Matches, ErrorValue? Error) DatabaseExtract(
        RangeValue database, ScalarValue fieldArg, RangeValue criteria)
    {
        if (database.RowCount < 2) return (new List<ScalarValue>(), null);

        int? fieldCol = ResolveDatabaseField(database, fieldArg);
        if (fieldCol is null) return (new List<ScalarValue>(), ErrorValue.Value);

        var matches = new List<ScalarValue>();
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
                var cell = database.Cells[r, fieldCol.Value];
                if (cell is ErrorValue ev) return (matches, ev);
                matches.Add(cell);
            }
        }
        return (matches, null);
    }

    private static (List<double> Nums, ErrorValue? Error) DatabaseExtractNumeric(
        RangeValue database, ScalarValue fieldArg, RangeValue criteria)
    {
        var (matches, err) = DatabaseExtract(database, fieldArg, criteria);
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

    private static ScalarValue DSum(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (!TryDbArgs(args, out var db, out var f, out var cr, out var err)) return err!;
        var (nums, e) = DatabaseExtractNumeric(db, f, cr);
        if (e is not null) return e;
        return NumberResult(nums.Sum());
    }

    private static ScalarValue DAverage(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (!TryDbArgs(args, out var db, out var f, out var cr, out var err)) return err!;
        var (nums, e) = DatabaseExtractNumeric(db, f, cr);
        if (e is not null) return e;
        if (nums.Count == 0) return ErrorValue.DivByZero;
        return NumberResult(nums.Average());
    }

    private static ScalarValue DCount(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (!TryDbArgs(args, out var db, out var f, out var cr, out var err)) return err!;
        var (nums, e) = DatabaseExtractNumeric(db, f, cr);
        if (e is not null) return e;
        return new NumberValue(nums.Count);
    }

    private static ScalarValue DCountA(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (!TryDbArgs(args, out var db, out var f, out var cr, out var err)) return err!;
        var (matches, e) = DatabaseExtract(db, f, cr);
        if (e is not null) return e;
        int count = 0;
        foreach (var v in matches)
            if (v is not BlankValue && !(v is TextValue tv && tv.Value.Length == 0)) count++;
        return new NumberValue(count);
    }

    private static ScalarValue DGet(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (!TryDbArgs(args, out var db, out var f, out var cr, out var err)) return err!;
        var (matches, e) = DatabaseExtract(db, f, cr);
        if (e is not null) return e;
        if (matches.Count == 0) return ErrorValue.Value;
        if (matches.Count > 1) return ErrorValue.Num;
        return matches[0];
    }

    private static ScalarValue DMax(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (!TryDbArgs(args, out var db, out var f, out var cr, out var err)) return err!;
        var (nums, e) = DatabaseExtractNumeric(db, f, cr);
        if (e is not null) return e;
        if (nums.Count == 0) return ErrorValue.Num;
        return NumberResult(nums.Max());
    }

    private static ScalarValue DMin(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (!TryDbArgs(args, out var db, out var f, out var cr, out var err)) return err!;
        var (nums, e) = DatabaseExtractNumeric(db, f, cr);
        if (e is not null) return e;
        if (nums.Count == 0) return ErrorValue.Num;
        return NumberResult(nums.Min());
    }

    private static ScalarValue DProduct(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (!TryDbArgs(args, out var db, out var f, out var cr, out var err)) return err!;
        var (nums, e) = DatabaseExtractNumeric(db, f, cr);
        if (e is not null) return e;
        if (nums.Count == 0) return new NumberValue(1);
        double prod = 1;
        foreach (var x in nums) prod *= x;
        return NumberResult(prod);
    }

    private static ScalarValue DStdev(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (!TryDbArgs(args, out var db, out var f, out var cr, out var err)) return err!;
        var (nums, e) = DatabaseExtractNumeric(db, f, cr);
        if (e is not null) return e;
        if (nums.Count < 2) return ErrorValue.DivByZero;
        double mean = nums.Average();
        double s = nums.Sum(x => (x - mean) * (x - mean)) / (nums.Count - 1);
        return NumberResult(Math.Sqrt(s));
    }

    private static ScalarValue DStdevP(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (!TryDbArgs(args, out var db, out var f, out var cr, out var err)) return err!;
        var (nums, e) = DatabaseExtractNumeric(db, f, cr);
        if (e is not null) return e;
        if (nums.Count == 0) return ErrorValue.DivByZero;
        double mean = nums.Average();
        double s = nums.Sum(x => (x - mean) * (x - mean)) / nums.Count;
        return NumberResult(Math.Sqrt(s));
    }

    private static ScalarValue DVar(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (!TryDbArgs(args, out var db, out var f, out var cr, out var err)) return err!;
        var (nums, e) = DatabaseExtractNumeric(db, f, cr);
        if (e is not null) return e;
        if (nums.Count < 2) return ErrorValue.DivByZero;
        double mean = nums.Average();
        double s = nums.Sum(x => (x - mean) * (x - mean)) / (nums.Count - 1);
        return NumberResult(s);
    }

    private static ScalarValue DVarP(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (!TryDbArgs(args, out var db, out var f, out var cr, out var err)) return err!;
        var (nums, e) = DatabaseExtractNumeric(db, f, cr);
        if (e is not null) return e;
        if (nums.Count == 0) return ErrorValue.DivByZero;
        double mean = nums.Average();
        double s = nums.Sum(x => (x - mean) * (x - mean)) / nums.Count;
        return NumberResult(s);
    }

}
