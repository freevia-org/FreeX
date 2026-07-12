using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

public partial class PhaseA2FunctionTests
{
    // ── OFFSET ───────────────────────────────────────────────────────────────

    [Fact]
    public void Offset_ZeroOffset_ReturnsBaseCellValue()
    {
        var (wb, sheet) = MakeWb((1, 1, new NumberValue(42)));
        _eval.Evaluate("=OFFSET(A1,0,0)", sheet, wb).Should().Be(new NumberValue(42));
    }

    [Fact]
    public void Offset_RowOffset_ReturnsTargetCell()
    {
        var (wb, sheet) = MakeWb(
            (1, 1, new NumberValue(10)),
            (3, 1, new NumberValue(30)));
        _eval.Evaluate("=OFFSET(A1,2,0)", sheet, wb).Should().Be(new NumberValue(30));
    }

    [Fact]
    public void Offset_ColOffset_ReturnsTargetCell()
    {
        var (wb, sheet) = MakeWb((1, 3, new NumberValue(99)));
        _eval.Evaluate("=OFFSET(A1,0,2)", sheet, wb).Should().Be(new NumberValue(99));
    }

    [Fact]
    public void Offset_OutOfBoundsRowNegative_ReturnsRef()
    {
        var (wb, sheet) = MakeWb();
        _eval.Evaluate("=OFFSET(A1,-1,0)", sheet, wb).Should().Be(ErrorValue.Ref);
    }

    [Fact]
    public void Offset_OutOfBoundsColNegative_ReturnsRef()
    {
        var (wb, sheet) = MakeWb();
        _eval.Evaluate("=OFFSET(A1,0,-1)", sheet, wb).Should().Be(ErrorValue.Ref);
    }

    [Fact]
    public void Offset_FeedsSumproduct_ReturnsRangeSum()
    {
        var (wb, sheet) = MakeWb(
            (2, 2, new NumberValue(1)), (2, 3, new NumberValue(2)),
            (3, 2, new NumberValue(3)), (3, 3, new NumberValue(4)));
        // SUMPRODUCT consumes the 2x2 RangeValue produced by OFFSET.
        _eval.Evaluate("=SUMPRODUCT(OFFSET(A1,1,1,2,2))", sheet, wb).Should().Be(new NumberValue(10));
    }

    [Fact]
    public void Offset_NegativeHeightOrWidth_ReturnsRefError()
    {
        var (wb, sheet) = MakeWb((1, 1, new NumberValue(42)));

        // Excel returns #REF! (not #VALUE!) for a negative height/width in OFFSET, consistent with
        // the zero height/width case below (round-9 finding O31).
        _eval.Evaluate("=OFFSET(A1,0,0,-1,1)", sheet, wb).Should().Be(ErrorValue.Ref);
        _eval.Evaluate("=OFFSET(A1,0,0,1,-1)", sheet, wb).Should().Be(ErrorValue.Ref);
    }

    [Fact]
    public void Offset_ZeroHeightOrWidth_ReturnsRefError()
    {
        var (wb, sheet) = MakeWb((1, 1, new NumberValue(42)));

        _eval.Evaluate("=OFFSET(A1,0,0,0,1)", sheet, wb).Should().Be(ErrorValue.Ref);
        _eval.Evaluate("=OFFSET(A1,0,0,1,0)", sheet, wb).Should().Be(ErrorValue.Ref);
    }

    [Fact]
    public void Offset_IsVolatile()
    {
        BuiltInFunctions.IsVolatile("OFFSET").Should().BeTrue();
    }

    // ── CELL ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Cell_Address_ReturnsAbsoluteAddress()
    {
        var (wb, sheet) = MakeWb();
        _eval.Evaluate("=CELL(\"address\",B3)", sheet, wb).Should().Be(new TextValue("$B$3"));
    }

    [Fact]
    public void Cell_OmittedReference_UsesCurrentFormulaCell()
    {
        var (wb, sheet) = MakeWb((4, 3, new TextValue("current")));
        var currentCell = new CellAddress(sheet.Id, 4, 3);

        _eval.Evaluate("=CELL(\"address\")", sheet, wb, currentCell).Should().Be(new TextValue("$C$4"));
        _eval.Evaluate("=CELL(\"contents\")", sheet, wb, currentCell).Should().Be(new TextValue("current"));
    }

