using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

// Errors inside a SORT / SORTBY key are ordinary values, not a reason to fail the whole call: measured against
// Excel 16.0 (build 20625) through COM, SORT({#N/A;1;#DIV/0!;2}) is {1;2;#DIV/0!;#N/A} -- numbers, then text,
// then logicals, then errors in error-code order (#NULL! < #DIV/0! < #VALUE! < #REF! < #NAME? < #NUM! < #N/A) --
// and descending order reverses all of it. (An older round pinned "all-or-nothing" propagation; Excel disagrees.)
public class FunctionLibraryTestsSortErrorPropagation
{
    private readonly FormulaEvaluator _eval = new();

    private static Sheet MakeSheet(params (int row, int col, ScalarValue val)[] cells)
    {
        var sheet = new Sheet(SheetId.New(), "S");
        foreach (var (r, c, v) in cells)
            sheet.SetCell(new CellAddress(sheet.Id, (uint)r, (uint)c), v);
        return sheet;
    }

    [Fact]
    public void Sort_ErrorInSortKeyColumn_SortsErrorsLast()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(3)),
            (2, 1, new NumberValue(1)),
            (3, 1, ErrorValue.DivByZero),
            (4, 1, new NumberValue(2)));

        var result = _eval.Evaluate("=SORT(A1:A4)", sheet).Should().BeOfType<RangeValue>().Subject;

        result.Cells[0, 0].Should().Be(new NumberValue(1));
        result.Cells[1, 0].Should().Be(new NumberValue(2));
        result.Cells[2, 0].Should().Be(new NumberValue(3));
        result.Cells[3, 0].Should().Be(ErrorValue.DivByZero);
    }

    [Fact]
    public void Sort_Descending_PutsErrorsFirstInReverseErrorOrder()
    {
        var sheet = MakeSheet(
            (1, 1, ErrorValue.NA),
            (2, 1, new NumberValue(1)),
            (3, 1, ErrorValue.DivByZero),
            (4, 1, new NumberValue(2)));

        var result = _eval.Evaluate("=SORT(A1:A4,1,-1)", sheet).Should().BeOfType<RangeValue>().Subject;

        result.Cells[0, 0].Should().Be(ErrorValue.NA);
        result.Cells[1, 0].Should().Be(ErrorValue.DivByZero);
        result.Cells[2, 0].Should().Be(new NumberValue(2));
        result.Cells[3, 0].Should().Be(new NumberValue(1));
    }

    [Fact]
    public void Sort_ErrorInNonKeyColumn_DoesNotAffectOrdering()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(3)), (1, 2, ErrorValue.DivByZero),
            (2, 1, new NumberValue(1)), (2, 2, new NumberValue(20)),
            (3, 1, new NumberValue(2)), (3, 2, new NumberValue(30)));

        var result = _eval.Evaluate("=SORT(A1:B3)", sheet)
            .Should().BeOfType<RangeValue>().Subject;

        result.Cells[0, 0].Should().Be(new NumberValue(1));
        result.Cells[1, 0].Should().Be(new NumberValue(2));
        result.Cells[2, 0].Should().Be(new NumberValue(3));
    }

    [Fact]
    public void Sort_ErrorInSelectedSortIndexColumn_SortsThatRowLast()
    {
        var sheet = MakeSheet(
            (1, 1, new TextValue("A")), (1, 2, new NumberValue(3)),
            (2, 1, new TextValue("B")), (2, 2, ErrorValue.NA),
            (3, 1, new TextValue("C")), (3, 2, new NumberValue(1)));

        var result = _eval.Evaluate("=SORT(A1:B3,2,1)", sheet).Should().BeOfType<RangeValue>().Subject;

        result.Cells[0, 0].Should().Be(new TextValue("C"));
        result.Cells[1, 0].Should().Be(new TextValue("A"));
        result.Cells[2, 0].Should().Be(new TextValue("B"));
        result.Cells[2, 1].Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void Sort_ErrorInSortKeyRow_ColumnOrientation_SortsThatColumnLast()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(3)), (1, 2, ErrorValue.Ref), (1, 3, new NumberValue(1)),
            (2, 1, new NumberValue(10)), (2, 2, new NumberValue(20)), (2, 3, new NumberValue(30)));

        var result = _eval.Evaluate("=SORT(A1:C2,1,1,TRUE)", sheet).Should().BeOfType<RangeValue>().Subject;

        result.Cells[0, 0].Should().Be(new NumberValue(1));
        result.Cells[0, 1].Should().Be(new NumberValue(3));
        result.Cells[0, 2].Should().Be(ErrorValue.Ref);
        result.Cells[1, 2].Should().Be(new NumberValue(20));
    }

    [Fact]
    public void Sortby_ErrorInByArray_SortsThatRowLast()
    {
        var sheet = MakeSheet(
            (1, 1, new TextValue("A")), (1, 2, new NumberValue(3)),
            (2, 1, new TextValue("B")), (2, 2, ErrorValue.DivByZero),
            (3, 1, new TextValue("C")), (3, 2, new NumberValue(1)));

        var result = _eval.Evaluate("=SORTBY(A1:A3,B1:B3)", sheet).Should().BeOfType<RangeValue>().Subject;

        result.Cells[0, 0].Should().Be(new TextValue("C"));
        result.Cells[1, 0].Should().Be(new TextValue("A"));
        result.Cells[2, 0].Should().Be(new TextValue("B"));
    }
}
