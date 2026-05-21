using FluentAssertions;
using Freexcel.App.UI;

namespace Freexcel.App.UI.Tests;

public sealed class WpfBitmapImageLoaderTests
{
    [Fact]
    public void TryLoad_ReturnsFalseForNullOrEmptyBytes()
    {
        WpfBitmapImageLoader.TryLoad(null, out var nullImage).Should().BeFalse();
        nullImage.Should().BeNull();

        WpfBitmapImageLoader.TryLoad([], out var emptyImage).Should().BeFalse();
        emptyImage.Should().BeNull();
    }

    [Fact]
    public void TryLoad_ReturnsFalseForInvalidImageBytes()
    {
        WpfBitmapImageLoader.TryLoad([1, 2, 3, 4], out var image).Should().BeFalse();
        image.Should().BeNull();
    }
}
