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
    [InlineData("25\uFF05")]
    [InlineData("-12.5\uFE6A")]
    [InlineData("'25\uFF05")]
    [InlineData("\uFF0B 1,234.50\uFE6A")]
    [InlineData("\u221242")]
    [InlineData("(\u2212 42%)")]
    [InlineData("(\u2212 42\uFF05)")]
    [InlineData("\uFF14\uFF12")]
    [InlineData("\uFF11\uFF0C\uFF12\uFF13\uFF14\uFF0E\uFF15\uFF10")]
    [InlineData("'\uFF11\uFF0C\uFF12\uFF13\uFF14\uFF0E\uFF15\uFF10")]
    [InlineData("\uFF0D \uFF11\uFF12\uFF0E\uFF15%")]
    [InlineData("\u2212\uFF14\uFF12\uFF05")]
    [InlineData("(\uFF11\uFF0C\uFF12\uFF13\uFF14\uFF0E\uFF15\uFF10)")]
    [InlineData("(\u2212 \uFF14\uFF12\uFF05)")]
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
    [InlineData("\uFF11\uFF0E\uFF12\uFF13\uFF25\uFF14")]
    [InlineData("\uFF11\uFF45\uFF0D\uFF13")]
    [InlineData("\uFF0B \uFF11\uFF0E\uFF12\uFF25\uFF0B\uFF13")]
    [InlineData("$\uFF11\uFF25\uFF13")]
    [InlineData("\uFF11\uFF25\uFF12\uFF05")]
    [InlineData("(\u2212 \uFF11\uFF25\uFF0B\uFF13)")]
    public void FindFormulaErrorIssues_ReturnsFullwidthScientificNumbersStoredAsText(string value)
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
    [InlineData("\u20B942")]
    [InlineData("- \u20A91,234.50")]
    [InlineData("\u20AA25%")]
    [InlineData("42 \u0E3F")]
    [InlineData("(+\u20BD25%)")]
    [InlineData("(- 42 \u20BA)")]
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
    [InlineData("$25\uFF05")]
    [InlineData("'$25\uFE6A")]
    [InlineData("-\u00A325%")]
    [InlineData("-\u00A325\uFF05")]
    [InlineData("- \u00A325%")]
    [InlineData("+\u00A325%")]
    [InlineData("+ \u20AC25%")]
    [InlineData("+ \u20AC25\uFE6A")]
    [InlineData("'+\u00A325%")]
    [InlineData("'+\u00A325\uFF05")]
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
    [InlineData("(+\u20AC25\uFF05)")]
    [InlineData("(+ \u20AC25%)")]
    [InlineData("'(+ \u20AC25\uFE6A)")]
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
    [InlineData("\u20B142")]
    [InlineData("- \u20AB1,234.50")]
    [InlineData("42 \u20A6")]
    [InlineData("\u20B425%")]
    [InlineData("(- 42 \u20B8)")]
    [InlineData("'(42 \u20A1)")]
    [InlineData("\uFF0D $1,234.50")]
    [InlineData("\uFE63\u20AC25%")]
    [InlineData("\uFF0B 42 \u20B1")]
    [InlineData("$\uFF11\uFF0C\uFF12\uFF13\uFF14\uFF0E\uFF15\uFF10")]
    [InlineData("+ \u20AC\uFF12\uFF15\uFF05")]
    [InlineData("'(\uFF14\uFF12 \uFFE5)")]
    [InlineData("(- \uFFE5\uFF11\uFF0C\uFF12\uFF13\uFF14\uFF0E\uFF15\uFF10)")]
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
    [InlineData("\u20B9")]
    [InlineData("\u20A9")]
    [InlineData("\u20AA")]
    [InlineData("\u0E3F")]
    [InlineData("\u20BD")]
    [InlineData("\u20BA")]
    [InlineData("\u20B1")]
    [InlineData("\u20AB")]
    [InlineData("\u20A6")]
    [InlineData("\u20B4")]
    [InlineData("\u20B8")]
    [InlineData("\u20A1")]
    [InlineData("'$")]
    [InlineData("'\u00A5")]
    [InlineData("-$")]
    [InlineData("- $")]
    [InlineData("+ $")]
    [InlineData("-\u20AC")]
    [InlineData("-\u00A5")]
    [InlineData("+ \uFFE5")]
    [InlineData("$%")]
    [InlineData("$\uFF05")]
    [InlineData("\u00A5%")]
    [InlineData("\u00A5\uFE6A")]
    [InlineData("$()")]
    [InlineData("($)")]
    [InlineData("(\uFFE5)")]
    [InlineData("($%)")]
    [InlineData("($\uFF05)")]
    [InlineData("(\u00A5%)")]
    [InlineData("(\u00A5\uFE6A)")]
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
    [InlineData("\u20B9\u20A942")]
    [InlineData("\u20AA42\u0E3F")]
    [InlineData("42 \u20BD \u20BA")]
    [InlineData("\u20B1\u20AB42")]
    [InlineData("\u20B842\u20A1")]
    [InlineData("42 \u20A6 \u20B4")]
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
    [InlineData("\u20B9NaN")]
    [InlineData("Infinity \u20A9")]
    [InlineData("- \u20A9Infinity")]
    [InlineData("--$42")]
    [InlineData("+-$42")]
    [InlineData("--42$")]
    [InlineData("+-42$")]
    [InlineData("-$-42")]
    [InlineData("- $-42")]
    [InlineData("-42-$")]
    [InlineData("+$+42")]
    [InlineData("+ $+42")]
    [InlineData("\uFF0D $")]
    [InlineData("\uFF0B USD 42")]
    [InlineData("\u2212 \u20B1PHP 42")]
    [InlineData("\uFE63\u20AC-25%")]
    [InlineData("\uFE63\u20AC-25\uFF05")]
    [InlineData("\u20B1PHP 42")]
    [InlineData("VND \u20AB42")]
    [InlineData("42 NGN \u20A6")]
    [InlineData("$\uFF14\uFF12USD")]
    [InlineData("\u20AC\uFF11\uFF12\uFF13\uFF21")]
    [InlineData("\u00A5\uFF11\uFF0E\uFF12\uFF0E\uFF13")]
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
    [InlineData("\uFF05")]
    [InlineData("'\uFE6A")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("(NaN)")]
    [InlineData("USD 42")]
    [InlineData("\u2212")]
    [InlineData("\uFF0D")]
    [InlineData("\uFE63")]
    [InlineData("\uFF0B")]
    [InlineData("\u2212-42")]
    [InlineData("\uFF0B+42")]
    [InlineData("\uFF0D\u221242")]
    [InlineData("\u2212-42\uFF05")]
    [InlineData("\uFF0B+42\uFE6A")]
    [InlineData("42\u2212")]
    [InlineData("42 \uFF0B")]
    [InlineData("\u221242-")]
    [InlineData("42\uFF05%")]
    [InlineData("42%\uFF05")]
    [InlineData("4\uFF052")]
    [InlineData("42\uFE6Akg")]
    [InlineData("42\uFF05 USD")]
    [InlineData("\uFF0D-\uFF14\uFF12")]
    [InlineData("\uFF0B+\uFF14\uFF12")]
    [InlineData("\uFF14\uFF05\uFF12")]
    [InlineData("\uFF14\uFF12kg")]
    [InlineData("\uFF14\uFF12\uFF05 USD")]
    [InlineData("\uFF11\uFF12\uFF13\uFF21")]
    [InlineData("\uFF11\uFF0E\uFF12\uFF0E\uFF13")]
    public void FindFormulaErrorIssues_DoesNotReturnNumberStoredAsTextForInvalidNumberText(string value)
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue(value));

        FormulaAuditingService.FindFormulaErrorIssues(wb, sheet.Id)
            .Should().NotContain(i => i.ErrorCode == FormulaAuditingService.NumberStoredAsTextErrorCode);
    }

    [Theory]
    [InlineData("\uFF11\uFF12\uFF25")]
    [InlineData("\uFF11\uFF12\uFF25\uFF0B")]
    [InlineData("\uFF11\uFF12\uFF25\uFF0B\uFF0B\uFF13")]
    [InlineData("\uFF11\uFF12\uFF25\uFF13kg")]
    [InlineData("\uFF11\uFF12\uFF25\uFF13\uFF05 USD")]
    [InlineData("\uFF11\uFF12\uFF25\uFF13\uFF25\uFF14")]
    [InlineData("\uFF11\uFF12\uFF13\uFF21")]
    public void FindFormulaErrorIssues_DoesNotReturnNumberStoredAsTextForInvalidFullwidthScientificNumberText(string value)
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
