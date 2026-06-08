namespace FreeX.App.Services;

/// <summary>
/// Writes a file through a sibling temp file before replacing the target.
/// </summary>
public static class AtomicFileWriter
{
    public static void WriteAllText(string path, string content)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var tempPath = path + ".tmp";
        File.WriteAllText(tempPath, content);
        File.Move(tempPath, path, overwrite: true);
    }
}
