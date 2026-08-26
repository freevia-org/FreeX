using System;
using System.Collections;
using System.IO;
using System.IO.Compression;

public sealed class DialogPngMetrics
{
    public string Path { get; set; }
    public long FileBytes { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public double DpiX { get; set; }
    public double DpiY { get; set; }
    public double LogicalWidth { get; set; }
    public double LogicalHeight { get; set; }
    public long Pixels { get; set; }
    public int DistinctColors { get; set; }
    public double OpaqueRatio { get; set; }
    public double NonBackgroundRatio { get; set; }
    public bool IsNonBlank { get; set; }
    public double MeanAlpha { get; set; }
    public double MeanLuma { get; set; }
    public double MeanRed { get; set; }
    public double MeanGreen { get; set; }
    public double MeanBlue { get; set; }
    public int[] Signature { get; set; }
}

// A deliberately small managed PNG decoder for release-preflight evidence. It
// validates the container and compressed pixels instead of relying on native
// Windows GDI+. Capture evidence is required to be non-interlaced 8-bit RGB or
// RGBA, the two lossless formats emitted by the repository's capture harnesses.
public static class DialogPngAnalyzer
{
    private static readonly byte[] PngSignature = new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 };

    public static DialogPngMetrics Analyze(string path)
    {
        string fullPath = System.IO.Path.GetFullPath(path);
        byte[] fileBytes = File.ReadAllBytes(fullPath);
        if (fileBytes.Length < PngSignature.Length || !BytesEqual(fileBytes, 0, PngSignature))
            throw new InvalidDataException("Not a PNG file: " + fullPath);

        int width = 0;
        int height = 0;
        int colorType = -1;
        int bytesPerPixel = 0;
        double dpiX = 96.0;
        double dpiY = 96.0;
        bool foundHeader = false;
        bool foundEnd = false;
        bool hasSignificantBitsChunk = false;
        var compressed = new MemoryStream();

        int offset = PngSignature.Length;
        while (offset < fileBytes.Length)
        {
            if (fileBytes.Length - offset < 12)
                throw new InvalidDataException("Truncated PNG chunk in " + fullPath);

            uint lengthValue = ReadUInt32(fileBytes, offset);
            if (lengthValue > Int32.MaxValue)
                throw new InvalidDataException("PNG chunk is too large in " + fullPath);
            int length = (int)lengthValue;
            int dataOffset = offset + 8;
            if (length > fileBytes.Length - dataOffset - 4)
                throw new InvalidDataException("Truncated PNG chunk data in " + fullPath);

            uint expectedCrc = ReadUInt32(fileBytes, dataOffset + length);
            uint actualCrc = ComputeCrc32(fileBytes, offset + 4, length + 4);
            if (expectedCrc != actualCrc)
                throw new InvalidDataException("PNG chunk CRC mismatch in " + fullPath);

            string type = System.Text.Encoding.ASCII.GetString(fileBytes, offset + 4, 4);
            if (type == "IHDR")
            {
                if (foundHeader || length != 13)
                    throw new InvalidDataException("Invalid PNG IHDR in " + fullPath);
                width = CheckedDimension(ReadUInt32(fileBytes, dataOffset), "width", fullPath);
                height = CheckedDimension(ReadUInt32(fileBytes, dataOffset + 4), "height", fullPath);
                int bitDepth = fileBytes[dataOffset + 8];
                colorType = fileBytes[dataOffset + 9];
                if (bitDepth != 8 || (colorType != 2 && colorType != 6))
                    throw new InvalidDataException("PNG must use 8-bit RGB or RGBA pixels: " + fullPath);
                if (fileBytes[dataOffset + 10] != 0 || fileBytes[dataOffset + 11] != 0 || fileBytes[dataOffset + 12] != 0)
                    throw new InvalidDataException("PNG uses an unsupported compression, filter, or interlace method: " + fullPath);
                bytesPerPixel = colorType == 6 ? 4 : 3;
                foundHeader = true;
            }
            else if (type == "pHYs" && length == 9 && fileBytes[dataOffset + 8] == 1)
            {
                uint pixelsPerMeterX = ReadUInt32(fileBytes, dataOffset);
                uint pixelsPerMeterY = ReadUInt32(fileBytes, dataOffset + 4);
                // Preserve the legacy decoder's single-precision DPI
                // representation so regenerated evidence does not churn solely
                // because its decoder became platform neutral.
                if (pixelsPerMeterX > 0) dpiX = (double)((float)pixelsPerMeterX * 0.0254f);
                if (pixelsPerMeterY > 0) dpiY = (double)((float)pixelsPerMeterY * 0.0254f);
            }
            else if (type == "sBIT")
            {
                hasSignificantBitsChunk = true;
            }
            else if (type == "IDAT")
            {
                if (!foundHeader)
                    throw new InvalidDataException("PNG IDAT precedes IHDR in " + fullPath);
                compressed.Write(fileBytes, dataOffset, length);
            }
            else if (type == "IEND")
            {
                foundEnd = true;
                offset = dataOffset + length + 4;
                break;
            }

            offset = dataOffset + length + 4;
        }

        if (!foundHeader || !foundEnd || compressed.Length < 6)
            throw new InvalidDataException("PNG is missing required chunks in " + fullPath);

        byte[] rgba = DecodePixels(compressed.ToArray(), width, height, bytesPerPixel, fullPath);
        // The historical metrics read the backing bytes of a 32bpp ARGB
        // surface after drawing the PNG onto it. Those backing color channels
        // are alpha-premultiplied with nearest-integer rounding. Reproduce that
        // deterministic pixel contract so changing decoders does not rewrite
        // established evidence scores.
        PreserveLegacyColorChannels(rgba, hasSignificantBitsChunk);
        long pixels = (long)width * height;
        int bgR = rgba[0];
        int bgG = rgba[1];
        int bgB = rgba[2];
        int bgA = rgba[3];
        var distinctColors = new Hashtable();
        long opaquePixels = 0;
        long nonBackgroundPixels = 0;
        long alphaTotal = 0;
        long redTotal = 0;
        long greenTotal = 0;
        long blueTotal = 0;

        for (int pixelOffset = 0; pixelOffset < rgba.Length; pixelOffset += 4)
        {
            int r = rgba[pixelOffset];
            int g = rgba[pixelOffset + 1];
            int b = rgba[pixelOffset + 2];
            int a = rgba[pixelOffset + 3];
            distinctColors[ToArgb(a, r, g, b)] = true;
            alphaTotal += a;
            redTotal += r;
            greenTotal += g;
            blueTotal += b;
            if (a > 0) opaquePixels++;
            if (Math.Abs(a - bgA) + Math.Abs(r - bgR) + Math.Abs(g - bgG) + Math.Abs(b - bgB) > 24)
                nonBackgroundPixels++;
        }

        const int signatureSize = 32;
        int[] signature = new int[signatureSize * signatureSize];
        int signatureIndex = 0;
        for (int sy = 0; sy < signatureSize; sy++)
        {
            int sourceY = (int)Math.Round((double)sy * (height - 1) / (signatureSize - 1));
            for (int sx = 0; sx < signatureSize; sx++)
            {
                int sourceX = (int)Math.Round((double)sx * (width - 1) / (signatureSize - 1));
                int pixelOffset = ((sourceY * width) + sourceX) * 4;
                signature[signatureIndex++] = ToArgb(rgba[pixelOffset + 3], rgba[pixelOffset], rgba[pixelOffset + 1], rgba[pixelOffset + 2]);
            }
        }

        double meanAlpha = (double)alphaTotal / pixels;
        double meanRed = (double)redTotal / pixels;
        double meanGreen = (double)greenTotal / pixels;
        double meanBlue = (double)blueTotal / pixels;
        return new DialogPngMetrics
        {
            Path = fullPath,
            FileBytes = fileBytes.LongLength,
            Width = width,
            Height = height,
            DpiX = dpiX,
            DpiY = dpiY,
            LogicalWidth = width * 96.0 / dpiX,
            LogicalHeight = height * 96.0 / dpiY,
            Pixels = pixels,
            DistinctColors = distinctColors.Count,
            OpaqueRatio = (double)opaquePixels / pixels,
            NonBackgroundRatio = (double)nonBackgroundPixels / pixels,
            IsNonBlank = opaquePixels > 0 && distinctColors.Count > 1 && nonBackgroundPixels > 0,
            MeanAlpha = meanAlpha,
            MeanLuma = (0.2126 * meanRed) + (0.7152 * meanGreen) + (0.0722 * meanBlue),
            MeanRed = meanRed,
            MeanGreen = meanGreen,
            MeanBlue = meanBlue,
            Signature = signature
        };
    }

