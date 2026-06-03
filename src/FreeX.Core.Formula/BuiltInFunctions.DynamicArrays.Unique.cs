using FreeX.Core.Model;

namespace FreeX.Core.Formula;

public static partial class BuiltInFunctions
{
    private static ScalarValue Unique(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue arrayError) return arrayError;
        var arr = args[0] is RangeValue arrayRange
            ? arrayRange
            : new RangeValue(new ScalarValue[1, 1] { { args[0] } });
        if (!TryGetScalarControlArgument(args.Count > 1 ? args[1] : BlankValue.Instance, out var byColArg, out var byColError)) return byColError;
        if (!TryGetScalarControlArgument(args.Count > 2 ? args[2] : BlankValue.Instance, out var exactlyOnceArg, out var exactlyOnceError)) return exactlyOnceError;
        bool byCol       = byColArg is not BlankValue && ToBool(byColArg);
        bool exactlyOnce = exactlyOnceArg is not BlankValue && ToBool(exactlyOnceArg);

        if (!byCol)
        {
            if (arr.ColCount == 1)
                return UniqueSingleColumn(arr, exactlyOnce);

            var keyIndex  = new Dictionary<string, int>(arr.RowCount);
            var keyCounts = new List<int>(arr.RowCount);
            var rowOfKey  = new List<int>(arr.RowCount);

            var keySb = new System.Text.StringBuilder();
            for (int r = 0; r < arr.RowCount; r++)
            {
                keySb.Clear();
                for (int c = 0; c < arr.ColCount; c++)
                {
                    if (c > 0) keySb.Append('\0');
                    AppendUniqueKey(keySb, arr.Cells[r, c]);
                }
                var key = keySb.ToString();
                if (keyIndex.TryGetValue(key, out int idx))
                {
                    keyCounts[idx]++;
                }
                else
                {
                    keyIndex[key] = rowOfKey.Count;
                    keyCounts.Add(1);
                    rowOfKey.Add(r);
                }
            }

            int selectedCount = exactlyOnce ? 0 : rowOfKey.Count;
            if (exactlyOnce)
            {
                for (int i = 0; i < keyCounts.Count; i++)
                    if (keyCounts[i] == 1) selectedCount++;
            }

            if (selectedCount == 0) return ErrorValue.Calc;
            var result = new ScalarValue[selectedCount, arr.ColCount];
            for (int i = 0, ri = 0; i < rowOfKey.Count; i++)
            {
                if (exactlyOnce && keyCounts[i] != 1) continue;
                int sourceRow = rowOfKey[i];
                for (int c = 0; c < arr.ColCount; c++)
                    result[ri, c] = arr.Cells[sourceRow, c];
                ri++;
            }
            return new RangeValue(result);
        }
        else
        {
            var keyIndex  = new Dictionary<string, int>(arr.ColCount);
            var keyCounts = new List<int>(arr.ColCount);
            var colOfKey  = new List<int>(arr.ColCount);

            var colKeySb = new System.Text.StringBuilder();
            for (int c = 0; c < arr.ColCount; c++)
            {
                colKeySb.Clear();
                for (int r = 0; r < arr.RowCount; r++)
                {
                    if (r > 0) colKeySb.Append('\0');
                    AppendUniqueKey(colKeySb, arr.Cells[r, c]);
                }
                var key = colKeySb.ToString();
                if (keyIndex.TryGetValue(key, out int idx))
                {
                    keyCounts[idx]++;
                }
                else
                {
                    keyIndex[key] = colOfKey.Count;
                    keyCounts.Add(1);
                    colOfKey.Add(c);
                }
            }

            int selectedCount = exactlyOnce ? 0 : colOfKey.Count;
            if (exactlyOnce)
            {
                for (int i = 0; i < keyCounts.Count; i++)
                    if (keyCounts[i] == 1) selectedCount++;
            }

            if (selectedCount == 0) return ErrorValue.Calc;
            var result = new ScalarValue[arr.RowCount, selectedCount];
            for (int r = 0; r < arr.RowCount; r++)
            {
                for (int i = 0, ci = 0; i < colOfKey.Count; i++)
                {
                    if (exactlyOnce && keyCounts[i] != 1) continue;
                    result[r, ci] = arr.Cells[r, colOfKey[i]];
                    ci++;
                }
            }
            return new RangeValue(result);
        }
    }

    private static ScalarValue UniqueSingleColumn(RangeValue arr, bool exactlyOnce)
    {
        if (!exactlyOnce)
            return UniqueSingleColumnAllOccurrences(arr);

        var keyIndex  = new Dictionary<ScalarValue, int>(arr.RowCount, UniqueScalarComparer.Instance);
        var rowOfKey  = new List<int>(arr.RowCount);
        List<int>? keyCounts = exactlyOnce ? new List<int>(arr.RowCount) : null;

        for (int r = 0; r < arr.RowCount; r++)
        {
            var value = arr.Cells[r, 0];
            if (keyIndex.TryGetValue(value, out int idx))
            {
                if (keyCounts is not null)
                    keyCounts[idx]++;
            }
            else
            {
                keyIndex[value] = rowOfKey.Count;
                rowOfKey.Add(r);
                keyCounts?.Add(1);
            }
        }

        int selectedCount = exactlyOnce ? 0 : rowOfKey.Count;
        if (keyCounts is not null)
        {
            for (int i = 0; i < keyCounts.Count; i++)
                if (keyCounts[i] == 1) selectedCount++;
        }

        if (selectedCount == 0) return ErrorValue.Calc;
        var result = new ScalarValue[selectedCount, 1];
        for (int i = 0, ri = 0; i < rowOfKey.Count; i++)
        {
            if (keyCounts is not null && keyCounts[i] != 1) continue;
            result[ri, 0] = arr.Cells[rowOfKey[i], 0];
            ri++;
        }

        return new RangeValue(result);
    }

    private static ScalarValue UniqueSingleColumnAllOccurrences(RangeValue arr)
    {
        var seen = new HashSet<ScalarValue>(arr.RowCount, UniqueScalarComparer.Instance);
        var result = new ScalarValue[arr.RowCount, 1];
        var selectedCount = 0;

        for (int r = 0; r < arr.RowCount; r++)
        {
            var value = arr.Cells[r, 0];
            if (!seen.Add(value))
                continue;

            result[selectedCount, 0] = value;
            selectedCount++;
        }

        if (selectedCount == 0) return ErrorValue.Calc;
        if (selectedCount == arr.RowCount) return new RangeValue(result);

        var trimmed = new ScalarValue[selectedCount, 1];
        Array.Copy(result, trimmed, selectedCount);
        return new RangeValue(trimmed);
    }

    private sealed class UniqueScalarComparer : IEqualityComparer<ScalarValue>
    {
        internal static readonly UniqueScalarComparer Instance = new();

        private UniqueScalarComparer()
        {
        }

        public bool Equals(ScalarValue? x, ScalarValue? y)
        {
            if (ReferenceEquals(x, y)) return true;
            if (x is null || y is null) return false;

            return x switch
            {
                BlankValue => y is BlankValue,
                NumberValue n => TryGetUniqueNumber(y, out double value) && n.Value.Equals(value),
                DateTimeValue dt => TryGetUniqueNumber(y, out double value) && dt.Value.Equals(value),
                TextValue t => y is TextValue other && string.Equals(t.Value, other.Value, StringComparison.OrdinalIgnoreCase),
                BoolValue b => y is BoolValue other && b.Value == other.Value,
                ErrorValue e => y is ErrorValue other && e.Code == other.Code,
                _ => x.Equals(y)
            };
        }

        public int GetHashCode(ScalarValue obj)
        {
            return obj switch
            {
                BlankValue => HashCode.Combine(nameof(BlankValue)),
                NumberValue n => HashCode.Combine("number", n.Value),
                DateTimeValue dt => HashCode.Combine("number", dt.Value),
                TextValue t => HashCode.Combine("text", StringComparer.OrdinalIgnoreCase.GetHashCode(t.Value)),
                BoolValue b => HashCode.Combine("bool", b.Value),
                ErrorValue e => HashCode.Combine("error", e.Code),
                _ => obj.GetHashCode()
            };
        }

        private static bool TryGetUniqueNumber(ScalarValue value, out double number)
        {
            switch (value)
            {
                case NumberValue n:
                    number = n.Value;
                    return true;
                case DateTimeValue dt:
                    number = dt.Value;
                    return true;
                default:
                    number = 0d;
                    return false;
            }
        }
    }

    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    private static void AppendUniqueKey(System.Text.StringBuilder sb, ScalarValue value)
    {
        switch (value)
        {
            case BlankValue:
                sb.Append("blank");
                break;
            case NumberValue n:
                sb.Append("number:").Append(n.Value.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
                break;
            case DateTimeValue dt:
                sb.Append("number:").Append(dt.Value.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
                break;
            case TextValue t:
                sb.Append("text:").Append(t.Value.ToUpperInvariant());
                break;
            case BoolValue b:
                sb.Append("bool:").Append(b.Value ? '1' : '0');
                break;
            case ErrorValue e:
                sb.Append("error:").Append(e.Code);
                break;
            default:
                sb.Append("other:").Append(ToText(value));
                break;
        }
    }
}

