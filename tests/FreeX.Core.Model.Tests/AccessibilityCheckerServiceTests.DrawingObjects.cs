using FreeX.Core.Commands;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Model.Tests;

public sealed partial class AccessibilityCheckerServiceTests
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

    [Theory]
    [InlineData("Picture 1")]
    [InlineData("Drawing")]
    [InlineData("Drawing 1")]
    [InlineData("AutoShape")]
    [InlineData("AutoShape 3")]
    [InlineData("Freeform")]
    [InlineData("Freeform 2")]
    [InlineData("Group")]
    [InlineData("Group 1")]
    [InlineData("Image")]
    [InlineData("Image 2.")]
    [InlineData("IMG_0001.jpg")]
    [InlineData("Object")]
    [InlineData("Object 1")]
    [InlineData("Graphic")]
    [InlineData("Graphic 2")]
    [InlineData("Diagram")]
    [InlineData("Diagram 3")]
    [InlineData("Screenshot")]
    [InlineData("Screenshot 4")]
    [InlineData("Screenshot 2026-06-04")]
    [InlineData("Photo")]
    [InlineData("Photo 5")]
    [InlineData("Photo 2026-06-04")]
    [InlineData("Icon")]
    [InlineData("Icon 6")]
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

    [Theory]
    [InlineData("Operations object model")]
    [InlineData("Customer onboarding graphic")]
    [InlineData("Fulfillment diagram showing handoffs")]
    [InlineData("Screenshot 2026-06-04 showing sales dashboard")]
    [InlineData("Photo of warehouse team")]
    [InlineData("Status icon legend")]
    [InlineData("Drawing of approval workflow")]
    [InlineData("Group status legend")]
    public void FindIssues_AllowsDescriptiveGenericDrawingObjectTitleOrName(string accessibleText)
    {
        var workbook = new Workbook("Accessibility");
        var sheet = workbook.AddSheet("Objects");
        sheet.Pictures.Add(new PictureModel
        {
            Anchor = new CellAddress(sheet.Id, 1, 1),
            Kind = PictureKind.Image,
            AltText = "Image",
            Title = accessibleText
        });
        sheet.DrawingShapes.Add(new DrawingShapeModel
        {
            Anchor = new CellAddress(sheet.Id, 2, 1),
            Kind = DrawingShapeKind.Rectangle,
            Name = accessibleText
        });
        sheet.TextBoxes.Add(new TextBoxModel
        {
            Anchor = new CellAddress(sheet.Id, 3, 1),
            Text = "Status summary",
            Name = accessibleText,
            FillColor = CellColor.White
        });

        AccessibilityCheckerService.FindIssues(workbook)
            .Where(i => i.Kind is AccessibilityIssueKind.MissingAltText or AccessibilityIssueKind.GenericAltText)
            .Should()
            .BeEmpty();
    }

    [Theory]
    [InlineData("Drawing")]
    [InlineData("Drawing 1")]
    [InlineData("AutoShape")]
    [InlineData("AutoShape 3")]
    [InlineData("Freeform")]
    [InlineData("Freeform 2")]
    [InlineData("Group")]
    [InlineData("Group 1")]
    public void FindIssues_FlagsAdditionalDefaultDrawingObjectTitleOrNameWithoutDescriptiveAltText(string accessibleText)
    {
        var workbook = new Workbook("Accessibility");
        var sheet = workbook.AddSheet("Objects");
        sheet.Pictures.Add(new PictureModel
        {
            Anchor = new CellAddress(sheet.Id, 1, 1),
            Kind = PictureKind.Image,
            Title = accessibleText
        });
        sheet.DrawingShapes.Add(new DrawingShapeModel
        {
            Anchor = new CellAddress(sheet.Id, 2, 1),
            Kind = DrawingShapeKind.Rectangle,
            Name = accessibleText
        });
        sheet.TextBoxes.Add(new TextBoxModel
        {
            Anchor = new CellAddress(sheet.Id, 3, 1),
            Text = "Status summary",
            Name = accessibleText,
            FillColor = CellColor.White
        });

        var issues = AccessibilityCheckerService.FindIssues(workbook)
            .Where(i => i.Kind == AccessibilityIssueKind.GenericAltText)
            .ToList();

        issues.Select(i => i.Location).Should().Equal("A1", "A2", "A3");
        issues.Select(i => i.Message).Should().Equal(
            "Picture alternate text should describe the object.",
            "Shape alternate text should describe the object.",
            "Text box alternate text should describe the object.");
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
    public void FindIssues_FlagsCommonGenericDrawingObjectTitleOrNameWithoutDescriptiveAltText()
    {
        var workbook = new Workbook("Accessibility");
        var sheet = workbook.AddSheet("Objects");
        sheet.Pictures.Add(new PictureModel
        {
            Anchor = new CellAddress(sheet.Id, 1, 1),
            Kind = PictureKind.Image,
            Title = "Object 1"
        });
        sheet.Pictures.Add(new PictureModel
        {
            Anchor = new CellAddress(sheet.Id, 2, 1),
            Kind = PictureKind.Image,
            Title = "Screenshot 2026-06-04"
        });
        sheet.DrawingShapes.Add(new DrawingShapeModel
        {
            Anchor = new CellAddress(sheet.Id, 3, 1),
            Kind = DrawingShapeKind.Rectangle,
            Name = "Graphic 2"
        });
        sheet.DrawingShapes.Add(new DrawingShapeModel
        {
            Anchor = new CellAddress(sheet.Id, 4, 1),
            Kind = DrawingShapeKind.Rectangle,
            Name = "Diagram 3"
        });
        sheet.DrawingShapes.Add(new DrawingShapeModel
        {
            Anchor = new CellAddress(sheet.Id, 5, 1),
            Kind = DrawingShapeKind.Rectangle,
            Name = "Icon 6"
        });
        sheet.TextBoxes.Add(new TextBoxModel
        {
            Anchor = new CellAddress(sheet.Id, 6, 1),
            Text = "Profile photo",
            Title = "Photo 2026-06-04",
            FillColor = CellColor.White
        });

        var issues = AccessibilityCheckerService.FindIssues(workbook)
            .Where(i => i.Kind == AccessibilityIssueKind.GenericAltText)
            .ToList();

        issues.Select(i => i.Location).Should().Equal("A1", "A2", "A3", "A4", "A5", "A6");
        issues.Select(i => i.Message).Should().Equal(
            "Picture alternate text should describe the object.",
            "Picture alternate text should describe the object.",
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
}
