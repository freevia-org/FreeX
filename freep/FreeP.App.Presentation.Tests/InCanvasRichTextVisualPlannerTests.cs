using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Compositor.Tests;

public sealed class InCanvasRichTextVisualPlannerTests
{
    [Fact]
    public void EditorDefaults_UseWpfFallbackAndFirstExplicitSize()
    {
        var body = new TextBody();
        body.Paragraphs.Add(new Paragraph
        {
            Runs =
            {
                new Run { Text = "Inherited" },
                new Run { Text = "Explicit", FontSizePt = 18 },
            },
        });

        InCanvasRichTextEditorDefaults.FallbackFontFamily.Should().Be("Calibri");
        InCanvasRichTextEditorDefaults.ResolveFallbackFontSize(
                body,
                InCanvasRichTextEditorDefaults.ShapeFallbackFontSizePt)
            .Should().Be(18);
        InCanvasRichTextEditorDefaults.ResolveFallbackFontSize(
                new TextBody(),
                InCanvasRichTextEditorDefaults.TableCellFallbackFontSizePt)
            .Should().Be(13);
    }

    [Fact]
    public void Create_PreservesBodyWrapPolicy()
    {
        InCanvasRichTextVisualPlanner.Create(new TextBody { Wrap = true })
            .Wrap.Should().BeTrue();
        InCanvasRichTextVisualPlanner.Create(new TextBody { Wrap = false })
            .Wrap.Should().BeFalse();
        InCanvasRichTextVisualPlanner.Create(null)
            .Wrap.Should().BeTrue();
    }

    [Fact]
    public void Create_MapsMixedRunsAndParagraphOffsetsWithoutFlattening()
    {
        var body = new TextBody();
        body.Paragraphs.Add(new Paragraph
        {
            Align = TextAlign.Center,
            Runs =
            {
                new Run { Text = "Small ", FontFamily = "Arial", FontSizePt = 10 },
                new Run { Text = "Large", FontFamily = "Georgia", FontSizePt = 24, Bold = true },
            },
        });
        body.Paragraphs.Add(new Paragraph
        {
            Align = TextAlign.Right,
            Runs = { new Run { Text = "Tail", Italic = true } },
        });

        var plan = InCanvasRichTextVisualPlanner.Create(body);

        plan.PlainText.Should().Be("Small Large\nTail");
        plan.Paragraphs.Should().HaveCount(2);
        plan.Paragraphs[0].Alignment.Should().Be(TextAlign.Center);
        plan.Paragraphs[0].Runs.Select(run => (run.Start, run.Length))
            .Should().Equal((0, 6), (6, 5));
        plan.Paragraphs[0].Runs[1].FontFamily.Should().Be("Georgia");
        plan.Paragraphs[0].Runs[1].FontSizePt.Should().Be(24);
        plan.Paragraphs[1].GlobalStart.Should().Be(12);
        plan.Paragraphs[1].Alignment.Should().Be(TextAlign.Right);
    }

    [Fact]
    public void Create_BulletMetadataDoesNotAlterWpfAuthorityTextOrLogicalOffsets()
    {
        var body = new TextBody();
        body.Paragraphs.Add(new Paragraph
        {
            BulletKind = BulletKind.Char,
            BulletChar = "\u25aa",
            Runs = { new Run { Text = "Bullet" } },
        });
        body.Paragraphs.Add(new Paragraph
        {
            BulletKind = BulletKind.Auto,
            AutoNumType = AutoNumType.RomanUcPeriod,
            AutoNumStartAt = 3,
            Runs = { new Run { Text = "Three" } },
        });
        body.Paragraphs.Add(new Paragraph
        {
            BulletKind = BulletKind.Auto,
            AutoNumType = AutoNumType.RomanUcPeriod,
            Runs = { new Run { Text = "Four" } },
        });

        var plan = InCanvasRichTextVisualPlanner.Create(body);

        plan.Paragraphs.Select(paragraph => paragraph.Text)
            .Should().Equal("Bullet", "Three", "Four");
        plan.PlainText.Should().Be("Bullet\nThree\nFour");
        plan.Paragraphs[1].GlobalStart.Should().Be("Bullet\n".Length);
    }

