using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

/// <summary>
/// Corrections Excel applies to what is typed into a cell: missing closing parentheses at the end
/// of a formula are added, function names and cell references are upper-cased, and a number typed
/// with grouping, decimals, a percent sign, a currency sign or an exponent keeps that look through
/// an inferred number format.
/// </summary>
public sealed class CellEntryExcelEntryCorrectionsTests
{
    private static readonly CellAddress Anchor = new(SheetId.New(), 2, 2);

    [Theory]
    [InlineData("=SUM(A1:A3", "SUM(A1:A3)")]
    [InlineData("=IF(A1>0,SUM(B1:B2", "IF(A1>0,SUM(B1:B2))")]
    [InlineData("=(1+2", "(1+2)")]
    public void CreateCell_ClosesParenthesesMissingAtTheEnd(string typed, string expected)
    {
        CellEntryParser.CreateCell(typed, Anchor, useR1C1ReferenceStyle: false)
            .FormulaText.Should().Be(expected);
    }

    [Theory]
    [InlineData("=sum(a1:b2)", "SUM(A1:B2)")]
    [InlineData("=vlookup(a1,$b$1:$c$9,2,false)", "VLOOKUP(A1,$B$1:$C$9,2,FALSE)")]
    [InlineData("=stdev.p(a1:a5)", "STDEV.P(A1:A5)")]
    [InlineData("=Sheet2!a1+1", "Sheet2!A1+1")]
    [InlineData("='my sheet'!b3", "'my sheet'!B3")]
    [InlineData("=concat(\"abc\",a1)", "CONCAT(\"abc\",A1)")]
    [InlineData("=sum( a1 , a2 )", "SUM( A1 , A2 )")]
    [InlineData("=Table1[Amount]*2", "Table1[Amount]*2")]
    public void CreateCell_UpperCasesFunctionsAndReferencesButKeepsLiteralsAndSpacing(string typed, string expected)
    {
        CellEntryParser.CreateCell(typed, Anchor, useR1C1ReferenceStyle: false)
            .FormulaText.Should().Be(expected);
    }

    [Theory]
    [InlineData("1,234", 1234d, "#,##0")]
    [InlineData("1,234.5", 1234.5d, "#,##0.00")]
    [InlineData("10%", 0.1d, "0%")]
    [InlineData("10.5%", 0.105d, "0.00%")]
    [InlineData("$5", 5d, "$#,##0")]
    [InlineData("$5.50", 5.5d, "$#,##0.00")]
    [InlineData("1E3", 1000d, "0.00E+00")]
    public void CreateCell_InfersTheNumberFormatExcelUsesForTheTypedShape(string typed, double value, string format)
    {
        using var cultureScope = TestCultureScope.CurrentCulture("en-US");
        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("Sheet1");
        var address = new CellAddress(sheet.Id, 1, 1);

        var cell = CellEntryParser.CreateCell(typed, address, useR1C1ReferenceStyle: false, workbook);

        cell.Value.Should().Be(new NumberValue(value));
        workbook.GetStyle(cell.StyleId).NumberFormat.Should().Be(format);
    }

    [Theory]
    [InlineData("1234")]
    [InlineData("12.5")]
    public void CreateCell_LeavesAPlainNumberGeneral(string typed)
    {
        using var cultureScope = TestCultureScope.CurrentCulture("en-US");
        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("Sheet1");

        var cell = CellEntryParser.CreateCell(typed, new CellAddress(sheet.Id, 1, 1), useR1C1ReferenceStyle: false, workbook);

        workbook.GetStyle(cell.StyleId).NumberFormat.Should().Be("General");
    }
}
