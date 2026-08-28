using System.Buffers.Binary;
using System.IO.Compression;

namespace FreeX.ParityCompare.Core;

/// <summary>
/// A decoded image as a flat 32-bit BGRA pixel buffer (B,G,R,A per pixel, row-major).
/// </summary>
public sealed class PixelImage
{
    public int Width { get; }
    public int Height { get; }

    /// <summary>BGRA bytes, length = Width * Height * 4.</summary>
    public byte[] Pixels { get; }

    public PixelImage(int width, int height, byte[] pixels)
    {
        if (width <= 0 || height <= 0)
            throw new ArgumentException($"Invalid image size {width}x{height}");
        if (pixels.Length != width * height * 4)
            throw new ArgumentException("Pixel buffer length does not match dimensions");
        Width = width;
        Height = height;
        Pixels = pixels;
    }

    /// <summary>Create a solid-color image (BGRA).</summary>
    public static PixelImage Solid(int w, int h, byte b, byte g, byte r, byte a)
    {
        var px = new byte[w * h * 4];
        for (int i = 0; i < w * h; i++)
        {
            int o = i * 4;
            px[o] = b; px[o + 1] = g; px[o + 2] = r; px[o + 3] = a;
        }
        return new PixelImage(w, h, px);
    }
}

/// <summary>
/// Minimal, dependency-free PNG decoder/encoder for the parity tooling. Supports the
/// non-interlaced subset that the capture pipelines emit: 8-bit grayscale (with/without
/// alpha), 8-bit truecolor (RGB/RGBA), and 8-bit palette. Uses the in-box
/// <see cref="System.IO.Compression.ZLibStream"/> for IDAT inflate so no third-party
/// image package is needed (keeps this assembly portable for the test gate).
/// </summary>
public static class PngCodec
{
    private static readonly byte[] Signature = { 137, 80, 78, 71, 13, 10, 26, 10 };

    /// <summary>
    /// Largest canvas this decoder will accept, in pixels. A 4K screenshot is ~8.3M pixels, so this
    /// is ~32x the biggest capture the parity harness produces.
    /// </summary>
    private const long MaxPixelCount = 268_435_456;

    public static PixelImage Decode(byte[] data)
    {
        if (data.Length < 8)
            throw new FormatException("File too small to be a PNG");
        for (int i = 0; i < 8; i++)
            if (data[i] != Signature[i])
                throw new FormatException("Not a PNG (bad signature)");

        int pos = 8;
        int width = 0, height = 0, bitDepth = 0, colorType = 0, interlace = 0;
        byte[]? palette = null;
        byte[]? transparency = null;
        using var idat = new MemoryStream();

        while (pos + 8 <= data.Length)
        {
            int len = ReadInt32(data, pos); pos += 4;
            string type = System.Text.Encoding.ASCII.GetString(data, pos, 4); pos += 4;
            if (pos + len > data.Length)
                throw new FormatException($"Chunk '{type}' length {len} overruns file");

            switch (type)
            {
                case "IHDR":
                    width = ReadInt32(data, pos);
                    height = ReadInt32(data, pos + 4);
                    bitDepth = data[pos + 8];
                    colorType = data[pos + 9];
                    interlace = data[pos + 12];
                    break;
                case "PLTE":
                    palette = new byte[len];
                    Array.Copy(data, pos, palette, 0, len);
                    break;
                case "tRNS":
                    transparency = new byte[len];
                    Array.Copy(data, pos, transparency, 0, len);
                    break;
                case "IDAT":
                    idat.Write(data, pos, len);
                    break;
            }

            pos += len + 4; // skip data + CRC
            if (type == "IEND") break;
        }

        if (width == 0 || height == 0)
            throw new FormatException("PNG missing IHDR / zero dimensions");
        // r164 remediation, unbounded declared quantity: width and height come straight from IHDR and
        // every buffer below multiplies them, so a tiny file declaring 40000 x 60000 overflowed int
        // and surfaced as a bare OverflowException from `new byte[negative]` -- an opaque failure in
        // the middle of a capture comparison rather than "this PNG is malformed". Same guard the
        // shared PDF writer applies to its own IHDR read, and the same shape DialogPngAnalyzer's
        // CheckedDimension already enforces in this tools tree.
        if ((long)width * height > MaxPixelCount)
        {
            throw new FormatException(
                $"PNG declares {width}x{height} pixels, beyond the {MaxPixelCount:N0}-pixel limit this comparison decoder supports.");
        }
        if (bitDepth != 8)
            throw new NotSupportedException($"Unsupported PNG bit depth {bitDepth} (only 8 supported)");
        if (interlace != 0)
            throw new NotSupportedException("Interlaced PNG not supported");

        int channels = colorType switch
        {
            0 => 1, // grayscale
            2 => 3, // truecolor
            3 => 1, // palette index
            4 => 2, // grayscale + alpha
            6 => 4, // truecolor + alpha
            _ => throw new NotSupportedException($"Unsupported PNG color type {colorType}"),
        };

        idat.Position = 0;
        byte[] raw = Inflate(idat, height * (width * channels + 1));
        byte[] unfiltered = Unfilter(raw, width, height, channels);
        return ToBgra(unfiltered, width, height, colorType, channels, palette, transparency);
    }

