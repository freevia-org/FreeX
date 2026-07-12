using FluentAssertions;
using FreeX.App.Presentation.Charts;

namespace FreeX.App.Presentation.Tests.Charts;

/// <summary>
/// R34-chart-render-pixel-3-3: the Avalonia chart renderer used to draw the chart title at a fixed
/// 4px-from-top offset regardless of how tall the title actually rendered, so a large title font (or
/// a small chart box) overlapped the plot area's top edge. <see cref="ChartTitleFit.ResolveFittingFontSize"/>
/// is the extracted pure sizing math the renderer now uses to shrink the title font just enough to
/// keep its box within the space already reserved above the plot area (never growing it, never
/// letting it collide with the plot).
/// </summary>
public sealed class ChartTitleFitTests
{
    [Fact]
    public void Title_that_already_fits_keeps_its_natural_font_size()
    {
        // Sibling "already-working" case: default 16pt title measuring ~20px tall comfortably fits
        // inside the default ~24px reserve (28px inset minus 4px top margin) — must be unchanged.
        var fontSize = ChartTitleFit.ResolveFittingFontSize(naturalFontSize: 16, naturalHeight: 20, availableHeight: 24);

        fontSize.Should().Be(16);
    }

    [Fact]
    public void Oversized_title_font_is_shrunk_to_fit_the_available_height()
    {
        // Failure scenario: a 24pt title ("Format Chart Title" font size 24) renders ~30px tall but
        // only 24px is reserved above the plot — the font must shrink so the box fits exactly.
        var fontSize = ChartTitleFit.ResolveFittingFontSize(naturalFontSize: 24, naturalHeight: 30, availableHeight: 24);

        fontSize.Should().BeApproximately(19.2, 1e-9); // 24 * (24/30)
        fontSize.Should().BeLessThan(24);
    }

    [Fact]
    public void Shrinking_never_goes_below_the_minimum_floor_on_a_tiny_chart_box()
    {
        // A very small chart box (tiny available height) must still shrink to the floor, not to
        // zero/negative, so the title never fully disappears.
        var fontSize = ChartTitleFit.ResolveFittingFontSize(naturalFontSize: 24, naturalHeight: 30, availableHeight: 1);

        fontSize.Should().Be(ChartTitleFit.MinimumFontSize);
    }

    [Fact]
    public void Zero_available_height_falls_back_to_the_minimum_floor_without_throwing()
    {
        var fontSize = ChartTitleFit.ResolveFittingFontSize(naturalFontSize: 16, naturalHeight: 20, availableHeight: 0);

        fontSize.Should().Be(ChartTitleFit.MinimumFontSize);
    }

    [Fact]
    public void Title_exactly_matching_available_height_is_not_shrunk()
    {
        var fontSize = ChartTitleFit.ResolveFittingFontSize(naturalFontSize: 16, naturalHeight: 24, availableHeight: 24);

        fontSize.Should().Be(16);
    }
}
