namespace Free.Shared.AppServices;

/// <summary>
/// Writes a file through a sibling temp file before replacing the target.
/// </summary>
public static class AtomicFileWriter
{
    private const int MaxTempPathAttempts = 16;

    public static void WriteAllText(string path, string content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(content);

        Write(path, stream =>
        {
            using var writer = new StreamWriter(
                stream,
                new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                leaveOpen: true);
            writer.Write(content);
            writer.Flush();
        });
    }

    public static void WriteAllBytes(string path, byte[] bytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(bytes);

        Write(path, stream => stream.Write(bytes, 0, bytes.Length));
    }

    /// <summary>
    /// Creates a unique, non-existent temporary path alongside <paramref name="targetPath"/>.
    /// The caller owns cleanup until <see cref="ReplaceTarget"/> succeeds.
    /// </summary>
    public static string CreateTempPath(string targetPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);

        var fullTargetPath = Path.GetFullPath(targetPath);
        return CreateUniqueTempPath(fullTargetPath, Path.GetDirectoryName(fullTargetPath));
    }

    /// <summary>
    /// Reserves and owns a unique temporary file alongside <paramref name="targetPath"/>.
    /// Committing after <see cref="ReplaceTarget"/> prevents a redundant cleanup attempt.
    /// </summary>
    public static TemporaryFileLease CreateTempLease(string targetPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);

        var fullTargetPath = Path.GetFullPath(targetPath);
        var directory = Path.GetDirectoryName(fullTargetPath);
        var tempDirectory = string.IsNullOrEmpty(directory) ? "." : directory;
        return TemporaryFileLease.Create(
            $".{Path.GetFileName(fullTargetPath)}.",
            ".tmp",
            tempDirectory);
    }

    /// <summary>
    /// Replaces <paramref name="destinationPath"/> with a completed sibling temp file.
    /// </summary>
    public static void ReplaceTarget(string sourceTempPath, string destinationPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceTempPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);

        if (File.Exists(destinationPath))
        {
            try
            {
                File.Replace(sourceTempPath, destinationPath, null, ignoreMetadataErrors: true);
                return;
            }
            catch (Exception ex) when (IsUnsupportedReplaceFailure(ex))
            {
                // Some platforms and file systems do not support Replace; Move still keeps the
                // completed temp payload separate from the destination until the final operation.
            }
        }

        File.Move(sourceTempPath, destinationPath, overwrite: true);
    }

    private static void Write(string targetPath, Action<Stream> writePayload)
    {
        var fullTargetPath = Path.GetFullPath(targetPath);
        var directory = Path.GetDirectoryName(fullTargetPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        using var temporaryFile = CreateTempLease(fullTargetPath);
        using (var stream = temporaryFile.OpenWrite())
        {
            writePayload(stream);
            if (stream is FileStream fileStream)
                fileStream.Flush(flushToDisk: true);
            else
                stream.Flush();
        }

        ReplaceTarget(temporaryFile.Path, fullTargetPath);
        temporaryFile.Commit();
    }

    private static string CreateUniqueTempPath(
        string targetPath,
        string? targetDirectory)
    {
        var tempDirectory = string.IsNullOrEmpty(targetDirectory) ? "." : targetDirectory;
        var targetFileName = Path.GetFileName(targetPath);

        for (var attempt = 0; attempt < MaxTempPathAttempts; attempt++)
        {
            var candidatePath = Path.Combine(
                tempDirectory,
                $".{targetFileName}.{Guid.NewGuid():N}.tmp");

            if (!TempArtifactExists(candidatePath))
                return candidatePath;
        }

        throw new IOException($"Could not create a unique temporary file for '{targetPath}'.");
    }

    private static bool TempArtifactExists(string path) =>
        File.Exists(path) || Directory.Exists(path);

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
