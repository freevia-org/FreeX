using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;
using Xunit;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// Round-13 bucket S16 regression coverage.
///
/// R13-formula-array-cse-2: FREQUENCY hand-rolled its own `is NumberValue` filter instead of
/// using the shared TryCellNumber coercion helper, so DateTimeValue cells (dates loaded from
/// XLSX, or produced by DATE()/date literals) were silently dropped from both the data array
/// and the bins array — producing an all-zero histogram for date data instead of Excel's true
/// per-bin counts.
/// </summary>
public class FreeXR13S16Tests
{
    private readonly FormulaEvaluator _eval = new();

    [Fact]
    public void Frequency_DateTypedDataAndBins_CountsLikeExcel_InsteadOfAllZero()
    {
        var wb = new Workbook();
        var sheet = wb.AddSheet("S");

        // A1:A5 — five dates spread across three months.
        DateTime[] data =
        [
            new DateTime(2024, 1, 1),
            new DateTime(2024, 1, 15),
            new DateTime(2024, 2, 1),
            new DateTime(2024, 2, 15),
            new DateTime(2024, 3, 1),
        ];
        for (int i = 0; i < data.Length; i++)
            sheet.SetCell(new CellAddress(sheet.Id, (uint)(i + 1), 1), DateTimeValue.FromDateTime(data[i]));

        // B1:B2 — bin thresholds: end of January, end of February.
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), DateTimeValue.FromDateTime(new DateTime(2024, 1, 31)));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), DateTimeValue.FromDateTime(new DateTime(2024, 2, 28)));

        var result = _eval.Evaluate("=FREQUENCY(A1:A5,B1:B2)", sheet, wb);

        result.Should().BeOfType<RangeValue>();
        var rv = (RangeValue)result;
        rv.RowCount.Should().Be(3);
        rv.ColCount.Should().Be(1);

        // Excel: bucket1 (<=Jan31) = Jan1,Jan15 -> 2; bucket2 (<=Feb28) = Feb1,Feb15 -> 2;
        // bucket3 (>Feb28) = Mar1 -> 1. Pre-fix, every date was dropped from both lists,
        // yielding an empty bins list collapsed to a single all-zero bucket.
        ((NumberValue)rv.At(1, 1)).Value.Should().Be(2);
        ((NumberValue)rv.At(2, 1)).Value.Should().Be(2);
        ((NumberValue)rv.At(3, 1)).Value.Should().Be(1);
    }
}
