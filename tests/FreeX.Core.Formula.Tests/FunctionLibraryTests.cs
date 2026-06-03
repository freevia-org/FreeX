using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// Tests for the expanded Phase 4.2 function library.
/// Covers IFERROR, IFNA, VLOOKUP, HLOOKUP, INDEX, MATCH,
/// SUMIF, COUNTIF, AVERAGEIF, TEXT, TRIM, UPPER, LOWER, PROPER,
/// SUBSTITUTE, FIND, SEARCH, MID, REPT, VALUE,
/// DATE, YEAR, MONTH, DAY, HOUR, MINUTE, SECOND, WEEKDAY, EDATE, DATEDIF,
/// MOD, POWER, SQRT, INT, CEILING, FLOOR, RANDBETWEEN, SIGN, LOG, LN, EXP, PI, FACT,
/// LARGE, SMALL, RANK, STDEV, MEDIAN.
/// </summary>
public partial class FunctionLibraryTests
{
    private readonly FormulaEvaluator _eval = new();

    private sealed class CultureScope : IDisposable
    {
        private readonly System.Globalization.CultureInfo _originalCulture = System.Globalization.CultureInfo.CurrentCulture;
        private readonly System.Globalization.CultureInfo _originalUiCulture = System.Globalization.CultureInfo.CurrentUICulture;

        public CultureScope(string cultureName)
        {
            var culture = System.Globalization.CultureInfo.GetCultureInfo(cultureName);
            System.Globalization.CultureInfo.CurrentCulture = culture;
            System.Globalization.CultureInfo.CurrentUICulture = culture;
        }

        public void Dispose()
        {
            System.Globalization.CultureInfo.CurrentCulture = _originalCulture;
            System.Globalization.CultureInfo.CurrentUICulture = _originalUiCulture;
        }
    }

    private static Sheet MakeSheet(params (int row, int col, ScalarValue val)[] cells)
    {
        var sheet = new Sheet(SheetId.New(), "S");
        foreach (var (r, c, v) in cells)
            sheet.SetCell(new CellAddress(sheet.Id, (uint)r, (uint)c), v);
        return sheet;
    }

    private static void AssertTextColumn(ScalarValue value, params string[] expected)
    {
        var range = value.Should().BeOfType<RangeValue>().Subject;
        range.RowCount.Should().Be(expected.Length);
        range.ColCount.Should().Be(1);
        for (int row = 0; row < expected.Length; row++)
            range.At(row + 1, 1).Should().Be(new TextValue(expected[row]));
    }

    private static void AssertColumn(ScalarValue value, params ScalarValue[] expected)
    {
        var range = value.Should().BeOfType<RangeValue>().Subject;
        range.RowCount.Should().Be(expected.Length);
        range.ColCount.Should().Be(1);
        for (int row = 0; row < expected.Length; row++)
            range.At(row + 1, 1).Should().Be(expected[row]);
    }

    private static void AssertApproxColumn(ScalarValue value, params double[] expected)
    {
        var range = value.Should().BeOfType<RangeValue>().Subject;
        range.RowCount.Should().Be(expected.Length);
        range.ColCount.Should().Be(1);
        for (int row = 0; row < expected.Length; row++)
            ((NumberValue)range.At(row + 1, 1)).Value.Should().BeApproximately(expected[row], 1e-10);
    }

    private static BoolValue True() => new(true);

    private static BoolValue False() => new(false);
}
