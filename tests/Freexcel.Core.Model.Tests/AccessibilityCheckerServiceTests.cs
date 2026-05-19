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
    public void FindIssues_FlagsHiddenSheetsRowsAndColumnsThatContainWorkbookContent()
    {
        var workbook = new Workbook("Accessibility");
        var visible = workbook.AddSheet("Visible");
        var hidden = workbook.AddSheet("Hidden");

        visible.HiddenRows.Add(3);
        visible.HiddenCols.Add(4);
        visible.SetCell(new CellAddress(visible.Id, 3, 2), new TextValue("Hidden row value"));
        visible.SetCell(new CellAddress(visible.Id, 5, 4), new NumberValue(12));
        visible.Comments[new CellAddress(visible.Id, 3, 5)] = "Hidden row comment";
        visible.Hyperlinks[new CellAddress(visible.Id, 6, 4)] = "https://example.com";
        visible.Pictures.Add(new PictureModel
        {
            Anchor = new CellAddress(visible.Id, 3, 4),
            Kind = PictureKind.Image,
            AltText = "Logo"
        });

        hidden.IsHidden = true;
        hidden.SetCell(new CellAddress(hidden.Id, 1, 1), new TextValue("Hidden sheet value"));

        var issues = AccessibilityCheckerService.FindIssues(workbook);

        issues.Should().ContainSingle(i =>
            i.Kind == AccessibilityIssueKind.HiddenSheetContent &&
            i.SheetName == "Hidden" &&
            i.Location == "Sheet");
        issues.Should().ContainSingle(i =>
            i.Kind == AccessibilityIssueKind.HiddenRowContent &&
            i.SheetName == "Visible" &&
            i.Location == "3:3");
        issues.Should().ContainSingle(i =>
            i.Kind == AccessibilityIssueKind.HiddenColumnContent &&
            i.SheetName == "Visible" &&
            i.Location == "D:D");
    }

    [Fact]
    public void FindIssues_FlagsHiddenRowContainingOnlySparkline()
    {
        var workbook = new Workbook("Accessibility");
        var sheet = workbook.AddSheet("Sparklines");

        sheet.HiddenRows.Add(4);
        AddSparkline(sheet, 4, 7);

        var issues = AccessibilityCheckerService.FindIssues(workbook);

        issues.Should().ContainSingle(i =>
            i.Kind == AccessibilityIssueKind.HiddenRowContent &&
            i.SheetName == "Sparklines" &&
            i.Location == "4:4");
    }

    [Fact]
    public void FindIssues_FlagsHiddenColumnContainingOnlySparkline()
    {
        var workbook = new Workbook("Accessibility");
        var sheet = workbook.AddSheet("Sparklines");

        sheet.HiddenCols.Add(7);
        AddSparkline(sheet, 4, 7);

        var issues = AccessibilityCheckerService.FindIssues(workbook);

        issues.Should().ContainSingle(i =>
            i.Kind == AccessibilityIssueKind.HiddenColumnContent &&
            i.SheetName == "Sparklines" &&
            i.Location == "G:G");
    }

    [Fact]
    public void FindIssues_FlagsUnclearHyperlinkDisplayText()
    {
        var workbook = new Workbook("Accessibility");
        var sheet = workbook.AddSheet("Links");

        AddHyperlink(sheet, 1, "https://example.com", null);
        AddHyperlink(sheet, 2, "https://example.com/report", "https://example.com/report");
        AddHyperlink(sheet, 3, "https://example.com/help", "Click here");
        AddHyperlink(sheet, 4, "https://example.com/details", "Quarterly details");

        var issues = AccessibilityCheckerService.FindIssues(workbook);

        issues.Where(i => i.Kind == AccessibilityIssueKind.UnclearHyperlinkText)
            .Select(i => i.Location)
            .Should()
            .Equal("A1", "A2", "A3");
    }

    [Fact]
    public void FindIssues_FlagsChartsWithoutTitle()
    {
        var workbook = new Workbook("Accessibility");
        var sheet = workbook.AddSheet("Charts");
        var dataRange = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 3, 2));

        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Column,
            DataRange = dataRange,
            Title = " "
        });
        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Line,
            DataRange = dataRange,
            Title = "Revenue trend"
        });

        var issues = AccessibilityCheckerService.FindIssues(workbook);

        issues.Should().ContainSingle(i =>
            i.Kind == AccessibilityIssueKind.MissingChartTitle &&
            i.Location == "A1:B3");
    }

    [Fact]
    public void FindIssues_OrdersIssuesBySheetThenStableCheckOrderAndCoordinates()
    {
        var workbook = new Workbook("Accessibility");
        var first = workbook.AddSheet("First");
        var second = workbook.AddSheet("Second");

        first.HiddenRows.Add(5);
        first.HiddenRows.Add(2);
        first.HiddenCols.Add(3);
        first.HiddenCols.Add(1);
        first.SetCell(new CellAddress(first.Id, 5, 1), new TextValue("Later row"));
        first.SetCell(new CellAddress(first.Id, 2, 1), new TextValue("Earlier row"));
        first.SetCell(new CellAddress(first.Id, 1, 3), new TextValue("Later column"));
        first.SetCell(new CellAddress(first.Id, 1, 1), new TextValue("Earlier column"));
        second.HiddenRows.Add(1);
        second.SetCell(new CellAddress(second.Id, 1, 1), new TextValue("Second sheet"));

        var issues = AccessibilityCheckerService.FindIssues(workbook);

        issues.Select(i => $"{i.SheetName}:{i.Kind}:{i.Location}")
            .Should()
            .Equal(
                "First:HiddenRowContent:2:2",
                "First:HiddenRowContent:5:5",
                "First:HiddenColumnContent:A:A",
                "First:HiddenColumnContent:C:C",
                "Second:HiddenRowContent:1:1");
    }

    private static void AddHyperlink(Sheet sheet, uint row, string target, string? displayText)
    {
        var address = new CellAddress(sheet.Id, row, 1);
        sheet.Hyperlinks[address] = target;
        if (displayText is not null)
            sheet.SetCell(address, new TextValue(displayText));
    }

    private static void AddSparkline(Sheet sheet, uint row, uint col)
    {
        sheet.Sparklines.Add(new SparklineModel
        {
            DataRange = new GridRange(
                new CellAddress(sheet.Id, row, 1),
                new CellAddress(sheet.Id, row, 3)),
            Location = new CellAddress(sheet.Id, row, col)
        });
    }
}