    public static double SignatureDelta(int[] left, int[] right)
    {
        int length = Math.Min(left.Length, right.Length);
        if (length == 0) return 0;
        long total = 0;
        for (int i = 0; i < length; i++)
        {
            int l = left[i];
            int r = right[i];
            total += Math.Abs(((l >> 24) & 0xff) - ((r >> 24) & 0xff));
            total += Math.Abs(((l >> 16) & 0xff) - ((r >> 16) & 0xff));
            total += Math.Abs(((l >> 8) & 0xff) - ((r >> 8) & 0xff));
            total += Math.Abs((l & 0xff) - (r & 0xff));
        }
        return (double)total / (length * 4.0 * 255.0);
    }

    private static byte[] DecodePixels(byte[] zlib, int width, int height, int bytesPerPixel, string path)
    {
        if ((zlib[0] & 15) != 8 || ((zlib[0] << 8) + zlib[1]) % 31 != 0 || (zlib[1] & 32) != 0)
            throw new InvalidDataException("Invalid or unsupported PNG zlib header in " + path);

        byte[] inflated;
        using (var input = new MemoryStream(zlib, 2, zlib.Length - 6, false))
        using (var deflate = new DeflateStream(input, CompressionMode.Decompress))
        using (var output = new MemoryStream())
        {
            deflate.CopyTo(output);
            inflated = output.ToArray();
        }

        uint expectedAdler = ReadUInt32(zlib, zlib.Length - 4);
        if (expectedAdler != ComputeAdler32(inflated))
            throw new InvalidDataException("PNG pixel checksum mismatch in " + path);

        int rowBytes = checked(width * bytesPerPixel);
        int expectedLength = checked((rowBytes + 1) * height);
        if (inflated.Length != expectedLength)
            throw new InvalidDataException("PNG decompressed pixel length is invalid in " + path);

        byte[] decoded = new byte[checked(rowBytes * height)];
        int inputOffset = 0;
        for (int y = 0; y < height; y++)
        {
            int filter = inflated[inputOffset++];
            int rowOffset = y * rowBytes;
            for (int x = 0; x < rowBytes; x++)
            {
                int raw = inflated[inputOffset++];
                int left = x >= bytesPerPixel ? decoded[rowOffset + x - bytesPerPixel] : 0;
                int above = y > 0 ? decoded[rowOffset + x - rowBytes] : 0;
                int upperLeft = y > 0 && x >= bytesPerPixel ? decoded[rowOffset + x - rowBytes - bytesPerPixel] : 0;
                int value;
                switch (filter)
                {
                    case 0: value = raw; break;
                    case 1: value = raw + left; break;
                    case 2: value = raw + above; break;
                    case 3: value = raw + ((left + above) / 2); break;
                    case 4: value = raw + Paeth(left, above, upperLeft); break;
                    default: throw new InvalidDataException("PNG uses an invalid row filter in " + path);
                }
                decoded[rowOffset + x] = unchecked((byte)value);
            }
        }

        byte[] rgba = new byte[checked(width * height * 4)];
        int sourceOffset = 0;
        int targetOffset = 0;
        while (sourceOffset < decoded.Length)
        {
            rgba[targetOffset++] = decoded[sourceOffset++];
            rgba[targetOffset++] = decoded[sourceOffset++];
            rgba[targetOffset++] = decoded[sourceOffset++];
            rgba[targetOffset++] = bytesPerPixel == 4 ? decoded[sourceOffset++] : (byte)255;
        }
        return rgba;
    }

