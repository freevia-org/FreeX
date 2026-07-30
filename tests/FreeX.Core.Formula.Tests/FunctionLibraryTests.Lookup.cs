using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

public partial class FunctionLibraryTests
{
    // ── VLOOKUP ──────────────────────────────────────────────────────────────

    [Fact]
    public void Vlookup_ExactMatch_ReturnsValue()
    {
        // A1:B3 = {10,"apple"; 20,"banana"; 30,"cherry"}
        var sheet = MakeSheet(
            (1, 1, new NumberValue(10)), (1, 2, new TextValue("apple")),
            (2, 1, new NumberValue(20)), (2, 2, new TextValue("banana")),
            (3, 1, new NumberValue(30)), (3, 2, new TextValue("cherry")));
        _eval.Evaluate("=VLOOKUP(20,A1:B3,2,FALSE)", sheet).Should().Be(new TextValue("banana"));
    }

    [Fact]
    public void Vlookup_And_Hlookup_TreatScalarTablesAsSingleCellArrays()
    {
        _eval.Evaluate("=VLOOKUP(5,5,1,FALSE)", MakeSheet()).Should().Be(new NumberValue(5));
        _eval.Evaluate("=HLOOKUP(5,5,1,FALSE)", MakeSheet()).Should().Be(new NumberValue(5));
    }

