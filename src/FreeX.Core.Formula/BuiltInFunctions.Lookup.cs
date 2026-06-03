using FreeX.Core.Model;

namespace FreeX.Core.Formula;

public static partial class BuiltInFunctions
{
    // Lookup and reference functions.

    private readonly struct LookupRangeVector
    {
        private readonly RangeValue _range;
        private readonly int _fixedIndex;
        private readonly bool _isRow;

        private LookupRangeVector(RangeValue range, int fixedIndex, bool isRow)
        {
            _range = range;
            _fixedIndex = fixedIndex;
            _isRow = isRow;
        }

        public int Count => _isRow ? _range.ColCount : _range.RowCount;

        public ScalarValue this[int index] =>
            _isRow ? _range.Cells[_fixedIndex, index] : _range.Cells[index, _fixedIndex];

        public static bool TryCreate(RangeValue range, out LookupRangeVector vector)
        {
            if (range.ColCount == 1)
            {
                vector = Column(range, 0);
                return true;
            }

            if (range.RowCount == 1)
            {
                vector = Row(range, 0);
                return true;
            }

            vector = default;
            return false;
        }

        public static LookupRangeVector Row(RangeValue range, int rowIndex) => new(range, rowIndex, isRow: true);

        public static LookupRangeVector Column(RangeValue range, int colIndex) => new(range, colIndex, isRow: false);
    }

    private readonly struct LookupValueVector
    {
        private const byte RangeKind = 1;
        private const byte ListKind = 2;
        private const byte ScalarKind = 3;

        private readonly LookupRangeVector _range;
        private readonly IReadOnlyList<ScalarValue>? _list;
        private readonly ScalarValue? _scalar;
        private readonly byte _kind;

        private LookupValueVector(LookupRangeVector range)
        {
            _range = range;
            _list = null;
            _scalar = null;
            _kind = RangeKind;
        }

        private LookupValueVector(IReadOnlyList<ScalarValue> list)
        {
            _range = default;
            _list = list;
            _scalar = null;
            _kind = ListKind;
        }

        private LookupValueVector(ScalarValue scalar)
        {
            _range = default;
            _list = null;
            _scalar = scalar;
            _kind = ScalarKind;
        }

        public int Count => _kind switch
        {
            RangeKind => _range.Count,
            ListKind => _list!.Count,
            ScalarKind => 1,
            _ => 0
        };

        public ScalarValue this[int index] => _kind switch
        {
            RangeKind => _range[index],
            ListKind => _list![index],
            ScalarKind => index == 0 ? _scalar! : throw new ArgumentOutOfRangeException(nameof(index)),
            _ => throw new InvalidOperationException()
        };

        public static LookupValueVector FromRangeVector(LookupRangeVector range) => new(range);

        public static LookupValueVector FromValue(ScalarValue value)
        {
            if (value is RangeValue range)
                return LookupRangeVector.TryCreate(range, out var vector)
                    ? new LookupValueVector(vector)
                    : new LookupValueVector(range.Flatten());

            return new LookupValueVector(value);
        }
    }

    private static bool IsSimpleSheetQualifier(string sheetName) =>
        sheetName.Length > 0 && sheetName.All(IsSimpleSheetNameChar);

    private static bool IsSimpleSheetNameChar(char ch) =>
        char.IsLetterOrDigit(ch) || ch is '_' or '.';

}

