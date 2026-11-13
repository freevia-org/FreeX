using System.Buffers.Binary;
using Free.Shared.Drawing;

namespace Free.Shared.AppServices.Tests;

/// <summary>
/// <see cref="ImageHeader"/> is the single owner of image signature detection and pixel-size
/// reading. These use tiny in-memory headers: the reader must work on the header alone and must
/// never throw on truncated or malformed input.
/// </summary>
public sealed class ImageHeaderTests
{
    private static byte[] Png(int width, int height)
    {
        var bytes = new byte[33];
        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }.CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(8), 13);
        "IHDR"u8.CopyTo(bytes.AsSpan(12));
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(16), (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(20), (uint)height);
        bytes[24] = 8;
        bytes[25] = 6;
        return bytes;
    }

    private static byte[] Gif(int width, int height, string version = "89a")
    {
        var bytes = new byte[13];
        System.Text.Encoding.ASCII.GetBytes("GIF" + version).CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(6), (ushort)width);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(8), (ushort)height);
        return bytes;
    }

    private static byte[] Bmp(int width, int height, uint headerSize = 40)
    {
        var bytes = new byte[54];
        bytes[0] = (byte)'B';
        bytes[1] = (byte)'M';
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(14), headerSize);
        if (headerSize == 12)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(18), (ushort)width);
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(20), (ushort)height);
        }
        else
        {
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(18), width);
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(22), height);
        }

        return bytes;
    }

    /// <summary>SOI, optional leading segments, then a start-of-frame with the given marker.</summary>
    private static byte[] Jpeg(int width, int height, byte sofMarker = 0xC0, int precision = 8, int components = 3,
        bool leadingApp = false)
    {
        var stream = new List<byte> { 0xFF, 0xD8 };
        if (leadingApp)
        {
            // APP1 "Exif" (length 8 includes the two length bytes) then a COM segment.
            stream.AddRange(new byte[] { 0xFF, 0xE1, 0x00, 0x08, (byte)'E', (byte)'x', (byte)'i', (byte)'f', 0x00, 0x00 });
            stream.AddRange(new byte[] { 0xFF, 0xFE, 0x00, 0x04, (byte)'h', (byte)'i' });
        }

        var length = 8 + 3 * components;
        stream.AddRange(new byte[] { 0xFF, sofMarker, (byte)(length >> 8), (byte)length, (byte)precision });
        stream.AddRange(new[] { (byte)(height >> 8), (byte)height, (byte)(width >> 8), (byte)width, (byte)components });
        for (var i = 0; i < components; i++)
            stream.AddRange(new byte[] { (byte)(i + 1), 0x11, 0x00 });
        stream.AddRange(new byte[] { 0xFF, 0xDA, 0x00, 0x02, 0xFF, 0xD9 });
        return [.. stream];
    }

    [Fact]
    public void Detect_RecognisesEveryFormat()
    {
        ImageHeader.Detect(Png(1, 1)).Should().Be(ImageHeaderFormat.Png);
        ImageHeader.Detect(Jpeg(1, 1)).Should().Be(ImageHeaderFormat.Jpeg);
        ImageHeader.Detect(Gif(1, 1)).Should().Be(ImageHeaderFormat.Gif);
        ImageHeader.Detect(Bmp(1, 1)).Should().Be(ImageHeaderFormat.Bmp);
        ImageHeader.Detect([0x49, 0x49, 0x2A, 0x00, 0x08]).Should().Be(ImageHeaderFormat.Tiff);
        ImageHeader.Detect([0x4D, 0x4D, 0x00, 0x2A, 0x00]).Should().Be(ImageHeaderFormat.Tiff);
        ImageHeader.Detect([0xD7, 0xCD, 0xC6, 0x9A, 0x00]).Should().Be(ImageHeaderFormat.Wmf);
        ImageHeader.Detect([0x01, 0x00, 0x09, 0x00, 0x00]).Should().Be(ImageHeaderFormat.Wmf);
        ImageHeader.Detect([0x02, 0x00, 0x09, 0x00, 0x00]).Should().Be(ImageHeaderFormat.Wmf);

        var emf = new byte[48];
        emf[0] = 0x01;
        " EMF"u8.CopyTo(emf.AsSpan(40));
        ImageHeader.Detect(emf).Should().Be(ImageHeaderFormat.Emf);
    }

    [Fact]
    public void Detect_RequiresTheEmfMarkerAtOffsetFortyNotJustTheRecordType()
    {
        var notEmf = new byte[48];
        notEmf[0] = 0x01;
        ImageHeader.Detect(notEmf).Should().Be(ImageHeaderFormat.Unknown);
        ImageHeader.Detect([0x01, 0x00, 0x00, 0x00]).Should().Be(ImageHeaderFormat.Unknown);
    }

    [Theory]
    [InlineData(new byte[] { })]
    [InlineData(new byte[] { 0x89 })]
    [InlineData(new byte[] { 0x89, 0x50, 0x4E })] // truncated PNG magic
    [InlineData(new byte[] { 0xFF, 0xD8 })]       // SOI only: not a JPEG signature (needs FF D8 FF)
    [InlineData(new byte[] { 0x00, 0x01, 0x02, 0x03, 0x04 })]
    [InlineData(new byte[] { 0x3C, 0x73, 0x76, 0x67 })] // "<svg"
    public void Detect_ReturnsUnknownForShortOrWrongMagic(byte[] bytes)
    {
        ImageHeader.Detect(bytes).Should().Be(ImageHeaderFormat.Unknown);
    }

    [Fact]
    public void HasPngSignature_NeedsAllEightBytes()
    {
        ImageHeader.HasPngSignature(Png(1, 1)).Should().BeTrue();
        ImageHeader.HasPngSignature(Png(1, 1).AsSpan(0, 7)).Should().BeFalse();
        var altered = Png(1, 1);
        altered[4] = 0x00;
        ImageHeader.HasPngSignature(altered).Should().BeFalse();
    }

    [Fact]
    public void TryReadPixelSize_ReadsEveryFormat()
    {
        ImageHeader.TryReadPixelSize(Png(640, 480), out var w, out var h).Should().BeTrue();
        (w, h).Should().Be((640, 480));

        ImageHeader.TryReadPixelSize(Gif(32, 16, "87a"), out w, out h).Should().BeTrue();
        (w, h).Should().Be((32, 16));
        ImageHeader.TryReadPixelSize(Gif(300, 200), out w, out h).Should().BeTrue();
        (w, h).Should().Be((300, 200));

        ImageHeader.TryReadPixelSize(Bmp(100, 50), out w, out h).Should().BeTrue();
        (w, h).Should().Be((100, 50));
        ImageHeader.TryReadPixelSize(Bmp(100, 50, headerSize: 12), out w, out h).Should().BeTrue();
        (w, h).Should().Be((100, 50));
        ImageHeader.TryReadPixelSize(Bmp(100, -50), out w, out h).Should().BeTrue();
        (w, h).Should().Be((100, 50), "a negative height is a top-down bitmap");

        ImageHeader.TryReadPixelSize(Jpeg(800, 600), out w, out h).Should().BeTrue();
        (w, h).Should().Be((800, 600));
    }

    [Fact]
    public void TryReadPixelSize_SkipsLeadingAppAndExifSegmentsInAJpeg()
    {
        ImageHeader.TryReadPixelSize(Jpeg(1024, 768, leadingApp: true), out var w, out var h).Should().BeTrue();
        (w, h).Should().Be((1024, 768));
    }

    [Theory]
    [InlineData(0xC0)] // baseline
    [InlineData(0xC1)] // extended sequential
    [InlineData(0xC2)] // progressive
    [InlineData(0xC3)] // lossless
    [InlineData(0xC5)]
    [InlineData(0xC6)]
    [InlineData(0xC7)]
    [InlineData(0xC9)]
    [InlineData(0xCA)]
    [InlineData(0xCB)]
    [InlineData(0xCD)]
    [InlineData(0xCE)]
    [InlineData(0xCF)]
    public void TryReadJpegSize_HandlesEveryStartOfFrameMarker(int marker)
    {
        ImageHeader.TryReadJpegSize(Jpeg(123, 45, (byte)marker), out var w, out var h).Should().BeTrue();
        (w, h).Should().Be((123, 45));
    }

    [Theory]
    [InlineData(0xC4)] // DHT
    [InlineData(0xC8)] // JPG
    [InlineData(0xCC)] // DAC
    public void TryReadJpegSize_DoesNotTreatTableMarkersAsFrames(int marker)
    {
        ImageHeader.TryReadJpegSize(Jpeg(123, 45, (byte)marker), out _, out _).Should().BeFalse();
    }

    [Fact]
    public void TryReadJpegFrame_ReportsPrecisionAndComponents()
    {
        ImageHeader.TryReadJpegFrame(Jpeg(10, 20, 0xC2, precision: 8, components: 1, leadingApp: true),
            out var w, out var h, out var precision, out var components).Should().BeTrue();
        (w, h, precision, components).Should().Be((10, 20, 8, 1));

        ImageHeader.TryReadJpegFrame(Jpeg(10, 20, precision: 12, components: 3),
            out _, out _, out precision, out components).Should().BeTrue();
        (precision, components).Should().Be((12, 3));
    }

    [Fact]
    public void TryReadJpegFrame_RequiresTheWholeFrameSegment()
    {
        var full = Jpeg(10, 20);
        // Cut inside the SOF payload: size alone is still readable, the frame is not.
        var truncated = full.AsSpan(0, 2 + 4 + 1 + 4 + 0);
        ImageHeader.TryReadJpegFrame(truncated, out _, out _, out _, out _).Should().BeFalse();
    }

    [Fact]
    public void TryReadPixelSize_ReturnsFalseOnTruncatedInputAtEveryLength()
    {
        var samples = new[] { Png(5, 5), Gif(5, 5), Bmp(5, 5), Jpeg(5, 5, leadingApp: true) };
        foreach (var sample in samples)
        {
            for (var length = 0; length < sample.Length; length++)
            {
                var act = () => ImageHeader.TryReadPixelSize(sample.AsSpan(0, length), out _, out _);
                act.Should().NotThrow($"a {length}-byte prefix of a {sample.Length}-byte image must not throw");
            }
        }

        ImageHeader.TryReadPixelSize(Png(5, 5).AsSpan(0, 23), out _, out _).Should().BeFalse();
        ImageHeader.TryReadPixelSize(Gif(5, 5).AsSpan(0, 9), out _, out _).Should().BeFalse();
        ImageHeader.TryReadPixelSize(Bmp(5, 5).AsSpan(0, 25), out _, out _).Should().BeFalse();
        ImageHeader.TryReadPixelSize(Jpeg(5, 5).AsSpan(0, 6), out _, out _).Should().BeFalse();
    }

    [Fact]
    public void TryReadPixelSize_RejectsWrongMagicAndZeroOrHugeDimensions()
    {
        var wrongMagic = Png(5, 5);
        wrongMagic[1] = 0x51;
        ImageHeader.TryReadPixelSize(wrongMagic, out _, out _).Should().BeFalse();

        var noIhdr = Png(5, 5);
        noIhdr[12] = (byte)'X';
        ImageHeader.TryReadPixelSize(noIhdr, out _, out _).Should().BeFalse();

        ImageHeader.TryReadPixelSize(Png(0, 5), out _, out _).Should().BeFalse();
        ImageHeader.TryReadPixelSize(Png(5, 0), out _, out _).Should().BeFalse();
        var hugeWidth = Png(5, 5);
        hugeWidth[16] = 0x80; // width 0x80000005 > int.MaxValue
        ImageHeader.TryReadPixelSize(hugeWidth, out _, out _).Should().BeFalse(
            "a dimension above int.MaxValue is not a usable size");
        ImageHeader.TryReadPixelSize(Gif(0, 5), out _, out _).Should().BeFalse();
        ImageHeader.TryReadPixelSize(Gif(5, 5, "88a"), out _, out _).Should().BeFalse();
        ImageHeader.TryReadPixelSize(Bmp(5, 5, headerSize: 20), out _, out _).Should().BeFalse();
        ImageHeader.TryReadPixelSize(Bmp(5, int.MinValue), out _, out _).Should().BeFalse();
        ImageHeader.TryReadPixelSize(Jpeg(0, 5), out _, out _).Should().BeFalse();
        ImageHeader.TryReadPixelSize([0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09], out _, out _)
            .Should().BeFalse();
    }

    [Fact]
    public void TryReadPixelSize_FailureLeavesZeroDimensions()
    {
        ImageHeader.TryReadPixelSize(Png(0, 7), out var w, out var h).Should().BeFalse();
        (w, h).Should().Be((0, 0));
    }

    [Fact]
    public void TryReadPngSize_IsPngOnly()
    {
        ImageHeader.TryReadPngSize(Png(9, 8), out var w, out var h).Should().BeTrue();
        (w, h).Should().Be((9, 8));
        ImageHeader.TryReadPngSize(Gif(9, 8), out _, out _).Should().BeFalse();
        ImageHeader.TryReadPngSize(Jpeg(9, 8), out _, out _).Should().BeFalse();
    }
}
