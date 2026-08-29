using FluentAssertions;
using FreeX.App.Presentation.PageLayout;

namespace FreeX.App.Presentation.Tests.PageLayout;

/// <summary>
/// R101: <see cref="PageGeometryRules"/> is the single shared home for the two page-geometry rules
/// that were each independently discovered and fixed in several different renderers over several
/// rounds (R88/R96/R99/R100 for the header/footer-in-margin rule; R20/R100 for the uniform
/// fit-to-page scale rule) before being consolidated here. These tests exercise the rules directly,
/// covering both symmetric halves of each (top/bottom for the margin rule; width-constrained/
/// height-constrained for the scale rule) so a future edit that breaks either half is caught at the
/// single shared source instead of silently drifting at some downstream call site.
/// </summary>
public sealed class R101_PageGeometryRulesTests
{
    // ---- ResolveBodyEdge (header/footer-in-margin rule) ----------------------------------------

    [Fact]
    public void ResolveBodyEdge_HeaderMarginSmallerThanTopMargin_StaysAtPlainTopMargin()
    {
        // Top half: the common/default case (Excel's own 0.3in header margin under a 0.75in top
        // margin) -- the header band sits entirely within the margin, so the body edge is unaffected.
        PageGeometryRules.ResolveBodyEdge(margin: 0.75, headerOrFooterMargin: 0.3)
            .Should().Be(0.75, "the header margin fits within the top margin band and reserves nothing extra");
    }

    [Fact]
    public void ResolveBodyEdge_HeaderMarginLargerThanTopMargin_BodyEdgeMovesToHeaderMargin()
    {
        // Top half, oversized case: once the header margin exceeds the top margin, Excel pushes the
        // body's top edge out to the header margin itself (not their sum).
        PageGeometryRules.ResolveBodyEdge(margin: 0.75, headerOrFooterMargin: 1.5)
            .Should().Be(1.5, "the body edge must move out to the larger header margin, never past it, and never the sum of the two");
    }

    [Fact]
    public void ResolveBodyEdge_FooterMarginSmallerThanBottomMargin_StaysAtPlainBottomMargin()
    {
        // Bottom half (the mirror-image the r100 regression specifically targeted after r99 only
        // fixed the top/header half): common/default case.
        PageGeometryRules.ResolveBodyEdge(margin: 0.75, headerOrFooterMargin: 0.3)
            .Should().Be(0.75, "the footer margin fits within the bottom margin band and reserves nothing extra");
    }

    [Fact]
    public void ResolveBodyEdge_FooterMarginLargerThanBottomMargin_BodyEdgeMovesToFooterMargin()
    {
        // Bottom half, oversized case -- symmetric with the top-half oversized case above.
        PageGeometryRules.ResolveBodyEdge(margin: 0.2, headerOrFooterMargin: 1.5)
            .Should().Be(1.5, "the body's bottom edge must move out to the larger footer margin, never past it");
    }

    // ---- ResolveUniformScale (uniform fit-to-page scale rule) ----------------------------------

    [Fact]
    public void ResolveUniformScale_WidthMoreConstrainedThanHeight_ResolvesToWidthScale()
    {
        // Width-constrained half: the width axis needs the bigger shrink, so the single uniform scale
        // must be the (smaller) width ratio -- applied to BOTH axes, not just width.
        PageGeometryRules.ResolveUniformScale(widthScale: 0.4, heightScale: 0.9)
            .Should().Be(0.4, "Excel applies the more constrained axis's scale uniformly to both axes");
    }

    [Fact]
    public void ResolveUniformScale_HeightMoreConstrainedThanWidth_ResolvesToHeightScale()
    {
        // Height-constrained half -- symmetric with the width-constrained case above. A past round's
        // PDF/Avalonia path resolved each axis independently instead of taking this uniform minimum.
        PageGeometryRules.ResolveUniformScale(widthScale: 0.9, heightScale: 0.4)
            .Should().Be(0.4, "Excel applies the more constrained axis's scale uniformly to both axes");
    }

    [Fact]
    public void ResolveUniformScale_NeitherConstrained_ResolvesToOne()
    {
        PageGeometryRules.ResolveUniformScale(widthScale: 1.0, heightScale: 1.0)
            .Should().Be(1.0, "no shrink is needed when neither axis overflows");
    }

