using Free.Shared.Drawing;

namespace Free.Shared.Pdf;

/// <summary>
/// Reads the pixel dimensions of the encoded image formats the portable writers support, without
/// decoding pixel data. Shared so every adapter snaps <c>a:srcRect</c> crops to the same source
/// pixel grid (see <see cref="PdfRenderGeometry.GetImageCropPlan"/>).
/// </summary>
internal static class PdfImageDimensions
{
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
                return ImageHeader.TryReadPngSize(bytes, out width, out height);
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

    /// <summary>
    /// Reads the JPEG frame size and component count through the shared header reader
    /// (<see cref="ImageHeader.TryReadJpegFrame"/>). Throws the same exception types the previous
    /// private marker scan did: <see cref="FormatException"/> for a missing/truncated frame header,
    /// <see cref="NotSupportedException"/> for anything but 8-bit samples.
    /// </summary>
    public static (int Width, int Height, int Components) ReadJpegSize(byte[] bytes)
    {
        if (!ImageHeader.TryReadJpegFrame(bytes, out var width, out var height, out var precision, out var components))
            throw new FormatException("JPEG image is missing a usable start-of-frame segment.");
        if (precision != 8)
            throw new NotSupportedException("Portable PDF image export supports only 8-bit JPEG images.");

        return (width, height, components);
    }
}
