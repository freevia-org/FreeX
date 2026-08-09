using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

public partial class FunctionLibraryTests
{
    [Fact]
    public void IfError_ValueOk_ReturnsValue()
    {
        var sheet = MakeSheet();
        _eval.Evaluate("=IFERROR(10,99)", sheet).Should().Be(new NumberValue(10));
    }

    [Fact]
    public void IfError_DivByZero_ReturnsFallback()
    {
        var sheet = MakeSheet();
        _eval.Evaluate("=IFERROR(1/0,\"err\")", sheet).Should().Be(new TextValue("err"));
    }

    [Fact]
    public void IfError_NestedError_ReturnsFallback()
    {
        var sheet = MakeSheet();
        _eval.Evaluate("=IFERROR(NA(),0)", sheet).Should().Be(new NumberValue(0));
    }


    [Fact]
    public void IfNa_NonNaValue_ReturnsValue()
    {
        var sheet = MakeSheet();
        _eval.Evaluate("=IFNA(42,0)", sheet).Should().Be(new NumberValue(42));
    }

    [Fact]
    public void IfNa_NaError_ReturnsFallback()
    {
        var sheet = MakeSheet();
        _eval.Evaluate("=IFNA(NA(),\"not found\")", sheet).Should().Be(new TextValue("not found"));
    }

    [Fact]
    public void IfNa_DivByZero_ReturnsError_NotFallback()
    {
        // IFNA only catches #N/A, not other errors
        var sheet = MakeSheet();
        var result = _eval.Evaluate("=IFNA(1/0,0)", sheet);
        result.Should().Be(ErrorValue.DivByZero);
    }


    [Fact]
    public void Na_ReturnsNaError()
    {
        var sheet = MakeSheet();
        _eval.Evaluate("=NA()", sheet).Should().Be(ErrorValue.NA);
    }


