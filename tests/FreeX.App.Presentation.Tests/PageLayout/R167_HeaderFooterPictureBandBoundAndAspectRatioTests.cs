using FluentAssertions;
using FreeX.App.Presentation.PageLayout;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.PageLayout;

/// <summary>
/// Round 167 (C4): the second root cause of the header/footer-picture-sizing finding, left untouched
/// by Round 166's DPI-conversion fix (which only narrowed the range of picture sizes that reach this
/// code -- it did not remove the two bugs here). <see
/// cref="WorksheetPrintHeaderFooterGeometryPlanner.BuildBand"/> let a picture-driven header/footer
/// band grow to whatever height <c>ResolveLineHeight</c> computed from the picture's own (now
/// correctly DIP-converted, but still caller-supplied and unbounded) size, with nothing capping it
/// against the page; and <see cref="WorksheetPrintHeaderFooterGeometryPlanner.ResolvePictureBounds"/>
/// clamped the picture's width and height on independent axes (two separate <c>Math.Min</c> calls),
/// so a picture overflowing one axis far more than the other came out squashed to the wrong aspect
/// ratio instead of proportionally fit.
/// </summary>
public sealed class R167_HeaderFooterPictureBandBoundAndAspectRatioTests
{
    // Matches the WPF-native profile's own baseLineHeight; page geometry below mirrors a Letter
    // portrait page in the app's 96-DPI-based unit convention (816 x 1056).
    private const double BaseLineHeight = 18.0;

    [Fact]
    public void BuildBand_OversizedPicture_BandHeightStaysBoundedToAFractionOfThePage()
    {
        // A picture far taller than any page: the auditor's "72 DPI photo" scenario -- post-R166 DPI
        // conversion, a real photo decodes to a size the header/footer band previously grew to fit
        // verbatim, with no upper limit.
        var picture = new WorksheetHeaderFooterPicture([1, 2, 3], "image/png", "huge.png", Width: 1200, Height: 3000);
        var pictures = new WorksheetHeaderFooterPictureSet(null, null, picture);
        var header = new WorksheetHeaderFooter("", "", "&G");
        const double pageWidth = 816.0;
        const double pageHeight = 1056.0;

        var band = WorksheetPrintHeaderFooterGeometryPlanner.BuildBand(
            header,
            pictures,
            pageWidth,
            pageHeight,
            marginLeft: 72,
            marginRight: 72,
            marginBottom: 72,
            bandMargin: 28.8,
            alignWithMargins: true,
            isFooter: false,
            draftQuality: false,
            fontScale: 1.0,
            baseLineHeight: BaseLineHeight,
            sizeToContent: true);

        // The band must never balloon to (anywhere near) the picture's own 3000px height -- it must
        // stay within a bounded portion of the page so the printed grid still has room.
        band.Left.Height.Should().BeLessThan(pageHeight / 2);
        band.Left.Height.Should().BeGreaterThanOrEqualTo(BaseLineHeight);
    }

    [Fact]
    public void BuildBand_ModestPicture_StillGrowsToFitUnaffectedSiblingCase()
    {
        // No-regression sibling: a picture that comfortably fits within a sane band (the app's own
        // historical default picture size, 96x48) must still be honored in full, exactly like before
        // this fix -- the bound must not clip ordinary content.
        var picture = new WorksheetHeaderFooterPicture([1, 2, 3], "image/png", "logo.png", Width: 96, Height: 48);
        var pictures = new WorksheetHeaderFooterPictureSet(null, null, picture);
        var header = new WorksheetHeaderFooter("", "", "&G");

        var band = WorksheetPrintHeaderFooterGeometryPlanner.BuildBand(
            header,
            pictures,
            pageWidth: 816.0,
            pageHeight: 1056.0,
            marginLeft: 72,
            marginRight: 72,
            marginBottom: 72,
            bandMargin: 28.8,
            alignWithMargins: true,
            isFooter: false,
            draftQuality: false,
            fontScale: 1.0,
            baseLineHeight: BaseLineHeight,
            sizeToContent: true);

        band.Left.Height.Should().Be(48);
    }

    [Fact]
    public void ResolvePictureBounds_PictureDoesNotFitEitherAxis_ShrinksUniformlyPreservingAspectRatio()
    {
        // 800 x 400 (2:1 aspect) into a 100 x 100 section: the picture overflows width by 8x but
        // height by only 4x. The old independent-axis clamp (Math.Min per axis) produced 100 x 100 --
        // a square, distorting the original 2:1 aspect ratio.
        var picture = new WorksheetHeaderFooterPicture([1], "image/png", "wide.png", Width: 800, Height: 400);
        var section = new LayoutRect(0, 0, 100, 100);

        var bounds = WorksheetPrintHeaderFooterGeometryPlanner.ResolvePictureBounds(
            picture, section, PageTextAlignment.Left);

        // Fit inside the section on both axes...
        bounds.Width.Should().BeLessThanOrEqualTo(section.Width + 0.001);
        bounds.Height.Should().BeLessThanOrEqualTo(section.Height + 0.001);
        // ...and the more-constrained axis (width) drives a SINGLE uniform scale applied to both,
        // exactly like PageGeometryRules.ResolveUniformScale's page-level fit-to-N-pages rule -- so
        // the 2:1 original aspect ratio survives.
        (bounds.Width / bounds.Height).Should().BeApproximately(2.0, 0.001);
        bounds.Width.Should().BeApproximately(100, 0.001);
        bounds.Height.Should().BeApproximately(50, 0.001);
    }

    [Fact]
    public void ResolvePictureBounds_PictureAlreadyFitsSection_SizeIsUnchangedSiblingCase()
    {
        // No-regression sibling: a picture that already fits both axes must be left at its original
        // size (no shrink, no distortion) -- matching the prior behavior for this common case exactly.
        var picture = new WorksheetHeaderFooterPicture([1], "image/png", "small.png", Width: 40, Height: 20);
        var section = new LayoutRect(0, 0, 100, 100);

        var bounds = WorksheetPrintHeaderFooterGeometryPlanner.ResolvePictureBounds(
            picture, section, PageTextAlignment.Left);

        bounds.Width.Should().Be(40);
        bounds.Height.Should().Be(20);
    }
}
