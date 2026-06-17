namespace FreeW.Core.Model.Tests;

/// <summary>
/// Unit coverage for the floating-image wrapping model (roadmap item X3): the new
/// <see cref="ImageWrapping"/> enum + position fields on <see cref="InlineImage"/> default so existing
/// inline images are unchanged, and <see cref="InlineImage.IsFloating"/> reflects the wrapping mode.
/// </summary>
public class ImageWrappingTests
{
    private static byte[] Png() => [0x89, 0x50, 0x4E, 0x47];

    [Fact]
    public void NewImage_DefaultsToInlineWithNeutralPosition()
    {
        var image = new InlineImage(Png(), widthPt: 100, heightPt: 50);

        image.Wrapping.Should().Be(ImageWrapping.Inline);
        image.IsFloating.Should().BeFalse();
        image.HorizontalOffsetPt.Should().Be(0);
        image.VerticalOffsetPt.Should().Be(0);
        image.HorizontalAnchor.Should().Be(HorizontalAnchor.Column);
        image.VerticalAnchor.Should().Be(VerticalAnchor.Paragraph);
    }

    [Theory]
    [InlineData(ImageWrapping.Square)]
    [InlineData(ImageWrapping.Tight)]
    [InlineData(ImageWrapping.TopAndBottom)]
    [InlineData(ImageWrapping.Behind)]
    [InlineData(ImageWrapping.InFront)]
    public void FloatingWrappingModes_AreFloating(ImageWrapping wrapping)
    {
        var image = new InlineImage(Png(), 60, 60) { Wrapping = wrapping };

        image.IsFloating.Should().BeTrue();
    }

    [Fact]
    public void Inline_IsNotFloating()
    {
        new InlineImage(Png(), 60, 60) { Wrapping = ImageWrapping.Inline }.IsFloating.Should().BeFalse();
    }

    [Fact]
    public void PositionFields_AreSettable()
    {
        var image = new InlineImage(Png(), 60, 60)
        {
            Wrapping = ImageWrapping.Square,
            HorizontalAnchor = HorizontalAnchor.Page,
            HorizontalOffsetPt = 72,
            VerticalAnchor = VerticalAnchor.Margin,
            VerticalOffsetPt = 36,
        };

        image.HorizontalAnchor.Should().Be(HorizontalAnchor.Page);
        image.HorizontalOffsetPt.Should().Be(72);
        image.VerticalAnchor.Should().Be(VerticalAnchor.Margin);
        image.VerticalOffsetPt.Should().Be(36);
    }

    [Fact]
    public void FromImage_PreservesWrapping()
    {
        var image = new InlineImage(Png(), 60, 60) { Wrapping = ImageWrapping.Behind };

        var run = Run.FromImage(image);

        run.Image.Should().BeSameAs(image);
        run.Image!.Wrapping.Should().Be(ImageWrapping.Behind);
    }
}