    public static PixelImage DecodeFile(string path) => Decode(File.ReadAllBytes(path));

    private static byte[] Inflate(Stream zlib, int expected)
    {
        // r164 remediation, unbounded declared quantity: the inflated size was bounded by nothing in
        // the file -- a PNG declaring a 1x1 canvas whose IDAT expands to 256 MB inflated all of it
        // (measured: 1,025 MB allocated). `expected` is exactly how many bytes the declared geometry
        // can hold, so anything past it is data no pixel will ever read; stop there and say so.
        using var outStream = new MemoryStream(Math.Max(expected, 1024));
        using (var z = new ZLibStream(zlib, CompressionMode.Decompress, leaveOpen: true))
        {
            var buffer = new byte[81920];
            int read;
            while ((read = z.Read(buffer, 0, buffer.Length)) > 0)
            {
                if (outStream.Length + read > expected)
                {
                    throw new FormatException(
                        $"PNG IDAT inflates past the {expected:N0} bytes its declared dimensions can hold.");
                }

                outStream.Write(buffer, 0, read);
            }
        }

        return outStream.ToArray();
    }

    private static byte[] Unfilter(byte[] raw, int width, int height, int channels)
    {
        int stride = width * channels;
        var output = new byte[height * stride];
        int bpp = channels; // bytes per pixel (8-bit)
        int inPos = 0;

        for (int y = 0; y < height; y++)
        {
            byte filter = raw[inPos++];
            int rowStart = y * stride;
            for (int x = 0; x < stride; x++)
            {
                int rawVal = raw[inPos++];
                int a = x >= bpp ? output[rowStart + x - bpp] : 0;            // left
                int b = y > 0 ? output[rowStart - stride + x] : 0;            // up
                int c = (x >= bpp && y > 0) ? output[rowStart - stride + x - bpp] : 0; // up-left
                int val = filter switch
                {
                    0 => rawVal,
                    1 => rawVal + a,
                    2 => rawVal + b,
                    3 => rawVal + ((a + b) >> 1),
                    4 => rawVal + Paeth(a, b, c),
                    _ => throw new FormatException($"Unknown PNG filter type {filter}"),
                };
                output[rowStart + x] = (byte)(val & 0xFF);
            }
        }
        return output;
    }

    private static int Paeth(int a, int b, int c)
    {
        int p = a + b - c;
        int pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
        if (pa <= pb && pa <= pc) return a;
        return pb <= pc ? b : c;
    }

