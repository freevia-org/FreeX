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
