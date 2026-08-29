using FluentAssertions;
using FreeX.App.Presentation.PageLayout;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.PageLayout;

/// <summary>
/// R168-shared-headerfooter-picture-token-1: "does this header/footer section contain a picture
/// token?" was answered by a plain <c>Contains("&amp;G")</c> substring test, written out twice (in
/// the WPF-shared geometry planner and again in the Avalonia/Skia PDF builder). A substring test
/// cannot see Excel's format-code escapes that
/// <see cref="PagePrintTextPlanner.TokenizeSectionText"/> -- the tokenizer that decides what the
/// section actually RENDERS -- honours, so the two disagreed: text containing an escaped literal
/// ampersand followed by a G renders as plain text with no picture, yet both renderers grew the
/// band to fit a picture, reserved a text inset for it, and drew it.
///
/// These pin the detection against the tokenizer's own output, which is the property that has to
/// hold: a section shows a picture if and only if tokenizing it consumed a picture token.
/// </summary>
public sealed class R168_HeaderFooterPictureTokenDetectionTests
{
    [Theory]
    [InlineData("&G")]
    [InlineData("&g")]
    [InlineData("Logo &G")]
    [InlineData("&[Picture]")]
    [InlineData("&[picture]")]
    [InlineData("&B&\"Arial\"&G")]
    [InlineData("&&&G")]                    // an escaped literal '&' followed by a real token
    [InlineData("A && B &[Picture]")]
    public void HasPictureToken_RealPictureTokens_AreDetected(string text)
    {
        PagePrintTextPlanner.HasPictureToken(text).Should().BeTrue();
        WorksheetPrintHeaderFooterGeometryPlanner.HasPictureToken(text).Should().BeTrue(
            "the planner must agree with the shared detection it delegates to");
    }

    [Theory]
    [InlineData("R&&G Ltd")]                // the defect: renders "R&G Ltd", no picture
    [InlineData("Black && Gold")]
    [InlineData("&&g")]
    [InlineData("Sales &&Growth")]
    [InlineData("")]
    [InlineData("Plain header")]
    [InlineData("&B&IBold italic")]
    [InlineData("&[Date] &[Page]")]
    [InlineData("&P of &N")]
    [InlineData("Trailing &")]
    public void HasPictureToken_TextWithoutAPictureToken_IsNotMistakenForOne(string text)
    {
        PagePrintTextPlanner.HasPictureToken(text).Should().BeFalse();
        WorksheetPrintHeaderFooterGeometryPlanner.HasPictureToken(text).Should().BeFalse(
            "the planner must agree with the shared detection it delegates to");
    }

    [Theory]
    [InlineData("R&&G Ltd", "R&G Ltd")]
    [InlineData("Black && Gold", "Black & Gold")]
    [InlineData("Sales &&Growth", "Sales &Growth")]
    public void HasPictureToken_AgreesWithWhatTheTokenizerActuallyRenders(string raw, string rendered)
    {
        // The property the substring test violated: the escaped ampersand survives into the rendered
        // text as an ordinary character, so there is no token left for a picture to hang off.
        PagePrintTextPlanner.ExpandHeaderFooterText(
                raw, pageNumber: 1, totalPages: 1, workbookName: "Book.xlsx", sheetName: "Sheet1",
                new DateTime(2026, 8, 30))
            .Should().Be(rendered);

        PagePrintTextPlanner.HasPictureToken(raw).Should().BeFalse();
    }

    [Fact]
    public void PrunePicturesWithoutTokens_SectionWithAnEscapedAmpersand_DropsThePictureNothingWillDraw()
    {
        // The editor's own copy of the detection has to agree with the renderers', or an escaped
        // ampersand leaves an invisible picture attached to the section -- kept by the editor, saved
        // into the file, drawn by nobody.
        var picture = new WorksheetHeaderFooterPicture([1], "image/png", "logo.png", Width: 96, Height: 48);

        var pruned = HeaderFooterEditorPlanner.PrunePicturesWithoutTokens(
            new WorksheetHeaderFooter("R&&G Ltd", "&G", ""),
            new WorksheetHeaderFooterPictureSet(picture, picture, null));

        pruned.Left.Should().BeNull("\"R&&G Ltd\" renders as \"R&G Ltd\" -- there is no picture token in it");
        pruned.Center.Should().BeSameAs(picture, "the real token in the center section keeps its picture");
    }

    [Fact]
    public void ResolveLineHeight_SectionWithAnEscapedAmpersand_DoesNotGrowTheBandForItsPicture()
    {
        // The user-visible consequence in the WPF-shared geometry: a section whose text merely
        // contains "&&G" must keep its ordinary one-line band instead of ballooning to the height of
        // a picture it never asked to show.
        var picture = new WorksheetHeaderFooterPicture([1], "image/png", "logo.png", Width: 96, Height: 200);

        WorksheetPrintHeaderFooterGeometryPlanner.ResolveLineHeight(
                new WorksheetHeaderFooter("R&&G Ltd", "", ""),
                new WorksheetHeaderFooterPictureSet(picture, null, null),
                draftQuality: false,
                fontScale: 1.0,
                baseLineHeight: 18.0,
                sizeToContent: true)
            .Should().Be(18.0);
    }

    [Fact]
    public void ResolveLineHeight_SectionWithARealPictureToken_StillGrowsTheBand()
    {
        // No-regression sibling: the real token must keep working exactly as before.
        var picture = new WorksheetHeaderFooterPicture([1], "image/png", "logo.png", Width: 96, Height: 200);

        WorksheetPrintHeaderFooterGeometryPlanner.ResolveLineHeight(
                new WorksheetHeaderFooter("R&G Ltd", "", ""),
                new WorksheetHeaderFooterPictureSet(picture, null, null),
                draftQuality: false,
                fontScale: 1.0,
                baseLineHeight: 18.0,
                sizeToContent: true)
            .Should().Be(200.0);
    }
}