    [Fact]
    public void Create_ProjectsBulletMarkersSeparatelyFromEditableText()
    {
        var body = new TextBody();
        body.Paragraphs.Add(new Paragraph
        {
            BulletKind = BulletKind.Char,
            BulletChar = "\u25AA",
            Runs = { new Run { Text = "Alpha", FontFamily = "Arial", FontSizePt = 12 } },
        });
        body.Paragraphs.Add(new Paragraph
        {
            BulletKind = BulletKind.Auto,
            AutoNumType = AutoNumType.RomanUcPeriod,
            AutoNumStartAt = 3,
            Runs = { new Run { Text = "Beta", Color = new ThemeAwareColor(new SrgbColor(0x11, 0x22, 0x33)) } },
        });
        body.Paragraphs.Add(new Paragraph
        {
            BulletKind = BulletKind.Auto,
            AutoNumType = AutoNumType.RomanUcPeriod,
            Runs = { new Run { Text = "Gamma" } },
        });

        var plan = InCanvasRichTextVisualPlanner.Create(body);

        plan.PlainText.Should().Be("Alpha\nBeta\nGamma");
        plan.Paragraphs.Select(paragraph => paragraph.BulletText)
            .Should().Equal("\u25AA", "III.", "IV.");
        plan.Paragraphs[0].BulletFontFamily.Should().Be("Arial");
        plan.Paragraphs[0].BulletFontSizePt.Should().Be(12);
        plan.Paragraphs[1].BulletColor!.Resolved.Should().Be(new SrgbColor(0x11, 0x22, 0x33));
        plan.Paragraphs.Select(paragraph => (paragraph.GlobalStart, paragraph.GlobalEnd))
            .Should().Equal((0, 5), (6, 10), (11, 16));
    }

    [Fact]
    public void Create_UsesSharedMarkerContinuationAcrossRestartsLevelsAndNonLists()
    {
        var body = new TextBody();
        body.Paragraphs.Add(Numbered("First", AutoNumType.ArabicPeriod, 4, startSpecified: true));
        body.Paragraphs.Add(Numbered("Nested", AutoNumType.ArabicPeriod, 1, level: 1));
        body.Paragraphs.Add(Numbered("Sibling", AutoNumType.ArabicPeriod, 1));
        body.Paragraphs.Add(new Paragraph { Runs = { new Run { Text = "Plain" } } });
        body.Paragraphs.Add(Numbered("Restart", AutoNumType.ArabicPeriod, 7, startSpecified: true));
        body.Paragraphs.Add(Numbered("After", AutoNumType.ArabicPeriod, 1));

        var plan = InCanvasRichTextVisualPlanner.Create(body);

        plan.Paragraphs.Select(paragraph => paragraph.BulletText)
            .Should().Equal("4.", "1.", "5.", "", "7.", "8.");
    }

    [Fact]
    public void Create_ExpandsMultiLevelExternalLevelTextTemplates()
    {
        var body = new TextBody();
        body.Paragraphs.Add(new Paragraph
        {
            BulletKind = BulletKind.Auto,
            AutoNumType = AutoNumType.ArabicPeriod,
            AutoNumStartAtSpecified = true,
            AutoNumTextTemplate = "%1.",
            Runs = { new Run { Text = "Root" } },
        });
        body.Paragraphs.Add(new Paragraph
        {
            Level = 1,
            BulletKind = BulletKind.Auto,
            AutoNumType = AutoNumType.ArabicPeriod,
            AutoNumTextTemplate = "%1.%2.",
            Runs = { new Run { Text = "Child" } },
        });

        var plan = InCanvasRichTextVisualPlanner.Create(body);

        plan.Paragraphs.Select(paragraph => paragraph.BulletText)
            .Should().Equal("1.", "1.1.");
    }

    [Fact]
    public void Create_ResolvesInheritedParagraphLayoutAndLocalOverrides()
    {
        var body = new TextBody
        {
            DefaultParaAlign = TextAlign.Left,
            LstStyle = new TextStyleLevels
            {
                [0] = new TextStyleLevel
                {
                    Align = TextAlign.Right,
                    MarginLeftEmu = 914400,
                    IndentEmu = -228600,
                },
            },
        };
        body.Paragraphs.Add(new Paragraph
        {
            Runs = { new Run { Text = "Inherited" } },
        });
        body.Paragraphs.Add(new Paragraph
        {
            Align = TextAlign.Center,
            MarginLeftEmu = 0,
            IndentEmu = 0,
            Runs = { new Run { Text = "Local" } },
        });

        var paragraphs = InCanvasRichTextVisualPlanner.Create(body).Paragraphs;

        paragraphs[0].Alignment.Should().Be(TextAlign.Right);
        paragraphs[0].MarginLeftDip.Should().BeApproximately(96, 0.01);
        paragraphs[0].TextIndentDip.Should().BeApproximately(-24, 0.01);
        paragraphs[0].IndentDip.Should().BeApproximately(96, 0.01);
        paragraphs[0].HangingDip.Should().BeApproximately(24, 0.01);
        paragraphs[1].Alignment.Should().Be(TextAlign.Center);
        paragraphs[1].MarginLeftDip.Should().Be(0);
        paragraphs[1].TextIndentDip.Should().Be(0);
    }

