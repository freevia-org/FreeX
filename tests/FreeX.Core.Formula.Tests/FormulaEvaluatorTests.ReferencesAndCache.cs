using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

public partial class FormulaEvaluatorTests
{
    // ── Cell references ──

    [Fact]
    public void CellRef_ReadsValue()
    {
        var sheet = new Sheet(SheetId.New(), "S");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(42));
        _evaluator.Evaluate("=A1", sheet).Should().Be(new NumberValue(42));
    }

    /// <summary>
    /// Excel rule: a bare cell reference to an empty cell evaluates to 0 (NumberValue),
    /// not blank. This matches how Excel stores formula results — the cell shows "0".
    /// Ref: https://support.microsoft.com/en-us/office/isblank-function
    /// </summary>
    [Fact]
    public void CellRef_EmptyCell_ReturnsZero_NotBlank()
    {
        var sheet = new Sheet(SheetId.New(), "S");
        _evaluator.Evaluate("=A1", sheet).Should().Be(new NumberValue(0));
    }

    /// <summary>
    /// ISBLANK still returns TRUE for an empty cell even though =A1 yields 0.
    /// ISBLANK inspects the cell directly (receives BlankValue as an argument internally),
    /// not the formula result after top-level normalization.
    /// </summary>
    [Fact]
    public void IsBlank_EmptyCell_ReturnsTrue()
    {
        var sheet = new Sheet(SheetId.New(), "S");
        _evaluator.Evaluate("=ISBLANK(A1)", sheet).Should().Be(new BoolValue(true));
    }

    /// <summary>
    /// String concatenation with an empty cell yields "" (blank treated as empty string),
    /// not "0". This is correct: &amp; coercion of blank is "", not 0.
    /// </summary>
    [Fact]
    public void CellRef_EmptyCell_Concat_YieldsEmptyString()
    {
        var sheet = new Sheet(SheetId.New(), "S");
        _evaluator.Evaluate("=A1&\"\"", sheet).Should().Be(new TextValue(""));
    }

    /// <summary>
    /// IF with blank-equals-empty-string condition still takes the TRUE branch.
    /// </summary>
    [Fact]
    public void CellRef_EmptyCell_IfEqualsEmptyString_TakesTrueBranch()
    {
        var sheet = new Sheet(SheetId.New(), "S");
        _evaluator.Evaluate("=IF(A1=\"\",\"y\",\"n\")", sheet).Should().Be(new TextValue("y"));
    }

    /// <summary>
    /// Cross-sheet bare reference to an empty cell also returns 0.
    /// </summary>
    [Fact]
    public void CellRef_EmptyCell_CrossSheet_ReturnsZero()
    {
        var wb = new Workbook("Test");
        var sheet1 = wb.AddSheet("Sheet1");
        wb.AddSheet("Sheet2"); // Sheet2!A1 is empty
        _evaluator.Evaluate("=Sheet2!A1", sheet1, wb).Should().Be(new NumberValue(0));
    }

    [Fact]
    public void CellRef_Arithmetic()
    {
        var sheet = new Sheet(SheetId.New(), "S");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new NumberValue(5));
        _evaluator.Evaluate("=A1+B1", sheet).Should().Be(new NumberValue(15));
    }

    [Fact]
    public void RepeatedFormulaTextCache_UpdatesWhenFormulaTextChanges()
    {
        var sheet = new Sheet(SheetId.New(), "S");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new NumberValue(5));

        _evaluator.Evaluate("=A1+B1", sheet).Should().Be(new NumberValue(15));
        _evaluator.Evaluate("=A1-B1", sheet).Should().Be(new NumberValue(5));
        _evaluator.Evaluate("=A1+B1", sheet).Should().Be(new NumberValue(15));
    }
}
