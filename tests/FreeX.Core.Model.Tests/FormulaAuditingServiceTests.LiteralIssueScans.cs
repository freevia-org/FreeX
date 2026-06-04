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
    [InlineData("01-02-24")]
    [InlineData("Jan 2, 24")]
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
        var source = File.ReadAllText(FindWorkspaceFile("src", "FreeX.Core.Commands", "FormulaAuditingService.Errors.cs"));

        source.Should().Contain("FindLiteralFormulaErrorIssues(workbook, sheetId)");
        source.Should().Contain("foreach (var (address, cell) in sheet.EnumerateCells())");
        source.Should().NotContain("result.AddRange(FindNumbersStoredAsTextIssues(workbook, sheetId));");
        source.Should().NotContain("result.AddRange(FindTwoDigitYearTextDateIssues(workbook, sheetId));");
        source.Should().NotContain("result.AddRange(FindFormulaStoredAsTextIssues(workbook, sheetId));");
    }
}
