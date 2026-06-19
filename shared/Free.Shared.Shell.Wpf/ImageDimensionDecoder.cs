using System.IO;
using System.Windows.Media.Imaging;

namespace Free.Shared.Shell;

public readonly record struct DecodedImageDimensions(double Width, double Height);

public static class ImageDimensionDecoder
{
    private const double DefaultDpi = 96d;

    public static DecodedImageDimensions Decode(byte[] imageBytes)
    {
        if (imageBytes is not { Length: > 0 })
            throw new ArgumentException("Image data cannot be empty.", nameof(imageBytes));

        if (!TryDecode(imageBytes, out var dimensions))
            throw new InvalidOperationException("Decoded image dimensions must be positive.");

        return dimensions;
    }

    public static bool TryDecode(byte[]? imageBytes, out DecodedImageDimensions dimensions)
    {
        dimensions = default;
        if (imageBytes is not { Length: > 0 })
            return false;

        try
        {
            using var stream = new MemoryStream(imageBytes);
            var decoder = BitmapDecoder.Create(
                stream,
                BitmapCreateOptions.PreservePixelFormat,
                BitmapCacheOption.OnLoad);
            BitmapFrame? frame = null;
            foreach (var candidate in decoder.Frames)
            {
                frame = candidate;
                break;
            }

            if (frame is null || frame.PixelWidth <= 0 || frame.PixelHeight <= 0)
                return false;

            var width = PixelsToDeviceIndependentUnits(frame.PixelWidth, frame.DpiX);
            var height = PixelsToDeviceIndependentUnits(frame.PixelHeight, frame.DpiY);
            if (!double.IsFinite(width) || !double.IsFinite(height) || width <= 0 || height <= 0)
                return false;

            dimensions = new DecodedImageDimensions(width, height);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static double PixelsToDeviceIndependentUnits(int pixels, double dpi) =>
        pixels * DefaultDpi / (double.IsFinite(dpi) && dpi > 0 ? dpi : DefaultDpi);
}
