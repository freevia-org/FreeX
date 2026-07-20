using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Compositor.Tests;

public sealed class InCanvasRichTextVisualPlannerTests
{
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
}
