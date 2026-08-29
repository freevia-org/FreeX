using FluentAssertions;
using FreeX.App.Presentation.PageLayout;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.PageLayout;

/// <summary>
/// R168: two defects found while consolidating the header/footer picture geometry rules into
/// <see cref="PageGeometryRules"/>, both in <c>WorksheetPrintHeaderFooterGeometryPlanner</c> (the
/// WPF-shared print / print-preview / WPF-PDF geometry):
///
/// <list type="number">
/// <item>
/// <b>R168-presentation-headerfooter-text-inset-1.</b> <c>ResolveTextBounds</c> reserved space for
/// the picture's RAW authored width, while <c>ResolvePictureBounds</c> draws the picture at a
/// uniformly SCALED width (rounds 166/167). A picture that has to shrink to fit its section --
/// precisely the case those rounds fixed -- therefore left a text gap several times wider than the
/// picture actually drawn, pushing the section's text far to the right of (or entirely out of) its
/// own section. The Avalonia/Skia PDF export path already reserved the scaled width; only this
/// WPF-side path used the raw one.
/// </item>
/// <item>
/// <b>R168-presentation-headerfooter-footer-band-page-1.</b> <c>BuildBand</c>'s footer branch takes
/// the LOWER of two anchors so the footer never overlaps the grid's own bottom edge -- but nothing
/// then held the band on the page, so a footer band grown to fit a picture (up to the 25% cap) ran
/// straight off the bottom of the sheet of paper, taking its picture with it. The header branch has
/// always been clamped away from the top edge; the footer half simply never got the mirror-image
/// clamp.
/// </item>
/// </list>
/// </summary>
public sealed class R168_HeaderFooterPictureTextInsetAndBandPlacementTests
{
    private const double BaseLineHeight = 18.0;
    private const double PageWidth = 816.0;   // Letter portrait at the app's 96-DPI unit convention
    private const double PageHeight = 1056.0;

    // ---- R168-presentation-headerfooter-text-inset-1 -------------------------------------------

    [Fact]
    public void ResolveTextBounds_PictureShrunkToFitSection_ReservesTheDrawnWidthNotTheRawWidth()
    {
        // A tall, narrow picture (50 x 400) in a short, wide section: the height axis binds, so the
        // picture is drawn at 1/8 scale -- about 6 units wide, not 50. Reserving the raw 50 would
        // shove the text ~44 units further right than the picture it is meant to clear.
        var picture = new WorksheetHeaderFooterPicture([1], "image/png", "tall.png", Width: 50, Height: 400);
        var section = new LayoutRect(24, 10, 200, 50);

        var pictureBounds = WorksheetPrintHeaderFooterGeometryPlanner.ResolvePictureBounds(
            picture, section, PageTextAlignment.Left);
        var textBounds = WorksheetPrintHeaderFooterGeometryPlanner.ResolveTextBounds(
            section, picture, PageTextAlignment.Left);

        pictureBounds.Width.Should().BeApproximately(6.25, 0.001, "50 x 400 into 200 x 50 shrinks by 0.125");
        textBounds.Left.Should().BeApproximately(pictureBounds.Right + 4, 0.001,
            "the text starts one gap past where the picture is actually DRAWN, not past its raw authored width");
        textBounds.Right.Should().BeApproximately(section.Right, 0.001,
            "the text keeps the rest of its section");
    }

    [Fact]
    public void ResolveTextBounds_RightAlignedShrunkPicture_ReservesTheDrawnWidthOnTheRightEdge()
    {
        // Symmetric half: a right-aligned picture is drawn against the section's right edge, so the
        // text rect keeps its left edge and gives up only the drawn width plus the gap.
        var picture = new WorksheetHeaderFooterPicture([1], "image/png", "tall.png", Width: 50, Height: 400);
        var section = new LayoutRect(24, 10, 200, 50);

        var pictureBounds = WorksheetPrintHeaderFooterGeometryPlanner.ResolvePictureBounds(
            picture, section, PageTextAlignment.Right);
        var textBounds = WorksheetPrintHeaderFooterGeometryPlanner.ResolveTextBounds(
            section, picture, PageTextAlignment.Right);

        textBounds.Left.Should().Be(section.Left);
        textBounds.Right.Should().BeApproximately(pictureBounds.Left - 4, 0.001,
            "the text stops one gap short of where the picture is actually DRAWN");
    }