    [Fact]
    public void Cell_Address_OffsetReference_ReturnsTargetAddress()
    {
        var (wb, sheet) = MakeWb();

        _eval.Evaluate("=CELL(\"address\",OFFSET(A1,1,1))", sheet, wb)
            .Should().Be(new TextValue("$B$2"));
    }

    [Fact]
    public void Cell_Address_IndirectReference_ReturnsTargetAddress()
    {
        var (wb, sheet) = MakeWb();

        _eval.Evaluate("=CELL(\"address\",INDIRECT(\"B2\"))", sheet, wb)
            .Should().Be(new TextValue("$B$2"));
    }

    [Fact]
    public void Cell_Row_ReturnsRowNumber()
    {
        var (wb, sheet) = MakeWb();
        _eval.Evaluate("=CELL(\"row\",B5)", sheet, wb).Should().Be(new NumberValue(5));
    }

    [Fact]
    public void Cell_Col_ReturnsColumnNumber()
    {
        var (wb, sheet) = MakeWb();
        _eval.Evaluate("=CELL(\"col\",C1)", sheet, wb).Should().Be(new NumberValue(3));
    }

    [Fact]
    public void Cell_Contents_ReturnsCellValue()
    {
        var (wb, sheet) = MakeWb((1, 1, new NumberValue(123)));
        _eval.Evaluate("=CELL(\"contents\",A1)", sheet, wb).Should().Be(new NumberValue(123));
    }

    [Fact]
    public void Cell_TypeText_ReturnsL()
    {
        var (wb, sheet) = MakeWb((1, 1, new TextValue("hi")));
        _eval.Evaluate("=CELL(\"type\",A1)", sheet, wb).Should().Be(new TextValue("l"));
    }

    [Fact]
    public void Cell_TypeNumber_ReturnsV()
    {
        var (wb, sheet) = MakeWb((1, 1, new NumberValue(1)));
        _eval.Evaluate("=CELL(\"type\",A1)", sheet, wb).Should().Be(new TextValue("v"));
    }

    [Fact]
    public void Cell_TypeBlank_ReturnsB()
    {
        var (wb, sheet) = MakeWb();
        _eval.Evaluate("=CELL(\"type\",A1)", sheet, wb).Should().Be(new TextValue("b"));
    }

    [Fact]
    public void Cell_UnknownInfo_ReturnsValueError()
    {
        var (wb, sheet) = MakeWb();
        _eval.Evaluate("=CELL(\"bogus\",A1)", sheet, wb).Should().Be(ErrorValue.Value);
    }

    [Theory]
    [InlineData("=CELL(\"address\",1+1)")]
    [InlineData("=CELL(\"contents\",\"x\")")]
    public void Cell_NonReferenceSecondArgument_ReturnsValueError(string formula)
    {
        var (wb, sheet) = MakeWb();

        _eval.Evaluate(formula, sheet, wb).Should().Be(ErrorValue.Value);
    }

    [Theory]
    [InlineData(12.4, 12)]
    [InlineData(12.5, 13)]
    public void Cell_Width_ReturnsColumnWidthRoundedToInteger(double width, double expected)
    {
        var (wb, sheet) = MakeWb();
        sheet.ColumnWidths[1] = width;

        _eval.Evaluate("=CELL(\"width\",A1)", sheet, wb).Should().Be(new NumberValue(expected));
    }

    [Theory]
    [InlineData("General", "G")]
    [InlineData("0", "F0")]
    [InlineData("#,##0", ",0")]
    [InlineData("0.00", "F2")]
    [InlineData("#,##0.00", ",2")]
    [InlineData("$#,##0.00", "C2")]
    [InlineData("0%", "P0")]
    [InlineData("0.00%", "P2")]
    [InlineData("0.00E+00", "S2")]
    [InlineData("m/d/yyyy", "D4")]
    [InlineData("h:mm:ss", "D8")]
    public void Cell_Format_ReturnsExcelFormatCode(string numberFormat, string expected)
    {
        var (wb, sheet) = MakeWb((1, 1, new NumberValue(1234.5)));
        var styleId = wb.RegisterStyle(new CellStyle { NumberFormat = numberFormat });
        sheet.GetCell(1, 1)!.StyleId = styleId;

        _eval.Evaluate("=CELL(\"format\",A1)", sheet, wb).Should().Be(new TextValue(expected));
    }

