using FluentAssertions;
using FreeX.ParityCompare.Core;

namespace FreeX.ParityCompare.Tests;

public class PngCodecTests
{
    [Fact]
    public void Encode_then_decode_round_trips_pixels()
    {
        var img = PixelImage.Solid(7, 5, b: 10, g: 20, r: 30, a: 255);
        // make a couple of distinct pixels so we test more than a flat fill
        img.Pixels[0] = 99; img.Pixels[1] = 88; img.Pixels[2] = 77;

        var bytes = PngCodec.Encode(img);
        var decoded = PngCodec.Decode(bytes);

        decoded.Width.Should().Be(7);
        decoded.Height.Should().Be(5);
        decoded.Pixels.Should().Equal(img.Pixels);
    }

    [Fact]
    public void Decode_rejects_non_png()
    {
        Action act = () => PngCodec.Decode(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 });
        act.Should().Throw<FormatException>();
    }

    [Fact]
    public void Decoded_image_has_expected_alpha()
    {
        var img = PixelImage.Solid(3, 3, 0, 0, 0, 128);
        var decoded = PngCodec.Decode(PngCodec.Encode(img));
        decoded.Pixels[3].Should().Be(128);
    }
}
