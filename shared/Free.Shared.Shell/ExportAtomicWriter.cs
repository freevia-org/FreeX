using System.IO;

namespace Free.Shared.Shell;

/// <summary>
/// Writes export output through a sibling temp file before atomically replacing the target,
/// so that a mid-write failure does not corrupt or lock the destination the user chose.
/// </summary>
public static class ExportAtomicWriter
{
    /// <summary>
    /// Creates a temporary file path alongside <paramref name="targetPath"/> (same directory).
    /// The caller is responsible for writing to this path and then calling
    /// <see cref="ReplaceTarget"/> on success, or deleting the temp file on failure.
    /// </summary>
    public static string CreateTempPath(string targetPath)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(targetPath));
        var tempDirectory = string.IsNullOrEmpty(directory) ? Path.GetTempPath() : directory;
        return Path.Combine(tempDirectory, $".{Path.GetFileName(targetPath)}.{Guid.NewGuid():N}.tmp");
    }

    /// <summary>
    /// Writes <paramref name="bytes"/> to a temporary file alongside <paramref name="targetPath"/>,
    /// then atomically replaces <paramref name="targetPath"/> with the temp file.
    /// </summary>
    public static void WriteAllBytes(string targetPath, byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);

        var directory = Path.GetDirectoryName(Path.GetFullPath(targetPath));
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var tempPath = CreateTempPath(targetPath);
        try
        {
            File.WriteAllBytes(tempPath, bytes);
            ReplaceTarget(tempPath, targetPath);
        }
        catch
        {
            if (File.Exists(tempPath))
            {
                try { File.Delete(tempPath); } catch { /* best effort */ }
            }

            throw;
        }
    }

    /// <summary>
    /// Moves or replaces <paramref name="destinationPath"/> with <paramref name="sourceTempPath"/>.
    /// On success the temp file no longer exists at <paramref name="sourceTempPath"/>.
    /// </summary>
    public static void ReplaceTarget(string sourceTempPath, string destinationPath)
    {
        if (File.Exists(destinationPath))
        {
            try
            {
                File.Replace(sourceTempPath, destinationPath, null, ignoreMetadataErrors: true);
                return;
            }
            catch (Exception ex) when (IsUnsupportedReplaceFailure(ex))
            {
                // Fall through to Move on platforms or file systems that don't support Replace.
            }
        }

        File.Move(sourceTempPath, destinationPath, overwrite: true);
    }

    private static bool IsUnsupportedReplaceFailure(Exception exception)
    {
        if (exception is PlatformNotSupportedException or NotSupportedException)
            return true;

        if (exception is IOException ioException)
        {
            var errorCode = ioException.HResult & 0xFFFF;
            return errorCode is 38 or 45 or 50 or 95;
        }

        return false;
    }
}
