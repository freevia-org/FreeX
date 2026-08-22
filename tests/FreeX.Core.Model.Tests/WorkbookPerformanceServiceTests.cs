using FreeX.Core.Commands;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Model.Tests;

public sealed class WorkbookPerformanceServiceTests
{
    [Fact]
    public void GetContentUsedRange_ExcludesFormattingOnlyCells()
    {
        var workbook = new Workbook("Performance");
        var sheet = workbook.AddSheet("Sheet1");
        var valueAddress = new CellAddress(sheet.Id, 2, 2);
        sheet.SetCell(valueAddress, new NumberValue(12));
        sheet.SetStyleOnly(100, 26, workbook.RegisterStyle(new CellStyle { Bold = true }));

        sheet.GetContentUsedRange().Should().Be(new GridRange(valueAddress, valueAddress));
        sheet.GetUsedRange().Should().Be(new GridRange(valueAddress, new CellAddress(sheet.Id, 100, 26)));
    }

    [Fact]
    public void Analyze_DetectsFormattingOutsideContentWithoutMutatingWorkbook()
    {
        var workbook = new Workbook("Performance");
        var sheet = workbook.AddSheet("Data");
        var content = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(content, new NumberValue(7));
        sheet.SetStyleOnly(50, 10, workbook.RegisterStyle(new CellStyle { Italic = true }));
        var usedRangeBefore = sheet.GetUsedRange();
        var styleOnlyBefore = sheet.StyleOnlyCellCount;

        var report = WorkbookPerformanceService.Analyze(workbook);

        report.HasIssues.Should().BeTrue();
        var issue = report.Issues.Should().ContainSingle().Subject;
        issue.SheetName.Should().Be("Data");
        issue.ContentRange.Should().Be(new GridRange(content, content));
        issue.FormattingRange.Should().Be(usedRangeBefore);
        issue.FormattingOnlyCellCount.Should().Be(styleOnlyBefore);
        sheet.GetUsedRange().Should().Be(usedRangeBefore);
        sheet.StyleOnlyCellCount.Should().Be(styleOnlyBefore);
    }

    [Fact]
    public void Analyze_IgnoresFormattingContainedWithinContentExtent()
    {
        var workbook = new Workbook("Performance");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(7));
        sheet.SetCell(new CellAddress(sheet.Id, 10, 10), new NumberValue(8));
        sheet.SetStyleOnly(5, 5, workbook.RegisterStyle(new CellStyle { Bold = true }));

        WorkbookPerformanceService.Analyze(workbook).HasIssues.Should().BeFalse();
    }
}
