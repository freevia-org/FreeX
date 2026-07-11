using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;
using Xunit;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// R21-information-functions-1: CELL("address", ref) must prefix the sheet name (quoted
/// when Excel requires it) when the reference points at a sheet other than the one
/// containing the formula, matching Microsoft's documented CELL("address", ...) behavior.
/// </summary>
public sealed class R21_Cell_AddressSheetQualifier
{
    private readonly FormulaEvaluator _eval = new();

    [Fact]
    public void CellAddress_CrossSheetReference_IncludesSheetQualifier()
    {
        var workbook = new Workbook();
        var sheet1 = workbook.AddSheet("Sheet1");
        var sheet2 = workbook.AddSheet("Sheet2");
        sheet2.SetCell(new CellAddress(sheet2.Id, 7, 2), new NumberValue(42));

        _eval.Evaluate("=CELL(\"address\", Sheet2!B7)", sheet1, workbook)
            .Should().Be(new TextValue("Sheet2!$B$7"));
    }

    [Fact]
    public void CellAddress_CrossSheetReference_QuotesSheetNameWhenNeeded()
    {
        var workbook = new Workbook();
        var sheet1 = workbook.AddSheet("Sheet1");
        var sheet2 = workbook.AddSheet("Sheet 2");
        sheet2.SetCell(new CellAddress(sheet2.Id, 1, 1), new NumberValue(1));

        _eval.Evaluate("=CELL(\"address\", 'Sheet 2'!A1)", sheet1, workbook)
            .Should().Be(new TextValue("'Sheet 2'!$A$1"));
    }

    [Fact]
    public void CellAddress_SameSheetReference_OmitsSheetQualifier()
    {
        var workbook = new Workbook();
        var sheet1 = workbook.AddSheet("Sheet1");
        sheet1.SetCell(new CellAddress(sheet1.Id, 7, 2), new NumberValue(42));

        _eval.Evaluate("=CELL(\"address\", Sheet1!B7)", sheet1, workbook)
            .Should().Be(new TextValue("$B$7"));

        _eval.Evaluate("=CELL(\"address\", B7)", sheet1, workbook)
            .Should().Be(new TextValue("$B$7"));
    }
}
