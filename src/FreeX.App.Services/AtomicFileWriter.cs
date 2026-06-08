namespace FreeX.App.Services;

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
            using (var tempFile = CreateUniqueTempFile(path, directory, out tempPath))
            using (var writer = new StreamWriter(tempFile))
            {
                writer.Write(content);
            }

            File.Move(tempPath, path, overwrite: true);
        }
        finally
        {
            if (tempPath.Length > 0 && File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }

    private static FileStream CreateUniqueTempFile(
        string targetPath,
        string? targetDirectory,
        out string tempPath)
    {
        var tempDirectory = string.IsNullOrEmpty(targetDirectory) ? "." : targetDirectory;
        var targetFileName = Path.GetFileName(targetPath);

        for (var attempt = 0; attempt < MaxTempPathAttempts; attempt++)
        {
            var candidatePath = Path.Combine(
                tempDirectory,
                $".{targetFileName}.{Guid.NewGuid():N}.tmp");

            try
            {
                var stream = new FileStream(
                    candidatePath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None);
                tempPath = candidatePath;
                return stream;
            }
            catch (Exception ex) when (
                (ex is IOException || ex is UnauthorizedAccessException)
                && TempArtifactExists(candidatePath))
            {
            }
        }

        throw new IOException($"Could not create a unique temporary file for '{targetPath}'.");
    }

    private static bool TempArtifactExists(string path) =>
        File.Exists(path) || Directory.Exists(path);
}
