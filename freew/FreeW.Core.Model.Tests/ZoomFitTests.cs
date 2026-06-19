namespace FreeW.Core.Model.Tests;

public class ZoomFitTests
{
    [Fact]
    public void PageWidth_FitsFullPageWidthIntoViewport()
    {
        // 816 DIP page (US Letter, 612pt) in a 408 DIP viewport → half size → 50%.
        ZoomFit.PageWidth(pageWidthDip: 816, viewportWidthDip: 408).Should().BeApproximately(0.5, 1e-9);
    }

    [Fact]
    public void PageWidth_ClampsWideViewportToMax()
    {
        // A viewport far wider than the page would zoom past 200%; clamps to Max.
        ZoomFit.PageWidth(pageWidthDip: 400, viewportWidthDip: 4000).Should().Be(ZoomLevels.Max);
    }

    [Fact]
    public void PageWidth_ClampsNarrowViewportToMin()
    {
        ZoomFit.PageWidth(pageWidthDip: 816, viewportWidthDip: 100).Should().Be(ZoomLevels.Min);
    }

    [Fact]
    public void PageWidth_DegenerateInputFallsBackToDefault()
    {
        ZoomFit.PageWidth(pageWidthDip: 816, viewportWidthDip: 0).Should().Be(ZoomLevels.Default);
        ZoomFit.PageWidth(pageWidthDip: 0, viewportWidthDip: 408).Should().Be(ZoomLevels.Default);
    }

    [Fact]
    public void TextWidth_FitsContentColumn_AndZoomsFurtherThanPageWidth()
    {
        // Same viewport, but the narrower text column (page minus margins) yields a larger factor than the
        // full page width — matching Word, where Text width zooms in further than Page width.
        var pageWidthFit = ZoomFit.PageWidth(pageWidthDip: 816, viewportWidthDip: 612);
        var textWidthFit = ZoomFit.TextWidth(contentWidthDip: 612, viewportWidthDip: 612);
        textWidthFit.Should().BeGreaterThan(pageWidthFit);
        textWidthFit.Should().BeApproximately(1.0, 1e-9);
    }

    [Fact]
    public void WholePage_TakesSmallerOfWidthAndHeightFit()
    {
        // Width-fit would be 2.0 (clamped) but height is the binding constraint: 1056 DIP page in a 528 DIP
        // tall viewport → 50%. The whole page must be visible, so the smaller factor wins.
        ZoomFit.WholePage(pageWidthDip: 816, pageHeightDip: 1056, viewportWidthDip: 5000, viewportHeightDip: 528)
            .Should().BeApproximately(0.5, 1e-9);
    }

    [Fact]
    public void WholePage_WidthCanBeTheBindingConstraint()
    {
        // Tall, narrow viewport: width is the limiting dimension.
        ZoomFit.WholePage(pageWidthDip: 816, pageHeightDip: 1056, viewportWidthDip: 408, viewportHeightDip: 5000)
            .Should().BeApproximately(0.5, 1e-9);
    }

    [Fact]
    public void WholePage_DegenerateInputsFallBackToDefault()
    {
        ZoomFit.WholePage(pageWidthDip: 0, pageHeightDip: 0, viewportWidthDip: 0, viewportHeightDip: 0)
            .Should().Be(ZoomLevels.Default);
    }

    [Fact]
    public void WholePage_PartlyDegenerate_UsesTheMeasuredAxis()
    {
        // Height not yet measured (0) → only the width axis constrains the fit.
        ZoomFit.WholePage(pageWidthDip: 816, pageHeightDip: 1056, viewportWidthDip: 408, viewportHeightDip: 0)
            .Should().BeApproximately(0.5, 1e-9);
    }
}
