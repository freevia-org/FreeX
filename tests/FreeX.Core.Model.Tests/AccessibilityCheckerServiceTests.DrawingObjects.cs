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
    [InlineData("Picture1")]
    [InlineData("Drawing")]
    [InlineData("Drawing 1")]
    [InlineData("AutoShape")]
    [InlineData("AutoShape 3")]
    [InlineData("Arrow")]
    [InlineData("Arrow 2")]
    [InlineData("Block Arrow")]
    [InlineData("Block Arrow 3")]
    [InlineData("Callout")]
    [InlineData("Callout 4")]
    [InlineData("Connector")]
    [InlineData("Connector 5")]
    [InlineData("Straight Connector")]
    [InlineData("Straight Connector 6")]
    [InlineData("Curved Connector")]
    [InlineData("Curved Connector 7")]
    [InlineData("Freeform")]
    [InlineData("Freeform 2")]
    [InlineData("Flowchart")]
    [InlineData("Flowchart 8")]
    [InlineData("Group")]
    [InlineData("Group 1")]
    [InlineData("Image")]
    [InlineData("Image 2.")]
    [InlineData("Image_2")]
    [InlineData("image (1).png")]
    [InlineData("IMG_1234")]
    [InlineData("IMG_0001.jpg")]
    [InlineData("IMG_0001 (copy).jpg")]
    [InlineData("DSC_0001")]
    [InlineData("DSC-0001")]
    [InlineData("dsc 0001")]
    [InlineData("DSCF1234")]
    [InlineData("PXL_20260605_123456789")]
    [InlineData("PXL-20260605-123456789")]
    [InlineData("Object")]
    [InlineData("Object 1")]
    [InlineData("Graphic")]
    [InlineData("Graphic 2")]
    [InlineData("Diagram")]
    [InlineData("Diagram 3")]
    [InlineData("Screenshot")]
    [InlineData("Screenshot 4")]
    [InlineData("Screenshot-4")]
    [InlineData("Screenshot 2026-06-04")]
    [InlineData("Screenshot_2026-06-04")]
    [InlineData("Screenshot 20260604")]
    [InlineData("Screenshot 2026-06-05 at 3.14.15 PM")]
    [InlineData("Screen Shot 2026-06-05 at 15.14.15")]
    [InlineData("screenshot-20260605-151415")]
    [InlineData("Screenshot 2026-06-05 at 3.14.15 PM.png")]
    [InlineData("Screen Shot 2026-06-05 at 15.14.15.jpg")]
    [InlineData("Screenshot (2).jpeg")]
    [InlineData("Photo")]
    [InlineData("Photo 5")]
    [InlineData("Photo 2026-06-04")]
    [InlineData("Photo-2026-06-04")]
    [InlineData("Photo_2026_06_04")]
    [InlineData("Photo 2026-06-05 at 08.30.00")]
    [InlineData("photo-final (3).webp")]
    [InlineData("Icon")]
    [InlineData("Icon 6")]
    [InlineData("Shape")]
    [InlineData("SmartArt")]
    [InlineData("SmartArt 9")]
    [InlineData("SmartArt_9")]
    [InlineData("Text box")]
    [InlineData("TextBox7")]
    [InlineData("WordArt")]
    [InlineData("WordArt 10")]
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
            AltText = "Picture Q1 revenue trend"
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
    [InlineData("Screenshot_2026-06-04 showing sales dashboard")]
    [InlineData("Screenshot 2026-06-05 showing onboarding dashboard")]
    [InlineData("Screen Shot 2026-06-05 at 15.14.15 showing support queue")]
    [InlineData("screenshot-20260605-151415 showing sign-in flow")]
    [InlineData("Photo of warehouse team")]
    [InlineData("Storefront IMG_1234")]
    [InlineData("Warehouse DSC_0001 reference")]
    [InlineData("PXL_20260605_123456789 showing loading dock")]
    [InlineData("Status icon legend")]
    [InlineData("Drawing of approval workflow")]
    [InlineData("Group status legend")]
    [InlineData("Connector showing approval handoff")]
    [InlineData("Customer journey flowchart")]
    [InlineData("SmartArt summary of support tiers")]
    [InlineData("WordArt label for launch milestone")]
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
    [InlineData("Drawing_1")]
    [InlineData("AutoShape")]
    [InlineData("AutoShape 3")]
    [InlineData("Arrow")]
    [InlineData("Arrow 2")]
    [InlineData("Block Arrow")]
    [InlineData("Block Arrow 3")]
    [InlineData("Callout")]
    [InlineData("Callout 4")]
    [InlineData("Connector")]
    [InlineData("Connector 5")]
    [InlineData("Straight Connector")]
    [InlineData("Straight Connector 6")]
    [InlineData("Curved Connector")]
    [InlineData("Curved Connector 7")]
    [InlineData("Freeform")]
    [InlineData("Freeform 2")]
    [InlineData("Flowchart")]
    [InlineData("Flowchart 8")]
    [InlineData("Group")]
    [InlineData("Group 1")]
    [InlineData("SmartArt")]
    [InlineData("SmartArt 9")]
    [InlineData("SmartArt_9")]
    [InlineData("WordArt")]
    [InlineData("WordArt 10")]
    [InlineData("WordArt-10")]
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
            Title = "Screenshot_2026-06-04"
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
            Name = "Photo-2026-06-04"
        });
        sheet.TextBoxes.Add(new TextBoxModel
        {
            Anchor = new CellAddress(sheet.Id, 6, 1),
            Text = "Profile photo",
            Title = "Photo_2026_06_04",
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
    public void FindIssues_FlagsCameraDefaultDrawingObjectTitleOrNameWithoutDescriptiveAltText()
    {
        var workbook = new Workbook("Accessibility");
        var sheet = workbook.AddSheet("Objects");
        sheet.Pictures.Add(new PictureModel
        {
            Anchor = new CellAddress(sheet.Id, 1, 1),
            Kind = PictureKind.Image,
            Title = "IMG_1234"
        });
        sheet.DrawingShapes.Add(new DrawingShapeModel
        {
            Anchor = new CellAddress(sheet.Id, 2, 1),
            Kind = DrawingShapeKind.Rectangle,
            Name = "DSC_0001"
        });
        sheet.TextBoxes.Add(new TextBoxModel
        {
            Anchor = new CellAddress(sheet.Id, 3, 1),
            Text = "Dock photo",
            Title = "PXL_20260605_123456789",
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
    public void FindIssues_FlagsLowContrastShapeText()
    {
        // R131 (a): DrawingShapeModel.ShapeText was previously never checked for contrast at all --
        // this proves it is now reachable.
        var workbook = new Workbook("Accessibility");
        var sheet = workbook.AddSheet("Objects");
        sheet.DrawingShapes.Add(new DrawingShapeModel
        {
            Anchor = new CellAddress(sheet.Id, 2, 1),
            Kind = DrawingShapeKind.Rectangle,
            AltText = "Status banner",
            ShapeText = "Behind schedule",
            FillColor = new CellColor(20, 20, 20)
            // ShapeTextColor left null -> resolves to the default (black) text color, which is
            // low contrast against the near-black fill above.
        });

        var issue = AccessibilityCheckerService.FindIssues(workbook)
            .Should().ContainSingle(i => i.Kind == AccessibilityIssueKind.LowContrastObjectText).Subject;

        issue.Location.Should().Be("A2");
        issue.Message.Should().Be("Shape text should have at least 4.5:1 contrast against its fill.");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastShapeText_UsingExplicitShapeTextColorOverride()
    {
        // Sibling to the previous test: proves the new shape-text check reads the shape's own text
        // color/gradient-fill fields correctly rather than reproducing the (b)/(c) bugs in new code.
        var workbook = new Workbook("Accessibility");
        var sheet = workbook.AddSheet("Objects");
        sheet.DrawingShapes.Add(new DrawingShapeModel
        {
            Anchor = new CellAddress(sheet.Id, 3, 1),
            Kind = DrawingShapeKind.Rectangle,
            AltText = "Gradient banner",
            ShapeText = "Overdue",
            ShapeTextColor = new CellColor(40, 40, 40),
            FillColor = CellColor.White,
            GradientFillEndColor = new CellColor(35, 35, 35)
        });

        var issue = AccessibilityCheckerService.FindIssues(workbook)
            .Should().ContainSingle(i => i.Kind == AccessibilityIssueKind.LowContrastObjectText).Subject;

        issue.Location.Should().Be("A3");
    }

    [Fact]
    public void FindIssues_IgnoresShapeTextWithSufficientContrastOrWhitespaceOnlyTextOrNoFill()
    {
        // DO-NOT-WIDEN-PAST-THE-GUARD: adding the shape-text check must not flood the report with
        // false positives for shapes with sufficient contrast, whitespace-only text (which Excel
        // renders as blank -- see the low-contrast fill on the whitespace shape below, which proves
        // the guard itself, not incidental high contrast, is what suppresses the issue), or shapes
        // that are genuinely transparent (HasFill == false) even if they carry a stale/leftover
        // FillColor from before the fill was turned off.
        var workbook = new Workbook("Accessibility");
        var sheet = workbook.AddSheet("Objects");
        sheet.DrawingShapes.Add(new DrawingShapeModel
        {
            Anchor = new CellAddress(sheet.Id, 2, 1),
            Kind = DrawingShapeKind.Rectangle,
            AltText = "Readable banner",
            ShapeText = "On track",
            FillColor = CellColor.White
        });
        sheet.DrawingShapes.Add(new DrawingShapeModel
        {
            Anchor = new CellAddress(sheet.Id, 3, 1),
            Kind = DrawingShapeKind.Rectangle,
            AltText = "Blank shape",
            ShapeText = "   ",
            // Deliberately low-contrast (near-black fill, default black text, same pairing as
            // FindIssues_FlagsLowContrastShapeText above) so the ONLY thing suppressing an issue
            // here is the whitespace-only-text guard -- a test that used a high-contrast pairing
            // instead would still pass with the guard removed and would prove nothing.
            FillColor = new CellColor(20, 20, 20)
        });
        sheet.DrawingShapes.Add(new DrawingShapeModel
        {
            Anchor = new CellAddress(sheet.Id, 4, 1),
            Kind = DrawingShapeKind.Rectangle,
            AltText = "Transparent shape with stale fill color",
            ShapeText = "No fill in Excel",
            HasFill = false,
            FillColor = new CellColor(10, 10, 10)
        });

        AccessibilityCheckerService.FindIssues(workbook)
            .Should().NotContain(i => i.Kind == AccessibilityIssueKind.LowContrastObjectText);
    }

    [Fact]
    public void FindIssues_FlagsLowContrastTextBoxText_UsingExplicitTextColorOverride()
    {
        // R131 (b): the check previously always used the workbook-wide default text color and
        // ignored the text box's own TextColor/TextThemeColor override.
        var workbook = new Workbook("Accessibility");
        var sheet = workbook.AddSheet("Objects");
        sheet.TextBoxes.Add(new TextBoxModel
        {
            Anchor = new CellAddress(sheet.Id, 2, 1),
            Text = "White-on-white callout",
            AltText = "White-on-white callout annotation",
            FillColor = CellColor.White,
            TextColor = new CellColor(250, 250, 250)
        });

        var issue = AccessibilityCheckerService.FindIssues(workbook)
            .Should().ContainSingle(i => i.Kind == AccessibilityIssueKind.LowContrastObjectText).Subject;

        issue.Location.Should().Be("A2");
    }

    [Fact]
    public void FindIssues_IgnoresTextBoxTextColorOverrideWithSufficientContrast()
    {
        // Sibling no-regression: a text box whose own override is correctly readable against its
        // fill must not be flagged, even though the (buggy) default black text would have been
        // low-contrast against this same dark fill.
        var workbook = new Workbook("Accessibility");
        var sheet = workbook.AddSheet("Objects");
        sheet.TextBoxes.Add(new TextBoxModel
        {
            Anchor = new CellAddress(sheet.Id, 2, 1),
            Text = "Readable override callout",
            AltText = "Readable override callout annotation",
            FillColor = new CellColor(20, 20, 20),
            TextColor = CellColor.White
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
