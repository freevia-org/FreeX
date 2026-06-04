using System.Diagnostics;
using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

public sealed partial class FormulaAuditingServiceTests
{
    [Fact]
    public void FindFormulaErrorIssues_ReturnsNumbersStoredAsText()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var address = new CellAddress(sheet.Id, 3, 2);
        sheet.SetCell(address, new TextValue("42"));

        var issue = FormulaAuditingService.FindFormulaErrorIssues(wb, sheet.Id)
            .Should().ContainSingle().Subject;

        issue.SheetName.Should().Be("Sheet1");
        issue.Cell.Should().Be("B3");
        issue.ErrorCode.Should().Be(FormulaAuditingService.NumberStoredAsTextErrorCode);
        issue.FormulaText.Should().BeNull();
        issue.Description.Should().Contain("number in this cell is formatted as text");
    }

    [Theory]
    [InlineData("'42")]
    [InlineData("'1,234.50")]
    [InlineData("(1,234.50)")]
    [InlineData("(42)")]
    [InlineData("25%")]
    [InlineData("-12.5%")]
    [InlineData("'25%")]
    public void FindFormulaErrorIssues_ReturnsFormattedNumbersStoredAsText(string value)
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue(value));

        var issue = FormulaAuditingService.FindFormulaErrorIssues(wb, sheet.Id)
            .Should().ContainSingle().Subject;

        issue.Cell.Should().Be("A1");
        issue.ErrorCode.Should().Be(FormulaAuditingService.NumberStoredAsTextErrorCode);
    }

    [Theory]
    [InlineData("$42")]
    [InlineData("'$42")]
    [InlineData("-$42")]
    [InlineData("- $42")]
    [InlineData("'- $42")]
    [InlineData("- $ 42")]
    [InlineData("'-$42")]
    [InlineData("+$42")]
    [InlineData("+ $1,234.50")]
    [InlineData("$1,234.50")]
    [InlineData("-\u20AC1,234.50")]
    [InlineData("\u20AC1,234.50")]
    [InlineData("\u00A31,234.50")]
    [InlineData("\u00A542")]
    [InlineData("\uFFE51,234.50")]
    [InlineData("'\u00A542")]
    [InlineData("-\u00A542")]
    [InlineData("- \uFFE542")]
    [InlineData("+ \u00A51,234.50")]
    [InlineData("42$")]
    [InlineData("'42$")]
    [InlineData("42 $")]
    [InlineData("'42 $")]
    [InlineData("1,234.50 \u20AC")]
    [InlineData("-42 \u00A3")]
    [InlineData("- 42 \u20AC")]
    [InlineData("'- 42 \u20AC")]
    [InlineData("42\u00A5")]
    [InlineData("'42 \uFFE5")]
    [InlineData("- 42 \u00A5")]
    [InlineData("+42$")]
    [InlineData("+ 42 $")]
    [InlineData("$25%")]
    [InlineData("-\u00A325%")]
    [InlineData("- \u00A325%")]
    [InlineData("+\u00A325%")]
    [InlineData("+ \u20AC25%")]
    [InlineData("'+\u00A325%")]
    [InlineData("\u20AC25%")]
    [InlineData("\u00A325%")]
    [InlineData("\u00A525%")]
    [InlineData("+ \uFFE525%")]
    [InlineData("'\u00A525%")]
    [InlineData("($1,234.50)")]
    [InlineData("(-$1,234.50)")]
    [InlineData("(- $1,234.50)")]
    [InlineData("'(-$1,234.50)")]
    [InlineData("'(- $1,234.50)")]
    [InlineData("(+\u20AC25%)")]
    [InlineData("(+ \u20AC25%)")]
    [InlineData("'(+\u20AC25%)")]
    [InlineData("'(+ \u20AC25%)")]
    [InlineData("(\u20AC1,234.50)")]
    [InlineData("(\u00A31,234.50)")]
    [InlineData("(\u00A51,234.50)")]
    [InlineData("(- \uFFE51,234.50)")]
    [InlineData("'(\u00A51,234.50)")]
    [InlineData("(42 $)")]
    [InlineData("'(42 $)")]
    [InlineData("(-42 \u00A3)")]
    [InlineData("(- 42 \u20AC)")]
    public void FindFormulaErrorIssues_ReturnsCurrencyNumbersStoredAsText(string value)
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue(value));

        var issue = FormulaAuditingService.FindFormulaErrorIssues(wb, sheet.Id)
            .Should().ContainSingle().Subject;

        issue.Cell.Should().Be("A1");
        issue.ErrorCode.Should().Be(FormulaAuditingService.NumberStoredAsTextErrorCode);
    }

    [Theory]
    [InlineData("$")]
    [InlineData("\u20AC")]
    [InlineData("\u00A3")]
    [InlineData("\u00A5")]
    [InlineData("\uFFE5")]
    [InlineData("'$")]
    [InlineData("'\u00A5")]
    [InlineData("-$")]
    [InlineData("- $")]
    [InlineData("+ $")]
    [InlineData("-\u20AC")]
    [InlineData("-\u00A5")]
    [InlineData("+ \uFFE5")]
    [InlineData("$%")]
    [InlineData("\u00A5%")]
    [InlineData("$()")]
    [InlineData("($)")]
    [InlineData("(\uFFE5)")]
    [InlineData("($%)")]
    [InlineData("(\u00A5%)")]
    [InlineData("USD 42")]
    [InlineData("JPY 42")]
    [InlineData("CNY 42")]
    [InlineData("$USD 42")]
    [InlineData("US$42")]
    [InlineData("42 USD")]
    [InlineData("42 JPY")]
    [InlineData("42 US$")]
    [InlineData("$42\u20AC")]
    [InlineData("\u00A542\uFFE5")]
    [InlineData("\u00A5$42")]
    [InlineData("$\u00A542")]
    [InlineData("42$\u20AC")]
    [InlineData("42\u00A5\uFFE5")]
    [InlineData("42 $ $")]
    [InlineData("42 \u00A5 \uFFE5")]
    [InlineData("42$-")]
    [InlineData("$NaN")]
    [InlineData("$Infinity")]
    [InlineData("NaN$")]
    [InlineData("Infinity$")]
    [InlineData("NaN \u20AC")]
    [InlineData("Infinity \u00A3")]
    [InlineData("-$NaN")]
    [InlineData("-$Infinity")]
    [InlineData("- $NaN")]
    [InlineData("- $Infinity")]
    [InlineData("-NaN$")]
    [InlineData("- Infinity \u20AC")]
    [InlineData("--$42")]
    [InlineData("+-$42")]
    [InlineData("--42$")]
    [InlineData("+-42$")]
    [InlineData("-$-42")]
    [InlineData("- $-42")]
    [InlineData("-42-$")]
    [InlineData("+$+42")]
    [InlineData("+ $+42")]
    public void FindFormulaErrorIssues_DoesNotReturnNumberStoredAsTextForInvalidCurrencyText(string value)
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue(value));

        FormulaAuditingService.FindFormulaErrorIssues(wb, sheet.Id)
            .Should().NotContain(i => i.ErrorCode == FormulaAuditingService.NumberStoredAsTextErrorCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("'")]
    [InlineData("%")]
    [InlineData("'%")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("(NaN)")]
    [InlineData("USD 42")]
    public void FindFormulaErrorIssues_DoesNotReturnNumberStoredAsTextForInvalidNumberText(string value)
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue(value));

        FormulaAuditingService.FindFormulaErrorIssues(wb, sheet.Id)
            .Should().NotContain(i => i.ErrorCode == FormulaAuditingService.NumberStoredAsTextErrorCode);
    }

    [Theory]
    [InlineData("1/2/24")]
    [InlineData("'1/2/26")]
    [InlineData("1-2-26")]
    [InlineData("1.2.26")]
    [InlineData("01-02-24")]
    [InlineData("Jan 2, 24")]
    [InlineData("Jan 2 26")]
    [InlineData("Jan/2/24")]
    [InlineData("January/2/24")]
    [InlineData("'Jan/2/24")]
    [InlineData("Mon Jan 2, 24")]
    [InlineData("Mon, Jan 2, 24")]
    [InlineData("Monday, January 2, 24")]
    [InlineData("2 Jan 26")]
    [InlineData("2/Jan/26")]
    [InlineData("2/January/26")]
    [InlineData("'2/Jan/26")]
    [InlineData("Tue 2 Jan 26")]
    [InlineData("24/1/2")]
    [InlineData("'24/1/2")]
    [InlineData("Mon 1/2/24")]
    [InlineData("'Mon 1/2/24")]
    [InlineData("Monday, 1-2-24")]
    [InlineData("'Monday, 1-2-24")]
    [InlineData("Tue 24/1/2")]
    [InlineData("'Tue 24/1/2")]
    [InlineData("Friday, 26.1.2")]
    [InlineData("26-01-02")]
    [InlineData("'26-01-02")]
    [InlineData("26.1.2")]
    [InlineData("26 Jan 2")]
    [InlineData("Wednesday, 26 Jan 2")]
    [InlineData("'26 Jan 2")]
    [InlineData("26/Jan/2")]
    [InlineData("26/January/2")]
    [InlineData("'26/Jan/2")]
    [InlineData("26-Jan-02")]
    public void FindFormulaErrorIssues_ReturnsTextDatesWithTwoDigitYears(string value)
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue(value));

        var issue = FormulaAuditingService.FindFormulaErrorIssues(wb, sheet.Id)
            .Should().ContainSingle().Subject;

        issue.Cell.Should().Be("A2");
        issue.ErrorCode.Should().Be(FormulaAuditingService.TwoDigitYearTextDateErrorCode);
        issue.FormulaText.Should().BeNull();
        issue.Description.Should().Contain("two-digit year");
    }

    [Theory]
    [InlineData("Jan 2nd, 24")]
    [InlineData("'Jan 2nd, 24")]
    [InlineData("'Fri Jan 2nd, 24")]
    [InlineData("January 3rd 26")]
    [InlineData("Jan 11th, 24")]
    [InlineData("Jan 21st, 24")]
    [InlineData("2nd Jan 26")]
    [InlineData("2nd-January-26")]
    [InlineData("26 Jan 2nd")]
    [InlineData("Jan/2nd/24")]
    [InlineData("2nd/Jan/26")]
    [InlineData("'2nd/Jan/26")]
    [InlineData("26/Jan/2nd")]
    [InlineData("'26/Jan/2nd")]
    public void FindFormulaErrorIssues_ReturnsOrdinalTextDatesWithTwoDigitYears(string value)
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue(value));

        var issue = FormulaAuditingService.FindFormulaErrorIssues(wb, sheet.Id)
            .Should().ContainSingle().Subject;

        issue.Cell.Should().Be("A2");
        issue.ErrorCode.Should().Be(FormulaAuditingService.TwoDigitYearTextDateErrorCode);
        issue.FormulaText.Should().BeNull();
        issue.Description.Should().Contain("two-digit year");
    }

    [Theory]
    [InlineData("1/2/2026")]
    [InlineData("Jan/2/2026")]
    [InlineData("2/Jan/2026")]
    [InlineData("2026/Jan/2")]
    [InlineData("2026-01-02")]
    [InlineData("2026 Jan 2")]
    [InlineData("Q1 26")]
    [InlineData("26/13/2")]
    [InlineData("26-02-30")]
    [InlineData("Jan/32/24")]
    [InlineData("Jax/2/24")]
    [InlineData("2/Jax/26")]
    [InlineData("26/Jax/2")]
    [InlineData("26/Jan/32")]
    [InlineData("Jan 2nd, 2024")]
    [InlineData("Mon Jan 2, 2024")]
    [InlineData("Monday, January 2, 2024")]
    [InlineData("Tue 1/2/2024")]
    [InlineData("Monday, 2024-01-02")]
    [InlineData("Tue 24/13/2")]
    [InlineData("Wed 1/32/24")]
    [InlineData("Thursday, 26.02.30")]
    [InlineData("Wed Jan 32, 24")]
    [InlineData("Thu Jan 1nd, 24")]
    [InlineData("Friday, Jax 2, 24")]
    [InlineData("Wednesday, 99 Jan 32")]
    [InlineData("2nd/Jan/2026")]
    [InlineData("2026/Jan/2nd")]
    [InlineData("2026 Jan 2nd")]
    [InlineData("Jan 32nd, 24")]
    [InlineData("26/Jan/32nd")]
    [InlineData("Jax 2nd, 24")]
    [InlineData("2nd/Jax/26")]
    [InlineData("26/Jax/2nd")]
    [InlineData("Jan 1nd, 24")]
    [InlineData("2st Jan 26")]
    [InlineData("3th/Jan/26")]
    [InlineData("26/Jan/11st")]
    [InlineData("99 Jan 32")]
    [InlineData("'")]
    [InlineData("12345")]
    public void FindFormulaErrorIssues_DoesNotReturnTextDatesWithTwoDigitYearsForNonMatchingText(string value)
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue(value));

        FormulaAuditingService.FindFormulaErrorIssues(wb, sheet.Id)
            .Should().NotContain(i => i.ErrorCode == FormulaAuditingService.TwoDigitYearTextDateErrorCode);
    }

    [Fact]
    public void FindFormulaErrorIssues_ReturnsFormulaStoredAsText()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("=SUM(A1:A2)"));

        var issue = FormulaAuditingService.FindFormulaErrorIssues(wb, sheet.Id)
            .Should().ContainSingle().Subject;

        issue.Cell.Should().Be("A3");
        issue.ErrorCode.Should().Be(FormulaAuditingService.FormulaStoredAsTextErrorCode);
        issue.FormulaText.Should().BeNull();
        issue.Description.Should().Contain("stored as text");
    }

    [Theory]
    [InlineData("'=SUM(A1:A2)")]
    [InlineData("   '=SUM(A1:A2)")]
    [InlineData("'   =SUM(A1:A2)")]
    public void FindFormulaErrorIssues_ReturnsApostrophePrefixedFormulaStoredAsText(string value)
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue(value));

        var issue = FormulaAuditingService.FindFormulaErrorIssues(wb, sheet.Id)
            .Should().ContainSingle().Subject;

        issue.Cell.Should().Be("A3");
        issue.ErrorCode.Should().Be(FormulaAuditingService.FormulaStoredAsTextErrorCode);
        issue.FormulaText.Should().BeNull();
        issue.Description.Should().Contain("stored as text");
    }

    [Theory]
    [InlineData("'")]
    [InlineData("'Budget")]
    [InlineData("   'Budget")]
    [InlineData("''=SUM(A1:A2)")]
    [InlineData("   ''=SUM(A1:A2)")]
    public void FindFormulaErrorIssues_DoesNotReturnFormulaStoredAsTextForApostropheNonFormulas(string value)
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue(value));

        FormulaAuditingService.FindFormulaErrorIssues(wb, sheet.Id)
            .Should().NotContain(i => i.ErrorCode == FormulaAuditingService.FormulaStoredAsTextErrorCode);
    }

    [Fact]
    public void FindFormulaErrorIssues_DoesNotReturnFormulaStoredAsTextForActualFormulas()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), Cell.FromFormula("A1*2"));

        FormulaAuditingService.FindFormulaErrorIssues(wb, sheet.Id)
            .Should().NotContain(i => i.ErrorCode == FormulaAuditingService.FormulaStoredAsTextErrorCode);
    }

    [Fact]
    public void FindFormulaErrorIssues_CombinesLiteralIssueScans()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.Core.Commands", "FormulaAuditingService.Errors.cs"));

        source.Should().Contain("FindLiteralFormulaErrorIssues(workbook, sheetId)");
        source.Should().Contain("foreach (var (address, cell) in sheet.EnumerateCells())");
        source.Should().NotContain("result.AddRange(FindNumbersStoredAsTextIssues(workbook, sheetId));");
        source.Should().NotContain("result.AddRange(FindTwoDigitYearTextDateIssues(workbook, sheetId));");
        source.Should().NotContain("result.AddRange(FindFormulaStoredAsTextIssues(workbook, sheetId));");
    }
}
