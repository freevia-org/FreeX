using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// R21-information-functions-3 / R21-crosssheet-3d-refs-3: SHEETS() given a 3-D sheet-span
/// reference (e.g. Sheet1:Sheet3!A1) must return the number of sheets spanned (3), not hard-code
/// 1 for every RangeValue reference. The formula-parser/evaluator pipeline's handling of the
/// literal 3-D-span syntax is owned by a different fix (it currently short-circuits any such
/// argument to #VALUE! before SheetsFunc ever runs), so this test exercises SheetsFunc directly
/// via the public BuiltInFunctions registry with a RangeValue whose SheetName encodes a
/// "Start:End" span -- the representation SheetsFunc must now handle correctly.
/// </summary>
public sealed class R21_Sheets3DSpanCountTests
{
    [Fact]
    public void Sheets_CountsSheetsSpannedByRangeValueSpan()
    {
        var workbook = new Workbook("Test");
        workbook.AddSheet("Sheet1");
        workbook.AddSheet("Sheet2");
        workbook.AddSheet("Sheet3");

        BuiltInFunctions.TryGet("SHEETS", out var entry).Should().BeTrue();

        var spanRange = new RangeValue(new ScalarValue[,] { { new NumberValue(1) } })
        {
            SheetName = "Sheet1:Sheet3"
        };
        var ctx = new SpanOnlyEvalContext(workbook);

        entry.Func([spanRange], ctx).Should().Be(new NumberValue(3));
    }

    [Fact]
    public void Sheets_ReturnsOneForOrdinarySingleSheetRangeValue()
    {
        var workbook = new Workbook("Test");
        workbook.AddSheet("Sheet1");
        workbook.AddSheet("Sheet2");

        BuiltInFunctions.TryGet("SHEETS", out var entry).Should().BeTrue();

        var singleSheetRange = new RangeValue(new ScalarValue[,] { { new NumberValue(1) } })
        {
            SheetName = "Sheet2"
        };
        var ctx = new SpanOnlyEvalContext(workbook);

        entry.Func([singleSheetRange], ctx).Should().Be(new NumberValue(1));
    }

    /// <summary>Minimal IEvalContext test double: SheetsFunc's RangeValue path only reads CurrentWorkbook.</summary>
    private sealed class SpanOnlyEvalContext(Workbook workbook) : IEvalContext
    {
        public Workbook? CurrentWorkbook => workbook;
        public Sheet? CurrentSheet => null;

        public ScalarValue GetCellValue(uint row, uint col) => throw new NotSupportedException();
        public ScalarValue GetCellValue(string sheetName, uint row, uint col) => throw new NotSupportedException();
        public IReadOnlyList<ScalarValue> GetRangeValues(uint startRow, uint startCol, uint endRow, uint endCol) => throw new NotSupportedException();
        public IReadOnlyList<ScalarValue> GetRangeValues(string sheetName, uint startRow, uint startCol, uint endRow, uint endCol) => throw new NotSupportedException();
        public GridRange? TryResolveNamedRange(string name) => null;
        public string? TryGetSheetName(SheetId sheetId) => null;
        public bool SheetExists(string sheetName) =>
            workbook.Sheets.Any(s => string.Equals(s.Name, sheetName, StringComparison.OrdinalIgnoreCase));
        public bool IsRowHidden(uint row) => false;
        public bool IsRowHidden(string sheetName, uint row) => false;
        public bool IsRowFilterHidden(uint row) => false;
        public bool IsRowFilterHidden(string sheetName, uint row) => false;
        public Cell? TryGetCell(uint row, uint col) => null;
        public Cell? TryGetCell(string sheetName, uint row, uint col) => null;
    }
}