    private static PixelImage ToBgra(
        byte[] px, int width, int height, int colorType, int channels,
        byte[]? palette, byte[]? trns)
    {
        var bgra = new byte[width * height * 4];
        int stride = width * channels;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int si = y * stride + x * channels;
                int di = (y * width + x) * 4;
                byte r, g, b, a = 255;

                switch (colorType)
                {
                    case 0: // grayscale
                        r = g = b = px[si];
                        break;
                    case 2: // RGB
                        r = px[si]; g = px[si + 1]; b = px[si + 2];
                        break;
                    case 3: // palette
                    {
                        int idx = px[si];
                        if (palette == null || idx * 3 + 2 >= palette.Length)
                            throw new FormatException("Palette index out of range");
                        r = palette[idx * 3]; g = palette[idx * 3 + 1]; b = palette[idx * 3 + 2];
                        if (trns != null && idx < trns.Length) a = trns[idx];
                        break;
                    }
                    case 4: // grayscale + alpha
                        r = g = b = px[si]; a = px[si + 1];
                        break;
                    case 6: // RGBA
                        r = px[si]; g = px[si + 1]; b = px[si + 2]; a = px[si + 3];
                        break;
                    default:
                        throw new NotSupportedException();
                }

                bgra[di] = b; bgra[di + 1] = g; bgra[di + 2] = r; bgra[di + 3] = a;
            }
        }
        return new PixelImage(width, height, bgra);
    }

    private static int ReadInt32(byte[] data, int offset) =>
        BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(offset, 4));

    // -------------------------------------------------------------------
    // Encoder (used by tests + the side-by-side composite output).
    // Writes a single IDAT, 8-bit truecolor-with-alpha (color type 6).
    // -------------------------------------------------------------------
    public static byte[] Encode(PixelImage image)
    {
        using var ms = new MemoryStream();
        ms.Write(Signature, 0, Signature.Length);

        // IHDR
        var ihdr = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(0), image.Width);
        BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(4), image.Height);
        ihdr[8] = 8;  // bit depth
        ihdr[9] = 6;  // color type RGBA
        ihdr[10] = 0; ihdr[11] = 0; ihdr[12] = 0;
        WriteChunk(ms, "IHDR", ihdr);

        // IDAT: build filtered scanlines (filter 0) then zlib-deflate
        int stride = image.Width * 4;
        var rawData = new byte[image.Height * (stride + 1)];
        for (int y = 0; y < image.Height; y++)
        {
            int o = y * (stride + 1);
            rawData[o] = 0; // filter none
            for (int x = 0; x < image.Width; x++)
            {
                int si = (y * image.Width + x) * 4;
                int di = o + 1 + x * 4;
                // BGRA -> RGBA
                rawData[di] = image.Pixels[si + 2];
                rawData[di + 1] = image.Pixels[si + 1];
                rawData[di + 2] = image.Pixels[si];
                rawData[di + 3] = image.Pixels[si + 3];
            }
        }
        using (var comp = new MemoryStream())
        {
            using (var z = new ZLibStream(comp, CompressionLevel.Optimal, leaveOpen: true))
                z.Write(rawData, 0, rawData.Length);
            WriteChunk(ms, "IDAT", comp.ToArray());
        }

        WriteChunk(ms, "IEND", Array.Empty<byte>());
        return ms.ToArray();
    }

    public static void EncodeFile(PixelImage image, string path) =>
        File.WriteAllBytes(path, Encode(image));

    private static void WriteChunk(Stream s, string type, byte[] data)
    {
        Span<byte> lenBuf = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(lenBuf, data.Length);
        s.Write(lenBuf);
        var typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
        s.Write(typeBytes, 0, 4);
        s.Write(data, 0, data.Length);

        uint crc = Crc32(typeBytes, data);
        Span<byte> crcBuf = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crcBuf, crc);
        s.Write(crcBuf);
    }

    private static readonly uint[] CrcTable = BuildCrcTable();
    private static uint[] BuildCrcTable()
    {
        var t = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++)
                c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
            t[n] = c;
        }
        return t;
    }

    private static uint Crc32(byte[] type, byte[] data)
    {
        uint c = 0xFFFFFFFF;
        foreach (var b in type) c = CrcTable[(c ^ b) & 0xFF] ^ (c >> 8);
        foreach (var b in data) c = CrcTable[(c ^ b) & 0xFF] ^ (c >> 8);
        return c ^ 0xFFFFFFFF;
    }
}
