using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// Regressions from the 2026-11 sweep 5, each verified against real desktop Excel (COM, en-US):
///  * MATCH(x, range, -1) is a linear early-exit scan (equal wins, first smaller ends the scan,
///    blank/error/foreign-type cells are skipped),
///  * COUNT / COUNTA over a defined name whose target is #REF!.
/// </summary>
public sealed class ExcelCorpusSweep5ParityTests
{
    private readonly FormulaEvaluator _eval = new();

    private static (Workbook wb, Sheet sheet) Column(params double[] values)
    {
        var wb = new Workbook("T");
        var sheet = wb.AddSheet("S");
        for (var i = 0; i < values.Length; i++)
            sheet.SetCell(new CellAddress(sheet.Id, (uint)(i + 1), 1), new NumberValue(values[i]));
        return (wb, sheet);
    }

    // Expected positions were read back from real Excel for these exact unsorted vectors (0 = #N/A).
    [Theory]
    [InlineData(new double[] { 5, 28, 15 }, 8, 0)]
    [InlineData(new double[] { 18, 26, 26, 8 }, 26, 0)]
    [InlineData(new double[] { 22, 2, 19, 21 }, 2, 2)]
    [InlineData(new double[] { 29, 10, 9, 9 }, 9, 3)]
    [InlineData(new double[] { 10, 4, 12, 20, 4, 4 }, 4, 2)]
    [InlineData(new double[] { 7, 26, 26, 27, 7, 29, 5, 29, 12, 12 }, 7, 1)]
    [InlineData(new double[] { 6, 25, 19, 18, 13, 15, 1, 16, 21, 2, 27 }, 5, 6)]
    [InlineData(new double[] { 26, 19, 2, 4 }, 6, 2)]
    [InlineData(new double[] { 5, 12, 25, 10 }, 23, 0)]
    [InlineData(new double[] { 17, 21, 3, 11 }, 13, 2)]
    [InlineData(new double[] { 18, 3, 27, 8 }, 2, 4)]
    public void MatchDescending_UnsortedData_FollowsExcelLinearScan(double[] data, double key, int expected)
    {
        var (wb, sheet) = Column(data);
        var last = data.Length;
        var k = key.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var want = expected == 0 ? (ScalarValue)ErrorValue.NA : new NumberValue(expected);

        _eval.Evaluate($"=MATCH({k},A1:A{last},-1)", sheet, wb).Should().Be(want);
        _eval.Evaluate($"=MATCH({k},INDEX(A1:A{last},0,1),-1)", sheet, wb).Should().Be(want);
    }

    [Fact]
    public void MatchDescending_SkipsBlankErrorAndForeignTypeCells()
    {
        var wb = new Workbook("T");
        var sheet = wb.AddSheet("S");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(9));
        // A2 stays blank
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(3));

        // Excel: 9,blank,3 with key 0 and -1 both give 3 (blank is not coerced to 0).
        _eval.Evaluate("=MATCH(0,A1:A3,-1)", sheet, wb).Should().Be(new NumberValue(3));
        _eval.Evaluate("=MATCH(-1,A1:A3,-1)", sheet, wb).Should().Be(new NumberValue(3));

        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("m"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), ErrorValue.NA);
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("e"));
        // Excel: an error cell does not poison descending MATCH ("m", #N/A, "e" key "d" -> 3).
        _eval.Evaluate("=MATCH(\"d\",A1:A3,-1)", sheet, wb).Should().Be(new NumberValue(3));

        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(5));
        // Excel: "m",5,"e" key "d" -> 3 (the number is skipped).
        _eval.Evaluate("=MATCH(\"d\",A1:A3,-1)", sheet, wb).Should().Be(new NumberValue(3));
    }

    [Fact]
    public void CountAndCountA_OverNameReferringToRefError()
    {
        var wb = new Workbook("T");
        var sheet = wb.AddSheet("Sheet1");
        wb.NamedFormulas["SEL"] = "#REF!";

        // Excel (verified): COUNT -> 0, COUNTA -> 1 (an error value is a non-empty argument).
        _eval.Evaluate("=COUNT(SEL)", sheet, wb).Should().Be(new NumberValue(0));
        _eval.Evaluate("=COUNTA(SEL)", sheet, wb).Should().Be(new NumberValue(1));
        _eval.Evaluate("=SUM(SEL)", sheet, wb).Should().Be(ErrorValue.Ref);
        _eval.Evaluate("=ISREF(SEL)", sheet, wb).Should().Be(new BoolValue(false));
        _eval.Evaluate("=IFERROR(SEL,\"e\")", sheet, wb).Should().Be(new TextValue("e"));
        // Errors are never numbers (COUNT) but are non-empty values (COUNTA), bare or named.
        _eval.Evaluate("=COUNT(NA())", sheet, wb).Should().Be(new NumberValue(0));
        _eval.Evaluate("=COUNT(1,#REF!)", sheet, wb).Should().Be(new NumberValue(1));
        _eval.Evaluate("=COUNT(SEL,1)", sheet, wb).Should().Be(new NumberValue(1));
        _eval.Evaluate("=COUNTA(1,NA())", sheet, wb).Should().Be(new NumberValue(2));
        _eval.Evaluate("=COUNTA(SEL,1)", sheet, wb).Should().Be(new NumberValue(2));
    }
}
