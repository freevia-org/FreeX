using System.Diagnostics;
using FreeX.Core.Commands;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Model.Tests;

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
            Title = "Sales by quarter",
            XAxisTitle = "Quarter",
            YAxisTitle = "Sales"
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
    [InlineData("Chart Title")]
    [InlineData("chart title")]
    [InlineData("Title")]
    [InlineData("Chart 1")]
    public void FindIssues_FlagsChartsWithGenericTitleText(string title)
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
            Title = title,
            XAxisTitle = "Quarter",
            YAxisTitle = "Sales"
        });
        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Bar,
            DataRange = dataRange,
            Title = "Sales by quarter",
            XAxisTitle = "Quarter",
            YAxisTitle = "Sales"
        });

        var issue = AccessibilityCheckerService.FindIssues(workbook)
            .Should().ContainSingle(i => i.Kind == AccessibilityIssueKind.GenericChartTitle).Subject;

        issue.Location.Should().Be("A1:B4");
        issue.Message.Should().Be("Chart title should describe the chart.");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastChartTitleText()
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
            Title = "Sales by quarter",
            XAxisTitle = "Quarter",
            YAxisTitle = "Sales",
            ChartAreaFillColor = new CellColor(130, 130, 130),
            ChartTitleTextColor = new CellColor(120, 120, 120)
        });

        var issue = AccessibilityCheckerService.FindIssues(workbook)
            .Should().ContainSingle(i => i.Kind == AccessibilityIssueKind.LowContrastChartText).Subject;

        issue.Location.Should().Be("A1:B4");
        issue.Message.Should().Be("Chart title should have at least 4.5:1 contrast against its background.");
    }

    [Fact]
    public void FindIssues_FlagsChartsWithMissingAxisTitles()
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
            Title = "Sales by quarter",
            XAxisTitle = "",
            YAxisTitle = "   "
        });

        var issues = AccessibilityCheckerService.FindIssues(workbook)
            .Where(i => i.Kind == AccessibilityIssueKind.ChartMissingAxisTitle)
            .ToList();

        issues.Select(i => i.Location).Should().Equal("A1:B4", "A1:B4");
        issues.Select(i => i.Message).Should().Equal(
            "Chart X-axis is missing a title.",
            "Chart Y-axis is missing a title.");
    }

    [Fact]
    public void FindIssues_FlagsChartsWithGenericAxisTitles()
    {
        var workbook = new Workbook("Accessibility");
        var sheet = workbook.AddSheet("Charts");
        var dataRange = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 4, 2));
        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Line,
            DataRange = dataRange,
            Title = "Sales by quarter",
            XAxisTitle = "Axis Title",
            YAxisTitle = "Value Axis 1"
        });

        var issues = AccessibilityCheckerService.FindIssues(workbook)
            .Where(i => i.Kind == AccessibilityIssueKind.GenericChartAxisTitle)
            .ToList();

        issues.Select(i => i.Message).Should().Equal(
            "Chart X-axis title should describe the axis.",
            "Chart Y-axis title should describe the axis.");
    }

    [Fact]
    public void FindIssues_IgnoresAxisTitleRulesForHiddenAxesAndChartsWithoutAxes()
    {
        var workbook = new Workbook("Accessibility");
        var sheet = workbook.AddSheet("Charts");
        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Pie,
            DataRange = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 4, 2)),
            Title = "Product mix"
        });
        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Column,
            DataRange = new GridRange(
                new CellAddress(sheet.Id, 6, 1),
                new CellAddress(sheet.Id, 9, 2)),
            Title = "Hidden axes",
            HideXAxis = true,
            HideYAxis = true
        });

        AccessibilityCheckerService.FindIssues(workbook)
            .Should()
            .NotContain(i =>
                i.Kind == AccessibilityIssueKind.ChartMissingAxisTitle ||
                i.Kind == AccessibilityIssueKind.GenericChartAxisTitle);
    }

    [Fact]
    public void FindIssues_FlagsLowContrastChartAxisLabelsDataTableAndTrendlineText()
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
            Title = "Sales by quarter",
            XAxisTitle = "Quarter",
            YAxisTitle = "Revenue",
            ChartAreaFillColor = new CellColor(130, 130, 130),
            ChartTitleTextColor = CellColor.Black,
            AxisTitleTextColor = CellColor.Black,
            XAxisLabelTextColor = new CellColor(120, 120, 120),
            YAxisLabelTextColor = CellColor.Black,
            DataTable = new ChartDataTableModel
            {
                FillColor = new CellColor(130, 130, 130),
                TextColor = new CellColor(120, 120, 120),
                FontSize = 10
            },
            ShowLinearTrendline = true,
            ShowTrendlineEquation = true,
            TrendlineLabelFillColor = new CellColor(130, 130, 130),
            TrendlineLabelTextColor = new CellColor(120, 120, 120)
        });

        var messages = AccessibilityCheckerService.FindIssues(workbook)
            .Where(i => i.Kind == AccessibilityIssueKind.LowContrastChartText)
            .Select(i => i.Message);

        messages.Should().Equal(
            "X-axis labels should have at least 4.5:1 contrast against its background.",
            "Chart data table text should have at least 4.5:1 contrast against its background.",
            "Trendline label text should have at least 4.5:1 contrast against its background.");
    }

    [Fact]
    public void FindIssues_IgnoresChartTextWithSufficientContrast()
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
            Title = "Sales by quarter",
            XAxisTitle = "Quarter",
            YAxisTitle = "Revenue",
            ChartAreaFillColor = CellColor.White,
            ChartTitleTextColor = CellColor.Black,
            AxisTitleTextColor = CellColor.Black,
            LegendTextColor = CellColor.Black,
            LegendFillColor = CellColor.White
        });

        AccessibilityCheckerService.FindIssues(workbook)
            .Should().NotContain(i => i.Kind == AccessibilityIssueKind.LowContrastChartText);
    }

    [Theory]
    [InlineData("Picture 1")]
    [InlineData("Image")]
    [InlineData("Image 2.")]
    [InlineData("IMG_0001.jpg")]
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
    public void FindIssues_AllowsDrawingObjectTitleOrNameWhenAltDescriptionIsMissingOrGeneric()
    {
        var workbook = new Workbook("Accessibility");
        var sheet = workbook.AddSheet("Objects");
        sheet.Pictures.Add(new PictureModel
        {
            Anchor = new CellAddress(sheet.Id, 1, 1),
            Kind = PictureKind.Image,
            AltText = "Image",
            Title = "Regional revenue map"
        });
        sheet.DrawingShapes.Add(new DrawingShapeModel
        {
            Anchor = new CellAddress(sheet.Id, 2, 1),
            Kind = DrawingShapeKind.Rectangle,
            Name = "Approval workflow callout"
        });
        sheet.TextBoxes.Add(new TextBoxModel
        {
            Anchor = new CellAddress(sheet.Id, 3, 1),
            Text = "Risk summary",
            Title = "Risk summary callout",
            FillColor = CellColor.White
        });

        AccessibilityCheckerService.FindIssues(workbook)
            .Where(i => i.Kind is AccessibilityIssueKind.MissingAltText or AccessibilityIssueKind.GenericAltText)
            .Should()
            .BeEmpty();
    }

    [Fact]
    public void FindIssues_FlagsGenericDrawingObjectTitleOrNameWithoutDescriptiveAltText()
    {
        var workbook = new Workbook("Accessibility");
        var sheet = workbook.AddSheet("Objects");
        sheet.Pictures.Add(new PictureModel
        {
            Anchor = new CellAddress(sheet.Id, 1, 1),
            Kind = PictureKind.Image,
            Title = "Picture 1"
        });
        sheet.DrawingShapes.Add(new DrawingShapeModel
        {
            Anchor = new CellAddress(sheet.Id, 2, 1),
            Kind = DrawingShapeKind.Rectangle,
            Name = "Shape 2"
        });
        sheet.DrawingShapes.Add(new DrawingShapeModel
        {
            Anchor = new CellAddress(sheet.Id, 3, 1),
            Kind = DrawingShapeKind.Rectangle,
            Name = "Rectangle 1"
        });
        sheet.DrawingShapes.Add(new DrawingShapeModel
        {
            Anchor = new CellAddress(sheet.Id, 4, 1),
            Kind = DrawingShapeKind.Ellipse,
            Name = "Ellipse 1"
        });
        sheet.DrawingShapes.Add(new DrawingShapeModel
        {
            Anchor = new CellAddress(sheet.Id, 5, 1),
            Kind = DrawingShapeKind.Ellipse,
            Name = "Oval 1"
        });
        sheet.DrawingShapes.Add(new DrawingShapeModel
        {
            Anchor = new CellAddress(sheet.Id, 6, 1),
            Kind = DrawingShapeKind.Line,
            Name = "Line 1"
        });
        sheet.TextBoxes.Add(new TextBoxModel
        {
            Anchor = new CellAddress(sheet.Id, 7, 1),
            Text = "Risk summary",
            Name = "TextBox 3",
            FillColor = CellColor.White
        });

        var issues = AccessibilityCheckerService.FindIssues(workbook)
            .Where(i => i.Kind == AccessibilityIssueKind.GenericAltText)
            .ToList();

        issues.Select(i => i.Location).Should().Equal("A1", "A2", "A3", "A4", "A5", "A6", "A7");
        issues.Select(i => i.Message).Should().Equal(
            "Picture alternate text should describe the object.",
            "Shape alternate text should describe the object.",
            "Shape alternate text should describe the object.",
            "Shape alternate text should describe the object.",
            "Shape alternate text should describe the object.",
            "Shape alternate text should describe the object.",
            "Text box alternate text should describe the object.");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastTextBoxText_WithExplicitFill()
    {
        var workbook = new Workbook("Accessibility");
        var sheet = workbook.AddSheet("Objects");
        sheet.TextBoxes.Add(new TextBoxModel
        {
            Anchor = new CellAddress(sheet.Id, 2, 1),
            Text = "Revenue slipped in April",
            AltText = "April revenue annotation",
            FillColor = new CellColor(20, 20, 20)
        });

        var issue = AccessibilityCheckerService.FindIssues(workbook)
            .Should().ContainSingle(i => i.Kind == AccessibilityIssueKind.LowContrastObjectText).Subject;

        issue.Location.Should().Be("A2");
        issue.Message.Should().Be("Text box text should have at least 4.5:1 contrast against its fill.");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastTextBoxText_WithThemeFill()
    {
        var workbook = new Workbook("Accessibility")
        {
            Theme = WorkbookTheme.Office
                .WithColor(WorkbookThemeColorSlot.Accent1, new CellColor(245, 245, 245))
                .WithColor(WorkbookThemeColorSlot.Accent2, new CellColor(240, 240, 240))
                .WithSupplementalMetadata(
                    [],
                    hasObjectDefaults: true,
                    objectDefaults: new WorkbookThemeObjectDefaults(
                        Text: new WorkbookThemeTextObjectDefault(
                            TextThemeColor: new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent2, 0.1))))
        };
        var sheet = workbook.AddSheet("Objects");
        sheet.TextBoxes.Add(new TextBoxModel
        {
            Anchor = new CellAddress(sheet.Id, 3, 2),
            Text = "Theme-colored callout",
            AltText = "Theme-colored callout annotation",
            FillThemeColor = new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent1)
        });

        var issue = AccessibilityCheckerService.FindIssues(workbook)
            .Should().ContainSingle(i => i.Kind == AccessibilityIssueKind.LowContrastObjectText).Subject;

        issue.Location.Should().Be("B3");
        issue.Message.Should().Be("Text box text should have at least 4.5:1 contrast against its fill.");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastTextBoxText_WithObjectDefaultThemeFill()
    {
        var workbook = new Workbook("Accessibility")
        {
            Theme = WorkbookTheme.Office
                .WithColor(WorkbookThemeColorSlot.Accent1, new CellColor(25, 25, 25))
                .WithSupplementalMetadata(
                    [],
                    hasObjectDefaults: true,
                    objectDefaults: new WorkbookThemeObjectDefaults(
                        Shape: new WorkbookThemeShapeObjectDefault(
                            FillThemeColor: new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent1))))
        };
        var sheet = workbook.AddSheet("Objects");
        sheet.TextBoxes.Add(new TextBoxModel
        {
            Anchor = new CellAddress(sheet.Id, 4, 2),
            Text = "Object-default callout",
            AltText = "Object-default callout annotation"
        });

        var issue = AccessibilityCheckerService.FindIssues(workbook)
            .Should().ContainSingle(i => i.Kind == AccessibilityIssueKind.LowContrastObjectText).Subject;

        issue.Location.Should().Be("B4");
        issue.Message.Should().Be("Text box text should have at least 4.5:1 contrast against its fill.");
    }

    [Fact]
    public void FindIssues_TextBoxExplicitFillOverridesObjectDefaultFill()
    {
        var workbook = new Workbook("Accessibility")
        {
            Theme = WorkbookTheme.Office.WithSupplementalMetadata(
                [],
                hasObjectDefaults: true,
                objectDefaults: new WorkbookThemeObjectDefaults(
                    Shape: new WorkbookThemeShapeObjectDefault(
                        FillColor: new CellColor(25, 25, 25))))
        };
        var sheet = workbook.AddSheet("Objects");
        sheet.TextBoxes.Add(new TextBoxModel
        {
            Anchor = new CellAddress(sheet.Id, 5, 2),
            Text = "Readable override",
            AltText = "Readable override annotation",
            FillColor = CellColor.White
        });

        AccessibilityCheckerService.FindIssues(workbook)
            .Should().NotContain(i => i.Kind == AccessibilityIssueKind.LowContrastObjectText);
    }

    [Fact]
    public void FindIssues_IgnoresTextBoxTextWithSufficientContrastOrNoText()
    {
        var workbook = new Workbook("Accessibility");
        var sheet = workbook.AddSheet("Objects");
        sheet.TextBoxes.Add(new TextBoxModel
        {
            Anchor = new CellAddress(sheet.Id, 2, 1),
            Text = "Readable annotation",
            AltText = "Readable annotation",
            FillColor = CellColor.White
        });
        sheet.TextBoxes.Add(new TextBoxModel
        {
            Anchor = new CellAddress(sheet.Id, 3, 1),
            Text = "   ",
            AltText = "Blank annotation shape",
            FillColor = new CellColor(20, 20, 20)
        });

        AccessibilityCheckerService.FindIssues(workbook)
            .Should().NotContain(i => i.Kind == AccessibilityIssueKind.LowContrastObjectText);
    }

    [Fact]
    public void FindIssues_IgnoresHiddenDrawingObjectsForAltTextChecks()
    {
        var workbook = new Workbook("Accessibility");
        var sheet = workbook.AddSheet("Objects");

        sheet.Pictures.Add(new PictureModel
        {
            Anchor = new CellAddress(sheet.Id, 1, 1),
            Kind = PictureKind.Image,
            IsVisible = false
        });
        sheet.Pictures.Add(new PictureModel
        {
            Anchor = new CellAddress(sheet.Id, 2, 1),
            Kind = PictureKind.Image,
            AltText = "Picture 1",
            IsVisible = false
        });
        sheet.DrawingShapes.Add(new DrawingShapeModel
        {
            Anchor = new CellAddress(sheet.Id, 3, 1),
            Kind = DrawingShapeKind.Rectangle,
            IsVisible = false
        });
        sheet.DrawingShapes.Add(new DrawingShapeModel
        {
            Anchor = new CellAddress(sheet.Id, 4, 1),
            Kind = DrawingShapeKind.Rectangle,
            AltText = "Shape",
            IsVisible = false
        });
        sheet.TextBoxes.Add(new TextBoxModel
        {
            Anchor = new CellAddress(sheet.Id, 5, 1),
            Text = "Hidden annotation",
            IsVisible = false
        });
        sheet.TextBoxes.Add(new TextBoxModel
        {
            Anchor = new CellAddress(sheet.Id, 6, 1),
            Text = "Hidden annotation",
            AltText = "Text box",
            FillColor = new CellColor(20, 20, 20),
            IsVisible = false
        });

        var issues = AccessibilityCheckerService.FindIssues(workbook);

        issues.Where(i =>
                i.Kind == AccessibilityIssueKind.MissingAltText ||
                i.Kind == AccessibilityIssueKind.GenericAltText ||
                i.Kind == AccessibilityIssueKind.LowContrastObjectText)
            .Should()
            .BeEmpty();
    }

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

    [Fact]
    public void FindIssues_FlagsStructuredTablesWithoutHeaderRows()
    {
        var workbook = new Workbook("Accessibility");
        var sheet = workbook.AddSheet("Sales");
        sheet.StructuredTables.Add(new StructuredTableModel
        {
            Id = 1,
            Name = "Table1",
            DisplayName = "Table1",
            Range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 2)),
            HeaderRowCount = 0,
            HasAutoFilter = true,
        });

        var issue = AccessibilityCheckerService.FindIssues(workbook)
            .Should().ContainSingle(i => i.Kind == AccessibilityIssueKind.TableMissingHeaderRow).Subject;

        issue.SheetId.Should().Be(sheet.Id);
        issue.SheetName.Should().Be("Sales");
        issue.Location.Should().Be("A1:B3");
        issue.Message.Should().Be("Tables should include a header row.");
    }

    [Fact]
    public void FindIssues_FlagsStructuredTablesWithBlankHeaderText()
    {
        var workbook = new Workbook("Accessibility");
        var sheet = workbook.AddSheet("Sales");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue(" "));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("East"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(42));
        sheet.StructuredTables.Add(new StructuredTableModel
        {
            Id = 1,
            Name = "Table1",
            DisplayName = "Table1",
            Range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 2)),
            HeaderRowCount = 1,
            HasAutoFilter = true,
        });

        var issue = AccessibilityCheckerService.FindIssues(workbook)
            .Should().ContainSingle(i => i.Kind == AccessibilityIssueKind.TableMissingHeaderText).Subject;

        issue.SheetId.Should().Be(sheet.Id);
        issue.SheetName.Should().Be("Sales");
        issue.Location.Should().Be("B1");
        issue.Message.Should().Be("Table headers should not be blank.");
    }

    [Fact]
    public void FindIssues_FlagsStructuredTablesWithMissingHeaderCellDespiteColumnMetadata()
    {
        var workbook = new Workbook("Accessibility");
        var sheet = workbook.AddSheet("Sales");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("East"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(42));
        var table = new StructuredTableModel
        {
            Id = 1,
            Name = "Table1",
            DisplayName = "Table1",
            Range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 2)),
            HeaderRowCount = 1,
            HasAutoFilter = true,
        };
        table.Columns.Add(new StructuredTableColumnModel(1, "Region"));
        table.Columns.Add(new StructuredTableColumnModel(2, "Sales"));
        sheet.StructuredTables.Add(table);

        var issue = AccessibilityCheckerService.FindIssues(workbook)
            .Should().ContainSingle(i => i.Kind == AccessibilityIssueKind.TableMissingHeaderText).Subject;

        issue.Location.Should().Be("B1");
        issue.Message.Should().Be("Table headers should not be blank.");
    }

    [Fact]
    public void FindIssues_FlagsStructuredTablesWithDefaultAndDuplicateHeaderText()
    {
        var workbook = new Workbook("Accessibility");
        var sheet = workbook.AddSheet("Sales");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Column1"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new TextValue(" region "));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("East"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(42));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 3), new NumberValue(43));
        sheet.StructuredTables.Add(new StructuredTableModel
        {
            Id = 1,
            Name = "Table1",
            DisplayName = "Table1",
            Range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 3)),
            HeaderRowCount = 1,
            HasAutoFilter = true,
        });

        var issues = AccessibilityCheckerService.FindIssues(workbook);

        issues.Should().ContainSingle(i => i.Kind == AccessibilityIssueKind.TableDefaultHeaderText)
            .Which.Location.Should().Be("A1");
        issues.Should().ContainSingle(i => i.Kind == AccessibilityIssueKind.TableDuplicateHeaderText)
            .Which.Location.Should().Be("C1");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_WithExplicitFontAndFill()
    {
        var workbook = new Workbook("Accessibility");
        var sheet = workbook.AddSheet("Sales");
        var address = new CellAddress(sheet.Id, 3, 2);
        var lowContrastStyle = workbook.RegisterStyle(new CellStyle
        {
            FontColor = new CellColor(120, 120, 120),
            FillColor = new CellColor(130, 130, 130)
        });
        sheet.SetCell(address, new Cell
        {
            Value = new TextValue("Projected revenue"),
            StyleId = lowContrastStyle
        });

        var issue = AccessibilityCheckerService.FindIssues(workbook)
            .Should().ContainSingle(i => i.Kind == AccessibilityIssueKind.LowContrastCellText).Subject;

        issue.SheetId.Should().Be(sheet.Id);
        issue.SheetName.Should().Be("Sales");
        issue.Location.Should().Be("B3");
        issue.Message.Should().Be("Cell text should have at least 4.5:1 contrast against its fill.");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_ForDisplayedNonTextValues()
    {
        var workbook = new Workbook("Accessibility");
        var sheet = workbook.AddSheet("Values");
        var lowContrastStyle = workbook.RegisterStyle(new CellStyle
        {
            FontColor = new CellColor(120, 120, 120),
            FillColor = new CellColor(130, 130, 130)
        });
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new Cell
        {
            Value = new NumberValue(42),
            StyleId = lowContrastStyle
        });
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new Cell
        {
            Value = new BoolValue(true),
            StyleId = lowContrastStyle
        });
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new Cell
        {
            Value = ErrorValue.DivByZero,
            StyleId = lowContrastStyle
        });

        var issues = AccessibilityCheckerService.FindIssues(workbook)
            .Where(i => i.Kind == AccessibilityIssueKind.LowContrastCellText)
            .ToList();

        issues.Should().HaveCount(3);
        issues.Select(i => i.Location).Should().BeEquivalentTo(new[] { "A1", "B1", "C1" });
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_WithDefaultWhiteBackground()
    {
        var workbook = new Workbook("Accessibility");
        var sheet = workbook.AddSheet("Sales");
        var address = new CellAddress(sheet.Id, 1, 1);
        var lowContrastStyle = workbook.RegisterStyle(new CellStyle
        {
            FontColor = new CellColor(245, 245, 245)
        });
        sheet.SetCell(address, new Cell
        {
            Value = new TextValue("Low contrast on no fill"),
            StyleId = lowContrastStyle
        });

        var issue = AccessibilityCheckerService.FindIssues(workbook)
            .Should().ContainSingle(i => i.Kind == AccessibilityIssueKind.LowContrastCellText).Subject;

        issue.Location.Should().Be("A1");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_WithThemeTintFontAndFill()
    {
        var workbook = new Workbook("Accessibility")
        {
            Theme = WorkbookTheme.Office
                .WithColor(WorkbookThemeColorSlot.Accent1, new CellColor(245, 245, 245))
                .WithColor(WorkbookThemeColorSlot.Accent2, new CellColor(230, 230, 230))
        };
        var sheet = workbook.AddSheet("Sales");
        var address = new CellAddress(sheet.Id, 4, 2);
        var themedStyle = workbook.RegisterStyle(new CellStyle
        {
            FontColor = CellColor.Black,
            FontThemeColor = new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent2, 0.1),
            FillColor = CellColor.White,
            FillThemeColor = new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent1)
        });
        sheet.SetCell(address, new Cell
        {
            Value = new TextValue("Theme-derived warning"),
            StyleId = themedStyle
        });

        var issue = AccessibilityCheckerService.FindIssues(workbook)
            .Should().ContainSingle(i => i.Kind == AccessibilityIssueKind.LowContrastCellText).Subject;

        issue.Location.Should().Be("B4");
    }

    [Fact]
    public void FindIssues_IgnoresCellRgbFallbackWhenThemeColorsHaveSufficientContrast()
    {
        var workbook = new Workbook("Accessibility");
        var sheet = workbook.AddSheet("Sales");
        var address = new CellAddress(sheet.Id, 5, 2);
        var themedStyle = workbook.RegisterStyle(new CellStyle
        {
            FontColor = new CellColor(245, 245, 245),
            FontThemeColor = new WorkbookThemeColorReference(WorkbookThemeColorSlot.Dark1),
            FillColor = CellColor.White,
            FillThemeColor = new WorkbookThemeColorReference(WorkbookThemeColorSlot.Light1)
        });
        sheet.SetCell(address, new Cell
        {
            Value = new TextValue("Readable themed text"),
            StyleId = themedStyle
        });

        AccessibilityCheckerService.FindIssues(workbook)
            .Should().NotContain(i => i.Kind == AccessibilityIssueKind.LowContrastCellText);
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromMatchingConditionalFormat()
    {
        var workbook = new Workbook("Accessibility");
        var sheet = workbook.AddSheet("Sales");
        var address = new CellAddress(sheet.Id, 2, 2);
        sheet.SetCell(address, new TextValue("At risk"));
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(address, address),
            RuleType = CfRuleType.ContainsText,
            TextRuleText = "risk",
            FormatIfTrue = new CellStyle
            {
                FontColor = new CellColor(120, 120, 120),
                FillColor = new CellColor(130, 130, 130)
            }
        });

        var issue = AccessibilityCheckerService.FindIssues(workbook)
            .Should().ContainSingle(i => i.Kind == AccessibilityIssueKind.LowContrastCellText).Subject;

        issue.Location.Should().Be("B2");
        issue.Message.Should().Be("Cell text should have at least 4.5:1 contrast against its fill.");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromDuplicateValuesConditionalFormat()
    {
        var workbook = new Workbook("Accessibility");
        var sheet = workbook.AddSheet("Sales");
        var first = new CellAddress(sheet.Id, 1, 1);
        var second = new CellAddress(sheet.Id, 2, 1);
        var third = new CellAddress(sheet.Id, 3, 1);
        sheet.SetCell(first, new TextValue("West"));
        sheet.SetCell(second, new TextValue("West"));
        sheet.SetCell(third, new TextValue("North"));
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(first, third),
            RuleType = CfRuleType.DuplicateValues,
            FormatIfTrue = new CellStyle
            {
                FontColor = new CellColor(120, 120, 120),
                FillColor = new CellColor(130, 130, 130)
            }
        });

        var issues = AccessibilityCheckerService.FindIssues(workbook)
            .Where(issue => issue.Kind == AccessibilityIssueKind.LowContrastCellText)
            .ToList();

        issues.Select(issue => issue.Location).Should().Equal("A1", "A2");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromUniqueValuesConditionalFormat()
    {
        var workbook = new Workbook("Accessibility");
        var sheet = workbook.AddSheet("Sales");
        var first = new CellAddress(sheet.Id, 1, 1);
        var second = new CellAddress(sheet.Id, 2, 1);
        var third = new CellAddress(sheet.Id, 3, 1);
        sheet.SetCell(first, new TextValue("West"));
        sheet.SetCell(second, new TextValue("West"));
        sheet.SetCell(third, new TextValue("North"));
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(first, third),
            RuleType = CfRuleType.UniqueValues,
            FormatIfTrue = new CellStyle
            {
                FontColor = new CellColor(120, 120, 120),
                FillColor = new CellColor(130, 130, 130)
            }
        });

        var issue = AccessibilityCheckerService.FindIssues(workbook)
            .Should().ContainSingle(issue => issue.Kind == AccessibilityIssueKind.LowContrastCellText).Subject;

        issue.Location.Should().Be("A3");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromAboveAverageConditionalFormat()
    {
        var workbook = new Workbook("Accessibility");
        var sheet = workbook.AddSheet("Sales");
        var first = new CellAddress(sheet.Id, 1, 1);
        var second = new CellAddress(sheet.Id, 2, 1);
        var third = new CellAddress(sheet.Id, 3, 1);
        sheet.SetCell(first, new TextValue("10"));
        sheet.SetCell(second, new TextValue("20"));
        sheet.SetCell(third, new TextValue("30"));
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(first, third),
            RuleType = CfRuleType.AboveAverage,
            AboveAverage = true,
            FormatIfTrue = new CellStyle
            {
                FontColor = new CellColor(120, 120, 120),
                FillColor = new CellColor(130, 130, 130)
            }
        });

        var issue = AccessibilityCheckerService.FindIssues(workbook)
            .Should().ContainSingle(issue => issue.Kind == AccessibilityIssueKind.LowContrastCellText).Subject;

        issue.Location.Should().Be("A3");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromBelowAverageConditionalFormat()
    {
        var workbook = new Workbook("Accessibility");
        var sheet = workbook.AddSheet("Sales");
        var first = new CellAddress(sheet.Id, 1, 1);
        var second = new CellAddress(sheet.Id, 2, 1);
        var third = new CellAddress(sheet.Id, 3, 1);
        sheet.SetCell(first, new TextValue("10"));
        sheet.SetCell(second, new TextValue("20"));
        sheet.SetCell(third, new TextValue("30"));
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(first, third),
            RuleType = CfRuleType.AboveAverage,
            AboveAverage = false,
            FormatIfTrue = new CellStyle
            {
                FontColor = new CellColor(120, 120, 120),
                FillColor = new CellColor(130, 130, 130)
            }
        });

        var issue = AccessibilityCheckerService.FindIssues(workbook)
            .Should().ContainSingle(issue => issue.Kind == AccessibilityIssueKind.LowContrastCellText).Subject;

        issue.Location.Should().Be("A1");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromTopRankedConditionalFormat()
    {
        var workbook = new Workbook("Accessibility");
        var sheet = workbook.AddSheet("Sales");
        var first = new CellAddress(sheet.Id, 1, 1);
        var second = new CellAddress(sheet.Id, 2, 1);
        var third = new CellAddress(sheet.Id, 3, 1);
        sheet.SetCell(first, new TextValue("10"));
        sheet.SetCell(second, new TextValue("20"));
        sheet.SetCell(third, new TextValue("30"));
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(first, third),
            RuleType = CfRuleType.Top10,
            TopBottomRank = 2,
            AboveAverage = true,
            FormatIfTrue = new CellStyle
            {
                FontColor = new CellColor(120, 120, 120),
                FillColor = new CellColor(130, 130, 130)
            }
        });

        var issues = AccessibilityCheckerService.FindIssues(workbook)
            .Where(issue => issue.Kind == AccessibilityIssueKind.LowContrastCellText)
            .ToList();

        issues.Select(issue => issue.Location).Should().Equal("A2", "A3");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromBottomPercentConditionalFormat()
    {
        var workbook = new Workbook("Accessibility");
        var sheet = workbook.AddSheet("Sales");
        var first = new CellAddress(sheet.Id, 1, 1);
        var second = new CellAddress(sheet.Id, 2, 1);
        var third = new CellAddress(sheet.Id, 3, 1);
        sheet.SetCell(first, new TextValue("10"));
        sheet.SetCell(second, new TextValue("20"));
        sheet.SetCell(third, new TextValue("30"));
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(first, third),
            RuleType = CfRuleType.Top10,
            TopBottomRank = 50,
            TopBottomPercent = true,
            AboveAverage = false,
            FormatIfTrue = new CellStyle
            {
                FontColor = new CellColor(120, 120, 120),
                FillColor = new CellColor(130, 130, 130)
            }
        });

        var issues = AccessibilityCheckerService.FindIssues(workbook)
            .Where(issue => issue.Kind == AccessibilityIssueKind.LowContrastCellText)
            .ToList();

        issues.Select(issue => issue.Location).Should().Equal("A1", "A2");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromDateOccurringConditionalFormat()
    {
        var workbook = new Workbook("Accessibility");
        var sheet = workbook.AddSheet("Sales");
        var recent = new CellAddress(sheet.Id, 1, 1);
        var older = new CellAddress(sheet.Id, 2, 1);
        var today = DateTime.Today;
        sheet.SetCell(recent, DateTimeValue.FromDateTime(today.AddDays(-3)));
        sheet.SetCell(older, DateTimeValue.FromDateTime(today.AddDays(-8)));
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(recent, older),
            RuleType = CfRuleType.DateOccurring,
            DateOccurringPeriod = "last7Days",
            FormatIfTrue = new CellStyle
            {
                FontColor = new CellColor(120, 120, 120),
                FillColor = new CellColor(130, 130, 130)
            }
        });

        var issue = AccessibilityCheckerService.FindIssues(workbook)
            .Should().ContainSingle(issue => issue.Kind == AccessibilityIssueKind.LowContrastCellText).Subject;

        issue.Location.Should().Be("A1");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatComparison()
    {
        var workbook = new Workbook("Accessibility");
        var sheet = workbook.AddSheet("Sales");
        var firstLabel = new CellAddress(sheet.Id, 1, 2);
        var secondLabel = new CellAddress(sheet.Id, 2, 2);
        var thirdLabel = new CellAddress(sheet.Id, 3, 2);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(75));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(100));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(125));
        sheet.SetCell(firstLabel, new TextValue("On track"));
        sheet.SetCell(secondLabel, new TextValue("At threshold"));
        sheet.SetCell(thirdLabel, new TextValue("Escalated"));
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(firstLabel, thirdLabel),
            RuleType = CfRuleType.Formula,
            FormulaText = "$A1>=100",
            FormatIfTrue = new CellStyle
            {
                FontColor = new CellColor(120, 120, 120),
                FillColor = new CellColor(130, 130, 130)
            }
        });

        var issues = AccessibilityCheckerService.FindIssues(workbook)
            .Where(issue => issue.Kind == AccessibilityIssueKind.LowContrastCellText)
            .ToList();

        issues.Select(issue => issue.Location).Should().Equal("B2", "B3");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_WhenPatternForegroundIsLowContrast()
    {
        var workbook = new Workbook("Accessibility");
        var sheet = workbook.AddSheet("Sales");
        var address = new CellAddress(sheet.Id, 2, 2);
        var patternedStyle = workbook.RegisterStyle(new CellStyle
        {
            FontColor = CellColor.Black,
            FillColor = CellColor.White,
            FillPatternStyle = CellFillPatternStyle.DarkGrid,
            FillPatternColor = CellColor.Black
        });
        sheet.SetCell(address, new Cell
        {
            Value = new TextValue("Patterned exception note"),
            StyleId = patternedStyle
        });

        var issue = AccessibilityCheckerService.FindIssues(workbook)
            .Should().ContainSingle(i => i.Kind == AccessibilityIssueKind.LowContrastCellText).Subject;

        issue.Location.Should().Be("B2");
        issue.Message.Should().Be("Cell text should have at least 4.5:1 contrast against its fill.");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_WhenGrayPatternBlendIsLowContrast()
    {
        var workbook = new Workbook("Accessibility");
        var sheet = workbook.AddSheet("Sales");
        var address = new CellAddress(sheet.Id, 3, 2);
        var patternedStyle = workbook.RegisterStyle(new CellStyle
        {
            FontColor = CellColor.White,
            FillColor = CellColor.Black,
            FillPatternStyle = CellFillPatternStyle.DarkGray,
            FillPatternColor = CellColor.White
        });
        sheet.SetCell(address, new Cell
        {
            Value = new TextValue("Patterned risk note"),
            StyleId = patternedStyle
        });

        var issue = AccessibilityCheckerService.FindIssues(workbook)
            .Should().ContainSingle(i => i.Kind == AccessibilityIssueKind.LowContrastCellText).Subject;

        issue.Location.Should().Be("B3");
    }

    [Fact]
    public void FindIssues_IgnoresPatternedCellTextWithSufficientBaseAndPatternContrast()
    {
        var workbook = new Workbook("Accessibility");
        var sheet = workbook.AddSheet("Sales");
        var patternedStyle = workbook.RegisterStyle(new CellStyle
        {
            FontColor = CellColor.Black,
            FillColor = CellColor.White,
            FillPatternStyle = CellFillPatternStyle.DarkGrid,
            FillPatternColor = new CellColor(230, 230, 230)
        });
        sheet.SetCell(new CellAddress(sheet.Id, 4, 2), new Cell
        {
            Value = new TextValue("Readable patterned note"),
            StyleId = patternedStyle
        });

        AccessibilityCheckerService.FindIssues(workbook)
            .Should().NotContain(i => i.Kind == AccessibilityIssueKind.LowContrastCellText);
    }

    [Fact]
    public void FindIssues_IgnoresConditionalFormatContrastWhenRuleDoesNotMatchCell()
    {
        var workbook = new Workbook("Accessibility");
        var sheet = workbook.AddSheet("Sales");
        var address = new CellAddress(sheet.Id, 2, 2);
        sheet.SetCell(address, new TextValue("On track"));
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(address, address),
            RuleType = CfRuleType.ContainsText,
            TextRuleText = "risk",
            FormatIfTrue = new CellStyle
            {
                FontColor = new CellColor(120, 120, 120),
                FillColor = new CellColor(130, 130, 130)
            }
        });

        AccessibilityCheckerService.FindIssues(workbook)
            .Should().NotContain(i => i.Kind == AccessibilityIssueKind.LowContrastCellText);
    }

    [Fact]
    public void FindIssues_LowContrastCellText_SharedConditionalRulesPreserveStopIfTrue()
    {
        var workbook = new Workbook("Accessibility");
        var stopSheet = workbook.AddSheet("Stop");
        var stopAddress = new CellAddress(stopSheet.Id, 1, 1);
        stopSheet.SetCell(stopAddress, new TextValue("Escalated"));
        AddNoBlankContrastRule(stopSheet, stopAddress, priority: 1, stopIfTrue: true, CellColor.Black, CellColor.White);
        AddNoBlankContrastRule(
            stopSheet,
            stopAddress,
            priority: 2,
            stopIfTrue: false,
            new CellColor(120, 120, 120),
            new CellColor(130, 130, 130));

        var stackSheet = workbook.AddSheet("Stack");
        var stackAddress = new CellAddress(stackSheet.Id, 1, 1);
        stackSheet.SetCell(stackAddress, new TextValue("Escalated"));
        AddNoBlankContrastRule(stackSheet, stackAddress, priority: 1, stopIfTrue: false, CellColor.Black, CellColor.White);
        AddNoBlankContrastRule(
            stackSheet,
            stackAddress,
            priority: 2,
            stopIfTrue: false,
            new CellColor(120, 120, 120),
            new CellColor(130, 130, 130));

        var lowContrastIssues = AccessibilityCheckerService.FindIssues(workbook)
            .Where(issue => issue.Kind == AccessibilityIssueKind.LowContrastCellText)
            .ToList();

        lowContrastIssues.Should().ContainSingle()
            .Which.SheetName.Should().Be("Stack");
    }

    [Theory]
    [InlineData(18, false)]
    [InlineData(14, true)]
    public void FindIssues_UsesLowerContrastThresholdForLargeCellText(double fontSize, bool bold)
    {
        var workbook = new Workbook("Accessibility");
        var sheet = workbook.AddSheet("Sales");
        var largeTextStyle = workbook.RegisterStyle(new CellStyle
        {
            FontColor = new CellColor(120, 120, 120),
            FillColor = CellColor.White,
            FontSize = fontSize,
            Bold = bold
        });
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new Cell
        {
            Value = new TextValue("Large readable heading"),
            StyleId = largeTextStyle
        });

        AccessibilityCheckerService.FindIssues(workbook)
            .Should().NotContain(i => i.Kind == AccessibilityIssueKind.LowContrastCellText);
    }

    [Fact]
    public void FindIssues_IgnoresBlankCellsAndSufficientContrast()
    {
        var workbook = new Workbook("Accessibility");
        var sheet = workbook.AddSheet("Sales");
        var lowContrastStyle = workbook.RegisterStyle(new CellStyle
        {
            FontColor = new CellColor(245, 245, 245)
        });
        var sufficientContrastStyle = workbook.RegisterStyle(new CellStyle
        {
            FontColor = CellColor.Black,
            FillColor = CellColor.White
        });
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new Cell
        {
            Value = BlankValue.Instance,
            StyleId = lowContrastStyle
        });
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new Cell
        {
            Value = new TextValue("Readable text"),
            StyleId = sufficientContrastStyle
        });

        AccessibilityCheckerService.FindIssues(workbook)
            .Should().NotContain(i => i.Kind == AccessibilityIssueKind.LowContrastCellText);
    }

    [Fact]
    public void FindIssues_StreamsOccupiedCellsWithoutCopyingUsedCellDictionary()
    {
        var servicePath = FindWorkspaceFile("src", "FreeX.Core.Commands", "AccessibilityCheckerService.cs");
        var serviceDirectory = Path.GetDirectoryName(servicePath)!;
        var source = string.Join(
            Environment.NewLine,
            Directory.EnumerateFiles(serviceDirectory, "AccessibilityCheckerService*.cs")
                .OrderBy(path => path)
                .Select(File.ReadAllText));

        source.Should().NotContain("GetUsedCells()");
        source.Should().Contain("GetOccupiedCellMap()");
        source.Should().Contain("GetConditionalContrastRules(workbook, sheet, occupiedCells)");
        source.Should().Contain("ConditionalFormatEvaluationCache");
        source.Should().Contain("MatchesTopBottomRule");
        source.Should().Contain("MatchesFormulaRule");
        source.Should().Contain("TryCreateFormulaComparison");
        source.Should().Contain("SharedAppliesToRange");
    }

    [Fact]
    public void Benchmark_LowContrastTextWithConditionalFormats_ReportsTimingAndAllocatedBytes()
    {
        const int rows = 20_000;
        const int ruleCount = 8;
        const int iterations = 3;
        var workbook = new Workbook("Accessibility");
        var sheet = workbook.AddSheet("Orders");
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, rows, 1));

        for (uint row = 1; row <= rows; row++)
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new TextValue($"Order {row}"));

        for (var i = 0; i < ruleCount; i++)
        {
            sheet.ConditionalFormats.Add(new ConditionalFormat
            {
                AppliesTo = range,
                Priority = ruleCount - i,
                RuleType = CfRuleType.NoBlanks,
                FormatIfTrue = new CellStyle
                {
                    FontColor = CellColor.Black,
                    FillColor = CellColor.White
                }
            });
        }

        AccessibilityCheckerService.FindIssues(workbook);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var timings = new List<double>(iterations);
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var total = Stopwatch.StartNew();
        IReadOnlyList<AccessibilityIssue> issues = [];
        for (var i = 0; i < iterations; i++)
        {
            var step = Stopwatch.StartNew();
            issues = AccessibilityCheckerService.FindIssues(workbook);
            step.Stop();
            timings.Add(step.Elapsed.TotalMilliseconds);
        }

        total.Stop();
        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        var ordered = timings.OrderBy(value => value).ToArray();
        var p95 = ordered[Math.Clamp((int)Math.Ceiling(ordered.Length * 0.95) - 1, 0, ordered.Length - 1)];

        Console.WriteLine(
            "PERF ACCESSIBILITY_LOW_CONTRAST_CF_TEXT " +
            $"rows={rows} rules={ruleCount} steps={iterations} " +
            $"total_ms={total.Elapsed.TotalMilliseconds:F2} mean_ms={timings.Average():F2} " +
            $"p95_ms={p95:F2} max_ms={ordered[^1]:F2} allocated_bytes={allocatedBytes:N0}");

        issues.Should().NotContain(issue => issue.Kind == AccessibilityIssueKind.LowContrastCellText);
    }

    private static string FindWorkspaceFile(params string[] parts)
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            var candidate = Path.Combine([dir, .. parts]);
            if (File.Exists(candidate))
                return candidate;

            dir = Directory.GetParent(dir)?.FullName;
        }

        throw new FileNotFoundException($"Could not find workspace file: {Path.Combine(parts)}");
    }

    private static void AddNoBlankContrastRule(
        Sheet sheet,
        CellAddress address,
        int priority,
        bool stopIfTrue,
        CellColor fontColor,
        CellColor fillColor)
    {
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(address, address),
            Priority = priority,
            RuleType = CfRuleType.NoBlanks,
            StopIfTrue = stopIfTrue,
            FormatIfTrue = new CellStyle
            {
                FontColor = fontColor,
                FillColor = fillColor
            }
        });
    }
}