    /// <summary>
    /// Create must merge the shape's own lstStyle with the layout placeholder's lstStyle and the
    /// master's txStyles per property (SlideCompositor.ResolveTextStyleInheritance), not just
    /// consult the shape's own lstStyle. Before the fix, Create had no layoutBody/masterTextStyles
    /// parameters at all, so a paragraph whose shape lstStyle left size/color unset always
    /// resolved InheritedRunStyle to Empty even when the layout or master defined them --
    /// the WPF/Avalonia in-canvas editors would preview default text where the static render
    /// (and the committed text, once editing ends) shows the inherited font size and color.
    /// </summary>
    [Fact]
    public void Create_WithLayoutAndMasterContext_MergesInheritedRunStylePerProperty()
    {
        var body = new TextBody(); // No shape-level lstStyle at all.
        body.Paragraphs.Add(new Paragraph
        {
            Runs = { new Run { Text = "Inherits from layout and master" } },
        });

        var layoutBody = new TextBody
        {
            LstStyle = new TextStyleLevels
            {
                // Layout overrides only the font size; color is left to the master.
                [0] = new TextStyleLevel { FontSizePt = 32.0 },
            },
        };
        var masterColor = new ThemeAwareColor(new SrgbColor(0x11, 0x22, 0x33));
        var masterTextStyles = new MasterTextStyles();
        masterTextStyles.BodyStyle[0] = new TextStyleLevel { FontSizePt = 24.0, Color = masterColor };

        var plan = InCanvasRichTextVisualPlanner.Create(
            body, layoutBody, masterTextStyles, SlideCompositor.TextStyleCategory.Body);

        var inherited = plan.Paragraphs[0].InheritedRunStyle;
        inherited.IsPresent.Should().BeTrue();
        inherited.FontSizePt.Should().Be(32.0, "the layout's own lstStyle must beat the master's txStyles");
        inherited.Color.Should().Be(masterColor,
            "the layout did not set a color, so it must fall through to the master per property");
    }

    /// <summary>
    /// Sibling no-regression guard: calling Create without a layout/master context (the previous
    /// call shape) must resolve purely from the shape's own lstStyle, unchanged.
    /// </summary>
    [Fact]
    public void Create_WithoutLayoutOrMasterContext_ResolvesFromShapeLstStyleOnly()
    {
        var body = new TextBody
        {
            LstStyle = new TextStyleLevels
            {
                [0] = new TextStyleLevel { FontSizePt = 40.0 },
            },
        };
        body.Paragraphs.Add(new Paragraph
        {
            Runs = { new Run { Text = "Shape only" } },
        });

        var plan = InCanvasRichTextVisualPlanner.Create(body);

        plan.Paragraphs[0].InheritedRunStyle.FontSizePt.Should().Be(40.0);
    }

    [Fact]
    public void Create_ResolvesCharacterNumberAndImageMarkersIntoOnePlan()
    {
        var image = new ImagePart
        {
            Bytes = [1, 2, 3],
            ContentType = "image/png",
        };
        var body = new TextBody
        {
            LstStyle = new TextStyleLevels
            {
                [0] = new TextStyleLevel
                {
                    BulletKind = BulletKind.Char,
                    BulletChar = "\u00A7",
                },
                [1] = new TextStyleLevel
                {
                    BulletKind = BulletKind.Auto,
                    AutoNumType = AutoNumType.RomanUcPeriod,
                },
            },
        };
        body.Paragraphs.Add(new Paragraph
        {
            Runs = { new Run { Text = "Character" } },
        });
        body.Paragraphs.Add(new Paragraph
        {
            Level = 1,
            Runs = { new Run { Text = "One" } },
        });
        body.Paragraphs.Add(new Paragraph
        {
            Level = 1,
            Runs = { new Run { Text = "Two" } },
        });
        body.Paragraphs.Add(new Paragraph
        {
            BulletKind = BulletKind.Image,
            BulletImage = image,
            Runs = { new Run { Text = "Picture" } },
        });

        var paragraphs = InCanvasRichTextVisualPlanner.Create(body).Paragraphs;

        paragraphs.Select(paragraph => paragraph.BulletKind)
            .Should().Equal(BulletKind.Char, BulletKind.Auto, BulletKind.Auto, BulletKind.Image);
        paragraphs.Select(paragraph => paragraph.BulletText)
            .Should().Equal("\u00A7", "I.", "II.", string.Empty);
        paragraphs[3].BulletImage.Should().BeSameAs(image);
    }

