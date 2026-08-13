namespace Free.Shared.Drawing;

/// <summary>
/// Statistics for contiguous four-byte BGRA raster data.
/// </summary>
public static class BgraRasterStatistics
{
    private const int NonBackgroundColorDistanceThreshold = 12;

    /// <summary>
    /// Counts pixels whose summed BGR channel distance from the first pixel is greater than 12.
    /// Alpha does not participate in the comparison.
    /// </summary>
    public static long CountNonBackgroundPixels(ReadOnlySpan<byte> pixels)
    {
        if (pixels.Length < 4)
            return 0;

        var backgroundBlue = pixels[0];
        var backgroundGreen = pixels[1];
        var backgroundRed = pixels[2];
        long count = 0;
        for (var index = 0; index < pixels.Length; index += 4)
        {
            if (Math.Abs(pixels[index] - backgroundBlue) +
                Math.Abs(pixels[index + 1] - backgroundGreen) +
                Math.Abs(pixels[index + 2] - backgroundRed) > NonBackgroundColorDistanceThreshold)
                count++;
        }

        return count;
    }
}
