using FluentAssertions;
using FreeP.App.Compositor;
using Xunit;

namespace FreeP.App.Compositor.Tests;

public sealed class RotationOptionsPlannerTests
{
    [Theory]
    [InlineData("45", 45)]
    [InlineData("-90", 270)]
    [InlineData("360", 0)]
    public void TryParse_NormalizesValidAngles(string text, double expected)
    {
        RotationOptionsPlanner.TryParse(text, out var degrees).Should().BeTrue();
        degrees.Should().Be(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("361")]
    [InlineData("-361")]
    [InlineData("not an angle")]
    public void TryParse_RejectsInvalidAngles(string text)
    {
        RotationOptionsPlanner.TryParse(text, out _).Should().BeFalse();
    }
}