    [Fact]
    public void ResolveTextBounds_PictureThatAlreadyFits_LeavesTheFullGapPastTheDrawnPicture()
    {
        // No-regression sibling for the common unscaled case: the reserved space is still the
        // picture's own width, and the gap between the drawn picture and the text is the full gap
        // (the raw-width formula previously ate half of it, because ResolvePictureBounds insets a
        // left-aligned picture by 2 while the text inset measured from the section's edge).
        var picture = new WorksheetHeaderFooterPicture([1], "image/png", "logo.png", Width: 96, Height: 42);
        var section = new LayoutRect(24, 10, 200, 42);

        var pictureBounds = WorksheetPrintHeaderFooterGeometryPlanner.ResolvePictureBounds(
            picture, section, PageTextAlignment.Left);
        var textBounds = WorksheetPrintHeaderFooterGeometryPlanner.ResolveTextBounds(
            section, picture, PageTextAlignment.Left);

        pictureBounds.Should().Be(new LayoutRect(26, 10, 96, 42));
        textBounds.Should().Be(new LayoutRect(126, 10, 98, 42));
    }

    [Fact]
    public void ResolveTextBounds_CenterAlignedPicture_LeavesTheTextRectUnshifted()
    {
        // Unchanged by this fix and asserted so it stays that way: a center section's text is not
        // pushed aside at all (the Avalonia/Skia PDF path mirrors this deliberately).
        var picture = new WorksheetHeaderFooterPicture([1], "image/png", "logo.png", Width: 96, Height: 42);
        var section = new LayoutRect(24, 10, 200, 42);

        WorksheetPrintHeaderFooterGeometryPlanner.ResolveTextBounds(section, picture, PageTextAlignment.Center)
            .Should().Be(section);
    }

    [Fact]
    public void ResolveTextBounds_NoPicture_LeavesTheWholeSectionToTheText()
    {
        var section = new LayoutRect(24, 10, 200, 42);

        WorksheetPrintHeaderFooterGeometryPlanner.ResolveTextBounds(section, picture: null, PageTextAlignment.Left)
            .Should().Be(section);
    }

    // ---- R168-presentation-headerfooter-footer-band-page-1 -------------------------------------

    [Fact]
    public void BuildBand_FooterGrownByAPicture_StaysOnThePage()
    {
        // The footer anchor that keeps the band clear of the grid (pageHeight - the resolved bottom
        // body edge) sits only one bottom margin above the page's bottom edge, so a band grown to the
        // 25% cap used to start there and run right off the paper.
        var band = BuildFooterBandWithOversizedPicture();

        band.Left.Top.Should().BeGreaterThanOrEqualTo(0);
        (band.Left.Top + band.Left.Height).Should().BeLessThanOrEqualTo(PageHeight + 0.001,
            "a grown footer band must stay on the page instead of running off its bottom edge");
    }

    [Fact]
    public void BuildBand_FooterGrownByAPicture_SitsFlushWithTheBottomEdgeRatherThanShrinking()
    {
        // How it stays on the page matters: the band keeps its full grown height (the picture is not
        // silently squashed) and is pushed up to sit flush against the page's bottom edge -- the
        // mirror image of the header band, which pins to the top edge and overlaps the grid downward.
        var band = BuildFooterBandWithOversizedPicture();

        band.Left.Height.Should().Be(PageHeight * PageGeometryRules.MaxHeaderFooterBandHeightFraction);
        band.Left.Top.Should().BeApproximately(PageHeight - band.Left.Height, 0.001);
    }

    [Fact]
    public void BuildBand_FooterWithOrdinaryTextBand_KeepsItsExistingAnchorUnchanged()
    {
        // No-regression sibling: an ordinary (ungrown) footer band is nowhere near the page edge, so
        // the new clamp must not move it at all -- it still sits one footer margin plus its own line
        // height above the page's bottom edge (1056 - 28.8 - 18), the higher of its two anchors.
        var band = WorksheetPrintHeaderFooterGeometryPlanner.BuildBand(
            new WorksheetHeaderFooter("", "Page &P", ""),
            WorksheetHeaderFooterPictureSet.Empty,
            PageWidth,
            PageHeight,
            marginLeft: 72,
            marginRight: 72,
            marginBottom: 96,
            bandMargin: 28.8,
            alignWithMargins: true,
            isFooter: true,
            draftQuality: false,
            fontScale: 1.0,
            baseLineHeight: BaseLineHeight,
            sizeToContent: true);

        band.Left.Top.Should().BeApproximately(PageHeight - 28.8 - BaseLineHeight, 0.001);
        band.Left.Height.Should().Be(BaseLineHeight);
    }

    private static WorksheetPrintHeaderFooterBandGeometry BuildFooterBandWithOversizedPicture()
    {
        var picture = new WorksheetHeaderFooterPicture([1, 2, 3], "image/png", "huge.png", Width: 1200, Height: 3000);

        return WorksheetPrintHeaderFooterGeometryPlanner.BuildBand(
            new WorksheetHeaderFooter("", "", "&G"),
            new WorksheetHeaderFooterPictureSet(null, null, picture),
            PageWidth,
            PageHeight,
            marginLeft: 72,
            marginRight: 72,
            marginBottom: 96,
            bandMargin: 28.8,
            alignWithMargins: true,
            isFooter: true,
            draftQuality: false,
            fontScale: 1.0,
            baseLineHeight: BaseLineHeight,
            sizeToContent: true);
    }
}
