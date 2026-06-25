namespace Free.Shared.AppServices;

/// <summary>
/// Writes a file through a sibling temp file before replacing the target.
/// </summary>
public static class AtomicFileWriter
{
    private const int MaxTempPathAttempts = 16;

    public static void WriteAllText(string path, string content)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var tempPath = "";
        try
        {
            tempPath = CreateUniqueTempPath(path, directory);

            // Write via an explicit FileStream so we can flush to disk before renaming.
            // File.WriteAllText returns before data is durably on disk; a power loss after
            // the subsequent File.Move could leave a renamed-but-truncated file at the target
            // path. Flush(flushToDisk: true) syncs OS buffers to storage before we move.
            // Use UTF-8 without BOM to match File.WriteAllText(path, content) default behavior.
            using (var fs = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(fs, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false), leaveOpen: true))
            {
                writer.Write(content);
                writer.Flush();
                fs.Flush(flushToDisk: true);
            }

            File.Move(tempPath, path, overwrite: true);
        }
        finally
        {
            if (tempPath.Length > 0 && File.Exists(tempPath))
                File.Delete(tempPath);
        }
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
}
