using FreeX.Core.Commands;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Model.Tests;

public sealed partial class AccessibilityCheckerServiceTests
{
    [Fact]
    public void FindIssues_IgnoresHiddenChartsForTitleChecks()
    {
        var workbook = new Workbook("Accessibility");
        var sheet = workbook.AddSheet("Charts");
        var dataRange = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 4, 2));

        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Column,
            DataRange = dataRange,
            IsVisible = false
        });
        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Line,
            DataRange = dataRange,
            Title = "Chart Title",
            IsVisible = false
        });

        var issues = AccessibilityCheckerService.FindIssues(workbook);

        issues.Where(i =>
                i.Kind == AccessibilityIssueKind.ChartMissingTitle ||
                i.Kind == AccessibilityIssueKind.GenericChartTitle)
            .Should()
            .BeEmpty();
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
    [InlineData("download")]
    [InlineData("download now")]
    [InlineData("open")]
    [InlineData("view")]
    [InlineData("visit website")]
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

    [Theory]
    [InlineData("Click here.")]
    [InlineData("Learn more >")]
    [InlineData("example.com/report")]
    [InlineData("support@example.com")]
    public void FindIssues_FlagsHyperlinksWithPunctuatedGenericOrAddressLikeDisplayText(string displayText)
    {
        var workbook = new Workbook("Accessibility");
        var sheet = workbook.AddSheet("Links");
        var address = new CellAddress(sheet.Id, 1, 1);

        sheet.SetCell(address, new TextValue(displayText));
        sheet.Hyperlinks[address] = "https://example.com/report";

        AccessibilityCheckerService.FindIssues(workbook)
            .Should().ContainSingle(i => i.Kind == AccessibilityIssueKind.HyperlinkDisplayTextIsUrl)
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

    [Fact]
    public void FindIssues_FlagsHiddenSheetsThatContainContent()
    {
        var workbook = new Workbook("Accessibility");
        var hiddenSheet = workbook.AddSheet("Archived Data");
        hiddenSheet.IsHidden = true;
        hiddenSheet.SetCell(new CellAddress(hiddenSheet.Id, 2, 3), new TextValue("Confidential forecast"));

        var visibleEmptySheet = workbook.AddSheet("Visible Summary");
        var hiddenEmptySheet = workbook.AddSheet("Empty Archive");
        hiddenEmptySheet.IsHidden = true;

        var issues = AccessibilityCheckerService.FindIssues(workbook);

        var issue = issues.Should().ContainSingle(i => i.Kind == AccessibilityIssueKind.HiddenSheetWithContent).Subject;
        issue.SheetId.Should().Be(hiddenSheet.Id);
        issue.SheetName.Should().Be("Archived Data");
        issue.Location.Should().Be("Archived Data");
        issue.Message.Should().Be("Hidden sheets with content may not be available to assistive technologies.");
        issues.Should().NotContain(i => i.SheetId == visibleEmptySheet.Id);
        issues.Should().NotContain(i => i.SheetId == hiddenEmptySheet.Id && i.Kind == AccessibilityIssueKind.HiddenSheetWithContent);
    }

    [Fact]
    public void FindIssues_FlagsHiddenSheetsThatContainOnlyComments()
    {
        var workbook = new Workbook("Accessibility");
        var hiddenSheet = workbook.AddSheet("Archived Notes");
        hiddenSheet.IsVeryHidden = true;
        hiddenSheet.Comments[new CellAddress(hiddenSheet.Id, 2, 3)] = "Confidential forecast note";

        var issue = AccessibilityCheckerService.FindIssues(workbook)
            .Should().ContainSingle(i => i.Kind == AccessibilityIssueKind.HiddenSheetWithContent).Subject;

        issue.SheetId.Should().Be(hiddenSheet.Id);
        issue.SheetName.Should().Be("Archived Notes");
        issue.Location.Should().Be("Archived Notes");
        issue.Message.Should().Be("Hidden sheets with content may not be available to assistive technologies.");
    }

    [Fact]
    public void FindIssues_FlagsHiddenSheetsThatContainOnlyHyperlinks()
    {
        var workbook = new Workbook("Accessibility");
        var hiddenSheet = workbook.AddSheet("Archived Links");
        hiddenSheet.IsHidden = true;
        hiddenSheet.Hyperlinks[new CellAddress(hiddenSheet.Id, 2, 3)] = "https://example.com/confidential-report";

        var issue = AccessibilityCheckerService.FindIssues(workbook)
            .Should().ContainSingle(i => i.Kind == AccessibilityIssueKind.HiddenSheetWithContent).Subject;

        issue.SheetId.Should().Be(hiddenSheet.Id);
        issue.SheetName.Should().Be("Archived Links");
        issue.Location.Should().Be("Archived Links");
        issue.Message.Should().Be("Hidden sheets with content may not be available to assistive technologies.");
    }

    [Fact]
    public void FindIssues_FlagsHiddenRowsAndColumnsThatContainContent()
    {
        var workbook = new Workbook("Accessibility");
        var sheet = workbook.AddSheet("Q1 Revenue");
        sheet.HiddenRows.Add(4);
        sheet.GroupHiddenRows.Add(5);
        sheet.HiddenCols.Add(3);
        sheet.GroupHiddenCols.Add(6);
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new TextValue("Hidden row note"));
        sheet.SetCell(new CellAddress(sheet.Id, 5, 2), new TextValue("Grouped row note"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new TextValue("Hidden column note"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 6), new TextValue("Grouped column note"));
        sheet.SetCell(new CellAddress(sheet.Id, 8, 8), new TextValue("Visible note"));

        var issues = AccessibilityCheckerService.FindIssues(workbook);

        issues.Where(i => i.Kind == AccessibilityIssueKind.HiddenRowWithContent)
            .Select(i => i.Location)
            .Should()
            .BeEquivalentTo(["4:4", "5:5"]);
        issues.Where(i => i.Kind == AccessibilityIssueKind.HiddenColumnWithContent)
            .Select(i => i.Location)
            .Should()
            .BeEquivalentTo(["C:C", "F:F"]);
        issues.Should().NotContain(i => i.Location == "H8");
    }

    [Fact]
    public void FindIssues_FlagsHiddenRowsAndColumnsThatContainNonCellContent()
    {
        var workbook = new Workbook("Accessibility");
        var sheet = workbook.AddSheet("Q1 Revenue");
        sheet.HiddenRows.Add(4);
        sheet.GroupHiddenRows.Add(5);
        sheet.HiddenRows.Add(7);
        sheet.HiddenRows.Add(8);
        sheet.HiddenRows.Add(9);
        sheet.HiddenCols.Add(3);
        sheet.GroupHiddenCols.Add(6);
        sheet.HiddenCols.Add(7);

        sheet.Comments[new CellAddress(sheet.Id, 4, 1)] = "Hidden row note";
        sheet.ThreadedComments[new CellAddress(sheet.Id, 1, 3)] = new ThreadedComment("Hidden column thread");
        sheet.Hyperlinks[new CellAddress(sheet.Id, 9, 2)] = "https://example.com/hidden-row";
        sheet.Hyperlinks[new CellAddress(sheet.Id, 2, 7)] = "https://example.com/hidden-column";
        var table = new StructuredTableModel
        {
            Id = 1,
            Name = "Table1",
            DisplayName = "Table1",
            Range = new GridRange(new CellAddress(sheet.Id, 5, 1), new CellAddress(sheet.Id, 6, 2)),
            HeaderRowCount = 1,
            HasAutoFilter = true
        };
        table.Columns.Add(new StructuredTableColumnModel(1, "Region"));
        table.Columns.Add(new StructuredTableColumnModel(2, "Sales"));
        sheet.StructuredTables.Add(table);
        sheet.Sparklines.Add(new SparklineModel
        {
            DataRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 1)),
            Location = new CellAddress(sheet.Id, 2, 6)
        });
        sheet.Pictures.Add(new PictureModel
        {
            Anchor = new CellAddress(sheet.Id, 7, 2),
            AltText = "Regional revenue image"
        });
        sheet.DrawingShapes.Add(new DrawingShapeModel
        {
            Anchor = new CellAddress(sheet.Id, 8, 2),
            AltText = "Hidden object marker",
            IsVisible = false
        });

        var issues = AccessibilityCheckerService.FindIssues(workbook);

        issues.Where(i => i.Kind == AccessibilityIssueKind.HiddenRowWithContent)
            .Select(i => i.Location)
            .Should()
            .Equal("4:4", "5:5", "7:7", "9:9");
        issues.Where(i => i.Kind == AccessibilityIssueKind.HiddenColumnWithContent)
            .Select(i => i.Location)
            .Should()
            .Equal("C:C", "F:F", "G:G");
        issues.Should().NotContain(i => i.Kind == AccessibilityIssueKind.HiddenRowWithContent && i.Location == "8:8");
    }
}
