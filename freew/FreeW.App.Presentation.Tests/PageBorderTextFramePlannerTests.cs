using FreeW.App.Presentation.DocumentView;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Tests;

public sealed class PageBorderTextFramePlannerTests
{
    [Theory]
    [InlineData(false, false, 29, 740)]
    [InlineData(true, false, 65, 704)]
    [InlineData(false, true, 29, 710)]
    [InlineData(true, true, 65, 674)]
    public void Build_UsesHeaderAndFooterExclusionReferencesIndependently(
        bool doNotSurroundHeader,
        bool doNotSurroundFooter,
        double expectedY,
        double expectedHeight)
    {
        var page = new PageSettings
        {
            MarginLeftPt = 72,
            MarginRightPt = 54,
            MarginTopPt = 72,
            MarginBottomPt = 60,
            HeaderDistancePt = 36,
            FooterDistancePt = 30,
        };
        var border = new PageBorder { SpacePt = 6 };

        var frame = PageBorderTextFramePlanner.Build(
            page,
            border,
            pageWidth: 612,
            pageHeight: 792,
            unitsPerPoint: 1,
            strokeRegistration: 1,
            doNotSurroundHeader,
            doNotSurroundFooter);

        frame.Should().Be(new PageBorderTextFrame(65, expectedY, 500, expectedHeight));
    }

    [Fact]
    public void Build_DefaultsMissingHeaderAndFooterDistancesToHalfAnInch()
    {
        var page = new PageSettings
        {
            MarginLeftPt = 72,
            MarginRightPt = 72,
            MarginTopPt = 90,
            MarginBottomPt = 90,
        };
        var border = new PageBorder { SpacePt = 6 };

        var frame = PageBorderTextFramePlanner.Build(
            page,
            border,
            pageWidth: 612,
            pageHeight: 792,
            unitsPerPoint: 1,
            strokeRegistration: 0,
            doNotSurroundHeader: false,
            doNotSurroundFooter: false);

        frame.Should().Be(new PageBorderTextFrame(66, 30, 480, 732));
    }
}
