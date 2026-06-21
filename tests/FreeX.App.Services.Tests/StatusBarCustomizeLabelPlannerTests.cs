using FluentAssertions;
using Free.Shared.AppServices;

namespace FreeX.App.Services.Tests;

public sealed class StatusBarCustomizeLabelPlannerTests
{
    [Theory]
    [InlineData(StatusBarCustomizeResourceKeys.CustomizeStatusBar, "Customize Status Bar")]
    [InlineData(StatusBarCustomizeResourceKeys.CellMode, "Cell Mode")]
    [InlineData(StatusBarCustomizeResourceKeys.EndMode, "End Mode")]
    [InlineData(StatusBarCustomizeResourceKeys.SelectionMode, "Selection Mode")]
    [InlineData(StatusBarCustomizeResourceKeys.PageNumber, "Page Number")]
    [InlineData(StatusBarCustomizeResourceKeys.Average, "Average")]
    [InlineData(StatusBarCustomizeResourceKeys.Count, "Count")]
    [InlineData(StatusBarCustomizeResourceKeys.NumericalCount, "Numerical Count")]
    [InlineData(StatusBarCustomizeResourceKeys.Minimum, "Minimum")]
    [InlineData(StatusBarCustomizeResourceKeys.Maximum, "Maximum")]
    [InlineData(StatusBarCustomizeResourceKeys.Sum, "Sum")]
    [InlineData(StatusBarCustomizeResourceKeys.ViewShortcuts, "View Shortcuts")]
    [InlineData(StatusBarCustomizeResourceKeys.Zoom, "Zoom")]
    [InlineData(StatusBarCustomizeResourceKeys.ZoomSlider, "Zoom Slider")]
    public void EnglishHeader_ReturnsSharedFallbackText(string resourceKey, string expected)
    {
        StatusBarCustomizeLabelPlanner.EnglishHeader(resourceKey).Should().Be(expected);
    }

    [Fact]
    public void EnglishHeader_UnknownKeyEchoesResourceKey()
    {
        StatusBarCustomizeLabelPlanner.EnglishHeader("StatusBar_Custom").Should().Be("StatusBar_Custom");
    }
}
