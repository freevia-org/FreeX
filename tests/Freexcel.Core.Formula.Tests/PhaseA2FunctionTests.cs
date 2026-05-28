using Freexcel.Core.Formula;
using Freexcel.Core.Model;
using FluentAssertions;

namespace Freexcel.Core.Formula.Tests;

/// <summary>
/// Tests for Phase A2 functions:
///   ISREF, ISFORMULA, FORMULATEXT, OFFSET, CELL, INFO, AGGREGATE, CONVERT.
/// </summary>
public class PhaseA2FunctionTests
{
    private readonly FormulaEvaluator _eval = new();

    private static (Workbook wb, Sheet sheet) MakeWb(params (int row, int col, ScalarValue val)[] cells)
    {
        var wb = new Workbook();
        var sheet = wb.AddSheet("S");
        foreach (var (r, c, v) in cells)
            sheet.SetCell(new CellAddress(sheet.Id, (uint)r, (uint)c), v);
        return (wb, sheet);
    }

    // ── ISREF ────────────────────────────────────────────────────────────────

    [Fact]
    public void IsRef_CellRef_ReturnsTrue()
    {
        var (wb, sheet) = MakeWb();
        _eval.Evaluate("=ISREF(A1)", sheet, wb).Should().Be(new BoolValue(true));
    }

    [Fact]
    public void IsRef_RangeRef_ReturnsTrue()
    {
        var (wb, sheet) = MakeWb();
        _eval.Evaluate("=ISREF(A1:B3)", sheet, wb).Should().Be(new BoolValue(true));
    }

    [Fact]
    public void IsRef_NumberLiteral_ReturnsFalse()
    {
        var (wb, sheet) = MakeWb();
        _eval.Evaluate("=ISREF(1)", sheet, wb).Should().Be(new BoolValue(false));
    }

    [Fact]
    public void IsRef_UndefinedName_ReturnsFalse()
    {
        var (wb, sheet) = MakeWb();
        _eval.Evaluate("=ISREF(SomeUndefinedName)", sheet, wb).Should().Be(new BoolValue(false));
    }

