using FluentAssertions;
using Free.Shared.AppServices;

namespace FreeX.App.Services.Tests;

public sealed class EnglishStatusBarTextProviderTests
{
    [Theory]
    [InlineData(StatusBarReadoutKind.Average, "Average", "Average: {0}")]
    [InlineData(StatusBarReadoutKind.Count, "Count", "Count: {0}")]
    [InlineData(StatusBarReadoutKind.NumericalCount, "Numerical Count", "Numerical Count: {0}")]
    [InlineData(StatusBarReadoutKind.Sum, "Sum", "Sum: {0}")]
    [InlineData(StatusBarReadoutKind.Minimum, "Minimum", "Min: {0}")]
    [InlineData(StatusBarReadoutKind.Maximum, "Maximum", "Max: {0}")]
    public void GetReadoutText_ReturnsEnglishStatusBarStrings(
        StatusBarReadoutKind kind,
        string expectedLabel,
        string expectedFormat)
    {
        EnglishStatusBarTextProvider.Instance.GetReadoutLabel(kind).Should().Be(expectedLabel);
        EnglishStatusBarTextProvider.Instance.GetReadoutFormat(kind).Should().Be(expectedFormat);
    }
}
