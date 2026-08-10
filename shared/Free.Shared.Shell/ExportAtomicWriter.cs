using Free.Shared.AppServices;

namespace Free.Shared.Shell;

/// <summary>
/// Compatibility adapter for export callers. Atomic write and replacement semantics are owned by
/// <see cref="AtomicFileWriter"/> so document and settings writes cannot drift.
/// </summary>
public static class ExportAtomicWriter
{
    /// <summary>
    /// Creates a temporary file path alongside <paramref name="targetPath"/> (same directory).
    /// The caller is responsible for writing to this path and then calling
    /// <see cref="ReplaceTarget"/> on success, or deleting the temp file on failure.
    /// </summary>
    public static string CreateTempPath(string targetPath) =>
        AtomicFileWriter.CreateTempPath(targetPath);

    /// <summary>
    /// Reserves and owns a temporary file alongside <paramref name="targetPath"/>.
    /// </summary>
    public static TemporaryFileLease CreateTempLease(string targetPath) =>
        AtomicFileWriter.CreateTempLease(targetPath);

    /// <summary>
    /// Writes <paramref name="bytes"/> to a temporary file alongside <paramref name="targetPath"/>,
    /// then atomically replaces <paramref name="targetPath"/> with the temp file.
    /// </summary>
    public static void WriteAllBytes(string targetPath, byte[] bytes) =>
        AtomicFileWriter.WriteAllBytes(targetPath, bytes);

    /// <summary>
    /// Moves or replaces <paramref name="destinationPath"/> with <paramref name="sourceTempPath"/>.
    /// On success the temp file no longer exists at <paramref name="sourceTempPath"/>.
    /// </summary>
    public static void ReplaceTarget(string sourceTempPath, string destinationPath) =>
        AtomicFileWriter.ReplaceTarget(sourceTempPath, destinationPath);
}
