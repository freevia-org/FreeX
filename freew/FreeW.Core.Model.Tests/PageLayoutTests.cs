namespace FreeW.Core.Model.Tests;

public class PageLayoutTests
{
    private sealed class CommandContext(TextDocument document) : IDocumentCommandContext
    {
        public TextDocument Document { get; } = document;
    }

    [Fact]
    public void PointsToDip_ConvertsAt96Over72()
    {
        // 72 points = 1 inch = 96 DIP.
        PageLayout.PointsToDip(72).Should().BeApproximately(96, 0.0001);
        PageLayout.PointsToDip(0).Should().Be(0);
    }

    [Fact]
    public void PageSizeDip_ScalesLetterToDip()
    {
        var page = new PageSettings(); // 612 x 792 pt = 8.5" x 11"

        var (width, height) = PageLayout.PageSizeDip(page);

        width.Should().BeApproximately(816, 0.0001);  // 8.5 * 96
        height.Should().BeApproximately(1056, 0.0001); // 11 * 96
    }

    [Fact]
    public void ContentAreaDip_SubtractsMargins()
    {
        var page = new PageSettings(); // 1in margins all round

        var (width, height) = PageLayout.ContentAreaDip(page);

        width.Should().BeApproximately(816 - 192, 0.0001);  // page - left - right (96 each)
        height.Should().BeApproximately(1056 - 192, 0.0001); // page - top - bottom
    }

    [Fact]
    public void MarginsDip_AddsGutterToConfiguredBindingEdge()
    {
        var side = new PageSettings { GutterPt = 18 };
        var top = new PageSettings { GutterPt = 18, GutterAtTop = true };

        PageLayout.MarginsDip(side).Should().Be((120, 96, 96, 96));
        PageLayout.MarginsDip(top).Should().Be((96, 120, 96, 96));
        PageLayout.ContentAreaDip(side).Should().Be((600, 864));
        PageLayout.ContentAreaDip(top).Should().Be((624, 840));
    }

    [Fact]
    public void MarginsDip_MirrorMarginsOwnsGutterEdgeAcrossPages()
    {
        var page = new PageSettings
        {
            GutterPt = 18,
            GutterAtTop = true,
            MirrorMargins = true,
        };

        PageLayout.MarginsDip(page, pageIndex: 0).Should().Be((120, 96, 96, 96));
        PageLayout.MarginsDip(page, pageIndex: 1).Should().Be((96, 96, 120, 96));
    }

    [Fact]
    public void ContentAreaDip_ClampsToZeroWhenMarginsExceedPage()
    {
        var page = new PageSettings
        {
            WidthPt = 100, HeightPt = 100,
            MarginLeftPt = 80, MarginRightPt = 80,
            MarginTopPt = 10, MarginBottomPt = 10
        };

        var (width, height) = PageLayout.ContentAreaDip(page);

        width.Should().Be(0); // 100 - 80 - 80 clamps to 0 instead of going negative
        height.Should().BeGreaterThan(0); // 100 - 10 - 10 still positive
    }

    [Fact]
    public void PageCount_IsOneWhenContentFits()
    {
        var page = new PageSettings();
        var (_, contentHeight) = PageLayout.ContentAreaDip(page);

        PageLayout.PageCount(page, contentHeight).Should().Be(1);
        PageLayout.PageCount(page, 0).Should().Be(1);
    }

    [Fact]
    public void PageCount_RoundsUpForOverflowingContent()
    {
        var page = new PageSettings();
        var (_, contentHeight) = PageLayout.ContentAreaDip(page);

        // Just over two pages of content -> three pages.
        PageLayout.PageCount(page, contentHeight * 2 + 1).Should().Be(3);
        // Exactly two pages -> two pages.
        PageLayout.PageCount(page, contentHeight * 2).Should().Be(2);
    }

    [Fact]
    public void PageCount_ReturnsOneForDegenerateContentArea()
    {
        var page = new PageSettings { WidthPt = 100, HeightPt = 100, MarginTopPt = 60, MarginBottomPt = 60 };

        PageLayout.PageCount(page, 5000).Should().Be(1);
    }

