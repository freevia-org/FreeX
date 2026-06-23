using FluentAssertions;
using Free.Shared.AppServices;

namespace FreeX.App.Services.Tests;

public sealed class StatusBarCustomizeResourceKeysTests
{
    [Theory]
    [InlineData(StatusBarCustomizeResourceKeys.CustomizeStatusBar)]
    [InlineData(StatusBarCustomizeResourceKeys.CellMode)]
    [InlineData(StatusBarCustomizeResourceKeys.EndMode)]
    [InlineData(StatusBarCustomizeResourceKeys.SelectionMode)]
    [InlineData(StatusBarCustomizeResourceKeys.PageNumber)]
    [InlineData(StatusBarCustomizeResourceKeys.Average)]
    [InlineData(StatusBarCustomizeResourceKeys.Count)]
    [InlineData(StatusBarCustomizeResourceKeys.NumericalCount)]
    [InlineData(StatusBarCustomizeResourceKeys.Minimum)]
    [InlineData(StatusBarCustomizeResourceKeys.Maximum)]
    [InlineData(StatusBarCustomizeResourceKeys.Sum)]
    [InlineData(StatusBarCustomizeResourceKeys.ViewShortcuts)]
    [InlineData(StatusBarCustomizeResourceKeys.Zoom)]
    [InlineData(StatusBarCustomizeResourceKeys.ZoomSlider)]
    public void RequiredKeys_ContainsEveryCustomizeResourceKey(string resourceKey)
    {
        StatusBarCustomizeResourceKeys.RequiredKeys.Should().Contain(resourceKey);
    }

    [Fact]
    public void RequiredKeys_HasNoDuplicates()
    {
        StatusBarCustomizeResourceKeys.RequiredKeys
            .Should()
            .OnlyHaveUniqueItems();
    }
}
