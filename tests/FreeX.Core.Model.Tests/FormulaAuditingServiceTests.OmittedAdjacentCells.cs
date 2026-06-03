using System.Diagnostics;
using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

public sealed partial class FormulaAuditingServiceTests
{
    [Fact]
    public void FindFormulaErrorIssues_ReturnsFormulaOmitsAdjacentCellsInColumn()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(30));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 2), new NumberValue(40));
        sheet.SetCell(new CellAddress(sheet.Id, 5, 2), Cell.FromFormula("SUM(B1:B3)"));

        var issue = FormulaAuditingService.FindFormulaErrorIssues(wb, sheet.Id)
            .Should().ContainSingle(i => i.ErrorCode == FormulaAuditingService.FormulaOmitsAdjacentCellsErrorCode).Subject;

        issue.Cell.Should().Be("B5");
        issue.FormulaText.Should().Be("=SUM(B1:B3)");
        issue.Description.Should().Contain("omits adjacent cells");
    }

    [Fact]
    public void FindFormulaErrorIssues_ReturnsFormulaOmitsAdjacentCellsInRow()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 3), new NumberValue(30));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 4), new NumberValue(40));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 5), Cell.FromFormula("SUM(A2:C2)"));

        var issue = FormulaAuditingService.FindFormulaErrorIssues(wb, sheet.Id)
            .Should().ContainSingle(i => i.ErrorCode == FormulaAuditingService.FormulaOmitsAdjacentCellsErrorCode).Subject;

        issue.Cell.Should().Be("E2");
        issue.FormulaText.Should().Be("=SUM(A2:C2)");
    }

    [Theory]
    [InlineData("AVERAGE")]
    [InlineData("COUNT")]
    [InlineData("COUNTA")]
    [InlineData("MEDIAN")]
    [InlineData("MIN")]
    [InlineData("MAX")]
    [InlineData("PRODUCT")]
    [InlineData("STDEV")]
    [InlineData("STDEVP")]
    [InlineData("STDEV.S")]
    [InlineData("STDEV.P")]
    [InlineData("VAR")]
    [InlineData("VARP")]
    [InlineData("VAR.S")]
    [InlineData("VAR.P")]
    public void FindFormulaErrorIssues_ReturnsFormulaOmitsAdjacentCellsForAggregateFunctions(string functionName)
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(30));
        var formula = $"{functionName}(A1:A2)";
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), Cell.FromFormula(formula));

        var issue = FormulaAuditingService.FindFormulaErrorIssues(wb, sheet.Id)
            .Should().ContainSingle(i => i.ErrorCode == FormulaAuditingService.FormulaOmitsAdjacentCellsErrorCode).Subject;

        issue.Cell.Should().Be("A4");
        issue.FormulaText.Should().Be("=" + formula);
    }

    [Theory]
    [InlineData("Sheet1", "SUM(Sheet1!A1:A2)")]
    [InlineData("Sales Data", "SUM('Sales Data'!A1:A2)")]
    public void FindFormulaErrorIssues_ReturnsFormulaOmitsAdjacentCellsForCurrentSheetQualifiedAggregateRanges(
        string sheetName,
        string formula)
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet(sheetName);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(30));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), Cell.FromFormula(formula));

        var issue = FormulaAuditingService.FindFormulaErrorIssues(wb, sheet.Id)
            .Should().ContainSingle(i => i.ErrorCode == FormulaAuditingService.FormulaOmitsAdjacentCellsErrorCode).Subject;

        issue.Cell.Should().Be("A4");
        issue.FormulaText.Should().Be("=" + formula);
    }

    [Fact]
    public void FindFormulaErrorIssues_DoesNotTreatOtherSheetQualifiedAggregateRangeAsOmittedAdjacentCurrentSheetRange()
    {
        var wb = new Workbook("test");
        var current = wb.AddSheet("Current");
        var other = wb.AddSheet("Other");
        other.SetCell(new CellAddress(other.Id, 1, 1), new NumberValue(10));
        other.SetCell(new CellAddress(other.Id, 2, 1), new NumberValue(20));
        other.SetCell(new CellAddress(other.Id, 3, 1), new NumberValue(30));
        current.SetCell(new CellAddress(current.Id, 4, 1), Cell.FromFormula("SUM(Other!A1:A2)"));

        FormulaAuditingService.FindFormulaErrorIssues(wb, current.Id)
            .Should().NotContain(i => i.ErrorCode == FormulaAuditingService.FormulaOmitsAdjacentCellsErrorCode);
    }

    [Fact]
    public void FindFormulaErrorIssues_ReturnsFormulaOmitsAdjacentCellsForSameSheetNamedAggregateRange()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(30));
        wb.DefineNamedRange(
            "Revenue",
            new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 2, 1)));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), Cell.FromFormula("SUM(Revenue)"));

        var issue = FormulaAuditingService.FindFormulaErrorIssues(wb, sheet.Id)
            .Should().ContainSingle(i => i.ErrorCode == FormulaAuditingService.FormulaOmitsAdjacentCellsErrorCode).Subject;

        issue.Cell.Should().Be("A4");
        issue.FormulaText.Should().Be("=SUM(Revenue)");
    }

    [Fact]
    public void FindFormulaErrorIssues_DoesNotTreatOtherSheetNamedAggregateRangeAsOmittedAdjacentCurrentSheetRange()
    {
        var wb = new Workbook("test");
        var current = wb.AddSheet("Current");
        var other = wb.AddSheet("Other");
        other.SetCell(new CellAddress(other.Id, 1, 1), new NumberValue(10));
        other.SetCell(new CellAddress(other.Id, 2, 1), new NumberValue(20));
        other.SetCell(new CellAddress(other.Id, 3, 1), new NumberValue(30));
        wb.DefineNamedRange(
            "Revenue",
            new GridRange(
                new CellAddress(other.Id, 1, 1),
                new CellAddress(other.Id, 2, 1)));
        current.SetCell(new CellAddress(current.Id, 4, 1), Cell.FromFormula("SUM(Revenue)"));

        FormulaAuditingService.FindFormulaErrorIssues(wb, current.Id)
            .Should().NotContain(i => i.ErrorCode == FormulaAuditingService.FormulaOmitsAdjacentCellsErrorCode);
    }

    [Theory]
    [InlineData("SUBTOTAL(9,A1:A2)")]
    [InlineData("SUBTOTAL(109,A1:A2)")]
    [InlineData("AGGREGATE(9,4,A1:A2)")]
    [InlineData("AGGREGATE(14,4,A1:A2,1)")]
    public void FindFormulaErrorIssues_ReturnsFormulaOmitsAdjacentCellsForAggregateWrappers(string formula)
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(30));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), Cell.FromFormula(formula));

        var issue = FormulaAuditingService.FindFormulaErrorIssues(wb, sheet.Id)
            .Should().ContainSingle(i => i.ErrorCode == FormulaAuditingService.FormulaOmitsAdjacentCellsErrorCode).Subject;

        issue.Cell.Should().Be("A4");
        issue.FormulaText.Should().Be("=" + formula);
    }

    [Theory]
    [InlineData("SUBTOTAL(9,A1,A3)")]
    [InlineData("AGGREGATE(9,4,A1,A3)")]
    public void FindFormulaErrorIssues_ReturnsFormulaOmitsAdjacentCellsBetweenAggregateWrapperArguments(string formula)
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(30));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), Cell.FromFormula(formula));

        var issue = FormulaAuditingService.FindFormulaErrorIssues(wb, sheet.Id)
            .Should().ContainSingle(i => i.ErrorCode == FormulaAuditingService.FormulaOmitsAdjacentCellsErrorCode).Subject;

        issue.Cell.Should().Be("A4");
        issue.FormulaText.Should().Be("=" + formula);
    }

    [Theory]
    [InlineData("SUBTOTAL(A1:A2,A1:A2)")]
    [InlineData("SUBTOTAL(12,A1:A2)")]
    [InlineData("AGGREGATE(A1:A2,4,A1:A2)")]
    [InlineData("AGGREGATE(9,A1:A2,A1:A2)")]
    [InlineData("AGGREGATE(20,4,A1:A2)")]
    [InlineData("AGGREGATE(9,8,A1:A2)")]
    public void FindFormulaErrorIssues_DoesNotFlagAggregateWrappersWithUnsupportedSelectorArguments(string formula)
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(30));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), Cell.FromFormula(formula));

        FormulaAuditingService.FindFormulaErrorIssues(wb, sheet.Id)
            .Should().NotContain(i => i.ErrorCode == FormulaAuditingService.FormulaOmitsAdjacentCellsErrorCode);
    }

    [Fact]
    public void FindFormulaErrorIssues_DoesNotTreatAggregateKArgumentAsOmittedAdjacentRangeArgument()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(30));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), Cell.FromFormula("AGGREGATE(14,4,A1,A3)"));

        FormulaAuditingService.FindFormulaErrorIssues(wb, sheet.Id)
            .Should().NotContain(i => i.ErrorCode == FormulaAuditingService.FormulaOmitsAdjacentCellsErrorCode);
    }

    [Fact]
    public void FindFormulaErrorIssues_ReturnsFormulaOmitsAdjacentCellsBetweenSumArgumentsInColumn()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(30));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), Cell.FromFormula("SUM(A1,A3)"));

        var issue = FormulaAuditingService.FindFormulaErrorIssues(wb, sheet.Id)
            .Should().ContainSingle(i => i.ErrorCode == FormulaAuditingService.FormulaOmitsAdjacentCellsErrorCode).Subject;

        issue.Cell.Should().Be("A4");
        issue.FormulaText.Should().Be("=SUM(A1,A3)");
    }

    [Fact]
    public void FindFormulaErrorIssues_ReturnsFormulaOmitsAdjacentCellsBetweenSumArgumentsInRow()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new NumberValue(30));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 4), Cell.FromFormula("SUM(A1,C1)"));

        var issue = FormulaAuditingService.FindFormulaErrorIssues(wb, sheet.Id)
            .Should().ContainSingle(i => i.ErrorCode == FormulaAuditingService.FormulaOmitsAdjacentCellsErrorCode).Subject;

        issue.Cell.Should().Be("D1");
        issue.FormulaText.Should().Be("=SUM(A1,C1)");
    }

    [Fact]
    public void FindFormulaErrorIssues_DoesNotFlagSeparatedSumArgumentsWhenGapIsBlank()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(30));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), Cell.FromFormula("SUM(A1,A3)"));

        FormulaAuditingService.FindFormulaErrorIssues(wb, sheet.Id)
            .Should().NotContain(i => i.ErrorCode == FormulaAuditingService.FormulaOmitsAdjacentCellsErrorCode);
    }
}
