using System.IO.Compression;
using System.Text;
using FluentAssertions;
using FreeX.ParityCompare.Core;

namespace FreeX.ParityCompare.Tests;

/// <summary>
/// r164 remediation, unbounded declared quantity -- tools tier. Two shapes in the parity harness's
/// own PNG decoder, both measured:
///
/// A canvas size is a COUNT the file declares, and every buffer here multiplies width by height. A
/// tiny file declaring 40000x60000 overflowed int and surfaced as a bare OverflowException out of
/// <c>new byte[negative]</c> -- an opaque failure mid-comparison rather than "this PNG is malformed".
///
/// The IDAT inflate was bounded by nothing in the file at all: a PNG declaring a 1x1 canvas whose
/// IDAT expands to 256 MB inflated the whole thing (1,025 MB allocated, against ~258 MB for building
/// the test payload itself). This is developer tooling on inputs we generate ourselves, so the
/// severity is a confusing crash rather than a user-facing bug -- but the decoder runs inside the
/// DefaultTests gate, where a truncated or corrupt capture should fail with a diagnosis.
/// </summary>
public sealed class R164_PngCodecMalformedInputTests
{
    [Theory]
    [InlineData(65535u, 65535u)]
    [InlineData(40000u, 60000u)]
    public void Decode_CanvasBeyondTheSupportedSize_ReportsAMalformedPngInsteadOfOverflowing(uint width, uint height)
    {
        var png = BuildPng(width, height, idatPayloadBytes: 64);

        var act = () => PngCodec.Decode(png);

        act.Should().Throw<FormatException>().WithMessage("*pixel limit*");
    }

    [Fact]
    public void Decode_IdatInflatingPastTheDeclaredCanvas_StopsInsteadOfInflatingItAll()
    {
        // 1x1 canvas holds 4 raw bytes; this IDAT expands to 8 MB. Small enough to keep the test
        // quick, and the decoder must refuse it the same way it refuses the 256 MB version.
        var png = BuildPng(1, 1, idatPayloadBytes: 8 * 1024 * 1024);

        var act = () => PngCodec.Decode(png);

        act.Should().Throw<FormatException>().WithMessage("*inflates past*");
    }

    [Fact]
    public void Decode_AnOrdinaryCapture_StillDecodes()
    {
        // Sibling/no-regression: the bound is exactly what the declared geometry holds, so a
        // well-formed image is unaffected. Every real capture in the repository (ribbon screenshots
        // and parity evidence, 16 sampled) still decodes with these guards in place.
        var png = BuildPng(4, 4, idatPayloadBytes: 4 * ((4 * 3) + 1));

        var image = PngCodec.Decode(png);

        image.Width.Should().Be(4);
        image.Height.Should().Be(4);
    }

    private static byte[] BuildPng(uint width, uint height, int idatPayloadBytes)
    {
        using var stream = new MemoryStream();
        stream.Write([0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A]);

        var header = new byte[13];
        WriteUInt32(header, 0, width);
        WriteUInt32(header, 4, height);
        header[8] = 8; // bit depth
        header[9] = 2; // colour type: truecolour
        WriteChunk(stream, "IHDR", header);

        using var deflated = new MemoryStream();
        using (var zlib = new ZLibStream(deflated, CompressionLevel.Optimal, leaveOpen: true))
            zlib.Write(new byte[idatPayloadBytes]);
        WriteChunk(stream, "IDAT", deflated.ToArray());
        WriteChunk(stream, "IEND", []);
        return stream.ToArray();
    }

    private static void WriteUInt32(byte[] buffer, int offset, uint value)
    {
        buffer[offset] = (byte)(value >> 24);
        buffer[offset + 1] = (byte)(value >> 16);
        buffer[offset + 2] = (byte)(value >> 8);
        buffer[offset + 3] = (byte)value;
    }

    private static void WriteChunk(Stream target, string type, byte[] data)
    {
        var length = new byte[4];
        WriteUInt32(length, 0, (uint)data.Length);
        target.Write(length);
        target.Write(Encoding.ASCII.GetBytes(type));
        target.Write(data);
        target.Write(new byte[4]); // CRC: this decoder does not verify it.
    }
}
