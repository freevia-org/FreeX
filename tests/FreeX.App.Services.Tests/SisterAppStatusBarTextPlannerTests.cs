using FluentAssertions;

namespace FreeX.App.Services.Tests;

public sealed class SisterAppStatusBarTextPlannerTests
{
    [Fact]
    public void DocumentStatusFormatters_MatchWordStyleStatusReadouts()
    {
        SisterAppStatusBarTextPlanner.FormatDocumentPageStatus(2, 7)
            .Should().Be("Page 2 of 7");
        SisterAppStatusBarTextPlanner.FormatDocumentSectionStatus(1, 3)
            .Should().Be("Section 1 of 3");
        SisterAppStatusBarTextPlanner.FormatDocumentSelectionStatus(12, 90)
            .Should().Be("Selection: 12 words, 90 characters");
        SisterAppStatusBarTextPlanner.FormatDocumentCountsStatus(25, 140, 4)
            .Should().Be("Words: 25   Characters: 140   Paragraphs: 4");
        SisterAppStatusBarTextPlanner.FormatDataFolderStatus("%LOCALAPPDATA%\\FreeW")
            .Should().Be("Data folder: %LOCALAPPDATA%\\FreeW");
    }

    [Fact]
    public void DocumentSummaryStatus_AppendsPageAndEditedStateWhenPresent()
    {
        var status = SisterAppStatusBarTextPlanner.FormatDocumentSummaryStatus(
            words: 5,
            characters: 22,
            paragraphs: 2,
            pageStatus: "Page 1 of 3",
            isEdited: true);

        status.Should().Be("Page 1 of 3   5 words   22 characters   2 paragraphs   \u2022 edited");
    }

    [Theory]
    [InlineData(0, 3, "", "Slide 1 / 3")]
    [InlineData(2, 3, "portable-data", "Slide 3 / 3   portable-data")]
    [InlineData(4, 3, "", "Slide 3 / 3")]
    [InlineData(0, 0, "", "No slides")]
    public void PresentationSlideStatus_ClampsSelectionAndAppendsTrailingStatus(
        int currentSlideIndex,
        int slideCount,
        string trailingStatus,
        string expected)
    {
        SisterAppStatusBarTextPlanner.FormatPresentationSlideStatus(
                currentSlideIndex,
                slideCount,
                trailingStatus)
            .Should().Be(expected);
    }

    [Fact]
    public void ChromeDefaults_KeepSisterAppStatusBarsAlignedAcrossRenderers()
    {
        SisterAppStatusBarChromeDefaults.Height.Should().Be(26);
        SisterAppStatusBarChromeDefaults.TextFontSize.Should().Be(12);
        SisterAppStatusBarChromeDefaults.SeparatorWidth.Should().Be(1);
        SisterAppStatusBarChromeDefaults.SeparatorAlpha.Should().Be(0x66);
        SisterAppStatusBarChromeDefaults.SeparatorHorizontalMargin.Should().Be(8);
        SisterAppStatusBarChromeDefaults.SeparatorVerticalMargin.Should().Be(3);
    }
}