    private static int Paeth(int left, int above, int upperLeft)
    {
        int prediction = left + above - upperLeft;
        int leftDistance = Math.Abs(prediction - left);
        int aboveDistance = Math.Abs(prediction - above);
        int upperLeftDistance = Math.Abs(prediction - upperLeft);
        return leftDistance <= aboveDistance && leftDistance <= upperLeftDistance ? left :
            aboveDistance <= upperLeftDistance ? above : upperLeft;
    }

    private static void PreserveLegacyColorChannels(byte[] rgba, bool hasSignificantBitsChunk)
    {
        for (int offset = 0; offset < rgba.Length; offset += 4)
        {
            int alpha = rgba[offset + 3];
            if (hasSignificantBitsChunk)
            {
                rgba[offset] = ConvertStraightAlphaChannel(rgba[offset], alpha);
                rgba[offset + 1] = ConvertStraightAlphaChannel(rgba[offset + 1], alpha);
                rgba[offset + 2] = ConvertStraightAlphaChannel(rgba[offset + 2], alpha);
            }
            else
            {
                rgba[offset] = (byte)((rgba[offset] * alpha + 127) / 255);
                rgba[offset + 1] = (byte)((rgba[offset + 1] * alpha + 127) / 255);
                rgba[offset + 2] = (byte)((rgba[offset + 2] * alpha + 127) / 255);
            }
        }
    }

