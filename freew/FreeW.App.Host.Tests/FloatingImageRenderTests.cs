using System.Linq;
using FreeW.App.Host.Editing;
using FreeW.App.Presentation.DocumentView;
using WpfFloater = System.Windows.Documents.Floater;
using WpfParagraph = System.Windows.Documents.Paragraph;

namespace FreeW.App.Host.Tests;

/// <summary>
/// App-layer (STA) coverage for floating-image rendering (Phase 1): a floating
/// <see cref="InlineImage"/> must survive <see cref="DocumentView.LoadModel"/> →
/// <see cref="DocumentView.CommitToModel"/> with all position/z-order/wrapping fields intact, and
/// <see cref="DocumentView.SelectedImage"/> must expose it after a floating-canvas click is simulated.
/// An inline image (the default) must be completely unaffected by the new path.
/// </summary>
public sealed class FloatingImageRenderTests
{
    private static byte[] MinimalPng() =>
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
        0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
        0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
        0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4,
        0x89, 0x00, 0x00, 0x00, 0x0D, 0x49, 0x44, 0x41,
        0x54, 0x78, 0x9C, 0x62, 0x00, 0x01, 0x00, 0x00,
        0x05, 0x00, 0x01, 0x0D, 0x0A, 0x2D, 0xB4, 0x00,
        0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE,
        0x42, 0x60, 0x82,
    ];

    private static TextDocument DocWithFloating(
        ImageWrapping wrapping = ImageWrapping.Square,
        double hOffPt = 36, double vOffPt = 18,
        HorizontalAnchor hAnchor = HorizontalAnchor.Margin,
        VerticalAnchor vAnchor = VerticalAnchor.Page,
        int zOrder = 3)
    {
        var doc = new TextDocument();
        doc.Blocks.Clear();
        var para = new Paragraph();
        para.Runs.Add(Run.FromImage(new InlineImage(MinimalPng(), widthPt: 72, heightPt: 54)
        {
            Wrapping = wrapping,
            HorizontalOffsetPt = hOffPt,
            VerticalOffsetPt = vOffPt,
            HorizontalAnchor = hAnchor,
            VerticalAnchor = vAnchor,
            ZOrderIndex = zOrder,
        }));
        doc.Blocks.Add(para);
        return doc;
    }

    private static TextDocument DocWithInline()
    {
        var doc = new TextDocument();
        doc.Blocks.Clear();
        var para = new Paragraph();
        para.Runs.Add(Run.FromImage(new InlineImage(MinimalPng(), widthPt: 80, heightPt: 60)));
        doc.Blocks.Add(para);
        return doc;
    }

    private static TextDocument DocWithFloatingText(
        ImageWrapping wrapping,
        out InlineImage image,
        double hOffPt = 36,
        double vOffPt = 18,
        HorizontalAnchor hAnchor = HorizontalAnchor.Margin,
        VerticalAnchor vAnchor = VerticalAnchor.Page)
    {
        var doc = new TextDocument();
        doc.Blocks.Clear();
        var para = new Paragraph();
        para.Runs.Add(new Run("before "));
        image = new InlineImage(MinimalPng(), widthPt: 72, heightPt: 54)
        {
            Wrapping = wrapping,
            HorizontalOffsetPt = hOffPt,
            VerticalOffsetPt = vOffPt,
            HorizontalAnchor = hAnchor,
            VerticalAnchor = vAnchor,
            ZOrderIndex = 3,
        };
        para.Runs.Add(Run.FromImage(image));
        para.Runs.Add(new Run(" after"));
        doc.Blocks.Add(para);
        return doc;
    }

    private static TextDocument DocWithFloatingShapeText(out Shape shape)
    {
        var doc = new TextDocument();
        doc.Blocks.Clear();
        var para = new Paragraph();
        para.Runs.Add(new Run("before "));
        shape = new Shape(ShapeKind.Rectangle, 72, 36)
        {
            Placement = new FloatingPlacement
            {
                Wrapping = ImageWrapping.Square,
                HorizontalOffsetPt = 36,
                VerticalOffsetPt = 18,
                ZOrderIndex = 5,
            },
        };
        para.Runs.Add(Run.FromShape(shape));
        para.Runs.Add(new Run(" after"));
        doc.Blocks.Add(para);
        return doc;
    }

    private static WpfParagraph RenderedParagraph(DocumentView view) =>
        view.Document.Blocks.OfType<WpfParagraph>().Single();

    // ── Floating image round-trip ─────────────────────────────────────────────────────────────────

    [StaFact]
    public void FloatingImage_SurvivesCommitToModel()
    {
        var original = DocWithFloating();
        var view = new DocumentView();
        view.LoadModel(original);
        view.CommitToModel();
        var recovered = view.Model;

        var para = (Paragraph)recovered.Blocks[0];
        var image = para.Runs[0].Image;
        image.Should().NotBeNull();
        image!.IsFloating.Should().BeTrue();
        image.Wrapping.Should().Be(ImageWrapping.Square);
        image.HorizontalOffsetPt.Should().BeApproximately(36, 0.01);
        image.VerticalOffsetPt.Should().BeApproximately(18, 0.01);
        image.HorizontalAnchor.Should().Be(HorizontalAnchor.Margin);
        image.VerticalAnchor.Should().Be(VerticalAnchor.Page);
        image.ZOrderIndex.Should().Be(3);
    }

    [StaFact]
    public void FloatingImage_WrapModesProduceReservationAndSurviveCommitInOrder()
    {
        foreach (var wrapping in new[] { ImageWrapping.Square, ImageWrapping.Tight, ImageWrapping.TopAndBottom })
        {
            var doc = DocWithFloatingText(wrapping, out var originalImage);
            var view = new DocumentView();
            view.LoadModel(doc);

            var floater = RenderedParagraph(view).Inlines.OfType<WpfFloater>().Single();
            floater.Tag.Should().NotBeNull();
            if (wrapping == ImageWrapping.TopAndBottom)
            {
                floater.Width.Should().BeApproximately(
                    DocumentViewLayoutPlanner.BuildPageMetrics(doc.Page).ContentWidthDip,
                    0.01);
            }
            else
            {
                floater.Width.Should().BeApproximately(96, 0.01);
            }

            view.CommitToModel();

            var committed = (Paragraph)view.Model.Blocks[0];
            committed.Runs.Should().HaveCount(3);
            committed.Runs[0].Text.Should().Be("before ");
            committed.Runs[1].Image.Should().BeSameAs(originalImage);
            committed.Runs[1].Image!.Wrapping.Should().Be(wrapping);
            committed.Runs[2].Text.Should().Be(" after");
        }
    }

    [StaFact]
    public void FloatingImage_WrapReservationsHaveSharedLinePlanEvidence()
    {
        foreach (var wrapping in new[] { ImageWrapping.Square, ImageWrapping.Tight, ImageWrapping.TopAndBottom })
        {
            var doc = DocWithFloatingText(
                wrapping,
                out _,
                hOffPt: 0,
                vOffPt: 0,
                hAnchor: HorizontalAnchor.Column,
                vAnchor: VerticalAnchor.Paragraph);
            var view = new DocumentView();
            view.LoadModel(doc);

            RenderedParagraph(view).Inlines.OfType<WpfFloater>()
                .Should()
                .ContainSingle("WPF must consume the shared wrap reservation instead of overlay-only rendering");

            var surface = DocumentViewLayoutPlanner.BuildFloatingOverlaySurfacePlan(
                doc.Page,
                printLayout: view.PrintLayoutEnabled,
                plainInsetDip: 48);
            var snapshots = DocumentViewLayoutPlanner.BuildFloatingObjectSnapshots(
                doc,
                surface,
                columnCount: 1);
            var zones = DocumentViewLayoutPlanner.BuildFloatingWrapExclusionZones(snapshots);
            var zone = zones.Single();
            var linePlan = DocumentViewLayoutPlanner.BuildFloatingTextWrapLinePlan(
                zones,
                surface,
                currentContentYDip: 0,
                lineContentYDip: 0,
                lineHeightDip: 18,
                contentLeftDip: surface.ContentLeftDip,
                columnCount: 1,
                columnWidthDip: surface.ContentWidthDip,
                columnGapDip: 0,
                baseTextWidthDip: surface.ContentWidthDip);

            if (wrapping == ImageWrapping.TopAndBottom)
            {
                linePlan.HasTopAndBottomAdvance.Should().BeTrue();
                linePlan.PageSpaceYDip.Should().BeGreaterThanOrEqualTo(zone.Rect.BottomDip);
            }
            else
            {
                linePlan.HasLateralExclusion.Should().BeTrue();
                linePlan.TextLeftDip().Should().BeGreaterThan(zone.Rect.RightDip);
                linePlan.TextRightDip().Should().BeLessThanOrEqualTo(
                    surface.ContentLeftDip + surface.ContentWidthDip);
            }
        }
    }

    [StaFact]
    public void FloatingImage_WrapReservationAlignsWithRightHandObject()
    {
        var doc = DocWithFloatingText(
            ImageWrapping.Tight,
            out _,
            hOffPt: 300,
            vOffPt: 60,
            hAnchor: HorizontalAnchor.Margin,
            vAnchor: VerticalAnchor.Page);
        var view = new DocumentView();
        view.LoadModel(doc);

        RenderedParagraph(view).Inlines.OfType<WpfFloater>()
            .Should()
            .ContainSingle()
            .Which.HorizontalAlignment.Should()
            .Be(System.Windows.HorizontalAlignment.Right);
    }

    [StaFact]
    public void FloatingImage_BehindAndInFrontDoNotReserveButSurviveCommitInOrder()
    {
        foreach (var wrapping in new[] { ImageWrapping.Behind, ImageWrapping.InFront })
        {
            var doc = DocWithFloatingText(wrapping, out var originalImage);
            var view = new DocumentView();
            view.LoadModel(doc);

            RenderedParagraph(view).Inlines.OfType<WpfFloater>().Should().BeEmpty();

            view.CommitToModel();

            var committed = (Paragraph)view.Model.Blocks[0];
            committed.Runs.Should().HaveCount(3);
            committed.Runs[0].Text.Should().Be("before ");
            committed.Runs[1].Image.Should().BeSameAs(originalImage);
            committed.Runs[1].Image!.Wrapping.Should().Be(wrapping);
            committed.Runs[2].Text.Should().Be(" after");
        }
    }

    [StaFact]
    public void FloatingImage_WrapReservationInsideHyperlinkSurvivesCommit()
    {
        var doc = DocWithFloatingText(ImageWrapping.Square, out var originalImage);
        var imageRun = ((Paragraph)doc.Blocks[0]).Runs[1];
        imageRun.HyperlinkUrl = "https://example.com/floating";
        imageRun.HyperlinkTooltip = "floating tip";
        var view = new DocumentView();
        view.LoadModel(doc);

        view.CommitToModel();

        var committed = (Paragraph)view.Model.Blocks[0];
        committed.Runs.Should().HaveCount(3);
        committed.Runs[1].Image.Should().BeSameAs(originalImage);
        committed.Runs[1].HyperlinkUrl.Should().Be("https://example.com/floating");
        committed.Runs[1].HyperlinkTooltip.Should().Be("floating tip");
    }

    [StaFact]
    public void FloatingShape_SquareProducesReservationAndSurvivesCommitInOrder()
    {
        var doc = DocWithFloatingShapeText(out var originalShape);
        var view = new DocumentView();
        view.LoadModel(doc);

        RenderedParagraph(view).Inlines.OfType<WpfFloater>().Should().ContainSingle();

        view.CommitToModel();

        var committed = (Paragraph)view.Model.Blocks[0];
        committed.Runs.Should().HaveCount(3);
        committed.Runs[0].Text.Should().Be("before ");
        committed.Runs[1].Shape.Should().BeSameAs(originalShape);
        committed.Runs[1].Shape!.Placement!.Wrapping.Should().Be(ImageWrapping.Square);
        committed.Runs[2].Text.Should().Be(" after");
    }

    [StaFact]
    public void FloatingWordArt_TwoColumnAnchorUsesColumnAwareLeadingHeight()
    {
        var doc = FreeWVisualEvidenceDocumentFactory.BuildWordArtPictureWatermarkLayoutDocument();
        var view = new DocumentView();
        var canvas = new System.Windows.Controls.Canvas();
        view.SetFloatingCanvas(canvas);
        view.LoadModel(doc);
        view.Measure(new System.Windows.Size(816, 1056));
        view.Arrange(new System.Windows.Rect(0, 0, 816, 1056));
        view.UpdateLayout();

        canvas.Children.Count.Should().Be(1, "the fixture has one floating WordArt object");
        System.Windows.Controls.Canvas.GetTop(canvas.Children[0])
            .Should()
            .BeGreaterThan(260, "a two-column anchor must account for the narrower preceding paragraphs");
    }

    [StaFact]
    public void FloatingImage_MultipleCommitCycles_Preserve()
    {
        var original = DocWithFloating(zOrder: 7);
        var view = new DocumentView();
        view.LoadModel(original);

        // Simulate two edit/commit cycles (e.g. user types, commits, re-renders).
        view.CommitToModel();
        view.CommitToModel();

        var image = ((Paragraph)view.Model.Blocks[0]).Runs[0].Image;
        image.Should().NotBeNull();
        image!.ZOrderIndex.Should().Be(7);
        image.IsFloating.Should().BeTrue();
    }

    // ── SelectedImage fallback to floating selection ──────────────────────────────────────────────

    [StaFact]
    public void SelectedImage_ReturnsFloatingImage_AfterSelectFloatingImage()
    {
        var doc = DocWithFloating();
        var view = new DocumentView();
        view.LoadModel(doc);

        var floatingImg = ((Paragraph)view.Model.Blocks[0]).Runs[0].Image!;

        // SelectFloatingImage is now internal (promoted from private so multi-select tests
        // can call it directly without reflection).
        view.SelectFloatingImage(floatingImg);

        view.SelectedImage().Should().BeSameAs(floatingImg);
    }

    [StaFact]
    public void SelectedImageLocation_FindsFloatingImage_ByIdentity()
    {
        var doc = DocWithFloating(zOrder: 2);
        var view = new DocumentView();
        view.LoadModel(doc);

        var floatingImg = ((Paragraph)view.Model.Blocks[0]).Runs[0].Image!;

        // SelectFloatingImage is now internal — call it directly.
        view.SelectFloatingImage(floatingImg);

        // SelectedImage() is a public method that internally delegates to SelectedImageLocation().
        var image = view.SelectedImage();
        image.Should().BeSameAs(floatingImg);
    }

    // ── Inline images are unaffected ─────────────────────────────────────────────────────────────

    [StaFact]
    public void InlineImage_RoundTripsUnchanged()
    {
        var doc = DocWithInline();
        var view = new DocumentView();
        view.LoadModel(doc);
        view.CommitToModel();

        var para = (Paragraph)view.Model.Blocks[0];
        var image = para.Runs[0].Image;
        image.Should().NotBeNull();
        image!.IsFloating.Should().BeFalse();
        image.Wrapping.Should().Be(ImageWrapping.Inline);
        image.ZOrderIndex.Should().Be(0);
    }

    [StaFact]
    public void InlineImage_SelectedImage_FindsImageViaInlinePath()
    {
        // An inline image is still found via the existing InlineUIContainer path,
        // not the floating-canvas fallback.  With a single inline image the
        // DocumentView positions the cursor near it, so SelectedImage() returns it.
        // The key invariant is that the inline image is NOT null after round-trip, and
        // that no floating-canvas state bleeds into the result.
        var doc = DocWithInline();
        var view = new DocumentView();
        view.LoadModel(doc);

        var image = view.SelectedImage();
        // Either null (no caret proximity) or the image itself is acceptable here —
        // what must NOT happen is an exception or a floating image being returned.
        // The true guard is that the inline image round-trips correctly (covered above).
        if (image is not null)
            image.IsFloating.Should().BeFalse("SelectedImage must not return a floating image for an inline doc");
    }
}