    [Fact]
    public void LegacyLookupFunctions_RangeScalarArguments_SpillElementwiseOrReturnValueForShapeMismatch()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(10)), (1, 2, new TextValue("apple")), (1, 3, new NumberValue(100)),
            (2, 1, new NumberValue(20)), (2, 2, new TextValue("banana")), (2, 3, new NumberValue(200)),
            (3, 1, new NumberValue(30)), (3, 2, new TextValue("cherry")), (3, 3, new NumberValue(300)),
            (1, 4, new NumberValue(20)), (2, 4, new NumberValue(30)),
            (1, 5, new NumberValue(2)), (2, 5, new NumberValue(3)),
            (1, 6, new NumberValue(0)), (2, 6, new NumberValue(1)),
            (1, 7, new NumberValue(10)), (1, 8, new NumberValue(20)), (1, 9, new NumberValue(30)),
            (2, 7, new TextValue("apple")), (2, 8, new TextValue("banana")), (2, 9, new TextValue("cherry")),
            (3, 7, new NumberValue(100)), (3, 8, new NumberValue(200)), (3, 9, new NumberValue(300)));

        AssertTextColumn(_eval.Evaluate("=VLOOKUP(D1:D2,A1:C3,2,FALSE)", sheet), "banana", "cherry");
        AssertColumn(_eval.Evaluate("=VLOOKUP(20,A1:C3,E1:E2,FALSE)", sheet), new TextValue("banana"), new NumberValue(200));
        // R98-formula-lookup-cross-broadcast: D1:D2 (2x1 column of lookup values, {20;30})
        // crossed with E1:F1 (1x2 row of col_index_num, {2,0}) must 2-D cross-broadcast into a
        // 2x2 spilled matrix (row i = lookup value i, col j = col_index_num j), matching Excel
        // dynamic arrays -- this previously asserted the old (superseded) #VALUE! behavior for
        // exactly this perpendicular-vector shape combination. col_index_num=0 (from F1) is
        // itself an out-of-range column index (#VALUE!), independent of the broadcast fix.
        AssertLookupGrid(_eval.Evaluate("=VLOOKUP(D1:D2,A1:C3,E1:F1,FALSE)", sheet), new ScalarValue[,]
        {
            { new TextValue("banana"), ErrorValue.Value },
            { new TextValue("cherry"), ErrorValue.Value },
        });

        AssertTextColumn(_eval.Evaluate("=HLOOKUP(D1:D2,G1:I3,2,FALSE)", sheet), "banana", "cherry");
        AssertColumn(_eval.Evaluate("=HLOOKUP(20,G1:I3,E1:E2,FALSE)", sheet), new TextValue("banana"), new NumberValue(200));
        // Same cross-broadcast rule for HLOOKUP's row_index_num (R98-formula-lookup-cross-broadcast);
        // row_index_num=0 (from F1) is itself out-of-range (#VALUE!), independent of the broadcast fix.
        AssertLookupGrid(_eval.Evaluate("=HLOOKUP(D1:D2,G1:I3,E1:F1,FALSE)", sheet), new ScalarValue[,]
        {
            { new TextValue("banana"), ErrorValue.Value },
            { new TextValue("cherry"), ErrorValue.Value },
        });

        AssertApproxColumn(_eval.Evaluate("=MATCH(D1:D2,A1:A3,0)", sheet), 2, 3);
        AssertApproxColumn(_eval.Evaluate("=MATCH(20,A1:A3,F1:F2)", sheet), 2, 2);
        // Same cross-broadcast rule for MATCH's match_type (R98-formula-lookup-cross-broadcast);
        // match_type=2 (from E1) is itself an invalid match_type (#N/A), independent of the
        // broadcast fix -- only F1=0 (exact match) yields a real hit.
        AssertLookupGrid(_eval.Evaluate("=MATCH(D1:D2,A1:A3,E1:F1)", sheet), new ScalarValue[,]
        {
            { ErrorValue.NA, new NumberValue(2) },
            { ErrorValue.NA, new NumberValue(3) },
        });
    }

    // R98-formula-lookup-cross-broadcast: asserts a spilled 2-D result grid (row-major), for
    // shapes that mix value types (text/number/error) where AssertColumn/AssertApproxColumn
    // (1-D only) don't apply.
    private static void AssertLookupGrid(ScalarValue value, ScalarValue[,] expected)
    {
        var range = value.Should().BeOfType<RangeValue>().Subject;
        range.RowCount.Should().Be(expected.GetLength(0));
        range.ColCount.Should().Be(expected.GetLength(1));
        for (int r = 0; r < expected.GetLength(0); r++)
            for (int c = 0; c < expected.GetLength(1); c++)
                range.At(r + 1, c + 1).Should().Be(expected[r, c], $"cell ({r + 1},{c + 1})");
    }

    [Fact]
    public void Vlookup_NotFound_ReturnsNA()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(10)), (1, 2, new TextValue("apple")));
        var result = _eval.Evaluate("=VLOOKUP(99,A1:B1,2,FALSE)", sheet);
        result.Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void Vlookup_ApproximateMatch_ReturnsBestFit()
    {
        // Sorted: 1,10,100
        var sheet = MakeSheet(
            (1, 1, new NumberValue(1)),   (1, 2, new TextValue("one")),
            (2, 1, new NumberValue(10)),  (2, 2, new TextValue("ten")),
            (3, 1, new NumberValue(100)), (3, 2, new TextValue("hundred")));
        // lookup 15 in approximate mode → row with 10
        _eval.Evaluate("=VLOOKUP(15,A1:B3,2,TRUE)", sheet).Should().Be(new TextValue("ten"));
    }

    [Fact]
    public void Vlookup_OmittedRangeLookup_DefaultsToApproximateMatch()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(1)),   (1, 2, new TextValue("one")),
            (2, 1, new NumberValue(10)),  (2, 2, new TextValue("ten")),
            (3, 1, new NumberValue(100)), (3, 2, new TextValue("hundred")));

        _eval.Evaluate("=VLOOKUP(15,A1:B3,2,)", sheet).Should().Be(new TextValue("ten"));
    }

    [Fact]
    public void Vlookup_TextKey_ExactMatch()
    {
        var sheet = MakeSheet(
            (1, 1, new TextValue("a")), (1, 2, new NumberValue(1)),
            (2, 1, new TextValue("b")), (2, 2, new NumberValue(2)),
            (3, 1, new TextValue("c")), (3, 2, new NumberValue(3)));
        _eval.Evaluate("=VLOOKUP(\"b\",A1:B3,2,FALSE)", sheet).Should().Be(new NumberValue(2));
    }

    [Fact]
    public void Vlookup_TextWildcard_ExactMatch()
    {
        var sheet = MakeSheet(
            (1, 1, new TextValue("Alpha")), (1, 2, new NumberValue(1)),
            (2, 1, new TextValue("Beta")), (2, 2, new NumberValue(2)),
            (3, 1, new TextValue("Alpine")), (3, 2, new NumberValue(3)));

        _eval.Evaluate("=VLOOKUP(\"Al*\",A1:B3,2,FALSE)", sheet).Should().Be(new NumberValue(1));
    }

    [Fact]
    public void Vlookup_TextWildcardTildeEscapesLiteralQuestion()
    {
        var sheet = MakeSheet(
            (1, 1, new TextValue("A1")), (1, 2, new NumberValue(1)),
            (2, 1, new TextValue("A?")), (2, 2, new NumberValue(2)));

        _eval.Evaluate("=VLOOKUP(\"A~?\",A1:B2,2,FALSE)", sheet).Should().Be(new NumberValue(2));
    }

    [Fact]
    public void Vlookup_RangeLookupError_PropagatesError()
    {
        var sheet = MakeSheet(
            (1, 1, new TextValue("a")), (1, 2, new NumberValue(1)),
            (2, 1, new TextValue("b")), (2, 2, new NumberValue(2)));

        _eval.Evaluate("=VLOOKUP(\"b\",A1:B2,2,NA())", sheet).Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void Vlookup_TableArgumentError_PropagatesError()
    {
        _eval.Evaluate("=VLOOKUP(\"b\",NA(),2,FALSE)", MakeSheet()).Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void Vlookup_IndexLessThanOne_ReturnsValueError()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(10)),
            (1, 2, new TextValue("ten")));

        _eval.Evaluate("=VLOOKUP(10,A1:B1,0,FALSE)", sheet).Should().Be(ErrorValue.Value);
        _eval.Evaluate("=VLOOKUP(10,A1:B1,-1,FALSE)", sheet).Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void Vlookup_IndexBeyondTable_ReturnsRefError()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(10)),
            (1, 2, new TextValue("ten")));

        _eval.Evaluate("=VLOOKUP(10,A1:B1,3,FALSE)", sheet).Should().Be(ErrorValue.Ref);
    }

    [Fact]
    public void Vlookup_DateKey_ExactMatchesDateSerial()
    {
        var date = DateTimeValue.FromDateTime(new DateTime(2026, 5, 16));
        var sheet = MakeSheet(
            (1, 1, date), (1, 2, new TextValue("match")),
            (2, 1, new NumberValue(10)), (2, 2, new TextValue("other")));

        _eval.Evaluate("=VLOOKUP(DATE(2026,5,16),A1:B2,2,FALSE)", sheet).Should().Be(new TextValue("match"));
    }

    // ── HLOOKUP ──────────────────────────────────────────────────────────────

    [Fact]
    public void Hlookup_ExactMatch_ReturnsValue()
    {
        // Row1: 10 20 30;  Row2: "a" "b" "c"
        var sheet = MakeSheet(
            (1, 1, new NumberValue(10)), (1, 2, new NumberValue(20)), (1, 3, new NumberValue(30)),
            (2, 1, new TextValue("a")),  (2, 2, new TextValue("b")),  (2, 3, new TextValue("c")));
        _eval.Evaluate("=HLOOKUP(20,A1:C2,2,FALSE)", sheet).Should().Be(new TextValue("b"));
    }

    [Fact]
    public void Hlookup_NotFound_ReturnsNA()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(10)), (1, 2, new NumberValue(20)));
        _eval.Evaluate("=HLOOKUP(99,A1:B2,2,FALSE)", sheet).Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void Hlookup_OmittedRangeLookup_DefaultsToApproximateMatch()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(1)), (1, 2, new NumberValue(10)), (1, 3, new NumberValue(100)),
            (2, 1, new TextValue("one")), (2, 2, new TextValue("ten")), (2, 3, new TextValue("hundred")));

        _eval.Evaluate("=HLOOKUP(15,A1:C2,2,)", sheet).Should().Be(new TextValue("ten"));
    }

    [Fact]
    public void Hlookup_TextWildcard_ExactMatch()
    {
        var sheet = MakeSheet(
            (1, 1, new TextValue("Alpha")), (1, 2, new TextValue("Beta")), (1, 3, new TextValue("Alpine")),
            (2, 1, new NumberValue(1)), (2, 2, new NumberValue(2)), (2, 3, new NumberValue(3)));

        _eval.Evaluate("=HLOOKUP(\"?eta\",A1:C2,2,FALSE)", sheet).Should().Be(new NumberValue(2));
    }

    [Fact]
    public void Hlookup_TextWildcardTildeEscapesLiteralAsterisk()
    {
        var sheet = MakeSheet(
            (1, 1, new TextValue("A1")), (1, 2, new TextValue("A*")),
            (2, 1, new NumberValue(1)), (2, 2, new NumberValue(2)));

        _eval.Evaluate("=HLOOKUP(\"A~*\",A1:B2,2,FALSE)", sheet).Should().Be(new NumberValue(2));
    }

    [Fact]
    public void Hlookup_RangeLookupError_PropagatesError()
    {
        var sheet = MakeSheet(
            (1, 1, new TextValue("a")), (1, 2, new TextValue("b")),
            (2, 1, new NumberValue(1)), (2, 2, new NumberValue(2)));

        _eval.Evaluate("=HLOOKUP(\"b\",A1:B2,2,NA())", sheet).Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void Hlookup_TableArgumentError_PropagatesError()
    {
        _eval.Evaluate("=HLOOKUP(\"b\",NA(),2,FALSE)", MakeSheet()).Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void Hlookup_IndexLessThanOne_ReturnsValueError()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(10)),
            (2, 1, new TextValue("ten")));

        _eval.Evaluate("=HLOOKUP(10,A1:A2,0,FALSE)", sheet).Should().Be(ErrorValue.Value);
        _eval.Evaluate("=HLOOKUP(10,A1:A2,-1,FALSE)", sheet).Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void Hlookup_IndexBeyondTable_ReturnsRefError()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(10)),
            (2, 1, new TextValue("ten")));

        _eval.Evaluate("=HLOOKUP(10,A1:A2,3,FALSE)", sheet).Should().Be(ErrorValue.Ref);
    }

    [Fact]
    public void Hlookup_DateKey_ExactMatchesDateSerial()
    {
        var date = DateTimeValue.FromDateTime(new DateTime(2026, 5, 16));
        var sheet = MakeSheet(
            (1, 1, date), (1, 2, new NumberValue(10)),
            (2, 1, new TextValue("match")), (2, 2, new TextValue("other")));

        _eval.Evaluate("=HLOOKUP(DATE(2026,5,16),A1:B2,2,FALSE)", sheet).Should().Be(new TextValue("match"));
    }

    [Fact]
    public void LegacyLookup_DirectCrossSheetTable_StreamsFromTargetSheet()
    {
        var workbook = new Workbook("T");
        var formulaSheet = workbook.AddSheet("Formula");
        var dataSheet = workbook.AddSheet("Data");

        dataSheet.SetCell(new CellAddress(dataSheet.Id, 1, 1), new NumberValue(10));
        dataSheet.SetCell(new CellAddress(dataSheet.Id, 1, 2), new TextValue("ten"));
        dataSheet.SetCell(new CellAddress(dataSheet.Id, 2, 1), new NumberValue(20));
        dataSheet.SetCell(new CellAddress(dataSheet.Id, 2, 2), new TextValue("twenty"));

        dataSheet.SetCell(new CellAddress(dataSheet.Id, 1, 4), new NumberValue(10));
        dataSheet.SetCell(new CellAddress(dataSheet.Id, 1, 5), new NumberValue(20));
        dataSheet.SetCell(new CellAddress(dataSheet.Id, 2, 4), new TextValue("ten"));
        dataSheet.SetCell(new CellAddress(dataSheet.Id, 2, 5), new TextValue("twenty"));

        _eval.Evaluate("=VLOOKUP(20,Data!A1:B2,2,FALSE)", formulaSheet, workbook)
            .Should().Be(new TextValue("twenty"));
        _eval.Evaluate("=HLOOKUP(20,Data!D1:E2,2,FALSE)", formulaSheet, workbook)
            .Should().Be(new TextValue("twenty"));
        _eval.Evaluate("=VLOOKUP(20,Missing!A1:B2,2,FALSE)", formulaSheet, workbook)
            .Should().Be(ErrorValue.Ref);
    }

    [Fact]
    public void LegacyLookup_InvalidRangeLookupWinsBeforeOutOfBoundsIndex()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(10)), (1, 2, new TextValue("ten")),
            (2, 1, new TextValue("twenty")), (2, 2, new TextValue("other")));

        _eval.Evaluate("=VLOOKUP(10,A1:B1,3,\"not-bool\")", sheet).Should().Be(ErrorValue.Value);
        _eval.Evaluate("=HLOOKUP(10,A1:B2,3,\"not-bool\")", sheet).Should().Be(ErrorValue.Value);
    }

    // ── INDEX ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Index_ReturnsCorrectCell()
    {
        // A1:C2
        var sheet = MakeSheet(
            (1, 1, new NumberValue(1)), (1, 2, new NumberValue(2)), (1, 3, new NumberValue(3)),
            (2, 1, new NumberValue(4)), (2, 2, new NumberValue(5)), (2, 3, new NumberValue(6)));
        _eval.Evaluate("=INDEX(A1:C2,2,3)", sheet).Should().Be(new NumberValue(6));
    }

    [Fact]
    public void Index_And_Match_TreatScalarArraysAsSingleItemArrays()
    {
        _eval.Evaluate("=INDEX(5,1)", MakeSheet()).Should().Be(new NumberValue(5));
        _eval.Evaluate("=MATCH(5,5,0)", MakeSheet()).Should().Be(new NumberValue(1));
    }

    [Fact]
    public void Index_OutOfRange_ReturnsRef()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(1)), (1, 2, new NumberValue(2)));
        var result = _eval.Evaluate("=INDEX(A1:B1,1,5)", sheet);
        result.Should().Be(ErrorValue.Ref);
    }

    [Fact]
    public void Index_SingleColumn_DefaultCol()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(10)),
            (2, 1, new NumberValue(20)),
            (3, 1, new NumberValue(30)));
        _eval.Evaluate("=INDEX(A1:A3,2)", sheet).Should().Be(new NumberValue(20));
    }

    [Fact]
    public void Index_FullColumnScalarLookup_DoesNotMaterializeEntireRange()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(10)),
            (2, 1, new NumberValue(20)),
            (1, 2, new NumberValue(99)));

        _eval.Evaluate("=INDEX(A:A,2)", sheet).Should().Be(new NumberValue(20));
        _eval.Evaluate("=INDEX(A:B,1,2)", sheet).Should().Be(new NumberValue(99));
        // INDEX(col, 0) returns the entire column. The column clamps to the used extent (A1:A2),
        // so this yields that range rather than #REF!.
        var entireColumn = _eval.Evaluate("=INDEX(A:A,0)", sheet).Should().BeOfType<RangeValue>().Subject;
        entireColumn.RowCount.Should().Be(2);
        entireColumn.Cells[0, 0].Should().Be(new NumberValue(10));
        entireColumn.Cells[1, 0].Should().Be(new NumberValue(20));
    }

    [Fact]
    public void Index_ZeroRow_ReturnsEntireColumn()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(1)), (1, 2, new NumberValue(2)), (1, 3, new NumberValue(3)),
            (2, 1, new NumberValue(4)), (2, 2, new NumberValue(5)), (2, 3, new NumberValue(6)));

        var result = _eval.Evaluate("=INDEX(A1:C2,0,2)", sheet).Should().BeOfType<RangeValue>().Subject;
        result.RowCount.Should().Be(2);
        result.ColCount.Should().Be(1);
        result.Cells[0, 0].Should().Be(new NumberValue(2));
        result.Cells[1, 0].Should().Be(new NumberValue(5));
    }

    [Fact]
    public void Index_OmittedRow_ReturnsEntireColumn()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(1)), (1, 2, new NumberValue(2)), (1, 3, new NumberValue(3)),
            (2, 1, new NumberValue(4)), (2, 2, new NumberValue(5)), (2, 3, new NumberValue(6)));

        var result = _eval.Evaluate("=INDEX(A1:C2,,2)", sheet).Should().BeOfType<RangeValue>().Subject;
        result.RowCount.Should().Be(2);
        result.ColCount.Should().Be(1);
        result.Cells[0, 0].Should().Be(new NumberValue(2));
        result.Cells[1, 0].Should().Be(new NumberValue(5));
    }

    [Fact]
    public void Index_ZeroColumn_ReturnsEntireRow()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(1)), (1, 2, new NumberValue(2)), (1, 3, new NumberValue(3)),
            (2, 1, new NumberValue(4)), (2, 2, new NumberValue(5)), (2, 3, new NumberValue(6)));

        var result = _eval.Evaluate("=INDEX(A1:C2,2,0)", sheet).Should().BeOfType<RangeValue>().Subject;
        result.RowCount.Should().Be(1);
        result.ColCount.Should().Be(3);
        result.Cells[0, 0].Should().Be(new NumberValue(4));
        result.Cells[0, 1].Should().Be(new NumberValue(5));
        result.Cells[0, 2].Should().Be(new NumberValue(6));
    }

    [Fact]
    public void Index_ZeroRowAndColumn_ReturnsEntireArray()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(1)), (1, 2, new NumberValue(2)),
            (2, 1, new NumberValue(3)), (2, 2, new NumberValue(4)));

        var result = _eval.Evaluate("=INDEX(A1:B2,0,0)", sheet).Should().BeOfType<RangeValue>().Subject;
        result.RowCount.Should().Be(2);
        result.ColCount.Should().Be(2);
        result.Cells[0, 0].Should().Be(new NumberValue(1));
        result.Cells[0, 1].Should().Be(new NumberValue(2));
        result.Cells[1, 0].Should().Be(new NumberValue(3));
        result.Cells[1, 1].Should().Be(new NumberValue(4));
    }

    [Fact]
    public void Index_RowAndColumnNumberRanges_SpillElementwiseOrReturnValueForShapeMismatch()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(1)), (1, 2, new NumberValue(2)), (1, 3, new NumberValue(3)),
            (2, 1, new NumberValue(4)), (2, 2, new NumberValue(5)), (2, 3, new NumberValue(6)),
            (1, 4, new NumberValue(1)), (2, 4, new NumberValue(2)),
            (1, 5, new NumberValue(3)), (2, 5, new NumberValue(2)),
            // F1 = 2 (so E1:F1 below is a clean {3,2} row vector of column_num, not a blank F1
            // that would trigger INDEX's unrelated column_num==0 "spill whole row" special case).
            (1, 6, new NumberValue(2)));

        AssertApproxColumn(_eval.Evaluate("=INDEX(A1:C2,D1:D2,E1:E2)", sheet), 3, 5);
        AssertApproxColumn(_eval.Evaluate("=INDEX(A1:C2,D1:D2,2)", sheet), 2, 5);
        // R98-formula-lookup-cross-broadcast: D1:D2 (2x1 column of row_num) crossed with E1:F1
        // (1x2 row of column_num) must 2-D cross-broadcast into a 2x2 spilled matrix (row i =
        // row_num i, col j = column_num j), matching Excel dynamic arrays -- this previously
        // asserted the old (superseded) #VALUE! behavior for exactly this perpendicular-vector
        // shape combination.
        AssertLookupGrid(_eval.Evaluate("=INDEX(A1:C2,D1:D2,E1:F1)", sheet), new ScalarValue[,]
        {
            { new NumberValue(3), new NumberValue(2) },
            { new NumberValue(6), new NumberValue(5) },
        });
    }

    [Fact]
    public void Index_ColumnError_PropagatesError()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(10)), (1, 2, new NumberValue(20)));

        _eval.Evaluate("=INDEX(A1:B1,1,NA())", sheet).Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void Index_ArrayArgumentError_PropagatesError()
    {
        _eval.Evaluate("=INDEX(NA(),1)", MakeSheet()).Should().Be(ErrorValue.NA);
    }

    // ── MATCH ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Match_ExactMatch_ReturnsPosition()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(10)),
            (2, 1, new NumberValue(20)),
            (3, 1, new NumberValue(30)));
        _eval.Evaluate("=MATCH(20,A1:A3,0)", sheet).Should().Be(new NumberValue(2));
    }

    [Fact]
    public void Match_NotFound_ReturnsNA()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(10)),
            (2, 1, new NumberValue(20)));
        _eval.Evaluate("=MATCH(99,A1:A2,0)", sheet).Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void Match_TwoDimensionalLookupArray_ReturnsNA()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(1)), (1, 2, new NumberValue(2)),
            (2, 1, new NumberValue(3)), (2, 2, new NumberValue(4)));

        _eval.Evaluate("=MATCH(3,A1:B2,0)", sheet).Should().Be(ErrorValue.NA);
        _eval.Evaluate("=MATCH(3,A1:B2,1/0)", sheet).Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void Match_ExactTextWildcard_ReturnsPosition()
    {
        var sheet = MakeSheet(
            (1, 1, new TextValue("Alpha")),
            (2, 1, new TextValue("Beta")),
            (3, 1, new TextValue("Alpine")));

        _eval.Evaluate("=MATCH(\"Al*\",A1:A3,0)", sheet).Should().Be(new NumberValue(1));
    }

    [Fact]
    public void Match_ExactTextWildcardTildeEscapesLiteralQuestion()
    {
        var sheet = MakeSheet(
            (1, 1, new TextValue("A1")),
            (2, 1, new TextValue("A?")));

        _eval.Evaluate("=MATCH(\"A~?\",A1:A2,0)", sheet).Should().Be(new NumberValue(2));
    }

    [Fact]
    public void Match_ApproximateAscending_ReturnsBestFit()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(1)),
            (2, 1, new NumberValue(5)),
            (3, 1, new NumberValue(10)));
        // lookup 7 with match_type=1 → position 2 (5 is largest <= 7)
        _eval.Evaluate("=MATCH(7,A1:A3,1)", sheet).Should().Be(new NumberValue(2));
    }

    [Fact]
    public void Match_OmittedMatchType_DefaultsToAscendingApproximate()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(1)),
            (2, 1, new NumberValue(5)),
            (3, 1, new NumberValue(10)));

        _eval.Evaluate("=MATCH(7,A1:A3,)", sheet).Should().Be(new NumberValue(2));
    }

    [Fact]
    public void Match_ApproximateDescending_ReturnsBestFit()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(10)),
            (2, 1, new NumberValue(8)),
            (3, 1, new NumberValue(5)));

        _eval.Evaluate("=MATCH(7,A1:A3,-1)", sheet).Should().Be(new NumberValue(2));
    }

    [Fact]
    public void Match_MatchTypeError_PropagatesError()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(10)),
            (2, 1, new NumberValue(20)));

        _eval.Evaluate("=MATCH(20,A1:A2,NA())", sheet).Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void Match_LookupArrayArgumentError_PropagatesError()
    {
        _eval.Evaluate("=MATCH(20,NA(),0)", MakeSheet()).Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void Match_InvalidMatchType_ReturnsNA()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(10)),
            (2, 1, new NumberValue(8)),
            (3, 1, new NumberValue(5)));

        _eval.Evaluate("=MATCH(7,A1:A3,2)", sheet).Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void Match_NonFiniteMatchType_ReturnsNA()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(10)),
            (2, 1, new NumberValue(8)),
            (3, 1, new NumberValue(5)),
            (1, 2, new TextValue("1E309")));

        _eval.Evaluate("=MATCH(7,A1:A3,B1)", sheet).Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void Match_TextMatchTypeCoercionFailure_ReturnsValueError()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(10)),
            (2, 1, new NumberValue(20)));

        _eval.Evaluate("=MATCH(20,A1:A2,\"not-a-number\")", sheet).Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void Match_DateCell_ExactMatchesDateSerial()
    {
        var date = DateTimeValue.FromDateTime(new DateTime(2026, 5, 16));
        var sheet = MakeSheet(
            (1, 1, new NumberValue(10)),
            (2, 1, date));

        _eval.Evaluate("=MATCH(DATE(2026,5,16),A1:A2,0)", sheet).Should().Be(new NumberValue(2));
    }

    [Fact]
    public void Match_DateCell_ApproximateAscendingComparesAsSerial()
    {
        var date = DateTimeValue.FromDateTime(new DateTime(2026, 5, 16));
        var sheet = MakeSheet(
            (1, 1, new NumberValue(10)),
            (2, 1, date));

        _eval.Evaluate("=MATCH(DATE(2026,5,17),A1:A2,1)", sheet).Should().Be(new NumberValue(2));
    }

    [Fact]
    public void Match_DirectHorizontalRange_StreamsWithoutChangingPosition()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(10)),
            (1, 2, new NumberValue(20)),
            (1, 3, new NumberValue(30)));

        _eval.Evaluate("=MATCH(20,A1:C1,0)", sheet).Should().Be(new NumberValue(2));
    }

    [Fact]
    public void Match_DirectCrossSheetRange_StreamsFromTargetSheet()
    {
        var workbook = new Workbook("T");
        var formulaSheet = workbook.AddSheet("Formula");
        var dataSheet = workbook.AddSheet("Data");
        dataSheet.SetCell(new CellAddress(dataSheet.Id, 1, 1), new NumberValue(10));
        dataSheet.SetCell(new CellAddress(dataSheet.Id, 2, 1), new NumberValue(20));
        dataSheet.SetCell(new CellAddress(dataSheet.Id, 3, 1), new NumberValue(30));

        _eval.Evaluate("=MATCH(20,Data!A1:A3,0)", formulaSheet, workbook).Should().Be(new NumberValue(2));
        _eval.Evaluate("=MATCH(20,Missing!A1:A3,0)", formulaSheet, workbook).Should().Be(ErrorValue.Ref);
    }

    [Fact]
    public void Match_DirectRangeElementError_PropagatesBeforeLaterMatch()
    {
        var sheet = MakeSheet(
            (1, 1, ErrorValue.DivByZero),
            (2, 1, new NumberValue(20)));

        _eval.Evaluate("=MATCH(20,A1:A2,0)", sheet).Should().Be(ErrorValue.DivByZero);
    }


    [Fact]
    public void Xlookup_RangeArg_WorksCorrectly()
    {
        var sheet = MakeSheet(
            (1, 1, new TextValue("A")), (2, 1, new TextValue("B")), (3, 1, new TextValue("C")),
            (1, 2, new NumberValue(1)), (2, 2, new NumberValue(2)), (3, 2, new NumberValue(3)));
        // XLOOKUP("B", A1:A3, B1:B3) → 2
        var result = _eval.Evaluate("=XLOOKUP(\"B\",A1:A3,B1:B3)", sheet);
        result.Should().Be(new NumberValue(2));
    }

    [Fact]
    public void XlookupAndXmatch_RangeLookupValue_SpillElementwise()
    {
        var sheet = MakeSheet(
            (1, 1, new TextValue("A")), (2, 1, new TextValue("B")), (3, 1, new TextValue("C")),
            (1, 2, new NumberValue(1)), (2, 2, new NumberValue(2)), (3, 2, new NumberValue(3)),
            (1, 4, new TextValue("B")), (2, 4, new TextValue("C")));

        AssertColumn(_eval.Evaluate("=XMATCH(D1:D2,A1:A3)", sheet), new NumberValue(2), new NumberValue(3));
        AssertColumn(_eval.Evaluate("=XLOOKUP(D1:D2,A1:A3,B1:B3)", sheet), new NumberValue(2), new NumberValue(3));
    }

    [Fact]
    public void Xlookup_RangeLookupValueAndMultiColumnReturnArray_SpillsRows()
    {
        var sheet = MakeSheet(
            (1, 1, new TextValue("A")), (2, 1, new TextValue("B")), (3, 1, new TextValue("C")),
            (1, 2, new TextValue("A1")), (1, 3, new TextValue("A2")),
            (2, 2, new TextValue("B1")), (2, 3, new TextValue("B2")),
            (3, 2, new TextValue("C1")), (3, 3, new TextValue("C2")),
            (1, 4, new TextValue("B")), (2, 4, new TextValue("C")));

        var result = _eval.Evaluate("=XLOOKUP(D1:D2,A1:A3,B1:C3)", sheet)
            .Should().BeOfType<RangeValue>()
            .Subject;

        result.RowCount.Should().Be(2);
        result.ColCount.Should().Be(2);
        result.At(1, 1).Should().Be(new TextValue("B1"));
        result.At(1, 2).Should().Be(new TextValue("B2"));
        result.At(2, 1).Should().Be(new TextValue("C1"));
        result.At(2, 2).Should().Be(new TextValue("C2"));
    }

    [Fact]
    public void Xlookup_RowLookupValuesAndMultiRowReturnArray_SpillsColumns()
    {
        var sheet = MakeSheet(
            (1, 1, new TextValue("A")), (1, 2, new TextValue("B")), (1, 3, new TextValue("C")),
            (2, 1, new TextValue("A1")), (3, 1, new TextValue("A2")),
            (2, 2, new TextValue("B1")), (3, 2, new TextValue("B2")),
            (2, 3, new TextValue("C1")), (3, 3, new TextValue("C2")),
            (5, 1, new TextValue("B")), (5, 2, new TextValue("C")));

        var result = _eval.Evaluate("=XLOOKUP(A5:B5,A1:C1,A2:C3)", sheet)
            .Should().BeOfType<RangeValue>()
            .Subject;

        result.RowCount.Should().Be(2);
        result.ColCount.Should().Be(2);
        result.At(1, 1).Should().Be(new TextValue("B1"));
        result.At(2, 1).Should().Be(new TextValue("B2"));
        result.At(1, 2).Should().Be(new TextValue("C1"));
        result.At(2, 2).Should().Be(new TextValue("C2"));
    }

    [Fact]
    public void Xlookup_And_Xmatch_TreatScalarLookupArraysAsSingleItemArrays()
    {
        _eval.Evaluate("=XMATCH(5,5)", MakeSheet()).Should().Be(new NumberValue(1));
        _eval.Evaluate("=XLOOKUP(5,5,\"found\")", MakeSheet()).Should().Be(new TextValue("found"));
    }

    [Fact]
    public void Xlookup_WildcardMatchMode_MatchesTextPattern()
    {
        var sheet = MakeSheet(
            (1, 1, new TextValue("Alpha")), (2, 1, new TextValue("Beta")), (3, 1, new TextValue("Alpine")),
            (1, 2, new NumberValue(10)),    (2, 2, new NumberValue(20)),   (3, 2, new NumberValue(30)));

        var result = _eval.Evaluate("=XLOOKUP(\"?eta\",A1:A3,B1:B3,\"\",2)", sheet);

        result.Should().Be(new NumberValue(20));
    }

    [Fact]
    public void Xlookup_WildcardMatchMode_MatchesNumericLookupValueByEquality()
    {
        // match_mode 2 is a superset of exact match: a non-wildcard numeric lookup_value
        // must still find an exact numeric candidate via plain equality (like MATCH's match_type=0).
        var sheet = MakeSheet(
            (1, 1, new NumberValue(1)), (2, 1, new NumberValue(5)), (3, 1, new NumberValue(10)),
            (1, 2, new TextValue("one")), (2, 2, new TextValue("five")), (3, 2, new TextValue("ten")));

        _eval.Evaluate("=XLOOKUP(5,A1:A3,B1:B3,\"\",2)", sheet).Should().Be(new TextValue("five"));
    }

    [Fact]
    public void Xlookup_WildcardMatchMode_NumericLookupValueFallsBackWhenAbsent()
    {
        // No-regression sibling: numeric equality fallback must not over-match values that
        // genuinely aren't present in the lookup array, and should still return the if-not-found value.
        var sheet = MakeSheet(
            (1, 1, new NumberValue(1)), (2, 1, new NumberValue(5)), (3, 1, new NumberValue(10)),
            (1, 2, new TextValue("one")), (2, 2, new TextValue("five")), (3, 2, new TextValue("ten")));

        _eval.Evaluate("=XLOOKUP(7,A1:A3,B1:B3,\"missing\",2)", sheet).Should().Be(new TextValue("missing"));
    }

    [Fact]
    public void Xlookup_ModeRangeArguments_SpillElementwiseOrReturnValueForShapeMismatch()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(1)), (2, 1, new NumberValue(3)),
            (3, 1, new NumberValue(5)), (4, 1, new NumberValue(7)),
            (1, 2, new TextValue("one")), (2, 2, new TextValue("three")),
            (3, 2, new TextValue("five")), (4, 2, new TextValue("seven")),
            (1, 4, new NumberValue(4)), (2, 4, new NumberValue(4)),
            (1, 5, new NumberValue(-1)), (2, 5, new NumberValue(1)),
            (1, 6, new NumberValue(1)), (2, 6, new NumberValue(1)), (3, 6, new NumberValue(1)));

        AssertColumn(_eval.Evaluate("=XLOOKUP(D1:D2,A1:A4,B1:B4,,E1:E2)", sheet), new TextValue("three"), new TextValue("five"));
        _eval.Evaluate("=XLOOKUP(D1:D2,A1:A4,B1:B4,,E1:E2,F1:F3)", sheet).Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void Xlookup_InvalidMatchMode_ReturnsValueError()
    {
        var sheet = MakeSheet(
            (1, 1, new TextValue("A")), (2, 1, new TextValue("B")),
            (1, 2, new NumberValue(1)), (2, 2, new NumberValue(2)));

        _eval.Evaluate("=XLOOKUP(\"B\",A1:A2,B1:B2,\"\",99)", sheet).Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void Xlookup_InvalidSearchMode_ReturnsValueError()
    {
        var sheet = MakeSheet(
            (1, 1, new TextValue("A")), (2, 1, new TextValue("B")),
            (1, 2, new NumberValue(1)), (2, 2, new NumberValue(2)));

        _eval.Evaluate("=XLOOKUP(\"B\",A1:A2,B1:B2,\"\",0,0)", sheet).Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void Xlookup_MatchModeError_PropagatesError()
    {
        var sheet = MakeSheet(
            (1, 1, new TextValue("A")), (2, 1, new TextValue("B")),
            (1, 2, new NumberValue(1)), (2, 2, new NumberValue(2)));

        _eval.Evaluate("=XLOOKUP(\"B\",A1:A2,B1:B2,\"\",NA())", sheet).Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void Xlookup_MatchFound_IgnoresErroringIfNotFound()
    {
        // Renamed/corrected for round-32 finding R32-formula-lookup-modern-1: if_not_found is only
        // consulted when the lookup actually fails to find a match -- like IFNA's lazy
        // value_if_na. "B" IS found in A1:A2 (at B2 = 2), so the found value must be returned even
        // though if_not_found (NA()) would itself evaluate to an error -- matching real Excel. This
        // test previously asserted the old (buggy) #N/A result, which came from eagerly
        // short-circuiting on if_not_found before the lookup even ran.
        var sheet = MakeSheet(
            (1, 1, new TextValue("A")), (2, 1, new TextValue("B")),
            (1, 2, new NumberValue(1)), (2, 2, new NumberValue(2)));

        _eval.Evaluate("=XLOOKUP(\"B\",A1:A2,B1:B2,NA())", sheet).Should().Be(new NumberValue(2));
    }

    [Fact]
    public void Xlookup_GenuineNotFound_StillPropagatesIfNotFoundError()
    {
        // Sibling already-working case: a genuine miss must still surface if_not_found, even when
        // it evaluates to an error.
        var sheet = MakeSheet(
            (1, 1, new TextValue("A")), (2, 1, new TextValue("B")),
            (1, 2, new NumberValue(1)), (2, 2, new NumberValue(2)));

        _eval.Evaluate("=XLOOKUP(\"Z\",A1:A2,B1:B2,NA())", sheet).Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void Xlookup_ExplicitlyEmptyIfNotFoundViaDoubleComma_ReturnsBlankNotNA()
    {
        // Renamed/corrected for round-26 finding R26-meta-2 (FormulaEvaluator.LookupFastPaths.cs's
        // TryEvaluateXlookupDirectRanges): the double-comma ",," is NOT an omitted argument -- the
        // parser records it as an OmittedArgumentNode occupying argument position 3, which
        // evaluates to BlankValue.Instance, same as an explicit blank cell reference would. Per
        // real Excel (and mirroring round-25's slow-path fix, see
        // R25_LookupModernTests.Xlookup_ExplicitlySuppliedBlankIfNotFound_ReturnsBlankNotNA), an
        // explicitly-supplied-but-blank if_not_found is returned verbatim, not coerced to #N/A --
        // only a genuinely omitted argument (no trailing comma at all) defaults to #N/A. This test
        // previously asserted the old (buggy) #N/A result.
        var sheet = MakeSheet(
            (1, 1, new TextValue("A")), (2, 1, new TextValue("B")),
            (1, 2, new NumberValue(1)), (2, 2, new NumberValue(2)));

        _eval.Evaluate("=XLOOKUP(\"Z\",A1:A2,B1:B2,,0)", sheet).Should().Be(new NumberValue(0));
    }

    [Fact]
    public void Xlookup_LookupArrayArgumentError_PropagatesError()
    {
        var sheet = MakeSheet((1, 1, new NumberValue(1)));

        _eval.Evaluate("=XLOOKUP(\"B\",NA(),A1:A1)", sheet).Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void Xlookup_LookupArrayElementError_PropagatesErrorWhenNoMatchFoundFirst()
    {
        var sheet = MakeSheet(
            (1, 1, ErrorValue.NA), (2, 1, new TextValue("A")),
            (1, 2, new NumberValue(1)), (2, 2, new NumberValue(2)));

        _eval.Evaluate("=XLOOKUP(\"Z\",A1:A2,B1:B2)", sheet).Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void Xlookup_ReturnArrayArgumentError_PropagatesError()
    {
        var sheet = MakeSheet((1, 1, new TextValue("B")));

        _eval.Evaluate("=XLOOKUP(\"B\",A1:A1,NA())", sheet).Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void Xlookup_SearchModeError_PropagatesError()
    {
        var sheet = MakeSheet(
            (1, 1, new TextValue("A")), (2, 1, new TextValue("B")),
            (1, 2, new NumberValue(1)), (2, 2, new NumberValue(2)));

        _eval.Evaluate("=XLOOKUP(\"B\",A1:A2,B1:B2,\"\",0,NA())", sheet).Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void Xlookup_OmittedSearchMode_DefaultsFirstToLast()
    {
        var sheet = MakeSheet(
            (1, 1, new TextValue("A")), (2, 1, new TextValue("B")),
            (1, 2, new NumberValue(1)), (2, 2, new NumberValue(2)));

        _eval.Evaluate("=XLOOKUP(\"B\",A1:A2,B1:B2,\"\",0,)", sheet).Should().Be(new NumberValue(2));
    }

    [Fact]
    public void Xmatch_ExactMatch_ReturnsPosition()
    {
        var sheet = MakeSheet(
            (1, 1, new TextValue("A")), (2, 1, new TextValue("B")), (3, 1, new TextValue("C")));

        _eval.Evaluate("=XMATCH(\"B\",A1:A3)", sheet).Should().Be(new NumberValue(2));
    }

    [Fact]
    public void Xmatch_DirectCrossSheetRange_StreamsFromTargetSheet()
    {
        var workbook = new Workbook("T");
        var formulaSheet = workbook.AddSheet("Formula");
        var dataSheet = workbook.AddSheet("Data");
        dataSheet.SetCell(new CellAddress(dataSheet.Id, 1, 1), new TextValue("A"));
        dataSheet.SetCell(new CellAddress(dataSheet.Id, 2, 1), new TextValue("B"));
        dataSheet.SetCell(new CellAddress(dataSheet.Id, 3, 1), new TextValue("C"));

        _eval.Evaluate("=XMATCH(\"B\",Data!A1:A3)", formulaSheet, workbook).Should().Be(new NumberValue(2));
        _eval.Evaluate("=XMATCH(1/0,Missing!A1:A3)", formulaSheet, workbook).Should().Be(ErrorValue.DivByZero);
        _eval.Evaluate("=XMATCH(\"B\",Missing!A1:A3)", formulaSheet, workbook).Should().Be(ErrorValue.Ref);
    }

    [Fact]
    public void Xmatch_ReverseSearch_ReturnsLastMatchingPosition()
    {
        var sheet = MakeSheet(
            (1, 1, new TextValue("A")), (2, 1, new TextValue("B")), (3, 1, new TextValue("B")));

        _eval.Evaluate("=XMATCH(\"B\",A1:A3,0,-1)", sheet).Should().Be(new NumberValue(3));
    }

    [Fact]
    public void Xmatch_BinarySearchModes_HandleDuplicateExactMatchesLikeExcel()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(1)), (2, 1, new NumberValue(2)),
            (3, 1, new NumberValue(2)), (4, 1, new NumberValue(3)),
            (1, 2, new NumberValue(3)), (2, 2, new NumberValue(2)),
            (3, 2, new NumberValue(2)), (4, 2, new NumberValue(1)));

        _eval.Evaluate("=XMATCH(2,A1:A4,0,2)", sheet).Should().Be(new NumberValue(2));
        _eval.Evaluate("=XMATCH(2,B1:B4,0,-2)", sheet).Should().Be(new NumberValue(3));
    }

    [Fact]
    public void Xmatch_And_Xlookup_DirectBinarySearchExactModes_HandleDuplicatesAndMissingValues()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(1)), (2, 1, new NumberValue(2)), (3, 1, new NumberValue(2)),
            (4, 1, new NumberValue(2)), (5, 1, new NumberValue(4)),
            (1, 2, new NumberValue(4)), (2, 2, new NumberValue(2)), (3, 2, new NumberValue(2)),
            (4, 2, new NumberValue(2)), (5, 2, new NumberValue(1)),
            (1, 3, new TextValue("asc-one")), (2, 3, new TextValue("asc-first")),
            (3, 3, new TextValue("asc-middle")), (4, 3, new TextValue("asc-last")),
            (5, 3, new TextValue("asc-four")),
            (1, 4, new TextValue("desc-four")), (2, 4, new TextValue("desc-first")),
            (3, 4, new TextValue("desc-middle")), (4, 4, new TextValue("desc-last")),
            (5, 4, new TextValue("desc-one")));

        _eval.Evaluate("=XMATCH(2,A1:A5,0,2)", sheet).Should().Be(new NumberValue(2));
        _eval.Evaluate("=XMATCH(2,B1:B5,0,-2)", sheet).Should().Be(new NumberValue(4));
        _eval.Evaluate("=XMATCH(3,A1:A5,0,2)", sheet).Should().Be(ErrorValue.NA);
        _eval.Evaluate("=XMATCH(3,B1:B5,0,-2)", sheet).Should().Be(ErrorValue.NA);

        _eval.Evaluate("=XLOOKUP(2,A1:A5,C1:C5,\"missing\",0,2)", sheet).Should().Be(new TextValue("asc-first"));
        _eval.Evaluate("=XLOOKUP(2,B1:B5,D1:D5,\"missing\",0,-2)", sheet).Should().Be(new TextValue("desc-last"));
        _eval.Evaluate("=XLOOKUP(3,A1:A5,C1:C5,\"missing\",0,2)", sheet).Should().Be(new TextValue("missing"));
        _eval.Evaluate("=XLOOKUP(3,B1:B5,D1:D5,\"missing\",0,-2)", sheet).Should().Be(new TextValue("missing"));
    }

    [Fact]
    public void Xmatch_And_Xlookup_DirectBinarySearchApproximateModes_HandleAscendingAndDescendingBounds()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(1)), (2, 1, new NumberValue(3)), (3, 1, new NumberValue(3)),
            (4, 1, new NumberValue(5)), (5, 1, new NumberValue(7)),
            (1, 2, new NumberValue(7)), (2, 2, new NumberValue(5)), (3, 2, new NumberValue(3)),
            (4, 2, new NumberValue(3)), (5, 2, new NumberValue(1)),
            (1, 3, new TextValue("asc-one")), (2, 3, new TextValue("asc-three-first")),
            (3, 3, new TextValue("asc-three-last")), (4, 3, new TextValue("asc-five")),
            (5, 3, new TextValue("asc-seven")),
            (1, 4, new TextValue("desc-seven")), (2, 4, new TextValue("desc-five")),
            (3, 4, new TextValue("desc-three-first")), (4, 4, new TextValue("desc-three-last")),
            (5, 4, new TextValue("desc-one")));

        _eval.Evaluate("=XMATCH(4,A1:A5,-1,2)", sheet).Should().Be(new NumberValue(2));
        _eval.Evaluate("=XMATCH(4,A1:A5,1,2)", sheet).Should().Be(new NumberValue(4));
        _eval.Evaluate("=XMATCH(4,B1:B5,-1,-2)", sheet).Should().Be(new NumberValue(4));
        _eval.Evaluate("=XMATCH(4,B1:B5,1,-2)", sheet).Should().Be(new NumberValue(2));
        _eval.Evaluate("=XMATCH(3,A1:A5,-1,2)", sheet).Should().Be(new NumberValue(2));
        _eval.Evaluate("=XMATCH(3,A1:A5,1,2)", sheet).Should().Be(new NumberValue(2));
        _eval.Evaluate("=XMATCH(3,B1:B5,-1,-2)", sheet).Should().Be(new NumberValue(4));
        _eval.Evaluate("=XMATCH(3,B1:B5,1,-2)", sheet).Should().Be(new NumberValue(4));
        _eval.Evaluate("=XMATCH(0,A1:A5,-1,2)", sheet).Should().Be(ErrorValue.NA);
        _eval.Evaluate("=XMATCH(8,B1:B5,1,-2)", sheet).Should().Be(ErrorValue.NA);

        _eval.Evaluate("=XLOOKUP(4,A1:A5,C1:C5,\"missing\",-1,2)", sheet).Should().Be(new TextValue("asc-three-first"));
        _eval.Evaluate("=XLOOKUP(4,A1:A5,C1:C5,\"missing\",1,2)", sheet).Should().Be(new TextValue("asc-five"));
        _eval.Evaluate("=XLOOKUP(4,B1:B5,D1:D5,\"missing\",-1,-2)", sheet).Should().Be(new TextValue("desc-three-last"));
        _eval.Evaluate("=XLOOKUP(4,B1:B5,D1:D5,\"missing\",1,-2)", sheet).Should().Be(new TextValue("desc-five"));
        _eval.Evaluate("=XLOOKUP(3,A1:A5,C1:C5,\"missing\",-1,2)", sheet).Should().Be(new TextValue("asc-three-first"));
        _eval.Evaluate("=XLOOKUP(3,A1:A5,C1:C5,\"missing\",1,2)", sheet).Should().Be(new TextValue("asc-three-first"));
        _eval.Evaluate("=XLOOKUP(3,B1:B5,D1:D5,\"missing\",-1,-2)", sheet).Should().Be(new TextValue("desc-three-last"));
        _eval.Evaluate("=XLOOKUP(3,B1:B5,D1:D5,\"missing\",1,-2)", sheet).Should().Be(new TextValue("desc-three-last"));
        _eval.Evaluate("=XLOOKUP(0,A1:A5,C1:C5,\"missing\",-1,2)", sheet).Should().Be(new TextValue("missing"));
        _eval.Evaluate("=XLOOKUP(8,B1:B5,D1:D5,\"missing\",1,-2)", sheet).Should().Be(new TextValue("missing"));
    }

    [Fact]
    public void Xmatch_WildcardMode_MatchesPattern()
    {
        var sheet = MakeSheet(
            (1, 1, new TextValue("Alpha")), (2, 1, new TextValue("Beta")), (3, 1, new TextValue("Alpine")));

        _eval.Evaluate("=XMATCH(\"Al*\",A1:A3,2)", sheet).Should().Be(new NumberValue(1));
    }

    [Fact]
    public void Xmatch_WildcardMode_MatchesNumericLookupValueByEquality()
    {
        // match_mode 2 is a superset of exact match: a non-wildcard numeric lookup_value
        // must still find an exact numeric candidate via plain equality (like MATCH's match_type=0).
        var sheet = MakeSheet(
            (1, 1, new NumberValue(1)), (2, 1, new NumberValue(5)), (3, 1, new NumberValue(10)));

        _eval.Evaluate("=XMATCH(5,A1:A3,2)", sheet).Should().Be(new NumberValue(2));
    }

    [Fact]
    public void Xmatch_WildcardMode_MatchesBooleanLookupValueByEquality()
    {
        var sheet = MakeSheet(
            (1, 1, new BoolValue(false)), (2, 1, new BoolValue(true)));

        _eval.Evaluate("=XMATCH(TRUE,A1:A2,2)", sheet).Should().Be(new NumberValue(2));
    }

    [Fact]
    public void Xmatch_WildcardMode_NumericLookupValueStillNoMatchWhenAbsent()
    {
        // No-regression sibling: numeric equality fallback must not over-match values that
        // genuinely aren't present in the lookup array.
        var sheet = MakeSheet(
            (1, 1, new NumberValue(1)), (2, 1, new NumberValue(5)), (3, 1, new NumberValue(10)));

        _eval.Evaluate("=XMATCH(7,A1:A3,2)", sheet).Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void Xmatch_ApproximateMode_PrefersExactMatchBeforeFallback()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(5)), (2, 1, new NumberValue(4)), (3, 1, new NumberValue(5)),
            (4, 1, new NumberValue(6)));

        _eval.Evaluate("=XMATCH(5,A1:A4,-1)", sheet).Should().Be(new NumberValue(1));
        _eval.Evaluate("=XMATCH(5,A1:A4,1,-1)", sheet).Should().Be(new NumberValue(3));
    }

    [Fact]
    public void Xmatch_ModeRangeArguments_SpillElementwiseOrReturnValueForShapeMismatch()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(1)), (2, 1, new NumberValue(3)),
            (3, 1, new NumberValue(5)), (4, 1, new NumberValue(7)),
            (1, 4, new NumberValue(4)), (2, 4, new NumberValue(4)),
            (1, 5, new NumberValue(-1)), (2, 5, new NumberValue(1)),
            (1, 6, new NumberValue(1)), (2, 6, new NumberValue(1)), (3, 6, new NumberValue(1)));

        AssertColumn(_eval.Evaluate("=XMATCH(D1:D2,A1:A4,E1:E2)", sheet), new NumberValue(2), new NumberValue(3));
        _eval.Evaluate("=XMATCH(D1:D2,A1:A4,E1:E2,F1:F3)", sheet).Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void Xmatch_InvalidModes_ReturnValueError()
    {
        var sheet = MakeSheet((1, 1, new TextValue("A")));

        _eval.Evaluate("=XMATCH(\"A\",A1:A1,99)", sheet).Should().Be(ErrorValue.Value);
        _eval.Evaluate("=XMATCH(\"A\",A1:A1,0,0)", sheet).Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void Xmatch_LookupArrayArgumentError_PropagatesError()
    {
        _eval.Evaluate("=XMATCH(\"A\",NA())", MakeSheet()).Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void Xmatch_LookupArrayElementError_PropagatesErrorWhenNoMatchFoundFirst()
    {
        var sheet = MakeSheet(
            (1, 1, ErrorValue.NA),
            (2, 1, new TextValue("A")));

        _eval.Evaluate("=XMATCH(\"Z\",A1:A2)", sheet).Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void Xlookup_DateKey_ExactMatchesDateSerial()
    {
        var date = DateTimeValue.FromDateTime(new DateTime(2026, 5, 16));
        var sheet = MakeSheet(
            (1, 1, date), (2, 1, new NumberValue(10)),
            (1, 2, new TextValue("match")), (2, 2, new TextValue("other")));

        _eval.Evaluate("=XLOOKUP(DATE(2026,5,16),A1:A2,B1:B2)", sheet).Should().Be(new TextValue("match"));
    }

    [Fact]
    public void Xlookup_MismatchedReturnArrayShape_ReturnsValueError()
    {
        var sheet = MakeSheet(
            (1, 1, new TextValue("A")), (2, 1, new TextValue("B")),
            (1, 2, new NumberValue(1)));

        _eval.Evaluate("=XLOOKUP(\"B\",A1:A2,B1:B1)", sheet).Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void Xlookup_VerticalLookup_ReturnsMatchingReturnRow()
    {
        var sheet = MakeSheet(
            (1, 1, new TextValue("A")), (2, 1, new TextValue("B")),
            (1, 2, new NumberValue(1)), (1, 3, new TextValue("one")),
            (2, 2, new NumberValue(2)), (2, 3, new TextValue("two")));

        var result = _eval.Evaluate("=XLOOKUP(\"B\",A1:A2,B1:C2)", sheet);

        var rv = result.Should().BeOfType<RangeValue>().Subject;
        rv.RowCount.Should().Be(1);
        rv.ColCount.Should().Be(2);
        rv.Cells[0, 0].Should().Be(new NumberValue(2));
        rv.Cells[0, 1].Should().Be(new TextValue("two"));
    }

    [Fact]
    public void Xlookup_HorizontalLookup_ReturnsMatchingReturnColumn()
    {
        var sheet = MakeSheet(
            (1, 1, new TextValue("A")), (1, 2, new TextValue("B")), (1, 3, new TextValue("C")),
            (2, 1, new NumberValue(1)), (2, 2, new NumberValue(2)), (2, 3, new NumberValue(3)),
            (3, 1, new NumberValue(10)), (3, 2, new NumberValue(20)), (3, 3, new NumberValue(30)));

        var result = _eval.Evaluate("=XLOOKUP(\"B\",A1:C1,A2:C3)", sheet);

        var rv = result.Should().BeOfType<RangeValue>().Subject;
        rv.RowCount.Should().Be(2);
        rv.ColCount.Should().Be(1);
        rv.Cells[0, 0].Should().Be(new NumberValue(2));
        rv.Cells[1, 0].Should().Be(new NumberValue(20));
    }

    [Fact]
    public void Xlookup_ApproximateMode_PrefersExactMatchBeforeFallback()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(5)), (2, 1, new NumberValue(4)), (3, 1, new NumberValue(5)), (4, 1, new NumberValue(6)),
            (1, 2, new TextValue("first exact")), (2, 2, new TextValue("smaller")),
            (3, 2, new TextValue("last exact")), (4, 2, new TextValue("larger")));

        _eval.Evaluate("=XLOOKUP(5,A1:A4,B1:B4,\"\",-1)", sheet).Should().Be(new TextValue("first exact"));
        _eval.Evaluate("=XLOOKUP(5,A1:A4,B1:B4,\"\",1,-1)", sheet).Should().Be(new TextValue("last exact"));
    }


    [Fact] public void Lookup_FindsValueInVector()
    {
        var sheet = MakeSheet(
            (1,1,new NumberValue(1)),(2,1,new NumberValue(2)),(3,1,new NumberValue(3)),
            (1,2,new TextValue("A")),(2,2,new TextValue("B")),(3,2,new TextValue("C")));
        _eval.Evaluate("=LOOKUP(2,A1:A3,B1:B3)", sheet).Should().Be(new TextValue("B"));
    }

    [Fact]
    public void Lookup_TreatsScalarLookupAndResultVectorsAsSingleItemArrays()
    {
        _eval.Evaluate("=LOOKUP(5,5,\"found\")", MakeSheet()).Should().Be(new TextValue("found"));
    }

    [Fact]
    public void Lookup_ErrorInsideLookupVector_PropagatesError()
    {
        // R61-formula-lookup-array-form-6-1: LOOKUP previously silently SKIPPED an error cell
        // encountered during its approximate-match scan (this test used to assert the resulting
        // "hit" -- the OLD, Excel-incorrect behavior). VLOOKUP/HLOOKUP/MATCH all RETURN an error
        // hit in the lookup column immediately, poisoning the whole lookup; LOOKUP must match.
        var sheet = MakeSheet(
            (1, 1, new NumberValue(1)),
            (2, 1, ErrorValue.DivByZero),
            (3, 1, new NumberValue(2)),
            (1, 2, new TextValue("first")),
            (2, 2, new TextValue("skip")),
            (3, 2, new TextValue("hit")));

        _eval.Evaluate("=LOOKUP(2,A1:A3,B1:B3)", sheet)
            .Should().Be(ErrorValue.DivByZero);
    }

    [Fact]
    public void Lookup_ArrayForm_SearchesFirstRowAndReturnsLastRowWhenWiderThanTall()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(1)), (1, 2, new NumberValue(2)), (1, 3, new NumberValue(3)),
            (2, 1, new TextValue("A")), (2, 2, new TextValue("B")), (2, 3, new TextValue("C")));

        _eval.Evaluate("=LOOKUP(2,A1:C2)", sheet).Should().Be(new TextValue("B"));
    }

    [Fact]
    public void Lookup_ArrayFormErrorInsideLookupVector_PropagatesError()
    {
        // R61-formula-lookup-array-form-6-1: same fix as the vector-form sibling above, but for
        // LOOKUP's array form (LookupArrayForm -> LookupVectorForm).
        var sheet = MakeSheet(
            (1, 1, new NumberValue(1)), (1, 2, ErrorValue.DivByZero), (1, 3, new NumberValue(2)),
            (2, 1, new TextValue("first")), (2, 2, new TextValue("skip")), (2, 3, new TextValue("hit")));

        _eval.Evaluate("=LOOKUP(2,A1:C2)", sheet).Should().Be(ErrorValue.DivByZero);
    }

    [Fact] public void Lookup_LookupVectorArgumentError_PropagatesError()
    {
        var sheet = MakeSheet((1, 1, new TextValue("A")));
        _eval.Evaluate("=LOOKUP(2,NA(),A1:A1)", sheet).Should().Be(ErrorValue.NA);
    }

    [Fact] public void Lookup_ResultVectorArgumentError_PropagatesError()
    {
        var sheet = MakeSheet((1, 1, new NumberValue(2)));
        _eval.Evaluate("=LOOKUP(2,A1:A1,NA())", sheet).Should().Be(ErrorValue.NA);
    }

    // ── Bug-fix regression: type-class skipping in approximate match ──────────

    [Fact]
    public void Vlookup_Approximate_SkipsTextHeaderAboveNumericData()
    {
        // Row 1 has a text header; rows 2–4 have sorted numeric keys.
        // VLOOKUP(3, …, TRUE) must skip the text header and find the numeric section.
        var sheet = MakeSheet(
            (1, 1, new TextValue("Key")), (1, 2, new TextValue("Value")),
            (2, 1, new NumberValue(1)),    (2, 2, new TextValue("one")),
            (3, 1, new NumberValue(3)),    (3, 2, new TextValue("three")),
            (4, 1, new NumberValue(5)),    (4, 2, new TextValue("five")));
        _eval.Evaluate("=VLOOKUP(3,A1:B4,2,TRUE)", sheet).Should().Be(new TextValue("three"));
    }

    [Fact]
    public void Hlookup_Approximate_SkipsTextHeaderBeforeNumericData()
    {
        // Col 1 has a text label; cols 2–4 have sorted numeric keys.
        var sheet = MakeSheet(
            (1, 1, new TextValue("Hdr")), (1, 2, new NumberValue(1)), (1, 3, new NumberValue(3)), (1, 4, new NumberValue(5)),
            (2, 1, new TextValue("lbl")), (2, 2, new TextValue("one")), (2, 3, new TextValue("three")), (2, 4, new TextValue("five")));
        _eval.Evaluate("=HLOOKUP(3,A1:D2,2,TRUE)", sheet).Should().Be(new TextValue("three"));
    }

    [Fact]
    public void Vlookup_Approximate_BlankNotChosenAsBestMatch()
    {
        // A blank in the lookup column must be skipped (not treated as 0).
        var sheet = MakeSheet(
            (1, 1, BlankValue.Instance), (1, 2, new TextValue("blank-row")),
            (2, 1, new NumberValue(1)),  (2, 2, new TextValue("one")),
            (3, 1, new NumberValue(3)),  (3, 2, new TextValue("three")));
        // Blank is type-class 0; lookup value 2 is numeric (class 1) — blank is skipped.
        _eval.Evaluate("=VLOOKUP(2,A1:B3,2,TRUE)", sheet).Should().Be(new TextValue("one"));
    }

    [Fact]
    public void Match_Approximate_Ascending_SkipsBoolEntriesWhenLookingUpText()
    {
        // Bool entries (class 3) must be skipped when the lookup value is text (class 2).
        var sheet = MakeSheet(
            (1, 1, new BoolValue(true)),
            (2, 1, new BoolValue(false)));
        _eval.Evaluate("=MATCH(\"x\",A1:A2,1)", sheet).Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void Match_Approximate_Descending_SkipsTextEntryAmongNumbers()
    {
        // A stray text entry in a descending numeric list must be skipped (not chosen as best match).
        // Old code: CompareScalar("stray", 6) returned 1 (non-numeric > numeric under old ordering),
        // so "stray" was incorrectly recorded as best → returned position 3.
        // New code: type-class mismatch causes continue; 8 (position 2) is the correct answer.
        var sheet = MakeSheet(
            (1, 1, new NumberValue(10)),
            (2, 1, new NumberValue(8)),
            (3, 1, new TextValue("stray")),
            (4, 1, new NumberValue(5)));
        _eval.Evaluate("=MATCH(6,A1:A4,-1)", sheet).Should().Be(new NumberValue(2));
    }

    [Fact]
    public void Vlookup_Approximate_NumericOnlySortedData_StillWorks()
    {
        // Regression: a clean numeric sorted table must still return the correct best fit.
        var sheet = MakeSheet(
            (1, 1, new NumberValue(1)),   (1, 2, new TextValue("one")),
            (2, 1, new NumberValue(10)),  (2, 2, new TextValue("ten")),
            (3, 1, new NumberValue(100)), (3, 2, new TextValue("hundred")));
        _eval.Evaluate("=VLOOKUP(15,A1:B3,2,TRUE)", sheet).Should().Be(new TextValue("ten"));
    }

    [Fact]
    public void Match_Approximate_Ascending_NumericOnlySortedData_StillWorks()
    {
        // Regression: existing ascending numeric approximate MATCH must be unaffected.
        var sheet = MakeSheet(
            (1, 1, new NumberValue(1)),
            (2, 1, new NumberValue(5)),
            (3, 1, new NumberValue(10)));
        _eval.Evaluate("=MATCH(7,A1:A3,1)", sheet).Should().Be(new NumberValue(2));
    }

    [Fact]
    public void Lookup_Approximate_SkipsTextEntriesWhenLookingUpNumber()
    {
        // LOOKUP vector form: text entries in a numeric lookup vector must be skipped.
        var sheet = MakeSheet(
            (1, 1, new TextValue("hdr")),
            (2, 1, new NumberValue(1)),
            (3, 1, new NumberValue(3)),
            (1, 2, new TextValue("lbl")),
            (2, 2, new TextValue("one")),
            (3, 2, new TextValue("three")));
        _eval.Evaluate("=LOOKUP(2,A1:A3,B1:B3)", sheet).Should().Be(new TextValue("one"));
    }

    // ── Bug-fix regression: CompareScalar mixed-type ordering ─────────────────

    [Fact]
    public void Sort_MixedTypes_OrdersNumberBeforeTextBeforeBool()
    {
        // SORT uses CompareScalar; verify number < text < bool ordering after the fix.
        var sheet = MakeSheet(
            (1, 1, new BoolValue(true)),
            (2, 1, new TextValue("alpha")),
            (3, 1, new NumberValue(42)));
        var result = _eval.Evaluate("=SORT(A1:A3)", sheet)
            .Should().BeOfType<RangeValue>().Subject;
        result.Cells[0, 0].Should().Be(new NumberValue(42),  "numbers come first");
        result.Cells[1, 0].Should().Be(new TextValue("alpha"), "text comes second");
        result.Cells[2, 0].Should().Be(new BoolValue(true),   "booleans come last");
    }

}