    [Fact]
    public void Cell_Format_UsesStyleOnlyCells()
    {
        var (wb, sheet) = MakeWb();
        var styleId = wb.RegisterStyle(new CellStyle { NumberFormat = "0.00%" });
        sheet.SetStyleOnly(1, 1, styleId);

        _eval.Evaluate("=CELL(\"format\",A1)", sheet, wb).Should().Be(new TextValue("P2"));
    }

    [Theory]
    [InlineData("#,##0;[Red]-#,##0", ",0-")]
    [InlineData("0;(#,##0)", "F0")]
    [InlineData("0;[Red](#,##0)", "F0-")]
    [InlineData("(0);-#,##0", "F0()")]
    [InlineData("(#,##0)", ",0()")]
    [InlineData("0;\"(\"#,##0\")\"", "F0")]
    public void Cell_Format_AppendsExcelDocumentedFormatSuffixes(string numberFormat, string expected)
    {
        var (wb, sheet) = MakeWb((1, 1, new NumberValue(-12)));
        var styleId = wb.RegisterStyle(new CellStyle { NumberFormat = numberFormat });
        sheet.GetCell(1, 1)!.StyleId = styleId;

        _eval.Evaluate("=CELL(\"format\",A1)", sheet, wb).Should().Be(new TextValue(expected));
    }

    [Theory]
    [InlineData("#,##0;[Red]-#,##0", 1)]
    [InlineData("#,##0;[Color10](#,##0)", 1)]
    [InlineData("#,##0;[<=-100]#,##0", 0)]
    [InlineData("#,##0;-#,##0", 0)]
    public void Cell_Color_ReportsNegativeNumberFormatColor(string numberFormat, double expected)
    {
        var (wb, sheet) = MakeWb((1, 1, new NumberValue(-12)));
        var styleId = wb.RegisterStyle(new CellStyle { NumberFormat = numberFormat });
        sheet.GetCell(1, 1)!.StyleId = styleId;

        _eval.Evaluate("=CELL(\"color\",A1)", sheet, wb).Should().Be(new NumberValue(expected));
    }

    [Theory]
    [InlineData("#,##0;(#,##0)", 0)]
    [InlineData("#,##0;[Red](#,##0)", 0)]
    [InlineData("(#,##0);-#,##0", 1)]
    [InlineData("(#,##0)", 1)]
    [InlineData("#,##0;-#,##0", 0)]
    [InlineData("\"(\"#,##0\")\";(#,##0)", 0)]
    public void Cell_Parentheses_ReportsPositiveOrAllValueParentheses(string numberFormat, double expected)
    {
        var (wb, sheet) = MakeWb((1, 1, new NumberValue(-12)));
        var styleId = wb.RegisterStyle(new CellStyle { NumberFormat = numberFormat });
        sheet.GetCell(1, 1)!.StyleId = styleId;

        _eval.Evaluate("=CELL(\"parentheses\",A1)", sheet, wb).Should().Be(new NumberValue(expected));
    }

    [Theory]
    [InlineData(HorizontalAlignment.Left, "'")]
    [InlineData(HorizontalAlignment.Center, "^")]
    [InlineData(HorizontalAlignment.Right, "\"")]
    // General-aligned TEXT is left-justified by Excel, which reports it with the
    // apostrophe label prefix (see R33_InformationCellPrefixFormatTests) -- General
    // does not mean "no prefix" for a text value, only for numbers/blanks.
    [InlineData(HorizontalAlignment.General, "'")]
    [InlineData(HorizontalAlignment.Justify, "")]
    [InlineData(HorizontalAlignment.Distributed, "")]
    public void Cell_Prefix_ReturnsHorizontalAlignmentCode(HorizontalAlignment alignment, string expected)
    {
        var (wb, sheet) = MakeWb((1, 1, new TextValue("text")));
        var styleId = wb.RegisterStyle(new CellStyle { HorizontalAlignment = alignment });
        sheet.GetCell(1, 1)!.StyleId = styleId;

        _eval.Evaluate("=CELL(\"prefix\",A1)", sheet, wb).Should().Be(new TextValue(expected));
    }

