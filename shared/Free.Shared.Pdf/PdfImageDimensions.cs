using System.Buffers.Binary;

namespace Free.Shared.Pdf;

/// <summary>
/// Reads the pixel dimensions of the encoded image formats the portable writers support, without
/// decoding pixel data. Shared so every adapter snaps <c>a:srcRect</c> crops to the same source
/// pixel grid (see <see cref="PdfRenderGeometry.GetImageCropPlan"/>).
/// </summary>
internal static class PdfImageDimensions
{
    private static ReadOnlySpan<byte> PngSignature => [137, 80, 78, 71, 13, 10, 26, 10];

    /// <summary>
    /// Reads the dimensions of a PNG or JPEG. Returns false for any other content type, and for
    /// bytes that do not parse -- callers treat that as "not exportable" rather than guessing.
    /// </summary>
    public static bool TryReadSize(byte[] bytes, string contentType, out int width, out int height)
    {
        width = 0;
        height = 0;
        if (bytes is null || bytes.Length == 0 || contentType is null)
            return false;

        try
        {
            if (contentType.Equals("image/png", StringComparison.OrdinalIgnoreCase))
            {
                (width, height) = ReadPngSize(bytes);
                return width > 0 && height > 0;
            }

            if (contentType.Equals("image/jpeg", StringComparison.OrdinalIgnoreCase))
            {
                (width, height, _) = ReadJpegSize(bytes);
                return width > 0 && height > 0;
            }
        }
        catch (Exception exception) when (exception is FormatException or NotSupportedException or ArgumentOutOfRangeException)
        {
            return false;
        }

        return false;
    }

    /// <summary>Reads width and height from a PNG IHDR chunk, which is always the first chunk.</summary>
    public static (int Width, int Height) ReadPngSize(byte[] bytes)
    {
        const int ihdrDataOffset = 16; // 8-byte signature + 4-byte length + 4-byte "IHDR" type
        if (bytes.Length < ihdrDataOffset + 8 ||
            !bytes.AsSpan(0, PngSignature.Length).SequenceEqual(PngSignature) ||
            bytes[12] != (byte)'I' || bytes[13] != (byte)'H' ||
            bytes[14] != (byte)'D' || bytes[15] != (byte)'R')
            throw new FormatException("Not a PNG image.");

        return (
            (int)BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(ihdrDataOffset, 4)),
            (int)BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(ihdrDataOffset + 4, 4)));
    }

    /// <summary>Scans JPEG marker segments for the start-of-frame carrying the frame dimensions.</summary>
    public static (int Width, int Height, int Components) ReadJpegSize(byte[] bytes)
    {
        if (bytes.Length < 4 || bytes[0] != 0xFF || bytes[1] != 0xD8)
            throw new FormatException("Not a JPEG image.");

        var position = 2;
        while (position + 4 <= bytes.Length)
        {
            while (position < bytes.Length && bytes[position] == 0xFF)
                position++;
            if (position >= bytes.Length)
                break;

            var marker = bytes[position++];
            if (marker is 0xD9 or 0xDA)
                break;
            if (position + 2 > bytes.Length)
                break;

            var length = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(position, 2));
            if (length < 2 || position + length > bytes.Length)
                throw new FormatException("JPEG segment overruns the file.");

            if (IsJpegStartOfFrame(marker))
            {
                if (length < 8)
                    throw new FormatException("JPEG start-of-frame segment is truncated.");
                var precision = bytes[position + 2];
                if (precision != 8)
                    throw new NotSupportedException("Portable PDF image export supports only 8-bit JPEG images.");

                var height = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(position + 3, 2));
                var width = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(position + 5, 2));
                var components = bytes[position + 7];
                return (width, height, components);
            }

            position += length;
        }

        throw new FormatException("JPEG image is missing a start-of-frame segment.");
    }

    private static bool IsJpegStartOfFrame(byte marker) =>
        marker is 0xC0 or 0xC1 or 0xC2 or 0xC3 or 0xC5 or 0xC6 or 0xC7 or 0xC9 or 0xCA or 0xCB or 0xCD or 0xCE or 0xCF;
}
