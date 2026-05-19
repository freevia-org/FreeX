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
        var sheet = workbook.AddSheet("Charts");
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
        issues.Should().OnlyContain(i => i.SheetName == "Charts");
        issues.Should().OnlyContain(i => i.Location == "A1:B4");
        issues.Should().OnlyContain(i => i.Message == "Chart is missing a title.");
    }

    [Theory]
    [InlineData("Picture 1")]
    [InlineData("Image")]
    [InlineData("Shape")]
    [InlineData("Text box")]
    public void FindIssues_FlagsObjectsWithGenericAltText(string altText)
    {
        var workbook = new Workbook("Accessibility");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.Pictures.Add(new PictureModel
        {
            Anchor = new CellAddress(sheet.Id, 3, 1),
            Kind = PictureKind.Image,
            AltText = altText
        });
        sheet.DrawingShapes.Add(new DrawingShapeModel
        {
            Anchor = new CellAddress(sheet.Id, 5, 1),
            Kind = DrawingShapeKind.Rectangle,
            AltText = "Quarterly revenue callout"
        });

        var issues = AccessibilityCheckerService.FindIssues(workbook);

        var issue = issues.Should().ContainSingle(i => i.Kind == AccessibilityIssueKind.GenericAltText).Subject;
        issue.Location.Should().Be("A3");
        issue.Message.Should().Be("Picture alternate text should describe the object.");
    }

    [Fact]
    public void FindIssues_AllowsSpecificAltText()
    {
        var workbook = new Workbook("Accessibility");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.TextBoxes.Add(new TextBoxModel
        {
            Anchor = new CellAddress(sheet.Id, 1, 1),
            Text = "Q1 revenue rose 8%",
            AltText = "Q1 revenue summary annotation"
        });

        AccessibilityCheckerService.FindIssues(workbook)
            .Should().NotContain(i => i.Kind == AccessibilityIssueKind.GenericAltText);
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
        issue.Message.Should().Be("Hyperlink display text should describe the destination.");
    }

    [Fact]
    public void FindIssues_FlagsHyperlinksWhoseDisplayTextLooksLikeAUrl()
    {
        var workbook = new Workbook("Accessibility");
        var sheet = workbook.AddSheet("Sheet1");
        var urlAddress = new CellAddress(sheet.Id, 1, 1);
        var descriptiveAddress = new CellAddress(sheet.Id, 2, 1);

        sheet.SetCell(urlAddress, new TextValue("www.example.com/report"));
        sheet.Hyperlinks[urlAddress] = "https://example.com/report?download=1";
        sheet.SetCell(descriptiveAddress, new TextValue("Download the quarterly report"));
        sheet.Hyperlinks[descriptiveAddress] = "https://example.com/report?download=1";

        var issues = AccessibilityCheckerService.FindIssues(workbook);

        var issue = issues.Should().ContainSingle(i => i.Kind == AccessibilityIssueKind.HyperlinkDisplayTextIsUrl).Subject;
        issue.Location.Should().Be("A1");
    }

    [Theory]
    [InlineData("HTTPS://EXAMPLE.COM/REPORT", "https://example.com/report")]
    [InlineData("mailto:help@example.com", "mailto:help@example.com?subject=Support")]
    [InlineData("ftp://example.com/report.csv", "ftp://example.com/report.csv?download=1")]
    public void FindIssues_FlagsHyperlinksWhoseDisplayTextIsARawDestination(string displayText, string target)
    {
        var workbook = new Workbook("Accessibility");
        var sheet = workbook.AddSheet("Sheet1");
        var address = new CellAddress(sheet.Id, 1, 1);

        sheet.SetCell(address, new TextValue(displayText));
        sheet.Hyperlinks[address] = target;

        var issues = AccessibilityCheckerService.FindIssues(workbook);

        issues.Should().ContainSingle(i => i.Kind == AccessibilityIssueKind.HyperlinkDisplayTextIsUrl)
            .Which.Location.Should().Be("A1");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("click here")]
    [InlineData("Click Here")]
    [InlineData("here")]
    [InlineData("link")]
    [InlineData("more")]
    [InlineData("read more")]
    [InlineData("learn more")]
    public void FindIssues_FlagsHyperlinksWithBlankOrGenericDisplayText(string displayText)
    {
        var workbook = new Workbook("Accessibility");
        var sheet = workbook.AddSheet("Sheet1");
        var address = new CellAddress(sheet.Id, 1, 1);

        sheet.SetCell(address, new TextValue(displayText));
        sheet.Hyperlinks[address] = "https://example.com/report";

        var issues = AccessibilityCheckerService.FindIssues(workbook);

        issues.Should().ContainSingle(i => i.Kind == AccessibilityIssueKind.HyperlinkDisplayTextIsUrl)
            .Which.Location.Should().Be("A1");
    }

    [Fact]
    public void FindIssues_FlagsDefaultWorksheetNames()
    {
        var workbook = new Workbook("Accessibility");
        var defaultSheet = workbook.AddSheet("Sheet1");
        workbook.AddSheet("Q1 Revenue");

        var issue = AccessibilityCheckerService.FindIssues(workbook)
            .Should().ContainSingle(i => i.Kind == AccessibilityIssueKind.DefaultWorksheetName).Subject;

        issue.SheetId.Should().Be(defaultSheet.Id);
        issue.SheetName.Should().Be("Sheet1");
        issue.Location.Should().Be("Sheet1");
        issue.Message.Should().Be("Worksheet tab names should describe their contents.");
    }
}
