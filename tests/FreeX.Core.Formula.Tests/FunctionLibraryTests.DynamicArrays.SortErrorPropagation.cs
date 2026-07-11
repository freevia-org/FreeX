using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

// R23-error-propagation-2: SORT()/SORTBY() must propagate an error found anywhere in the sort
// key as the whole function result (an all-or-nothing dynamic array, like FILTER's deciding
// array a few lines away in the same source file), instead of silently placing the erroring
// cell at a deterministic type-ordered position via CompareScalar's cross-type fallback.
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
    public void Sort_ErrorInSortKeyColumn_PropagatesErrorInsteadOfTypeOrderingIt()
    {
        // A1:A4 = {3;1;#DIV/0!;2}: default sort_index is column 1, which is also the only
        // column, so the whole array is the sort key. Real Excel returns #DIV/0! for the
        // entire SORT result rather than sorting the numbers around the error.
        var sheet = MakeSheet(
            (1, 1, new NumberValue(3)),
            (2, 1, new NumberValue(1)),
            (3, 1, ErrorValue.DivByZero),
            (4, 1, new NumberValue(2)));

        var result = _eval.Evaluate("=SORT(A1:A4)", sheet);

        result.Should().Be(ErrorValue.DivByZero,
            "an error anywhere in the sort key must propagate as the whole SORT result");
    }

    [Fact]
    public void Sort_ErrorInNonKeyColumn_DoesNotPropagate()
    {
        // The sort key is column 1 (default); column 2 holds an error but is never used as a
        // sort key, so it must not affect whether the SORT call as a whole errors.
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
    public void Sort_ErrorInSelectedSortIndexColumn_Propagates()
    {
        // sort_index=2 selects column 2 as the key; that column contains an error, so the
        // result must be that error even though column 1 (unused as a key) is error-free.
        var sheet = MakeSheet(
            (1, 1, new TextValue("A")), (1, 2, new NumberValue(3)),
            (2, 1, new TextValue("B")), (2, 2, ErrorValue.NA),
            (3, 1, new TextValue("C")), (3, 2, new NumberValue(1)));

        var result = _eval.Evaluate("=SORT(A1:B3,2,1)", sheet);

        result.Should().Be(ErrorValue.NA,
            "the sort key column contains an error, so the whole SORT result must be that error");
    }

    [Fact]
    public void Sort_ErrorInSortKeyRow_ColumnOrientation_Propagates()
    {
        // byCol=TRUE sorts columns using row 1 as the key; row 1 contains an error.
        var sheet = MakeSheet(
            (1, 1, new NumberValue(3)), (1, 2, ErrorValue.Ref), (1, 3, new NumberValue(1)),
            (2, 1, new NumberValue(10)), (2, 2, new NumberValue(20)), (2, 3, new NumberValue(30)));

        var result = _eval.Evaluate("=SORT(A1:C2,1,1,TRUE)", sheet);

        result.Should().Be(ErrorValue.Ref,
            "column-oriented SORT with an error in the sort key row must propagate that error");
    }

    [Fact]
    public void Sortby_ErrorInByArray_Propagates()
    {
        // SORTBY's key array (column B) contains an error; the whole result must be that error.
        var sheet = MakeSheet(
            (1, 1, new TextValue("A")), (1, 2, new NumberValue(3)),
            (2, 1, new TextValue("B")), (2, 2, ErrorValue.DivByZero),
            (3, 1, new TextValue("C")), (3, 2, new NumberValue(1)));

        var result = _eval.Evaluate("=SORTBY(A1:A3,B1:B3)", sheet);

        result.Should().Be(ErrorValue.DivByZero,
            "an error anywhere in a SORTBY key array must propagate as the whole result");
    }
}
