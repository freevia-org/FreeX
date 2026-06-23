using FluentAssertions;
using Free.Shared.AppServices;

namespace FreeX.App.Services.Tests;

public sealed class ResourceKeyStatusBarTextProviderTests
{
    [Theory]
    [InlineData(StatusBarReadoutKind.Average, StatusBarTextResourceKeys.Average, StatusBarTextResourceKeys.AverageFormat)]
    [InlineData(StatusBarReadoutKind.Count, StatusBarTextResourceKeys.Count, StatusBarTextResourceKeys.CountFormat)]
    [InlineData(StatusBarReadoutKind.NumericalCount, StatusBarTextResourceKeys.NumericalCount, StatusBarTextResourceKeys.NumericalCountFormat)]
    [InlineData(StatusBarReadoutKind.Sum, StatusBarTextResourceKeys.Sum, StatusBarTextResourceKeys.SumFormat)]
    [InlineData(StatusBarReadoutKind.Minimum, StatusBarTextResourceKeys.Minimum, StatusBarTextResourceKeys.MinimumFormat)]
    [InlineData(StatusBarReadoutKind.Maximum, StatusBarTextResourceKeys.Maximum, StatusBarTextResourceKeys.MaximumFormat)]
    public void GetReadoutText_ResolvesSharedResourceKeys(
        StatusBarReadoutKind kind,
        string labelKey,
        string formatKey)
    {
        var provider = new ResourceKeyStatusBarTextProvider(key => "text:" + key);

        provider.GetReadoutLabel(kind).Should().Be("text:" + labelKey);
        provider.GetReadoutFormat(kind).Should().Be("text:" + formatKey);
    }

    [Fact]
    public void RequiredKeys_HasNoDuplicates()
    {
        StatusBarTextResourceKeys.RequiredKeys
            .Should()
            .OnlyHaveUniqueItems();
    }
}
