using System.Buffers.Binary;

namespace Free.Shared.Drawing;

/// <summary>Raster/metafile container formats <see cref="ImageHeader.Detect"/> recognises by magic bytes.</summary>
public enum ImageHeaderFormat
{
    /// <summary>No recognised signature (including input too short to carry one).</summary>
    Unknown = 0,
    Png,
    Jpeg,
    Gif,
    Bmp,
    Tiff,
    Emf,
    Wmf,
}

/// <summary>
/// The single owner of image header sniffing: signature detection and pixel-size reading for
/// PNG, JPEG, GIF and BMP, without decoding any pixel data.
///
/// This replaced six independent copies (PDF dimensions, FreeP picture insertion, FreeW clipboard
/// paste, FreeW <c>InlineImage.DetectFormat</c>, FreeX chart emoji glyphs, FreeP RTF picture
/// capture). Every member is total: it takes a <see cref="ReadOnlySpan{T}"/>, never indexes past
/// the end, never throws on truncated or malformed input, and answers <c>false</c>/
/// <see cref="ImageHeaderFormat.Unknown"/> instead. What a caller does with an unrecognised image
/// (fall back to a default box, refuse to export, label it PNG) stays the caller's policy.
///
/// No EXIF orientation or DPI handling: none of the former copies read either, so the reported
/// size is the stored (un-rotated) pixel grid.
/// </summary>
public static class ImageHeader
{
    private static ReadOnlySpan<byte> PngSignature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>True when <paramref name="bytes"/> starts with the full 8-byte PNG signature.</summary>
    public static bool HasPngSignature(ReadOnlySpan<byte> bytes) => bytes.StartsWith(PngSignature);

    /// <summary>
    /// Identifies the container from its leading magic bytes. Prefixes are the ones
    /// <c>InlineImage.DetectFormat</c> used: PNG (4 bytes), JPEG (FF D8 FF), "GIF8", "BM",
    /// TIFF (II*\0 / MM\0*), EMF (record type 1 plus " EMF" at offset 40), WMF (placeable header
    /// or classic 01/02 00 09 00). Returns <see cref="ImageHeaderFormat.Unknown"/> otherwise.
    /// </summary>
    public static ImageHeaderFormat Detect(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 2)
            return ImageHeaderFormat.Unknown;

        if (bytes.StartsWith<byte>([0x89, 0x50, 0x4E, 0x47]))
            return ImageHeaderFormat.Png;
        if (bytes.StartsWith<byte>([0xFF, 0xD8, 0xFF]))
            return ImageHeaderFormat.Jpeg;
        if (bytes.StartsWith<byte>([0x47, 0x49, 0x46, 0x38]))
            return ImageHeaderFormat.Gif;
        if (bytes.StartsWith<byte>([0x42, 0x4D]))
            return ImageHeaderFormat.Bmp;
        if (bytes.StartsWith<byte>([0x49, 0x49, 0x2A, 0x00]) || bytes.StartsWith<byte>([0x4D, 0x4D, 0x00, 0x2A]))
            return ImageHeaderFormat.Tiff;
        // EMF: a 0x00000001 record type then, at byte offset 40, the ASCII signature " EMF".
        if (bytes.StartsWith<byte>([0x01, 0x00, 0x00, 0x00]) && bytes.Length >= 44
            && bytes[40] == 0x20 && bytes[41] == 0x45 && bytes[42] == 0x4D && bytes[43] == 0x46)
            return ImageHeaderFormat.Emf;
        // WMF: the placeable-metafile header (D7 CD C6 9A) or a classic WMF header (01/02 00 09 00).
        if (bytes.StartsWith<byte>([0xD7, 0xCD, 0xC6, 0x9A])
            || bytes.StartsWith<byte>([0x01, 0x00, 0x09, 0x00])
            || bytes.StartsWith<byte>([0x02, 0x00, 0x09, 0x00]))
            return ImageHeaderFormat.Wmf;

