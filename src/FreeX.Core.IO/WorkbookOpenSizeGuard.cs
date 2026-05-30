using System.Globalization;
using System.IO.Compression;

namespace FreeX.Core.IO;

/// <summary>
/// Raised when a workbook is rejected before loading because its on-disk size or
/// declared decompressed size exceeds the configured safety limits (zip-bomb guard).
/// </summary>
public sealed class WorkbookTooLargeException : Exception
{
    public WorkbookTooLargeException(string message) : base(message)
    {
    }
}

/// <summary>
/// Pre-open safety limits that protect against accidental or malicious oversized
/// workbooks (large files and "zip bomb" packages that declare an enormous
/// decompressed size from a tiny archive). The checks inspect declared sizes only
/// and never decompress, so they are cheap and run before the heavy load path.
/// </summary>
public static class WorkbookOpenSizeGuard
{
    /// <summary>Maximum accepted on-disk workbook size (2 GiB).</summary>
    public const long DefaultMaxFileBytes = 2L * 1024 * 1024 * 1024;

    /// <summary>Maximum accepted total declared decompressed size of a package (8 GiB).</summary>
    public const long DefaultMaxTotalUncompressedBytes = 8L * 1024 * 1024 * 1024;

    /// <summary>Maximum accepted overall compression ratio before a package is treated as a bomb.</summary>
    public const double DefaultMaxCompressionRatio = 1000.0;

    /// <summary>Compression ratio is only enforced once compressed bytes exceed this floor, to avoid flagging tiny files.</summary>
    public const long CompressionRatioFloorBytes = 64 * 1024;

    public static void EnsureFileWithinLimit(long fileBytes, long maxFileBytes = DefaultMaxFileBytes)
    {
        if (fileBytes > maxFileBytes)
        {
            throw new WorkbookTooLargeException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"The file is {FormatBytes(fileBytes)}, which exceeds the {FormatBytes(maxFileBytes)} open limit."));
        }
    }

    public static void EnsureArchiveWithinLimits(
        Stream packageStream,
        long maxTotalUncompressedBytes = DefaultMaxTotalUncompressedBytes,
        double maxCompressionRatio = DefaultMaxCompressionRatio,
        long compressionRatioFloorBytes = CompressionRatioFloorBytes)
    {
        var canSeek = packageStream.CanSeek;
        var originalPosition = canSeek ? packageStream.Position : 0;

        try
        {
            using var archive = new ZipArchive(packageStream, ZipArchiveMode.Read, leaveOpen: true);
            long totalUncompressed = 0;
            long totalCompressed = 0;

            foreach (var entry in archive.Entries)
            {
                totalUncompressed += entry.Length;
                totalCompressed += entry.CompressedLength;

                if (totalUncompressed > maxTotalUncompressedBytes)
                {
                    throw new WorkbookTooLargeException(
                        string.Create(
                            CultureInfo.InvariantCulture,
                            $"The workbook expands to at least {FormatBytes(totalUncompressed)}, which exceeds the {FormatBytes(maxTotalUncompressedBytes)} open limit."));
                }
            }

            if (totalCompressed >= compressionRatioFloorBytes && totalCompressed > 0)
            {
                var ratio = (double)totalUncompressed / totalCompressed;
                if (ratio > maxCompressionRatio)
                {
                    throw new WorkbookTooLargeException(
                        string.Create(
                            CultureInfo.InvariantCulture,
                            $"The workbook has an unusually high compression ratio ({ratio:F0}:1), which is characteristic of a malformed or malicious package."));
                }
            }
        }
        catch (InvalidDataException)
        {
            // Not a valid zip archive; let the real loader produce its own format error.
        }
        finally
        {
            if (canSeek)
                packageStream.Position = originalPosition;
        }
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["bytes", "KB", "MB", "GB", "TB"];
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return string.Create(CultureInfo.InvariantCulture, $"{value:0.#} {units[unit]}");
    }
}
