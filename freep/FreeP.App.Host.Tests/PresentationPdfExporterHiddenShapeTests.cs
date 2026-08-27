using System.Linq;
using Free.Shared.Drawing;
using Free.Shared.Pdf;
using FreeP.Core.IO;
using FreeP.Core.Model;

namespace FreeP.App.Host.Tests;

/// <summary>
/// <see cref="PresentationPdfExporter"/> is an independent, hand-written slide renderer that does
/// not share code with <c>FreeP.App.Presentation.SlideCompositor</c> (App depends on Core.IO, not
/// the other way around), so the compositor's two shape-hide rules had to be carried over here by
/// hand and never were:
/// <list type="bullet">
/// <item><c>SlideShape.IsHidden</c> -- <c>&lt;p:cNvPr hidden="1"/&gt;</c>, which the reader already
/// round-trips for every shape kind.</item>
/// <item><c>SlideShape.HasExplicitZeroExtentTransform</c> on a placeholder -- an explicit
/// <c>&lt;a:xfrm&gt;</c> with <c>&lt;a:ext cx="0" cy="0"/&gt;</c>, PowerPoint's way of hiding an
/// inherited placeholder without deleting it (see <c>SlideCompositor.ComposeAutoShape</c> and
/// <c>PptxPackageWriter</c>'s round-trip note).</item>
/// </list>
/// Both hid the shape on the editing canvas but rendered it in exported/printed PDF: a zero-extent
/// shape produces a degenerate box, so <c>TryAppendPositionedShape</c> returned false and the
/// caller fell back to the flowed-text placement, stamping the placeholder's text (or the
/// <c>"[Kind]"</c> debug label for a Picture/Media/Ole/Zoom shape with no text) down the left
/// margin of the page.
/// </summary>
public class PresentationPdfExporterHiddenShapeTests
{
    private const long SlideCx = 12_192_000; // 13.333in
    private const long SlideCy = 6_858_000;  // 7.5in

    private static PdfContentPage BuildWithPlaceholders(Slide slide) =>
        PresentationPdfExporter.BuildSlidePage(
            slide,
            SlideCx,
            SlideCy,
            includeCommentsAndInkMarkup: true,
            includePlaceholderShapeText: true);

    private static string[] TextOf(PdfContentPage page) =>
        page.Ops.OfType<PdfText>().Select(t => t.Text).ToArray();

    // ─── Explicit zero-extent placeholders (all shape kinds) ─────────────────────────────────

    [Theory]
    [InlineData(SlideShapeKind.AutoShape)]
    [InlineData(SlideShapeKind.Picture)]
    [InlineData(SlideShapeKind.Media)]
    [InlineData(SlideShapeKind.Ole)]
    [InlineData(SlideShapeKind.PreservedObject)]
    public void BuildSlidePage_ExplicitZeroExtentPlaceholder_IsOmittedEntirely(SlideShapeKind kind)
    {
        var slide = new Slide();
        slide.Shapes.Add(new SlideShape
        {
            Id = 1,
            Kind = kind,
            Placeholder = new Placeholder { Type = PlaceholderType.Body, Idx = 1 },
            HasExplicitZeroExtentTransform = true,
            Text = "Hidden body text",
        });

        var page = BuildWithPlaceholders(slide);

        TextOf(page).Should().BeEmpty(
            "an explicitly zero-extent placeholder is hidden on the canvas and must not reappear in the PDF");
        page.Ops.OfType<PdfFillRect>().Should().BeEmpty();
        page.Ops.OfType<PdfStrokeRect>().Should().BeEmpty();
    }

    [Fact]
    public void BuildSlidePage_ExplicitZeroExtentPicturePlaceholder_DoesNotEmitDebugLabel()
    {
        var slide = new Slide();
        slide.Shapes.Add(new SlideShape
        {
            Id = 1,
            Kind = SlideShapeKind.Picture,
            Placeholder = new Placeholder { Type = PlaceholderType.Picture, Idx = 2 },
            HasExplicitZeroExtentTransform = true,
        });

        TextOf(BuildWithPlaceholders(slide)).Should().NotContain("[Picture]");
    }

    [Fact]
    public void BuildSlidePage_ZeroExtentNonPlaceholderShape_StillFallsBackToFlowedText()
    {
        // The hide rule is placeholder-only, exactly as in SlideCompositor: a plain shape that
        // merely has no usable box keeps the pre-existing flowed-text fallback.
        var slide = new Slide();
        slide.Shapes.Add(new SlideShape
        {
            Id = 1,
            Kind = SlideShapeKind.AutoShape,
            HasExplicitZeroExtentTransform = true,
            Text = "Freestanding text",
        });

        TextOf(BuildWithPlaceholders(slide)).Should().Contain("Freestanding text");
    }

    // ─── hidden="1" ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void BuildSlidePage_HiddenShapeWithRealGeometry_IsOmittedEntirely()
    {
        var slide = new Slide();
        slide.Shapes.Add(new SlideShape
        {
            Id = 1,
            Kind = SlideShapeKind.AutoShape,
            IsHidden = true,
            OffsetXEmu = 1_000_000,
            OffsetYEmu = 1_000_000,
            ExtentCxEmu = 2_000_000,
            ExtentCyEmu = 1_000_000,
            Fill = new ShapeFill.Solid(new SrgbColor(0xFF, 0x00, 0x00)),
            Text = "Hidden shape text",
        });

        var page = BuildWithPlaceholders(slide);

        TextOf(page).Should().NotContain("Hidden shape text");
        page.Ops.OfType<PdfFillRect>().Should().BeEmpty();
    }

    [Fact]
    public void BuildSlidePage_HiddenGroupChild_IsOmittedFromComposedGroupContent()
    {
        var visible = new SlideShape
        {
            Id = 2,
            Kind = SlideShapeKind.AutoShape,
            OffsetXEmu = 1_000_000,
            OffsetYEmu = 1_000_000,
            ExtentCxEmu = 900_000,
            ExtentCyEmu = 600_000,
            Text = "Visible child",
        };
        var hidden = new SlideShape
        {
            Id = 3,
            Kind = SlideShapeKind.AutoShape,
            IsHidden = true,
            OffsetXEmu = 2_000_000,
            OffsetYEmu = 1_000_000,
            ExtentCxEmu = 900_000,
            ExtentCyEmu = 600_000,
            Text = "Hidden child",
        };

        var group = new SlideShape
        {
            Id = 1,
            Kind = SlideShapeKind.Group,
            OffsetXEmu = 900_000,
            OffsetYEmu = 900_000,
            ExtentCxEmu = 2_500_000,
            ExtentCyEmu = 800_000,
        };
        group.Children.Add(visible);
        group.Children.Add(hidden);

        var slide = new Slide();
        slide.Shapes.Add(group);

        var text = TextOf(BuildWithPlaceholders(slide));
        text.Should().Contain("Visible child");
        text.Should().NotContain("Hidden child");
    }
}
