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
        _eval.Evaluate("=VLOOKUP(D1:D2,A1:C3,E1:F1,FALSE)", sheet).Should().Be(ErrorValue.Value);

        AssertTextColumn(_eval.Evaluate("=HLOOKUP(D1:D2,G1:I3,2,FALSE)", sheet), "banana", "cherry");
        AssertColumn(_eval.Evaluate("=HLOOKUP(20,G1:I3,E1:E2,FALSE)", sheet), new TextValue("banana"), new NumberValue(200));
        _eval.Evaluate("=HLOOKUP(D1:D2,G1:I3,E1:F1,FALSE)", sheet).Should().Be(ErrorValue.Value);

        AssertApproxColumn(_eval.Evaluate("=MATCH(D1:D2,A1:A3,0)", sheet), 2, 3);
        AssertApproxColumn(_eval.Evaluate("=MATCH(20,A1:A3,F1:F2)", sheet), 2, 2);
        _eval.Evaluate("=MATCH(D1:D2,A1:A3,E1:F1)", sheet).Should().Be(ErrorValue.Value);
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
            (1, 5, new NumberValue(3)), (2, 5, new NumberValue(2)));

        AssertApproxColumn(_eval.Evaluate("=INDEX(A1:C2,D1:D2,E1:E2)", sheet), 3, 5);
        AssertApproxColumn(_eval.Evaluate("=INDEX(A1:C2,D1:D2,2)", sheet), 2, 5);
        _eval.Evaluate("=INDEX(A1:C2,D1:D2,E1:F1)", sheet).Should().Be(ErrorValue.Value);
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
    public void Xlookup_IfNotFoundError_PropagatesError()
    {
        var sheet = MakeSheet(
            (1, 1, new TextValue("A")), (2, 1, new TextValue("B")),
            (1, 2, new NumberValue(1)), (2, 2, new NumberValue(2)));

        _eval.Evaluate("=XLOOKUP(\"B\",A1:A2,B1:B2,NA())", sheet).Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void Xlookup_OmittedIfNotFound_DefaultsToNA()
    {
        var sheet = MakeSheet(
            (1, 1, new TextValue("A")), (2, 1, new TextValue("B")),
            (1, 2, new NumberValue(1)), (2, 2, new NumberValue(2)));

        _eval.Evaluate("=XLOOKUP(\"Z\",A1:A2,B1:B2,,0)", sheet).Should().Be(ErrorValue.NA);
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
            (3, 1, new NumberValue(2)), (4, 1, new NumberValue(3)));

        _eval.Evaluate("=XMATCH(2,A1:A4,0,2)", sheet).Should().Be(new NumberValue(2));
        _eval.Evaluate("=XMATCH(2,A1:A4,0,-2)", sheet).Should().Be(new NumberValue(3));
    }

    [Fact]
    public void Xmatch_WildcardMode_MatchesPattern()
    {
        var sheet = MakeSheet(
            (1, 1, new TextValue("Alpha")), (2, 1, new TextValue("Beta")), (3, 1, new TextValue("Alpine")));

        _eval.Evaluate("=XMATCH(\"Al*\",A1:A3,2)", sheet).Should().Be(new NumberValue(1));
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
    public void Lookup_IgnoresErrorsInsideLookupVector()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(1)),
            (2, 1, ErrorValue.DivByZero),
            (3, 1, new NumberValue(2)),
            (1, 2, new TextValue("first")),
            (2, 2, new TextValue("skip")),
            (3, 2, new TextValue("hit")));

        _eval.Evaluate("=LOOKUP(2,A1:A3,B1:B3)", sheet)
            .Should().Be(new TextValue("hit"));
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
    public void Lookup_ArrayFormIgnoresErrorsInsideLookupVector()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(1)), (1, 2, ErrorValue.DivByZero), (1, 3, new NumberValue(2)),
            (2, 1, new TextValue("first")), (2, 2, new TextValue("skip")), (2, 3, new TextValue("hit")));

        _eval.Evaluate("=LOOKUP(2,A1:C2)", sheet).Should().Be(new TextValue("hit"));
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

}
