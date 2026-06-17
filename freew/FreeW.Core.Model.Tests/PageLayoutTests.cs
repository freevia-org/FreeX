namespace FreeW.Core.Model.Tests;

public class PageLayoutTests
{
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
    public void EvenHeaderFooter_DefaultToNull()
    {
        var doc = new TextDocument();

        doc.EvenHeader.Should().BeNull();
        doc.EvenFooter.Should().BeNull();
    }
}