    [Fact]
    public void IsRef_DefinedName_ReturnsTrue()
    {
        var (wb, sheet) = MakeWb();
        wb.DefineNamedRange("MyData", new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 3)));
        _eval.Evaluate("=ISREF(MyData)", sheet, wb).Should().Be(new BoolValue(true));
    }

    [Fact]
    public void IsRef_OffsetReference_ReturnsTrue()
    {
        var (wb, sheet) = MakeWb((1, 1, new NumberValue(10)));

        _eval.Evaluate("=ISREF(OFFSET(A1,0,0))", sheet, wb).Should().Be(new BoolValue(true));
    }

    [Fact]
    public void IsRef_IndirectReference_ReturnsTrue()
    {
        var (wb, sheet) = MakeWb((1, 1, new NumberValue(10)));

        _eval.Evaluate("=ISREF(INDIRECT(\"A1\"))", sheet, wb).Should().Be(new BoolValue(true));
    }

    // ── ISFORMULA ────────────────────────────────────────────────────────────

    [Fact]
    public void IsFormula_FormulaCell_ReturnsTrue()
    {
        var (wb, sheet) = MakeWb();
        sheet.SetFormula(new CellAddress(sheet.Id, 1, 1), "1+2");
        _eval.Evaluate("=ISFORMULA(A1)", sheet, wb).Should().Be(new BoolValue(true));
    }

    [Fact]
    public void IsFormula_ValueCell_ReturnsFalse()
    {
        var (wb, sheet) = MakeWb((1, 1, new NumberValue(42)));
        _eval.Evaluate("=ISFORMULA(A1)", sheet, wb).Should().Be(new BoolValue(false));
    }

    [Fact]
    public void IsFormula_EmptyCell_ReturnsFalse()
    {
        var (wb, sheet) = MakeWb();
        _eval.Evaluate("=ISFORMULA(A1)", sheet, wb).Should().Be(new BoolValue(false));
    }

    [Fact]
    public void IsFormula_Number_ReturnsValueError()
    {
        var (wb, sheet) = MakeWb();
        _eval.Evaluate("=ISFORMULA(1)", sheet, wb).Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void IsFormula_OffsetReference_InspectsTargetCell()
    {
        var (wb, sheet) = MakeWb();
        sheet.SetFormula(new CellAddress(sheet.Id, 2, 2), "1+2");

        _eval.Evaluate("=ISFORMULA(OFFSET(A1,1,1))", sheet, wb).Should().Be(new BoolValue(true));
    }

    [Fact]
    public void IsFormula_IndirectReference_InspectsTargetCell()
    {
        var (wb, sheet) = MakeWb();
        sheet.SetFormula(new CellAddress(sheet.Id, 2, 2), "1+2");

        _eval.Evaluate("=ISFORMULA(INDIRECT(\"B2\"))", sheet, wb).Should().Be(new BoolValue(true));
    }

    // ── FORMULATEXT ──────────────────────────────────────────────────────────

    [Fact]
    public void FormulaText_FormulaCell_ReturnsFormulaWithEquals()
    {
        var (wb, sheet) = MakeWb();
        sheet.SetFormula(new CellAddress(sheet.Id, 1, 1), "SUM(B1:B3)");
        _eval.Evaluate("=FORMULATEXT(A1)", sheet, wb).Should().Be(new TextValue("=SUM(B1:B3)"));
    }

    [Fact]
    public void FormulaText_ValueCell_ReturnsNA()
    {
        var (wb, sheet) = MakeWb((1, 1, new NumberValue(42)));
        _eval.Evaluate("=FORMULATEXT(A1)", sheet, wb).Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void FormulaText_NonRef_ReturnsNA()
    {
        var (wb, sheet) = MakeWb();
        _eval.Evaluate("=FORMULATEXT(1)", sheet, wb).Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void FormulaText_OffsetReference_ReturnsTargetFormulaWithEquals()
    {
        var (wb, sheet) = MakeWb();
        sheet.SetFormula(new CellAddress(sheet.Id, 2, 2), "SUM(C1:C3)");

        _eval.Evaluate("=FORMULATEXT(OFFSET(A1,1,1))", sheet, wb)
            .Should().Be(new TextValue("=SUM(C1:C3)"));
    }

    [Fact]
    public void FormulaText_IndirectReference_ReturnsTargetFormulaWithEquals()
    {
        var (wb, sheet) = MakeWb();
        sheet.SetFormula(new CellAddress(sheet.Id, 2, 2), "SUM(C1:C3)");

        _eval.Evaluate("=FORMULATEXT(INDIRECT(\"B2\"))", sheet, wb)
            .Should().Be(new TextValue("=SUM(C1:C3)"));
    }

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
    public void Offset_NegativeHeightOrWidth_ReturnsValueError()
    {
        var (wb, sheet) = MakeWb((1, 1, new NumberValue(42)));

        _eval.Evaluate("=OFFSET(A1,0,0,-1,1)", sheet, wb).Should().Be(ErrorValue.Value);
        _eval.Evaluate("=OFFSET(A1,0,0,1,-1)", sheet, wb).Should().Be(ErrorValue.Value);
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
    [InlineData(HorizontalAlignment.General, "")]
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

    [Fact]
    public void GetPivotData_NoPivotAtReference_ReturnsRef()
    {
        var (wb, sheet) = MakeWb();
        _eval.Evaluate("=GETPIVOTDATA(\"Sum of Amount\",A1)", sheet, wb).Should().Be(ErrorValue.Ref);
    }

    [Fact]
    public void GetPivotData_RowFieldItem_ReturnsPivotValue()
    {
        var (wb, sheet) = MakeWb(
            (1, 1, new TextValue("Region")),
            (1, 2, new TextValue("Amount")),
            (2, 5, new TextValue("Region")),
            (2, 6, new TextValue("Sum of Amount")),
            (3, 5, new TextValue("East")),
            (3, 6, new NumberValue(25)),
            (4, 5, new TextValue("West")),
            (4, 6, new NumberValue(45)),
            (5, 5, new TextValue("Grand Total")),
            (5, 6, new NumberValue(70)));
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 2)),
            TargetRange = new GridRange(new CellAddress(sheet.Id, 2, 5), new CellAddress(sheet.Id, 5, 6))
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(1, "Sum of Amount", "sum"));
        sheet.PivotTables.Add(pivot);

        _eval.Evaluate("=GETPIVOTDATA(\"Sum of Amount\",E2,\"Region\",\"West\")", sheet, wb)
            .Should()
            .Be(new NumberValue(45));
    }

    [Fact]
    public void GetPivotData_RowAndColumnFieldItems_ReturnsMatrixValue()
    {
        var (wb, sheet) = MakeWb(
            (1, 1, new TextValue("Region")),
            (1, 2, new TextValue("Quarter")),
            (1, 3, new TextValue("Amount")),
            (2, 5, new TextValue("Region")),
            (2, 6, new TextValue("Q1")),
            (2, 7, new TextValue("Q2")),
            (2, 8, new TextValue("Grand Total")),
            (3, 5, new TextValue("East")),
            (3, 6, new NumberValue(10)),
            (3, 7, new NumberValue(15)),
            (3, 8, new NumberValue(25)),
            (4, 5, new TextValue("West")),
            (4, 6, new NumberValue(20)),
            (4, 7, new NumberValue(25)),
            (4, 8, new NumberValue(45)),
            (5, 5, new TextValue("Grand Total")),
            (5, 6, new NumberValue(30)),
            (5, 7, new NumberValue(40)),
            (5, 8, new NumberValue(70)));
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 3)),
            TargetRange = new GridRange(new CellAddress(sheet.Id, 2, 5), new CellAddress(sheet.Id, 5, 8))
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.ColumnFields.Add(new PivotFieldModel(1));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));
        sheet.PivotTables.Add(pivot);

        _eval.Evaluate("=GETPIVOTDATA(\"Sum of Amount\",E2,\"Region\",\"East\",\"Quarter\",\"Q2\")", sheet, wb)
            .Should()
            .Be(new NumberValue(15));
    }

    [Fact]
    public void GetPivotData_RowFieldOnlyInMatrix_ReturnsRowGrandTotal()
    {
        var (wb, sheet) = MakeWb(
            (1, 1, new TextValue("Region")),
            (1, 2, new TextValue("Quarter")),
            (1, 3, new TextValue("Amount")),
            (2, 5, new TextValue("Region")),
            (2, 6, new TextValue("Q1")),
            (2, 7, new TextValue("Q2")),
            (2, 8, new TextValue("Grand Total")),
            (3, 5, new TextValue("East")),
            (3, 6, new NumberValue(10)),
            (3, 7, new NumberValue(15)),
            (3, 8, new NumberValue(25)),
            (4, 5, new TextValue("West")),
            (4, 6, new NumberValue(20)),
            (4, 7, new NumberValue(25)),
            (4, 8, new NumberValue(45)),
            (5, 5, new TextValue("Grand Total")),
            (5, 6, new NumberValue(30)),
            (5, 7, new NumberValue(40)),
            (5, 8, new NumberValue(70)));
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 3)),
            TargetRange = new GridRange(new CellAddress(sheet.Id, 2, 5), new CellAddress(sheet.Id, 5, 8))
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.ColumnFields.Add(new PivotFieldModel(1));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));
        sheet.PivotTables.Add(pivot);

        _eval.Evaluate("=GETPIVOTDATA(\"Sum of Amount\",E2,\"Region\",\"East\")", sheet, wb)
            .Should()
            .Be(new NumberValue(25));
    }

    [Fact]
    public void GetPivotData_OuterRowFieldOnly_ReturnsSubtotal()
    {
        var (wb, sheet) = MakeWb(
            (1, 1, new TextValue("Region")),
            (1, 2, new TextValue("Quarter")),
            (1, 3, new TextValue("Amount")),
            (2, 5, new TextValue("Region")),
            (2, 6, new TextValue("Quarter")),
            (2, 7, new TextValue("Sum of Amount")),
            (3, 5, new TextValue("East")),
            (3, 6, new TextValue("Q1")),
            (3, 7, new NumberValue(10)),
            (4, 5, new TextValue("East")),
            (4, 6, new TextValue("Q2")),
            (4, 7, new NumberValue(15)),
            (5, 5, new TextValue("East Total")),
            (5, 7, new NumberValue(25)),
            (6, 5, new TextValue("West")),
            (6, 6, new TextValue("Q1")),
            (6, 7, new NumberValue(20)),
            (7, 5, new TextValue("West")),
            (7, 6, new TextValue("Q2")),
            (7, 7, new NumberValue(25)),
            (8, 5, new TextValue("West Total")),
            (8, 7, new NumberValue(45)),
            (9, 5, new TextValue("Grand Total")),
            (9, 7, new NumberValue(70)));
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 3)),
            TargetRange = new GridRange(new CellAddress(sheet.Id, 2, 5), new CellAddress(sheet.Id, 9, 7)),
            ShowSubtotals = true
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.RowFields.Add(new PivotFieldModel(1));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));
        sheet.PivotTables.Add(pivot);

        _eval.Evaluate("=GETPIVOTDATA(\"Sum of Amount\",E2,\"Region\",\"East\")", sheet, wb)
            .Should()
            .Be(new NumberValue(25));
    }

    [Fact]
    public void GetPivotData_CrossSheetPivotReference_ReturnsPivotValue()
    {
        var wb = new Workbook();
        var pivotSheet = wb.AddSheet("Pivot");
        var formulaSheet = wb.AddSheet("Report");
        pivotSheet.SetCell(new CellAddress(pivotSheet.Id, 1, 1), new TextValue("Region"));
        pivotSheet.SetCell(new CellAddress(pivotSheet.Id, 1, 2), new TextValue("Amount"));
        pivotSheet.SetCell(new CellAddress(pivotSheet.Id, 2, 5), new TextValue("Region"));
        pivotSheet.SetCell(new CellAddress(pivotSheet.Id, 2, 6), new TextValue("Sum of Amount"));
        pivotSheet.SetCell(new CellAddress(pivotSheet.Id, 3, 5), new TextValue("East"));
        pivotSheet.SetCell(new CellAddress(pivotSheet.Id, 3, 6), new NumberValue(25));
        pivotSheet.SetCell(new CellAddress(pivotSheet.Id, 4, 5), new TextValue("West"));
        pivotSheet.SetCell(new CellAddress(pivotSheet.Id, 4, 6), new NumberValue(45));
        pivotSheet.SetCell(new CellAddress(pivotSheet.Id, 5, 5), new TextValue("Grand Total"));
        pivotSheet.SetCell(new CellAddress(pivotSheet.Id, 5, 6), new NumberValue(70));
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = new GridRange(new CellAddress(pivotSheet.Id, 1, 1), new CellAddress(pivotSheet.Id, 5, 2)),
            TargetRange = new GridRange(new CellAddress(pivotSheet.Id, 2, 5), new CellAddress(pivotSheet.Id, 5, 6))
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(1, "Sum of Amount", "sum"));
        pivotSheet.PivotTables.Add(pivot);

        _eval.Evaluate("=GETPIVOTDATA(\"Sum of Amount\",Pivot!E2,\"Region\",\"West\")", formulaSheet, wb)
            .Should()
            .Be(new NumberValue(45));
    }

    [Fact]
    public void GetPivotData_SheetQualifiedReferenceIgnoresSameCoordinatesOnFormulaSheet()
    {
        var wb = new Workbook();
        var pivotSheet = wb.AddSheet("Pivot");
        var formulaSheet = wb.AddSheet("Report");

        pivotSheet.SetCell(new CellAddress(pivotSheet.Id, 1, 1), new TextValue("Region"));
        pivotSheet.SetCell(new CellAddress(pivotSheet.Id, 1, 2), new TextValue("Amount"));
        pivotSheet.SetCell(new CellAddress(pivotSheet.Id, 2, 5), new TextValue("Region"));
        pivotSheet.SetCell(new CellAddress(pivotSheet.Id, 2, 6), new TextValue("Sum of Amount"));
        pivotSheet.SetCell(new CellAddress(pivotSheet.Id, 3, 5), new TextValue("East"));
        pivotSheet.SetCell(new CellAddress(pivotSheet.Id, 3, 6), new NumberValue(25));
        pivotSheet.SetCell(new CellAddress(pivotSheet.Id, 4, 5), new TextValue("Grand Total"));
        pivotSheet.SetCell(new CellAddress(pivotSheet.Id, 4, 6), new NumberValue(25));

        formulaSheet.SetCell(new CellAddress(formulaSheet.Id, 1, 1), new TextValue("Region"));
        formulaSheet.SetCell(new CellAddress(formulaSheet.Id, 1, 2), new TextValue("Amount"));
        formulaSheet.SetCell(new CellAddress(formulaSheet.Id, 2, 5), new TextValue("Region"));
        formulaSheet.SetCell(new CellAddress(formulaSheet.Id, 2, 6), new TextValue("Sum of Amount"));
        formulaSheet.SetCell(new CellAddress(formulaSheet.Id, 3, 5), new TextValue("East"));
        formulaSheet.SetCell(new CellAddress(formulaSheet.Id, 3, 6), new NumberValue(999));
        formulaSheet.SetCell(new CellAddress(formulaSheet.Id, 4, 5), new TextValue("Grand Total"));
        formulaSheet.SetCell(new CellAddress(formulaSheet.Id, 4, 6), new NumberValue(999));

        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = new GridRange(new CellAddress(pivotSheet.Id, 1, 1), new CellAddress(pivotSheet.Id, 4, 2)),
            TargetRange = new GridRange(new CellAddress(pivotSheet.Id, 2, 5), new CellAddress(pivotSheet.Id, 4, 6))
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(1, "Sum of Amount", "sum"));
        pivotSheet.PivotTables.Add(pivot);

        var localPivot = new PivotTableModel
        {
            Name = "PivotTable2",
            CacheId = 2,
            SourceRange = new GridRange(new CellAddress(formulaSheet.Id, 1, 1), new CellAddress(formulaSheet.Id, 4, 2)),
            TargetRange = new GridRange(new CellAddress(formulaSheet.Id, 2, 5), new CellAddress(formulaSheet.Id, 4, 6))
        };
        localPivot.RowFields.Add(new PivotFieldModel(0));
        localPivot.DataFields.Add(new PivotDataFieldModel(1, "Sum of Amount", "sum"));
        formulaSheet.PivotTables.Add(localPivot);

        _eval.Evaluate("=GETPIVOTDATA(\"Sum of Amount\",Pivot!E2,\"Region\",\"East\")", formulaSheet, wb)
            .Should()
            .Be(new NumberValue(25));
    }

    [Fact]
    public void GetPivotData_PageFieldItem_MustMatchSelectedPageFilter()
    {
        var (wb, sheet) = MakeWb(
            (1, 1, new TextValue("Region")),
            (1, 2, new TextValue("Year")),
            (1, 3, new TextValue("Amount")),
            (2, 5, new TextValue("Region")),
            (2, 6, new TextValue("Sum of Amount")),
            (3, 5, new TextValue("East")),
            (3, 6, new NumberValue(25)),
            (4, 5, new TextValue("West")),
            (4, 6, new NumberValue(45)),
            (5, 5, new TextValue("Grand Total")),
            (5, 6, new NumberValue(70)));
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 3)),
            TargetRange = new GridRange(new CellAddress(sheet.Id, 2, 5), new CellAddress(sheet.Id, 5, 6))
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.PageFields.Add(new PivotFieldModel(1, SelectedItem: "2026"));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));
        sheet.PivotTables.Add(pivot);

        _eval.Evaluate("=GETPIVOTDATA(\"Sum of Amount\",E2,\"Region\",\"East\",\"Year\",\"2026\")", sheet, wb)
            .Should()
            .Be(new NumberValue(25));
        _eval.Evaluate("=GETPIVOTDATA(\"Sum of Amount\",E2,\"Region\",\"East\",\"Year\",\"2025\")", sheet, wb)
            .Should()
            .Be(ErrorValue.Ref);
    }

    // ── AGGREGATE ────────────────────────────────────────────────────────────

    [Fact]
    public void GetPivotData_UnknownField_ReturnsRef()
    {
        var (wb, sheet) = MakeWb(
            (1, 1, new TextValue("Region")),
            (1, 2, new TextValue("Amount")),
            (2, 5, new TextValue("Region")),
            (2, 6, new TextValue("Sum of Amount")),
            (3, 5, new TextValue("East")),
            (3, 6, new NumberValue(25)),
            (4, 5, new TextValue("Grand Total")),
            (4, 6, new NumberValue(25)));
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 2)),
            TargetRange = new GridRange(new CellAddress(sheet.Id, 2, 5), new CellAddress(sheet.Id, 4, 6))
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(1, "Sum of Amount", "sum"));
        sheet.PivotTables.Add(pivot);

        _eval.Evaluate("=GETPIVOTDATA(\"Sum of Amount\",E2,\"Bogus\",\"East\")", sheet, wb)
            .Should()
            .Be(ErrorValue.Ref);
    }

    [Fact]
    public void GetPivotData_ConflictingDuplicateField_ReturnsRef()
    {
        var (wb, sheet) = MakeWb(
            (1, 1, new TextValue("Region")),
            (1, 2, new TextValue("Amount")),
            (2, 5, new TextValue("Region")),
            (2, 6, new TextValue("Sum of Amount")),
            (3, 5, new TextValue("East")),
            (3, 6, new NumberValue(25)),
            (4, 5, new TextValue("West")),
            (4, 6, new NumberValue(45)),
            (5, 5, new TextValue("Grand Total")),
            (5, 6, new NumberValue(70)));
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 2)),
            TargetRange = new GridRange(new CellAddress(sheet.Id, 2, 5), new CellAddress(sheet.Id, 5, 6))
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(1, "Sum of Amount", "sum"));
        sheet.PivotTables.Add(pivot);

        _eval.Evaluate("=GETPIVOTDATA(\"Sum of Amount\",E2,\"Region\",\"East\",\"Region\",\"West\")", sheet, wb)
            .Should()
            .Be(ErrorValue.Ref);
    }

    [Fact]
    public void GetPivotData_CompactNestedRowFields_ReturnsLeafValue()
    {
        var (wb, sheet) = MakeWb(
            (1, 1, new TextValue("Region")),
            (1, 2, new TextValue("Quarter")),
            (1, 3, new TextValue("Amount")),
            (2, 5, new TextValue("Row Labels")),
            (2, 6, new TextValue("Sum of Amount")),
            (3, 5, new TextValue("East Q1")),
            (3, 6, new NumberValue(10)),
            (4, 5, new TextValue("East Q2")),
            (4, 6, new NumberValue(15)),
            (5, 5, new TextValue("West Q1")),
            (5, 6, new NumberValue(20)),
            (6, 5, new TextValue("West Q2")),
            (6, 6, new NumberValue(25)),
            (7, 5, new TextValue("Grand Total")),
            (7, 6, new NumberValue(70)));
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 3)),
            TargetRange = new GridRange(new CellAddress(sheet.Id, 2, 5), new CellAddress(sheet.Id, 7, 6)),
            ReportLayout = PivotReportLayout.Compact
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.RowFields.Add(new PivotFieldModel(1));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));
        sheet.PivotTables.Add(pivot);

        _eval.Evaluate("=GETPIVOTDATA(\"Sum of Amount\",E2,\"Region\",\"East\",\"Quarter\",\"Q2\")", sheet, wb)
            .Should()
            .Be(new NumberValue(15));
    }

    [Fact]
    public void GetPivotData_MultipleDataFieldsWithColumnItem_ReturnsRequestedDataField()
    {
        var (wb, sheet) = MakeWb(
            (1, 1, new TextValue("Region")),
            (1, 2, new TextValue("Quarter")),
            (1, 3, new TextValue("Amount")),
            (2, 5, new TextValue("Region")),
            (2, 6, new TextValue("Q1")),
            (2, 7, new TextValue("Q1 Count of Amount")),
            (2, 8, new TextValue("Q2")),
            (2, 9, new TextValue("Q2 Count of Amount")),
            (2, 10, new TextValue("Grand Total")),
            (2, 11, new TextValue("Grand Total Count of Amount")),
            (3, 5, new TextValue("East")),
            (3, 6, new NumberValue(10)),
            (3, 7, new NumberValue(1)),
            (3, 8, new NumberValue(15)),
            (3, 9, new NumberValue(1)),
            (3, 10, new NumberValue(25)),
            (3, 11, new NumberValue(2)),
            (4, 5, new TextValue("Grand Total")),
            (4, 6, new NumberValue(10)),
            (4, 7, new NumberValue(1)),
            (4, 8, new NumberValue(15)),
            (4, 9, new NumberValue(1)),
            (4, 10, new NumberValue(25)),
            (4, 11, new NumberValue(2)));
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 3)),
            TargetRange = new GridRange(new CellAddress(sheet.Id, 2, 5), new CellAddress(sheet.Id, 4, 11))
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.ColumnFields.Add(new PivotFieldModel(1));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Count of Amount", "count"));
        sheet.PivotTables.Add(pivot);

        _eval.Evaluate("=GETPIVOTDATA(\"Count of Amount\",E2,\"Region\",\"East\",\"Quarter\",\"Q2\")", sheet, wb)
            .Should()
            .Be(new NumberValue(1));
    }

    [Fact]
    public void Aggregate_Sum_BasicRange()
    {
        var (wb, sheet) = MakeWb(
            (1, 1, new NumberValue(1)),
            (2, 1, new NumberValue(2)),
            (3, 1, new NumberValue(3)));
        // function 9 = SUM, options 4 = ignore nothing
        _eval.Evaluate("=AGGREGATE(9,4,A1:A3)", sheet, wb).Should().Be(new NumberValue(6));
    }

    [Fact]
    public void Aggregate_Sum_Option5IgnoresHiddenRows()
    {
        var (wb, sheet) = MakeWb(
            (1, 1, new NumberValue(1)),
            (2, 1, new NumberValue(2)),
            (3, 1, new NumberValue(3)));
        sheet.HiddenRows.Add(2);

        _eval.Evaluate("=AGGREGATE(9,5,A1:A3)", sheet, wb).Should().Be(new NumberValue(4));
    }

    [Fact]
    public void Aggregate_Sum_Option4IncludesHiddenRows()
    {
        var (wb, sheet) = MakeWb(
            (1, 1, new NumberValue(1)),
            (2, 1, new NumberValue(2)),
            (3, 1, new NumberValue(3)));
        sheet.HiddenRows.Add(2);

        _eval.Evaluate("=AGGREGATE(9,4,A1:A3)", sheet, wb).Should().Be(new NumberValue(6));
    }

    [Fact]
    public void Aggregate_Sum_Option0IgnoresNestedSubtotalFormulaCell()
    {
        var (wb, sheet) = MakeWb(
            (1, 1, new NumberValue(10)),
            (3, 1, new NumberValue(30)));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new Cell
        {
            FormulaText = "SUBTOTAL(9,A1:A1)",
            Value = new NumberValue(10)
        });

        _eval.Evaluate("=AGGREGATE(9,0,A1:A3)", sheet, wb).Should().Be(new NumberValue(40));
    }

    [Fact]
    public void Aggregate_Sum_Option4IncludesNestedSubtotalFormulaCell()
    {
        var (wb, sheet) = MakeWb(
            (1, 1, new NumberValue(10)),
            (3, 1, new NumberValue(30)));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new Cell
        {
            FormulaText = "SUBTOTAL(9,A1:A1)",
            Value = new NumberValue(10)
        });

        _eval.Evaluate("=AGGREGATE(9,4,A1:A3)", sheet, wb).Should().Be(new NumberValue(50));
    }

    [Fact]
    public void Aggregate_Sum_IgnoresErrorsWhenOption6()
    {
        var (wb, sheet) = MakeWb(
            (1, 1, new NumberValue(1)),
            (2, 1, ErrorValue.DivByZero),
            (3, 1, new NumberValue(3)));
        _eval.Evaluate("=AGGREGATE(9,6,A1:A3)", sheet, wb).Should().Be(new NumberValue(4));
    }

    [Fact]
    public void Aggregate_Sum_PropagatesErrorsWhenOption4()
    {
        var (wb, sheet) = MakeWb(
            (1, 1, new NumberValue(1)),
            (2, 1, ErrorValue.DivByZero));
        _eval.Evaluate("=AGGREGATE(9,4,A1:A2)", sheet, wb).Should().Be(ErrorValue.DivByZero);
    }

    [Fact]
    public void Aggregate_Average_BasicRange()
    {
        var (wb, sheet) = MakeWb(
            (1, 1, new NumberValue(10)),
            (2, 1, new NumberValue(20)));
        _eval.Evaluate("=AGGREGATE(1,4,A1:A2)", sheet, wb).Should().Be(new NumberValue(15));
    }

    [Fact]
    public void Aggregate_Large_RequiresK()
    {
        var (wb, sheet) = MakeWb(
            (1, 1, new NumberValue(1)),
            (2, 1, new NumberValue(2)),
            (3, 1, new NumberValue(3)));
        _eval.Evaluate("=AGGREGATE(14,4,A1:A3,1)", sheet, wb).Should().Be(new NumberValue(3));
    }

    [Fact]
    public void Aggregate_Small_WithK()
    {
        var (wb, sheet) = MakeWb(
            (1, 1, new NumberValue(5)),
            (2, 1, new NumberValue(1)),
            (3, 1, new NumberValue(3)));
        _eval.Evaluate("=AGGREGATE(15,4,A1:A3,2)", sheet, wb).Should().Be(new NumberValue(3));
    }

    [Fact]
    public void Aggregate_InvalidFuncNum_ReturnsValueError()
    {
        var (wb, sheet) = MakeWb((1, 1, new NumberValue(1)));
        _eval.Evaluate("=AGGREGATE(20,4,A1)", sheet, wb).Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void Aggregate_Count()
    {
        var (wb, sheet) = MakeWb(
            (1, 1, new NumberValue(1)),
            (2, 1, new TextValue("x")),
            (3, 1, new NumberValue(3)));
        _eval.Evaluate("=AGGREGATE(2,4,A1:A3)", sheet, wb).Should().Be(new NumberValue(2));
    }

    [Fact]
    public void Aggregate_Max()
    {
        var (wb, sheet) = MakeWb(
            (1, 1, new NumberValue(5)),
            (2, 1, new NumberValue(11)));
        _eval.Evaluate("=AGGREGATE(4,4,A1:A2)", sheet, wb).Should().Be(new NumberValue(11));
    }

    // ── CONVERT ──────────────────────────────────────────────────────────────

    [Fact]
    public void Convert_KgToG_Multiplies()
    {
        var (wb, sheet) = MakeWb();
        var result = _eval.Evaluate("=CONVERT(1,\"kg\",\"g\")", sheet, wb);
        result.Should().Be(new NumberValue(1000));
    }

    [Fact]
    public void Convert_MeterToCentimeter()
    {
        var (wb, sheet) = MakeWb();
        var result = _eval.Evaluate("=CONVERT(1,\"m\",\"cm\")", sheet, wb);
        result.Should().BeOfType<NumberValue>();
        ((NumberValue)result).Value.Should().BeApproximately(100, 1e-9);
    }

    [Fact]
    public void Convert_HoursToSeconds()
    {
        var (wb, sheet) = MakeWb();
        _eval.Evaluate("=CONVERT(1,\"hr\",\"sec\")", sheet, wb).Should().Be(new NumberValue(3600));
    }

    [Fact]
    public void Convert_CelsiusToFahrenheit()
    {
        var (wb, sheet) = MakeWb();
        var result = _eval.Evaluate("=CONVERT(100,\"C\",\"F\")", sheet, wb);
        result.Should().BeOfType<NumberValue>();
        ((NumberValue)result).Value.Should().BeApproximately(212, 1e-9);
    }

    [Fact]
    public void Convert_FahrenheitToCelsius()
    {
        var (wb, sheet) = MakeWb();
        var result = _eval.Evaluate("=CONVERT(32,\"F\",\"C\")", sheet, wb);
        result.Should().BeOfType<NumberValue>();
        ((NumberValue)result).Value.Should().BeApproximately(0, 1e-9);
    }

    [Fact]
    public void Convert_CelsiusToKelvin()
    {
        var (wb, sheet) = MakeWb();
        var result = _eval.Evaluate("=CONVERT(0,\"C\",\"K\")", sheet, wb);
        result.Should().BeOfType<NumberValue>();
        ((NumberValue)result).Value.Should().BeApproximately(273.15, 1e-9);
    }

    [Fact]
    public void Convert_IncompatibleCategories_ReturnsNA()
    {
        var (wb, sheet) = MakeWb();
        _eval.Evaluate("=CONVERT(1,\"kg\",\"m\")", sheet, wb).Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void Convert_UnknownUnit_ReturnsNA()
    {
        var (wb, sheet) = MakeWb();
        _eval.Evaluate("=CONVERT(1,\"foo\",\"g\")", sheet, wb).Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void Convert_BytesToBits()
    {
        var (wb, sheet) = MakeWb();
        _eval.Evaluate("=CONVERT(1,\"byte\",\"bit\")", sheet, wb).Should().Be(new NumberValue(8));
    }
}