    [Fact]
    public void IsFunctions_RangeArgument_SpillElementwise()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(1)),
            (2, 1, new TextValue("x")),
            (3, 1, ErrorValue.NA),
            (4, 1, new BoolValue(true)));

        AssertColumn(_eval.Evaluate("=ISNUMBER(A1:A5)", sheet), True(), False(), False(), False(), False());
        AssertColumn(_eval.Evaluate("=ISTEXT(A1:A5)", sheet), False(), True(), False(), False(), False());
        AssertColumn(_eval.Evaluate("=ISERROR(A1:A5)", sheet), False(), False(), True(), False(), False());
        AssertColumn(_eval.Evaluate("=ISNA(A1:A5)", sheet), False(), False(), True(), False(), False());
        AssertColumn(_eval.Evaluate("=ISLOGICAL(A1:A5)", sheet), False(), False(), False(), True(), False());
        AssertColumn(_eval.Evaluate("=ISBLANK(A1:A5)", sheet), False(), False(), False(), False(), True());
    }

    [Fact] public void Xor_TrueTrue_ReturnsFalse() =>
        _eval.Evaluate("=XOR(TRUE,TRUE)", MakeSheet()).Should().Be(new BoolValue(false));

    [Fact] public void Xor_TrueFalse_ReturnsTrue() =>
        _eval.Evaluate("=XOR(TRUE,FALSE)", MakeSheet()).Should().Be(new BoolValue(true));

    [Fact] public void TrueFunc_ReturnsTrue() =>
        _eval.Evaluate("=TRUE()", MakeSheet()).Should().Be(new BoolValue(true));

    [Fact] public void FalseFunc_ReturnsFalse() =>
        _eval.Evaluate("=FALSE()", MakeSheet()).Should().Be(new BoolValue(false));

    [Fact] public void Iseven_4_ReturnsTrue() =>
        _eval.Evaluate("=ISEVEN(4)", MakeSheet()).Should().Be(new BoolValue(true));

    [Fact] public void Isodd_3_ReturnsTrue() =>
        _eval.Evaluate("=ISODD(3)", MakeSheet()).Should().Be(new BoolValue(true));

    [Fact]
    public void Countblank_Range_CountsBlankCells()
    {
        var sheet = MakeSheet((1, 1, new NumberValue(1)), (3, 1, new TextValue("x")));

        _eval.Evaluate("=COUNTBLANK(A1:A3)", sheet).Should().Be(new NumberValue(1));
    }

    [Fact]
    public void Countblank_SingleCellReference_CountsBlankCell()
    {
        var sheet = MakeSheet((1, 1, new NumberValue(1)));

        _eval.Evaluate("=COUNTBLANK(A2)", sheet).Should().Be(new NumberValue(1));
    }

    [Fact]
    public void Countblank_Range_CountsEmptyTextCells()
    {
        var sheet = MakeSheet((1, 1, new TextValue("")), (2, 1, new NumberValue(1)));

        _eval.Evaluate("=COUNTBLANK(A1:A2)", sheet).Should().Be(new NumberValue(1));
    }

    [Fact]
    public void Countblank_Range_IgnoresErrors()
    {
        var sheet = MakeSheet((1, 1, ErrorValue.NA), (2, 1, new TextValue("")));

        _eval.Evaluate("=COUNTBLANK(A1:A2)", sheet).Should().Be(new NumberValue(1));
    }

    [Fact]
    public void Rows_Range_ReturnsRangeHeight()
    {
        var sheet = MakeSheet((2, 2, new NumberValue(1)), (4, 3, new NumberValue(2)));

        _eval.Evaluate("=ROWS(B2:C4)", sheet).Should().Be(new NumberValue(3));
    }

    [Fact]
    public void Columns_Range_ReturnsRangeWidth()
    {
        var sheet = MakeSheet((2, 2, new NumberValue(1)), (4, 4, new NumberValue(2)));

        _eval.Evaluate("=COLUMNS(B2:D4)", sheet).Should().Be(new NumberValue(3));
    }

    [Fact]
    public void Rows_FullColumnReference_ReturnsWorksheetRowCount()
    {
        var sheet = MakeSheet();

        _eval.Evaluate("=ROWS(A:A)", sheet).Should().Be(new NumberValue(CellAddress.MaxRow));
    }

    [Fact]
    public void Columns_FullRowReference_ReturnsWorksheetColumnCount()
    {
        var sheet = MakeSheet();

        _eval.Evaluate("=COLUMNS(1:1)", sheet).Should().Be(new NumberValue(CellAddress.MaxCol));
    }

    [Theory]
    [InlineData("=AREAS(B2:C4)")]
    [InlineData("=AREAS(A:A)")]
    [InlineData("=AREAS(1:1)")]
    public void Areas_SingleReference_ReturnsOneWithoutMaterializingReference(string formula)
    {
        var sheet = MakeSheet();

        _eval.Evaluate(formula, sheet).Should().Be(new NumberValue(1));
    }

    [Fact]
    public void Areas_MissingSheetReference_ReturnsRefError()
    {
        var workbook = new Workbook();
        var sheet = workbook.AddSheet("S");

        _eval.Evaluate("=AREAS(Missing!A:A)", sheet, workbook).Should().Be(ErrorValue.Ref);
    }

    [Fact]
    public void Areas_NonReferenceArgument_ReturnsValueError()
    {
        _eval.Evaluate("=AREAS(1)", MakeSheet()).Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void Row_Range_SpillsRowNumbers()
    {
        var sheet = MakeSheet((2, 2, new NumberValue(1)), (4, 3, new NumberValue(2)));

        var result = _eval.Evaluate("=ROW(B2:C4)", sheet).Should().BeOfType<RangeValue>().Subject;
        result.RowCount.Should().Be(3);
        result.ColCount.Should().Be(1);
        result.Cells[0, 0].Should().Be(new NumberValue(2));
        result.Cells[1, 0].Should().Be(new NumberValue(3));
        result.Cells[2, 0].Should().Be(new NumberValue(4));
    }

    [Fact]
    public void Row_SingleCellReference_ReturnsCellRow()
    {
        var sheet = MakeSheet((5, 2, new NumberValue(1)));

        _eval.Evaluate("=ROW(B5)", sheet).Should().Be(new NumberValue(5));
    }

    [Fact]
    public void Row_NoArgument_ReturnsCurrentCellRow()
    {
        var sheet = MakeSheet();

        _eval.Evaluate("=ROW()", sheet, currentCell: new CellAddress(sheet.Id, 7, 4))
            .Should().Be(new NumberValue(7));
    }

    [Fact]
    public void Column_Range_SpillsColumnNumbers()
    {
        var sheet = MakeSheet((2, 2, new NumberValue(1)), (4, 3, new NumberValue(2)));

        var result = _eval.Evaluate("=COLUMN(B2:C4)", sheet).Should().BeOfType<RangeValue>().Subject;
        result.RowCount.Should().Be(1);
        result.ColCount.Should().Be(2);
        result.Cells[0, 0].Should().Be(new NumberValue(2));
        result.Cells[0, 1].Should().Be(new NumberValue(3));
    }

    [Fact]
    public void Column_SingleCellReference_ReturnsCellColumn()
    {
        var sheet = MakeSheet((5, 2, new NumberValue(1)));

        _eval.Evaluate("=COLUMN(B5)", sheet).Should().Be(new NumberValue(2));
    }

    [Fact]
    public void Column_NoArgument_ReturnsCurrentCellColumn()
    {
        var sheet = MakeSheet();

        _eval.Evaluate("=COLUMN()", sheet, currentCell: new CellAddress(sheet.Id, 7, 4))
            .Should().Be(new NumberValue(4));
    }

    [Fact] public void Indirect_A1String_ReturnsValue()
    {
        var sheet = MakeSheet((1, 1, new NumberValue(42)));
        _eval.Evaluate("=INDIRECT(\"A1\")", sheet).Should().Be(new NumberValue(42));
    }

    [Fact] public void Indirect_A1RangeString_ReturnsRangeValue()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(1)),
            (2, 1, new NumberValue(2)),
            (3, 1, new NumberValue(3)));
        _eval.Evaluate("=SUM(INDIRECT(\"A1:A3\"))", sheet).Should().Be(new NumberValue(6));
    }

    [Fact] public void Indirect_SheetQualifiedA1RangeString_ReturnsRangeValue()
    {
        var wb = new Workbook("T");
        var sheet = wb.AddSheet("S");
        var data = wb.AddSheet("Data");
        data.SetCell(new CellAddress(data.Id, 1, 1), new NumberValue(1));
        data.SetCell(new CellAddress(data.Id, 2, 1), new NumberValue(2));
        data.SetCell(new CellAddress(data.Id, 3, 1), new NumberValue(3));

        _eval.Evaluate("=SUM(INDIRECT(\"Data!A1:A3\"))", sheet, wb).Should().Be(new NumberValue(6));
    }

    [Fact] public void Indirect_UnquotedSheetNameWithSpace_ReturnsRefError()
    {
        var wb = new Workbook("T");
        var sheet = wb.AddSheet("S");
        var data = wb.AddSheet("My Sheet");
        data.SetCell(new CellAddress(data.Id, 1, 1), new NumberValue(42));

        _eval.Evaluate("=INDIRECT(\"My Sheet!A1\")", sheet, wb).Should().Be(ErrorValue.Ref);
        _eval.Evaluate("=INDIRECT(\"'My Sheet'!A1\")", sheet, wb).Should().Be(new NumberValue(42));
    }

    [Fact] public void Indirect_NamedRangeString_ReturnsRangeValue()
    {
        var wb = new Workbook("T");
        var sheet = wb.AddSheet("S");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(1));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(2));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(3));
        wb.DefineNamedRange("MyData", new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 3, 1)));

        _eval.Evaluate("=SUM(INDIRECT(\"MyData\"))", sheet, wb).Should().Be(new NumberValue(6));
    }

    [Fact] public void Indirect_NamedRangeStringWithR1C1Flag_ReturnsRangeValue()
    {
        var wb = new Workbook("T");
        var sheet = wb.AddSheet("S");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(1));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(2));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(3));
        wb.DefineNamedRange("MyData", new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 3, 1)));

        _eval.Evaluate("=SUM(INDIRECT(\"MyData\",FALSE))", sheet, wb).Should().Be(new NumberValue(6));
    }

    [Fact] public void Indirect_R1C1String_ReturnsValue()
    {
        var sheet = MakeSheet((2, 3, new NumberValue(99)));
        _eval.Evaluate("=INDIRECT(\"R2C3\",FALSE)", sheet).Should().Be(new NumberValue(99));
    }

    [Fact] public void Indirect_RelativeR1C1String_ReturnsValueRelativeToCurrentCell()
    {
        var sheet = MakeSheet((4, 6, new NumberValue(123)));

        _eval.Evaluate("=INDIRECT(\"R[-1]C[1]\",FALSE)", sheet, currentCell: new CellAddress(sheet.Id, 5, 5))
            .Should().Be(new NumberValue(123));
    }

    [Fact] public void Indirect_R1C1RangeString_ReturnsRangeValue()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(1)),
            (2, 1, new NumberValue(2)),
            (3, 1, new NumberValue(3)));

        _eval.Evaluate("=SUM(INDIRECT(\"R1C1:R3C1\",FALSE))", sheet)
            .Should().Be(new NumberValue(6));
    }

    [Fact] public void Indirect_A1FullRowString_ReturnsRangeValue()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(1)),
            (1, 2, new NumberValue(2)));

        _eval.Evaluate("=SUM(INDIRECT(\"1:1\"))", sheet)
            .Should().Be(new NumberValue(3));
    }

    [Fact] public void Indirect_A1FullColumnString_ReturnsRangeValue()
    {
        var sheet = MakeSheet(
            (1, 2, new NumberValue(1)),
            (2, 2, new NumberValue(2)));

        _eval.Evaluate("=SUM(INDIRECT(\"B:B\"))", sheet)
            .Should().Be(new NumberValue(3));
    }

    [Fact] public void Indirect_A1FullColumnAggregate_ClampsToUsedRange()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(1)),
            (2, 2, new NumberValue(2)),
            (5, 3, new NumberValue(3)));

        _eval.Evaluate("=SUM(INDIRECT(\"A:C\"))", sheet).Should().Be(new NumberValue(6));
        _eval.Evaluate("=SUM(INDIRECT(\"F:G\"))", sheet).Should().Be(new NumberValue(0));
    }

    [Fact] public void Indirect_A1FullRowAggregate_ClampsToUsedRange()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(1)),
            (2, 2, new NumberValue(2)),
            (10, 3, new NumberValue(3)));

        _eval.Evaluate("=SUM(INDIRECT(\"1:10\"))", sheet).Should().Be(new NumberValue(6));
    }

    [Fact] public void Indirect_SheetQualifiedFullColumnAggregate_ClampsToUsedRange()
    {
        var wb = new Workbook("T");
        var sheet = wb.AddSheet("S");
        var data = wb.AddSheet("Data");
        data.SetCell(new CellAddress(data.Id, 1, 1), new NumberValue(1));
        data.SetCell(new CellAddress(data.Id, 3, 2), new NumberValue(2));

        _eval.Evaluate("=SUM(INDIRECT(\"Data!A:C\"))", sheet, wb).Should().Be(new NumberValue(3));
    }

    [Fact] public void Indirect_A1FullColumnGenericAggregates_ClampToUsedRange()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(2)),
            (3, 1, new NumberValue(4)));

        _eval.Evaluate("=COUNTA(INDIRECT(\"A:A\"))", sheet).Should().Be(new NumberValue(2));
        _eval.Evaluate("=CONCAT(INDIRECT(\"A:C\"))", sheet).Should().Be(new TextValue("24"));
    }

    [Theory]
    [InlineData("=SUM(INDEX(INDIRECT(\"A:A\"),1))")]
    [InlineData("=SUM(INDEX(INDIRECT(\"A:XFD\"),1))")]
    public void Indirect_FullColumnTextRefs_ClampToUsedRangeInsteadOfRef(string formula)
    {
        // INDIRECT("A:A") etc. must clamp its open row extent to the sheet's used range exactly
        // like a direct =A:A reference does (ClampOpenEndedRangeToUsed), rather than unconditionally
        // materializing the nominal 1,048,576-row grid extent and refusing with #REF! even on an
        // otherwise-empty sheet. INDEX (not an aggregate function) forces evaluation through the
        // generic BuildIndirectRange path rather than the SUM/COUNTA/CONCAT literal-argument fast
        // path (TryExpandLiteralIndirectAggregateRange), which already clamped correctly before this
        // fix. On an empty sheet the used range is empty, so INDEX(...,1) reads a single blank cell.
        _eval.Evaluate(formula, MakeSheet()).Should().Be(new NumberValue(0));
    }

    [Fact]
    public void Indirect_FullRowTextRefSpanningEntireGrid_ComputesInsteadOfRefError()
    {
        // "1:1048576" is a full-ROW reference (every row, all columns) whose explicit row bounds
        // already span the entire 1,048,576-row grid — that dimension is fixed by the literal text,
        // not an "open" dimension clamping can shrink. Only the column span is open-ended here, and
        // clamping it down to the (empty) used range leaves 1,048,576 rows x 1 column (column A) —
        // the exact same 1,048,576-cell magnitude as the well-known Excel idiom
        // =OFFSET($A$1,0,0,ROWS($A:$A),1) (R126). This test previously asserted #REF! here and
        // called it correct because that fixed magnitude alone exceeded the old
        // FormulaSafetyLimits.MaxMaterializedRangeCells (1,000,000, deliberately just UNDER one
        // full column's height) — but that encoded the very defect Round 126 fixed: a single
        // worksheet column's worth of cells is trivially valid to materialize in real Excel
        // regardless of which axis is "fixed" vs "open". The cap is now sized comfortably above a
        // full column's height (16,777,216), so this computes instead of erroring.
        _eval.Evaluate("=SUM(INDEX(INDIRECT(\"1:1048576\"),1))", MakeSheet()).Should().Be(new NumberValue(0));
    }

    [Fact]
    public void Indirect_BareFullColumnTextRef_ClampsToUsedRangeInsteadOfRef()
    {
        // A bare (top-level, non-aggregate-wrapped) INDIRECT("A:A") call goes straight to
        // BuiltInFunctions.Indirect -> BuildIndirectRange without ever passing through the
        // SUM/COUNTA/CONCAT literal-argument fast path (that path only special-cases INDIRECT when
        // it is nested as an argument of an aggregate function). Verify it clamps to the used range
        // and returns the real data instead of #REF!.
        var sheet = MakeSheet(
            (1, 1, new NumberValue(2)),
            (3, 1, new NumberValue(4)));

        var result = _eval.Evaluate("=INDIRECT(\"A:A\")", sheet);

        var range = result.Should().BeOfType<RangeValue>().Subject;
        range.RowCount.Should().Be(3);
        range.ColCount.Should().Be(1);
        range.Cells[0, 0].Should().Be(new NumberValue(2));
        range.Cells[2, 0].Should().Be(new NumberValue(4));
    }

    [Fact]
    public void Indirect_ComputedFullColumnArgument_ClampsToUsedRangeInsteadOfRef()
    {
        // INDIRECT's argument coming from a cell reference (not a literal string) bypasses
        // TryBuildLiteralIndirectArguments's fast path entirely, so this exercises
        // BuildIndirectRange's own clamping directly.
        var sheet = MakeSheet(
            (1, 1, new NumberValue(2)),
            (3, 1, new NumberValue(4)),
            (1, 2, new TextValue("A:A")));

        _eval.Evaluate("=SUM(INDIRECT(B1))", sheet).Should().Be(new NumberValue(6));
    }

    [Fact]
    public void NonFastPathFullColumnRanges_ClampToUsedRange_ComputeLikeExcel()
    {
        // Excel evaluates full-column ranges over the populated extent, not the whole 1,048,576-row
        // grid. These non-fast-path aggregates used to return #REF! (refusing to materialize); they
        // now clamp to the used range and compute the same result Excel does.
        var sheet = MakeSheet(
            (1, 1, new NumberValue(2)),
            (2, 1, new NumberValue(4)),
            (3, 1, new NumberValue(6)));

        _eval.Evaluate("=COUNTA(A:A)", sheet).Should().Be(new NumberValue(3));
        _eval.Evaluate("=STDEV(A:A)", sheet).Should().Be(new NumberValue(2));      // sample stdev of 2,4,6
        _eval.Evaluate("=CONCAT(A:A)", sheet).Should().Be(new TextValue("246"));
        _eval.Evaluate("=MAX(A:A)", sheet).Should().Be(new NumberValue(6));
    }

    [Theory]
    [InlineData("=COUNTA(A:A)")]
    [InlineData("=COUNT(A:A)")]
    [InlineData("=SUM(A:A)")]
    public void FullColumnRanges_EmptySheet_ComputeZeroNotRef(string formula)
    {
        // On an empty sheet the used range is empty, so full-column aggregates evaluate over nothing
        // (0), matching Excel — rather than returning #REF!.
        _eval.Evaluate(formula, MakeSheet()).Should().Be(new NumberValue(0));
    }

    [Fact]
    public void RangeMaterializationLimit_ReturnsCatchableError()
    {
        _eval.Evaluate("=IFERROR(COUNTA(A:A),0)", MakeSheet()).Should().Be(new NumberValue(0));
    }

    [Fact] public void Offset_A1FullColumnReferenceWithExplicitHeight_ReturnsRangeValue()
    {
        var sheet = MakeSheet(
            (1, 2, new NumberValue(1)),
            (2, 2, new NumberValue(2)),
            (3, 2, new NumberValue(100)));

        _eval.Evaluate("=SUM(OFFSET(B:B,0,0,2,1))", sheet)
            .Should().Be(new NumberValue(3));
    }

    [Fact] public void Offset_A1FullRowReferenceWithExplicitWidth_ReturnsRangeValue()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(1)),
            (1, 2, new NumberValue(2)),
            (1, 3, new NumberValue(100)));

        _eval.Evaluate("=SUM(OFFSET(1:1,0,0,1,2))", sheet)
            .Should().Be(new NumberValue(3));
    }

    [Fact] public void Offset_SheetQualifiedFullColumnReference_ReturnsRangeValue()
    {
        var wb = new Workbook("T");
        var sheet = wb.AddSheet("S");
        var data = wb.AddSheet("Data");
        data.SetCell(new CellAddress(data.Id, 1, 2), new NumberValue(1));
        data.SetCell(new CellAddress(data.Id, 2, 2), new NumberValue(2));
        data.SetCell(new CellAddress(data.Id, 3, 2), new NumberValue(100));

        _eval.Evaluate("=SUM(OFFSET(Data!B:B,0,0,2,1))", sheet, wb)
            .Should().Be(new NumberValue(3));
    }

    [Fact] public void Indirect_InvalidR1C1String_ReturnsRefError()
    {
        _eval.Evaluate("=INDIRECT(\"R0C1\",FALSE)", MakeSheet()).Should().Be(ErrorValue.Ref);
    }

    [Fact] public void Indirect_A1ArgumentError_PropagatesError() =>
        _eval.Evaluate("=INDIRECT(\"A1\",NA())", MakeSheet()).Should().Be(ErrorValue.NA);

    [Fact] public void Address_AbsoluteRef_ReturnsString() =>
        _eval.Evaluate("=ADDRESS(2,3)", MakeSheet()).Should().Be(new TextValue("$C$2"));

    [Fact] public void Address_RelativeRef_ReturnsString() =>
        _eval.Evaluate("=ADDRESS(2,3,4)", MakeSheet()).Should().Be(new TextValue("C2"));

    [Fact] public void Address_R1C1AbsoluteRef_ReturnsString() =>
        _eval.Evaluate("=ADDRESS(2,3,1,FALSE)", MakeSheet()).Should().Be(new TextValue("R2C3"));

    [Fact] public void Address_R1C1RelativeRef_ReturnsString() =>
        _eval.Evaluate("=ADDRESS(2,3,4,FALSE)", MakeSheet()).Should().Be(new TextValue("R[2]C[3]"));

    [Fact] public void Address_SimpleSheetTextDoesNotAddQuotes() =>
        _eval.Evaluate("=ADDRESS(2,3,1,TRUE,\"Sheet1\")", MakeSheet()).Should().Be(new TextValue("Sheet1!$C$2"));

    [Fact] public void Address_SheetTextEscapesApostrophes() =>
        _eval.Evaluate("=ADDRESS(2,3,1,TRUE,\"O'Brien\")", MakeSheet()).Should().Be(new TextValue("'O''Brien'!$C$2"));

    [Fact] public void Address_ExternalWorkbookBracketSheetText_DoesNotAddQuotes() =>
        _eval.Evaluate("=ADDRESS(2,3,1,FALSE,\"[Book1]Sheet1\")", MakeSheet()).Should().Be(new TextValue("[Book1]Sheet1!R2C3"));

    [Fact] public void Address_ExternalWorkbookBracketSheetTextNeedingQuotes_QuotesWholeText() =>
        _eval.Evaluate("=ADDRESS(2,3,1,FALSE,\"[Book1]Sheet 1\")", MakeSheet()).Should().Be(new TextValue("'[Book1]Sheet 1'!R2C3"));

    [Fact] public void Address_InvalidAbsNum_ReturnsValueError() =>
        _eval.Evaluate("=ADDRESS(2,3,5)", MakeSheet()).Should().Be(ErrorValue.Value);

    [Fact] public void Address_AbsNumError_PropagatesError() =>
        _eval.Evaluate("=ADDRESS(2,3,NA())", MakeSheet()).Should().Be(ErrorValue.NA);

    [Fact] public void Address_A1Error_PropagatesError() =>
        _eval.Evaluate("=ADDRESS(2,3,1,NA())", MakeSheet()).Should().Be(ErrorValue.NA);

    [Fact] public void Address_SheetTextError_PropagatesError() =>
        _eval.Evaluate("=ADDRESS(2,3,1,TRUE,NA())", MakeSheet()).Should().Be(ErrorValue.NA);

    [Fact] public void N_Text_ReturnsZero() =>
        _eval.Evaluate("=N(\"hello\")", MakeSheet()).Should().Be(new NumberValue(0));

    [Fact] public void N_Number_ReturnsNumber() =>
        _eval.Evaluate("=N(42)", MakeSheet()).Should().Be(new NumberValue(42));

    [Fact] public void N_True_ReturnsOne() =>
        _eval.Evaluate("=N(TRUE)", MakeSheet()).Should().Be(new NumberValue(1));

    [Fact]
    public void N_DateTimeCell_ReturnsDateSerial()
    {
        var date = DateTimeValue.FromDateTime(new DateTime(2026, 5, 16));
        var sheet = MakeSheet((1, 1, date));

        _eval.Evaluate("=N(A1)", sheet).Should().Be(new NumberValue(date.Value));
    }


    [Fact]
    public void N_RangeArgument_SpillsElementwise()
    {
        var date = DateTimeValue.FromDateTime(new DateTime(2026, 5, 16));
        var sheet = MakeSheet(
            (1, 1, new NumberValue(42)),
            (2, 1, new TextValue("x")),
            (3, 1, new BoolValue(true)),
            (4, 1, date),
            (5, 1, ErrorValue.NA));

        AssertColumn(
            _eval.Evaluate("=N(A1:A5)", sheet),
            new NumberValue(42),
            new NumberValue(0),
            new NumberValue(1),
            new NumberValue(date.Value),
            ErrorValue.NA);
    }

    [Fact]
    public void Type_Number_Returns1() =>
        _eval.Evaluate("=TYPE(1)", MakeSheet()).Should().Be(new NumberValue(1));

    [Fact]
    public void Type_Text_Returns2() =>
        _eval.Evaluate("=TYPE(\"x\")", MakeSheet()).Should().Be(new NumberValue(2));

    [Fact]
    public void Type_Logical_Returns4() =>
        _eval.Evaluate("=TYPE(TRUE)", MakeSheet()).Should().Be(new NumberValue(4));

    [Fact]
    public void Type_Error_Returns16() =>
        _eval.Evaluate("=TYPE(NA())", MakeSheet()).Should().Be(new NumberValue(16));

    // (TYPE on a range argument is subject to implicit intersection in scalar contexts;
    // tested via TRANSPOSE result indirectly via the dedicated TRANSPOSE tests.)


    [Fact]
    public void ErrorType_DivByZero_Returns2() =>
        _eval.Evaluate("=ERROR.TYPE(1/0)", MakeSheet()).Should().Be(new NumberValue(2));

    [Fact]
    public void ErrorType_Na_Returns7() =>
        _eval.Evaluate("=ERROR.TYPE(NA())", MakeSheet()).Should().Be(new NumberValue(7));

    [Fact]
    public void ErrorType_GettingDataLiteral_Returns8()
    {
        _eval.Evaluate("=ERROR.TYPE(#GETTING_DATA)", MakeSheet()).Should().Be(new NumberValue(8));
    }

    [Fact]
    public void ErrorType_NotAnError_ReturnsNa() =>
        _eval.Evaluate("=ERROR.TYPE(1)", MakeSheet()).Should().Be(ErrorValue.NA);


    [Fact]
    public void ErrorType_RangeArgument_SpillsElementwise()
    {
        var sheet = MakeSheet(
            (1, 1, ErrorValue.DivByZero),
            (2, 1, ErrorValue.NA),
            (3, 1, new NumberValue(1)));

        AssertColumn(
            _eval.Evaluate("=ERROR.TYPE(A1:A3)", sheet),
            new NumberValue(2),
            new NumberValue(7),
            ErrorValue.NA);
    }
}