    [Fact]
    public void ResolveUniformScale_IsNeverTheProductOfBothAxisScales()
    {
        // R101-app-host-uniform-residual-scale-1's exact regression shape: a formula that multiplies
        // both axis scales together (instead of taking their minimum) silently over-shrinks whenever
        // both axes are constrained at once. Pin the rule's actual arithmetic so that class of bug
        // cannot creep back into this shared helper itself.
        var result = PageGeometryRules.ResolveUniformScale(widthScale: 0.5, heightScale: 0.25);

        result.Should().Be(0.25);
        result.Should().NotBe(0.5 * 0.25, "the uniform scale is the MIN of the two axis scales, never their product");
    }

    // ---- ResolveContainScale (shrink-only uniform picture fit) ---------------------------------

    [Fact]
    public void ResolveContainScale_ContentOverflowsWidthMoreThanHeight_ShrinksBothByTheWidthRatio()
    {
        // 800x400 into 100x100: width overflows 8x but height only 4x, so the width ratio (0.125)
        // must drive BOTH axes. The independent-per-axis clamp this rule replaced produced 100x100 --
        // a square, destroying the 2:1 source aspect ratio.
        PageGeometryRules.ResolveContainScale(
            contentWidth: 800, contentHeight: 400, availableWidth: 100, availableHeight: 100)
            .Should().Be(0.125);
    }

    [Fact]
    public void ResolveContainScale_ContentOverflowsHeightMoreThanWidth_ShrinksBothByTheHeightRatio()
    {
        // Symmetric half: the height axis binds. Both halves must hold -- the header/footer picture
        // bug this rule fixed showed up on the height axis (a tall, narrow picture in a short band).
        PageGeometryRules.ResolveContainScale(
            contentWidth: 400, contentHeight: 800, availableWidth: 100, availableHeight: 100)
            .Should().Be(0.125);
    }

    [Fact]
    public void ResolveContainScale_ContentAlreadyFitsBothAxes_IsNeverEnlarged()
    {
        // Shrink-only: content smaller than the box stays at its authored size. Dropping the
        // Math.Min(1, ...) clamp would blow a small logo up to fill its header/footer section.
        PageGeometryRules.ResolveContainScale(
            contentWidth: 40, contentHeight: 20, availableWidth: 100, availableHeight: 100)
            .Should().Be(1.0);
    }

    // ---- ResolveHeaderFooterBandHeight (grow-to-picture, capped at a fraction of the page) ------

    [Fact]
    public void ResolveHeaderFooterBandHeight_PictureTallerThanText_GrowsToThePicture()
    {
        // The band grows past its text-derived height to fit a configured picture -- the app's
        // deliberate SizeHeaderFooterBandsToContent departure from Excel.
        PageGeometryRules.ResolveHeaderFooterBandHeight(
            baseHeight: 18, tallestPictureHeight: 48, pageHeight: 1056)
            .Should().Be(48);
    }

    [Fact]
    public void ResolveHeaderFooterBandHeight_TextTallerThanPicture_KeepsTheTextHeight()
    {
        // Symmetric half: a multi-line text band that already exceeds its picture is not shrunk to it.
        PageGeometryRules.ResolveHeaderFooterBandHeight(
            baseHeight: 54, tallestPictureHeight: 20, pageHeight: 1056)
            .Should().Be(54);
    }

    [Fact]
    public void ResolveHeaderFooterBandHeight_OversizedPicture_IsCappedAtAQuarterOfThePage()
    {
        // The bound itself, pinned at the single shared source: a huge picture must not let one band
        // swallow the page. 792pt Letter portrait * 0.25 = 198pt -- the same number the R167
        // Avalonia PDF export test derives independently at its own call site.
        PageGeometryRules.ResolveHeaderFooterBandHeight(
            baseHeight: 12, tallestPictureHeight: 3000, pageHeight: 792)
            .Should().Be(198);

        PageGeometryRules.MaxHeaderFooterBandHeightFraction.Should().Be(0.25);
    }

    [Fact]
    public void ResolveHeaderFooterBandHeight_NoPageContext_IsUncapped()
    {
        // PrintRenderer.CalculateHeaderFooterLineHeight has no page geometry and passes infinity to
        // opt out of the cap; that must be an exact no-op rather than a degenerate clamp.
        PageGeometryRules.ResolveHeaderFooterBandHeight(
            baseHeight: 18, tallestPictureHeight: 3000, pageHeight: double.PositiveInfinity)
            .Should().Be(3000);
    }

    [Fact]
    public void ResolveHeaderFooterBandHeight_DegeneratePageHeight_StillLeavesAVisibleBand()
    {
        // The cap's own floor: a zero/near-zero page height must not collapse the band to nothing.
        PageGeometryRules.ResolveHeaderFooterBandHeight(
            baseHeight: 18, tallestPictureHeight: 0, pageHeight: 0)
            .Should().Be(1.0);
    }
}
