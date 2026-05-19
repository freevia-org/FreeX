using Freexcel.Core.Commands;
using Freexcel.Core.Model;
using FluentAssertions;

namespace Freexcel.Core.Model.Tests;

public sealed class AccessibilityCheckerServiceTests
{
    [Fact]
    public void FindIssues_FlagsMergedCellsAndObjectsWithoutAltText()
    {
        var workbook = new Workbook("Accessibility");
        var sheet = workbook.AddSheet("Sheet1");
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b2 = new CellAddress(sheet.Id, 2, 2);

        sheet.AddMergedRegion(new GridRange(a1, b2));
        sheet.Pictures.Add(new PictureModel
        {
            Anchor = new CellAddress(sheet.Id, 4, 1),
            Kind = PictureKind.Image
        });
        sheet.DrawingShapes.Add(new DrawingShapeModel
        {
            Anchor = new CellAddress(sheet.Id, 6, 1),
            Kind = DrawingShapeKind.Rectangle,
            AltText = "Process block"
        });

        var issues = AccessibilityCheckerService.FindIssues(workbook);

        issues.Should().Contain(i => i.Kind == AccessibilityIssueKind.MergedCells);
        issues.Should().Contain(i => i.Kind == AccessibilityIssueKind.MissingAltText);
        issues.Should().NotContain(i => i.Location.Contains("6,1", StringComparison.Ordinal));
    }

    [Fact]
    public void FindIssues_FlagsChartsWithoutTitleText()
    {
        var workbook = new Workbook("Accessibility");
        var sheet = workbook.AddSheet("Sheet1");
        var dataRange = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 4, 2));

        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Column,
            DataRange = dataRange
        });
        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Line,
            DataRange = dataRange,
            Title = "   "
        });
        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Bar,
            DataRange = dataRange,
            Title = "Sales by quarter"
        });

        var issues = AccessibilityCheckerService.FindIssues(workbook);

        issues.Should().HaveCount(2);
        issues.Should().OnlyContain(i => i.Kind == AccessibilityIssueKind.ChartMissingTitle);
        issues.Should().OnlyContain(i => i.SheetId == sheet.Id);
        issues.Should().OnlyContain(i => i.SheetName == "Sheet1");
        issues.Should().OnlyContain(i => i.Location == "A1:B4");
        issues.Should().OnlyContain(i => i.Message == "Chart is missing a title.");
    }

    [Fact]
    public void FindIssues_FlagsHyperlinksWhoseDisplayTextIsTheUrl()
    {
        var workbook = new Workbook("Accessibility");
        var sheet = workbook.AddSheet("Sheet1");
        var urlAddress = new CellAddress(sheet.Id, 1, 1);
        var descriptiveAddress = new CellAddress(sheet.Id, 2, 1);

        sheet.SetCell(urlAddress, new TextValue("https://example.com/report"));
        sheet.Hyperlinks[urlAddress] = "https://example.com/report";
        sheet.SetCell(descriptiveAddress, new TextValue("Quarterly report"));
        sheet.Hyperlinks[descriptiveAddress] = "https://example.com/report";

        var issues = AccessibilityCheckerService.FindIssues(workbook);

        var issue = issues.Should().ContainSingle(i => i.Kind == AccessibilityIssueKind.HyperlinkDisplayTextIsUrl).Subject;
        issue.SheetId.Should().Be(sheet.Id);
        issue.SheetName.Should().Be("Sheet1");
        issue.Location.Should().Be("A1");
        issue.Message.Should().Be("Hyperlink display text should describe the destination instead of repeating the URL.");
    }
}