        return ImageHeaderFormat.Unknown;
    }

    /// <summary>
    /// Pixel size of a PNG, GIF, BMP or JPEG. False when the format is not one of those, or the
    /// header is truncated/malformed, or either dimension is not positive.
    /// </summary>
    public static bool TryReadPixelSize(ReadOnlySpan<byte> bytes, out int width, out int height) =>
        TryReadPngSize(bytes, out width, out height)
        || TryReadGifSize(bytes, out width, out height)
        || TryReadBmpSize(bytes, out width, out height)
        || TryReadJpegSize(bytes, out width, out height);

    /// <summary>
    /// Pixel size from a PNG's IHDR chunk (always the first chunk: length(4) "IHDR"(4) width(4, BE)
    /// height(4, BE)). Requires the full 8-byte signature.
    /// </summary>
    public static bool TryReadPngSize(ReadOnlySpan<byte> bytes, out int width, out int height)
    {
        width = 0;
        height = 0;
        if (bytes.Length < 24 || !HasPngSignature(bytes)
            || bytes[12] != (byte)'I' || bytes[13] != (byte)'H' || bytes[14] != (byte)'D' || bytes[15] != (byte)'R')
        {
            return false;
        }

        width = (int)BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(16, 4));
        height = (int)BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(20, 4));
        return Positive(ref width, ref height);
    }

    /// <summary>Pixel size from a GIF87a/GIF89a logical screen descriptor (16-bit little-endian).</summary>
    public static bool TryReadGifSize(ReadOnlySpan<byte> bytes, out int width, out int height)
    {
        width = 0;
        height = 0;
        if (bytes.Length < 10
            || bytes[0] != (byte)'G' || bytes[1] != (byte)'I' || bytes[2] != (byte)'F'
            || bytes[3] != (byte)'8' || (bytes[4] != (byte)'7' && bytes[4] != (byte)'9') || bytes[5] != (byte)'a')
        {
            return false;
        }

        width = BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(6, 2));
        height = BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(8, 2));
        return Positive(ref width, ref height);
    }

    /// <summary>
    /// Pixel size from a BMP's DIB header: BITMAPCOREHEADER (12 bytes, 16-bit) or BITMAPINFOHEADER and
    /// newer (40+ bytes, 32-bit). A negative height (top-down bitmap) is reported as its magnitude.
    /// </summary>
    public static bool TryReadBmpSize(ReadOnlySpan<byte> bytes, out int width, out int height)
    {
        width = 0;
        height = 0;
        if (bytes.Length < 26 || bytes[0] != (byte)'B' || bytes[1] != (byte)'M')
            return false;

        var headerSize = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(14, 4));
        if (headerSize == 12)
        {
            width = BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(18, 2));
            height = BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(20, 2));
        }
        else if (headerSize >= 40)
        {
            width = BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(18, 4));
            var signedHeight = BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(22, 4));
            // Math.Abs(int.MinValue) would throw; that value is not a real height, so it is rejected.
            if (signedHeight == int.MinValue)
                return false;
            height = Math.Abs(signedHeight);
        }
        else
        {
            return false;
        }

        return Positive(ref width, ref height);
    }

    /// <summary>
    /// Pixel size from a JPEG's start-of-frame segment (any of SOF0-SOF15 except DHT/JPG/DAC, so
    /// baseline, extended, progressive, lossless and arithmetic-coded frames). Leading APPn/EXIF/COM
    /// segments are skipped; non-marker bytes between segments are resynchronised over.
    /// </summary>
    public static bool TryReadJpegSize(ReadOnlySpan<byte> bytes, out int width, out int height)
    {
        width = 0;
        height = 0;
        if (!TryFindJpegStartOfFrame(bytes, out var offset) || offset + 9 > bytes.Length)
            return false;

        height = BinaryPrimitives.ReadUInt16BigEndian(bytes.Slice(offset + 5, 2));
        width = BinaryPrimitives.ReadUInt16BigEndian(bytes.Slice(offset + 7, 2));
        return Positive(ref width, ref height);
    }

    /// <summary>
    /// The JPEG start-of-frame fields a PDF writer needs: sample precision in bits and colour
    /// component count, besides the size. Stricter than <see cref="TryReadJpegSize"/>: the whole
    /// SOF segment (length field plus payload) must be present, with a length of at least 8.
    /// </summary>
    public static bool TryReadJpegFrame(
        ReadOnlySpan<byte> bytes, out int width, out int height, out int precisionBits, out int components)
    {
        width = 0;
        height = 0;
        precisionBits = 0;
        components = 0;
        if (!TryFindJpegStartOfFrame(bytes, out var offset) || offset + 10 > bytes.Length)
            return false;

        var segmentLength = BinaryPrimitives.ReadUInt16BigEndian(bytes.Slice(offset + 2, 2));
        if (segmentLength < 8 || offset + 2 + segmentLength > bytes.Length)
            return false;

        precisionBits = bytes[offset + 4];
        height = BinaryPrimitives.ReadUInt16BigEndian(bytes.Slice(offset + 5, 2));
        width = BinaryPrimitives.ReadUInt16BigEndian(bytes.Slice(offset + 7, 2));
        components = bytes[offset + 9];
        if (!Positive(ref width, ref height))
        {
            precisionBits = 0;
            components = 0;
            return false;
        }

        return true;
    }

    private static bool Positive(ref int width, ref int height)
    {
        if (width > 0 && height > 0)
            return true;

        width = 0;
        height = 0;
        return false;
    }

    /// <summary>
    /// Walks marker segments from just after SOI and returns the offset of the 0xFF that starts the
    /// first start-of-frame marker; stops (false) at EOI, SOS, a malformed length, or the end.
    /// </summary>
    private static bool TryFindJpegStartOfFrame(ReadOnlySpan<byte> bytes, out int sofOffset)
    {
        sofOffset = 0;
        if (bytes.Length < 4 || bytes[0] != 0xFF || bytes[1] != 0xD8)
            return false;

        var offset = 2;
        while (offset + 3 < bytes.Length)
        {
            if (bytes[offset] != 0xFF)
            {
                offset++;
                continue;
            }

            var marker = bytes[offset + 1];
            if (marker == 0xFF)
            {
                offset++;
                continue;
            }

            // Markers with no payload: TEM, SOI, and the restart markers RST0-RST7.
            if (marker == 0x01 || marker == 0xD8 || (marker >= 0xD0 && marker <= 0xD7))
            {
                offset += 2;
                continue;
            }

            // EOI, or SOS: the entropy-coded scan follows, so a frame header can no longer appear.
            if (marker == 0xD9 || marker == 0xDA)
                return false;

            // SOF0-SOF15 except DHT (C4), JPG (C8) and DAC (CC).
            if (marker is >= 0xC0 and <= 0xCF && marker != 0xC4 && marker != 0xC8 && marker != 0xCC)
            {
                sofOffset = offset;
                return true;
            }

            var segmentLength = BinaryPrimitives.ReadUInt16BigEndian(bytes.Slice(offset + 2, 2));
            if (segmentLength < 2)
                return false;

            offset += 2 + segmentLength;
        }

        return false;
    }
}
