using FreeP.App.Compositor;
using PresentationModel = FreeP.Core.Model.Presentation;

namespace FreeP.App.Compositor.Tests;

/// <summary>
/// Percent-based paragraph spacing (a:spcBef/a:spcAft with a:spcPct) must survive the
/// SlideCompositor resolution path and resolve to real spacing at layout time, the same way
/// percent line spacing (a:lnSpc/a:spcPct) already does. Reading only Paragraph.SpaceBeforePt /
/// SpaceAfterPt made percent-authored spacing render as zero everywhere the compositor feeds —
/// canvas, slide show, print and PDF export.
/// </summary>
public sealed class ParagraphPercentSpacingRenderTests
{
    private const double DipPerPoint = 96.0 / 72.0;

    /// <summary>One line's height in points for a given font size — the basis for spcPct.</summary>
    private static double LineHeightPt(double fontSizePt) => fontSizePt * 1.2;

    private static Paragraph MakeParagraph(double fontSizePt, Action<Paragraph> configure)
    {
        var para = new Paragraph();
        para.Runs.Add(new Run { Text = "Percent spaced paragraph", FontSizePt = fontSizePt });
        configure(para);
        return para;
    }

    private static SlideShape MakeTextShape(TextBody body) => new()
    {
        Id = 1,
        Kind = SlideShapeKind.AutoShape,
        AutoShapeKind = DrawingShapeKind.Rectangle,
        OffsetXEmu = 457200,
        OffsetYEmu = 274320,
        ExtentCxEmu = 4572000,
        ExtentCyEmu = 1371600,
        TextBody = body
    };

    private static ResolvedParagraph ResolveShapeParagraph(double fontSizePt, Action<Paragraph> configure)
    {
        var body = new TextBody();
        body.Paragraphs.Add(MakeParagraph(fontSizePt, configure));

        var p = PresentationModel.CreateEmpty();
        p.Slides[0].Shapes.Clear();
        p.Slides[0].Shapes.Add(MakeTextShape(body));

        return SlideCompositor.Compose(p, p.Slides[0])
            .OfType<DrawOp.Shape>().Single().Text!.Paragraphs[0];
    }

    // ─── Compositor resolution: shape text body ───────────────────────────────

    [Fact]
    public void Compose_ShapeTextBody_PercentSpacing_ResolvesToProportionalPoints()
    {
        const double fontSizePt = 20;

        var resolved = ResolveShapeParagraph(fontSizePt, para =>
        {
            para.SpaceBeforePercent = 50;
            para.SpaceAfterPercent = 100;
        });

        resolved.SpaceBeforePercent.Should().Be(50,
            "the compositor must carry a:spcBef/a:spcPct through to the renderer");
        resolved.SpaceAfterPercent.Should().Be(100);

        TextLayoutPlanner.ResolveSpaceBeforePoints(resolved).Should().BeApproximately(
            0.50 * LineHeightPt(fontSizePt), 1e-9,
            "spcBef spcPct is a percentage of a single line's height");
        TextLayoutPlanner.ResolveSpaceAfterPoints(resolved).Should().BeApproximately(
            1.00 * LineHeightPt(fontSizePt), 1e-9);
        TextLayoutPlanner.ResolveSpaceBeforePoints(resolved).Should().BeGreaterThan(0,
            "percent-authored spacing must not render as absent");
    }

    [Fact]
    public void Compose_ShapeTextBody_PercentSpacing_ScalesWithFontSize()
    {
        static void SetFullLineSpacing(Paragraph para)
        {
            para.SpaceBeforePercent = 100;
            para.SpaceAfterPercent = 100;
        }

        double small = TextLayoutPlanner.ResolveSpaceBeforePoints(
            ResolveShapeParagraph(12, SetFullLineSpacing));
        double large = TextLayoutPlanner.ResolveSpaceBeforePoints(
            ResolveShapeParagraph(24, SetFullLineSpacing));

        small.Should().BeGreaterThan(0);
        large.Should().BeApproximately(small * 2, 1e-9,
            "a percentage of one line's height doubles when the font size doubles");
    }

    // ─── Compositor resolution: table cell text body ──────────────────────────

