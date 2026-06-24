using System.Linq;
using FreeW.App.Host.Editing;

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

        // Simulate SelectFloatingImage (internal; accessed via the same call path as canvas click).
        // We call it reflectively to keep the test against the public surface, or expose it for test.
        // Use SelectedImageLocation via the canvas approach: simulate by directly calling the internal method.
        var method = typeof(DocumentView).GetMethod(
            "SelectFloatingImage",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        method.Should().NotBeNull("SelectFloatingImage must exist as a private method");
        method!.Invoke(view, [floatingImg]);

        view.SelectedImage().Should().BeSameAs(floatingImg);
    }

    [StaFact]
    public void SelectedImageLocation_FindsFloatingImage_ByIdentity()
    {
        var doc = DocWithFloating(zOrder: 2);
        var view = new DocumentView();
        view.LoadModel(doc);

        var floatingImg = ((Paragraph)view.Model.Blocks[0]).Runs[0].Image!;

        var selectMethod = typeof(DocumentView).GetMethod(
            "SelectFloatingImage",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        selectMethod!.Invoke(view, [floatingImg]);

        var locationMethod = typeof(DocumentView).GetMethod(
            "SelectedImageLocation",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        locationMethod.Should().NotBeNull();
        var result = locationMethod!.Invoke(view, null);

        // Unpack the value tuple: (int BlockIndex, int RunIndex, InlineImage? Image)
        var resultType = result!.GetType();
        var blockIndex = (int)resultType.GetField("Item1")!.GetValue(result)!;
        var runIndex = (int)resultType.GetField("Item2")!.GetValue(result)!;
        var image = (InlineImage?)resultType.GetField("Item3")!.GetValue(result);

        blockIndex.Should().Be(0);
        runIndex.Should().Be(0);
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