    [Fact]
    public void Cell_Prefix_UsesStyleOnlyCells()
    {
        var (wb, sheet) = MakeWb();
        var styleId = wb.RegisterStyle(new CellStyle { HorizontalAlignment = HorizontalAlignment.Center });
        sheet.SetStyleOnly(1, 1, styleId);

        _eval.Evaluate("=CELL(\"prefix\",A1)", sheet, wb).Should().Be(new TextValue("^"));
    }

    [Fact]
    public void Cell_Metadata_UsesReferencedSheetForSheetQualifiedReferences()
    {
        var wb = new Workbook();
        var host = wb.AddSheet("Host");
        var data = wb.AddSheet("Data");
        data.SetCell(new CellAddress(data.Id, 1, 1), new NumberValue(12.34));
        data.ColumnWidths[1] = 14.6;
        data.IsProtected = true;
        var styleId = wb.RegisterStyle(new CellStyle
        {
            NumberFormat = "0.00",
            HorizontalAlignment = HorizontalAlignment.Center
        });
        data.GetCell(1, 1)!.StyleId = styleId;

        _eval.Evaluate("=CELL(\"width\",Data!A1)", host, wb).Should().Be(new NumberValue(15));
        _eval.Evaluate("=CELL(\"format\",Data!A1)", host, wb).Should().Be(new TextValue("F2"));
        _eval.Evaluate("=CELL(\"prefix\",Data!A1)", host, wb).Should().Be(new TextValue("^"));
        _eval.Evaluate("=CELL(\"protect\",Data!A1)", host, wb).Should().Be(new NumberValue(1));
    }

    [Fact]
    public void Cell_Protect_UnprotectedSheetStillReportsLockedStyle()
    {
        var (wb, sheet) = MakeWb();
        var unlocked = wb.RegisterStyle(new CellStyle { Locked = false });
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new NumberValue(2));
        sheet.GetCell(1, 2)!.StyleId = unlocked;

        _eval.Evaluate("=CELL(\"protect\",A1)", sheet, wb).Should().Be(new NumberValue(1));
        _eval.Evaluate("=CELL(\"protect\",B1)", sheet, wb).Should().Be(new NumberValue(0));
    }

    // ── INFO ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Info_NumFile_ReturnsSheetCount()
    {
        var (wb, sheet) = MakeWb();
        wb.AddSheet("S2");
        _eval.Evaluate("=INFO(\"numfile\")", sheet, wb).Should().Be(new NumberValue(2));
    }

    [Fact]
    public void Info_Release_ReturnsSixteenZero()
    {
        var (wb, sheet) = MakeWb();
        _eval.Evaluate("=INFO(\"release\")", sheet, wb).Should().Be(new TextValue("16.0"));
    }

    [Fact]
    public void Info_System_ReturnsPcDos()
    {
        var (wb, sheet) = MakeWb();
        _eval.Evaluate("=INFO(\"system\")", sheet, wb).Should().Be(new TextValue("pcdos"));
    }

    [Fact]
    public void Info_Origin_ReturnsAbsoluteVisibleCellReference()
    {
        var (wb, sheet) = MakeWb();
        _eval.Evaluate("=INFO(\"origin\")", sheet, wb).Should().Be(new TextValue("$A:$A$1"));
    }

    [Fact]
    public void Info_Directory_ReturnsCurrentFolderPath()
    {
        var (wb, sheet) = MakeWb();
        var expected = Environment.CurrentDirectory;
        if (!System.IO.Path.EndsInDirectorySeparator(expected))
            expected += System.IO.Path.DirectorySeparatorChar;

        _eval.Evaluate("=INFO(\"directory\")", sheet, wb).Should().Be(new TextValue(expected));
    }

    [Fact]
    public void Info_Recalc_AutomaticByDefault()
    {
        var (wb, sheet) = MakeWb();
        _eval.Evaluate("=INFO(\"recalc\")", sheet, wb).Should().Be(new TextValue("Automatic"));
    }

    [Fact]
    public void Info_Recalc_ManualWhenSet()
    {
        var (wb, sheet) = MakeWb();
        wb.CalculationMode = WorkbookCalculationMode.Manual;
        _eval.Evaluate("=INFO(\"recalc\")", sheet, wb).Should().Be(new TextValue("Manual"));
    }

    [Fact]
    public void Info_Unknown_ReturnsValueError()
    {
        var (wb, sheet) = MakeWb();
        _eval.Evaluate("=INFO(\"bogus\")", sheet, wb).Should().Be(ErrorValue.Value);
    }
}