    [Fact]
    public void DifferentOddEvenPagesAndBackground_DefaultToOffAndNull()
    {
        var page = new PageSettings();

        page.DifferentOddEvenPages.Should().BeFalse();
        page.BackgroundColorHex.Should().BeNull();
    }

    [Fact]
    public void Clone_CopiesDifferentOddEvenPagesAndBackground()
    {
        var page = new PageSettings
        {
            DifferentOddEvenPages = true,
            BackgroundColorHex = "#FFEEDD"
        };

        var clone = page.Clone();

        clone.DifferentOddEvenPages.Should().BeTrue();
        clone.BackgroundColorHex.Should().Be("#FFEEDD");

        // The clone is independent of the source.
        clone.DifferentOddEvenPages = false;
        clone.BackgroundColorHex = null;
        page.DifferentOddEvenPages.Should().BeTrue();
        page.BackgroundColorHex.Should().Be("#FFEEDD");
    }

    [Fact]
    public void SetPageSettingsCommand_CopiesAndRestoresPictureWatermarkOptions()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Page.WatermarkOptions = new WatermarkOptions("OLD")
        {
            Opacity = 0.3,
            ImageBytes = [9, 9, 9],
            ScalePct = 30
        };
        var settings = doc.Page.Clone();
        settings.WatermarkOptions = new WatermarkOptions(string.Empty)
        {
            ImageBytes = [1, 2, 3, 4],
            ScalePct = 55,
            Opacity = 0.45,
            Layout = WatermarkLayout.Horizontal
        };
        var command = new SetPageSettingsCommand(settings);
        var context = new CommandContext(doc);

        command.Apply(context);

        doc.Page.WatermarkOptions.Should().NotBeNull();
        doc.Page.WatermarkOptions.Should().NotBeSameAs(settings.WatermarkOptions);
        doc.Page.WatermarkOptions!.ImageBytes.Should().Equal(1, 2, 3, 4);
        doc.Page.WatermarkOptions.ImageBytes.Should().NotBeSameAs(settings.WatermarkOptions!.ImageBytes);
        doc.Page.WatermarkOptions.ScalePct.Should().Be(55);
        doc.Page.WatermarkOptions.Layout.Should().Be(WatermarkLayout.Horizontal);

        command.Revert(context);

        doc.Page.WatermarkOptions.Should().NotBeNull();
        doc.Page.WatermarkOptions!.Text.Should().Be("OLD");
        doc.Page.WatermarkOptions.ImageBytes.Should().Equal(9, 9, 9);
        doc.Page.WatermarkOptions.ScalePct.Should().Be(30);
    }

    [Fact]
    public void SetPageSettingsCommand_CopiesAndRestoresPageNumbering()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Page.PageNumberFormat = PageNumberFormat.Decimal;
        doc.Page.PageNumberStartAt = null;
        doc.Page.PageNumberChapterStyleLevel = null;
        doc.Page.PageNumberChapterSeparator = PageNumberChapterSeparator.Period;
        var settings = doc.Page.Clone();
        settings.PageNumberFormat = PageNumberFormat.UpperRoman;
        settings.PageNumberStartAt = 4;
        settings.PageNumberChapterStyleLevel = 2;
        settings.PageNumberChapterSeparator = PageNumberChapterSeparator.Colon;
        var command = new SetPageSettingsCommand(settings);
        var context = new CommandContext(doc);

        command.Apply(context);

        doc.Page.PageNumberFormat.Should().Be(PageNumberFormat.UpperRoman);
        doc.Page.PageNumberStartAt.Should().Be(4);
        doc.Page.PageNumberChapterStyleLevel.Should().Be(2);
        doc.Page.PageNumberChapterSeparator.Should().Be(PageNumberChapterSeparator.Colon);

        command.Revert(context);

        doc.Page.PageNumberFormat.Should().Be(PageNumberFormat.Decimal);
        doc.Page.PageNumberStartAt.Should().BeNull();
        doc.Page.PageNumberChapterStyleLevel.Should().BeNull();
        doc.Page.PageNumberChapterSeparator.Should().Be(PageNumberChapterSeparator.Period);
    }

    [Fact]
    public void EvenHeaderFooter_DefaultToNull()
    {
        var doc = new TextDocument();

        doc.EvenHeader.Should().BeNull();
        doc.EvenFooter.Should().BeNull();
    }
}