    private static byte ConvertStraightAlphaChannel(byte channel, int alpha)
    {
        if (alpha == 0) return 0;
        int premultiplied = (channel * alpha + 127) / 255;
        int reciprocal = (255 << 16) / alpha;
        return (byte)((premultiplied * reciprocal) >> 16);
    }

    private static int CheckedDimension(uint value, string name, string path)
    {
        if (value == 0 || value > Int32.MaxValue)
            throw new InvalidDataException("PNG " + name + " is invalid in " + path);
        return (int)value;
    }

    private static int ToArgb(int a, int r, int g, int b)
    {
        return unchecked((int)(((uint)a << 24) | ((uint)r << 16) | ((uint)g << 8) | (uint)b));
    }

    private static bool BytesEqual(byte[] bytes, int offset, byte[] expected)
    {
        for (int i = 0; i < expected.Length; i++) if (bytes[offset + i] != expected[i]) return false;
        return true;
    }

    private static uint ReadUInt32(byte[] bytes, int offset)
    {
        return ((uint)bytes[offset] << 24) | ((uint)bytes[offset + 1] << 16) |
            ((uint)bytes[offset + 2] << 8) | bytes[offset + 3];
    }

    private static uint ComputeCrc32(byte[] bytes, int offset, int count)
    {
        uint crc = 0xffffffffU;
        for (int i = 0; i < count; i++)
        {
            crc ^= bytes[offset + i];
            for (int bit = 0; bit < 8; bit++)
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xedb88320U : crc >> 1;
        }
        return crc ^ 0xffffffffU;
    }

    private static uint ComputeAdler32(byte[] bytes)
    {
        const uint modulus = 65521;
        uint a = 1;
        uint b = 0;
        for (int i = 0; i < bytes.Length; i++)
        {
            a = (a + bytes[i]) % modulus;
            b = (b + a) % modulus;
        }
        return (b << 16) | a;
    }
}