    [Fact]
    public void Create_ResolvesInheritedMarkerTypographyAndFollowsTextOverrides()
    {
        var inheritedColor = new ThemeAwareColor(new SrgbColor(0x11, 0x22, 0x33));
        var textColor = new ThemeAwareColor(new SrgbColor(0x44, 0x55, 0x66));
        var body = new TextBody
        {
            LstStyle = new TextStyleLevels
            {
                [0] = new TextStyleLevel
                {
                    BulletKind = BulletKind.Char,
                    BulletFontFamily = "Wingdings",
                    BulletSizePct = 150000,
                    BulletColor = inheritedColor,
                },
            },
        };
        body.Paragraphs.Add(new Paragraph
        {
            Runs =
            {
                new Run
                {
                    Text = "Inherited typography",
                    FontFamily = "Arial",
                    FontSizePt = 20,
                    Color = textColor,
                },
            },
        });
        body.Paragraphs.Add(new Paragraph
        {
            BulletKind = BulletKind.Char,
            BulletFontFollowsText = true,
            BulletSizeFollowsText = true,
            BulletColorFollowsText = true,
            Runs =
            {
                new Run
                {
                    Text = "Follow text",
                    FontFamily = "Georgia",
                    FontSizePt = 15,
                    Color = textColor,
                },
            },
        });

        var paragraphs = InCanvasRichTextVisualPlanner.Create(body).Paragraphs;

        paragraphs[0].BulletFontFamily.Should().Be("Wingdings");
        paragraphs[0].BulletFontSizePt.Should().Be(30);
        paragraphs[0].BulletColor.Should().BeSameAs(inheritedColor);
        paragraphs[1].BulletFontFamily.Should().Be("Georgia");
        paragraphs[1].BulletFontSizePt.Should().Be(15);
        paragraphs[1].BulletColor.Should().BeSameAs(textColor);
    }

    [Fact]
    public void Create_HonorsWpfAuthorityParagraphSpacingWithoutIntroducingIndent()
    {
        var body = new TextBody();
        body.Paragraphs.Add(new Paragraph
        {
            BulletKind = BulletKind.Char,
            MarginLeftEmu = 381000,
            IndentEmu = -190500,
            SpaceBeforePt = 3,
            SpaceAfterPt = 6,
            Runs = { new Run { Text = "Indented" } },
        });

        var paragraph = InCanvasRichTextVisualPlanner.Create(body).Paragraphs.Single();

        paragraph.SpaceBeforeDip.Should().BeApproximately(4, 0.01);
        paragraph.SpaceAfterDip.Should().BeApproximately(8, 0.01);
    }

    [Fact]
    public void Create_ProjectsParagraphTabStopsIntoResolvedVisualPlan()
    {
        var body = new TextBody();
        body.Paragraphs.Add(new Paragraph
        {
            Runs = { new Run { Text = "Label\tValue" } },
            TabStops =
            {
                new TabStop { PositionEmu = 914400, Alignment = TabStopAlignment.Right },
                new TabStop
                {
                    PositionEmu = 1828800,
                    Alignment = TabStopAlignment.Decimal,
                    Leader = TabStopLeader.Dots,
                },
            },
        });

        var paragraph = InCanvasRichTextVisualPlanner.Create(body).Paragraphs.Single();

        paragraph.TabStops.Should().NotBeNull();
        paragraph.TabStops!.Select(stop => (stop.PositionDip, stop.Alignment, stop.Leader))
            .Should().Equal(
                (96d, TabStopAlignment.Right, TabStopLeader.None),
                (192d, TabStopAlignment.Decimal, TabStopLeader.Dots));
    }

    private static Paragraph Numbered(
        string text,
        AutoNumType type,
        int startAt,
        int level = 0,
        bool startSpecified = false)
    {
        return new Paragraph
        {
            Level = level,
            BulletKind = BulletKind.Auto,
            AutoNumType = type,
            AutoNumStartAt = startAt,
            AutoNumStartAtSpecified = startSpecified,
            Runs = { new Run { Text = text } },
        };
    }
}