    [Fact]
    public void Compose_TableCellTextBody_PercentSpacing_ResolvesToProportionalPoints()
    {
        const double fontSizePt = 18;
        var cellBody = new TextBody();
        cellBody.Paragraphs.Add(MakeParagraph(fontSizePt, para =>
        {
            para.SpaceBeforePercent = 200;
            para.SpaceAfterPercent = 25;
        }));

        var table = new TableShape();
        table.ColumnWidthsEmu.Add(2286000);
        var row = new TableRow { HeightEmu = 685800 };
        row.Cells.Add(new TableCell { TextBody = cellBody });
        table.Rows.Add(row);

        var p = PresentationModel.CreateEmpty();
        p.Slides[0].Shapes.Clear();
        p.Slides[0].Shapes.Add(new SlideShape
        {
            Id = 1,
            Kind = SlideShapeKind.Table,
            OffsetXEmu = 457200,
            OffsetYEmu = 274320,
            ExtentCxEmu = 2286000,
            ExtentCyEmu = 685800,
            Table = table
        });

        var ops = SlideCompositor.Compose(p, p.Slides[0]);
        var tableOp = ops.OfType<DrawOp.Table>().Single();
        var resolved = tableOp.Cells.Single().Text!.Paragraphs[0];

        resolved.SpaceBeforePercent.Should().Be(200);
        resolved.SpaceAfterPercent.Should().Be(25);
        TextLayoutPlanner.ResolveSpaceBeforePoints(resolved).Should().BeApproximately(
            2.00 * LineHeightPt(fontSizePt), 1e-9);
        TextLayoutPlanner.ResolveSpaceAfterPoints(resolved).Should().BeApproximately(
            0.25 * LineHeightPt(fontSizePt), 1e-9);
    }

    // ─── Precedence + layout ──────────────────────────────────────────────────

    [Fact]
    public void Compose_ExplicitZeroPointSpacing_DropsCompetingPercent()
    {
        var resolved = ResolveShapeParagraph(20, para =>
        {
            para.SpaceBeforePt = 0;
            para.SpaceBeforePercent = 300;
        });

        resolved.SpaceBeforePercent.Should().BeNull(
            "an authored 0pt spcBef wins over spcPct, and the .pptx writer emits the 0pt value — " +
            "letting the percent survive the nullable collapse would make render and file disagree");
        TextLayoutPlanner.ResolveSpaceBeforePoints(resolved).Should().Be(0);
    }

    [Fact]
    public void ResolveSpacePoints_ExplicitPointsWinOverPercent()
    {
        var paragraph = new ResolvedParagraph
        {
            Runs = new[] { new ResolvedRun { Text = "Body", FontSizePt = 20 } },
            SpaceBeforePt = 7,
            SpaceAfterPt = 9,
            SpaceBeforePercent = 400,
            SpaceAfterPercent = 400
        };

        TextLayoutPlanner.ResolveSpaceBeforePoints(paragraph).Should().Be(7,
            "spcPts and spcPct are mutually exclusive and the explicit points value wins");
        TextLayoutPlanner.ResolveSpaceAfterPoints(paragraph).Should().Be(9);
    }

    [Fact]
    public void PlanMeasuredBodyText_PercentSpaceBefore_OffsetsFollowingParagraph()
    {
        const double fontSizePt = 20;
        const double paragraphHeightDip = 24;

        static ResolvedTextLayout MakeLayout(double fontSizePt, double? spaceBeforePercent) =>
            new()
            {
                AutoFitKind = TextAutoFitKind.None,
                Paragraphs = new[]
                {
                    new ResolvedParagraph
                    {
                        Runs = new[] { new ResolvedRun { Text = "First", FontSizePt = fontSizePt } }
                    },
                    new ResolvedParagraph
                    {
                        Runs = new[] { new ResolvedRun { Text = "Second", FontSizePt = fontSizePt } },
                        SpaceBeforePercent = spaceBeforePercent
                    }
                }
            };

        static double SecondParagraphY(ResolvedTextLayout text, double heightDip) =>
            TextLayoutPlanner.PlanMeasuredBodyText(
                text,
                new LayoutRect(0, 0, 400, 600),
                _ => new TextNativeMeasurement<string>("artifact", heightDip))
                .Layout.Paragraphs.Single(placement => placement.ParagraphIndex == 1).Y;

        double baseline = SecondParagraphY(MakeLayout(fontSizePt, null), paragraphHeightDip);
        double spaced = SecondParagraphY(MakeLayout(fontSizePt, 150), paragraphHeightDip);

        spaced.Should().BeApproximately(
            baseline + 1.50 * LineHeightPt(fontSizePt) * DipPerPoint, 1e-9,
            "percent space-before must push the next paragraph down at layout time");
    }
}
