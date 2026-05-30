using System.IO;

namespace FreeX.App.Host;

/// <summary>
/// Writes a file atomically: content is written to a sibling temp file and then
/// moved into place, so an interrupted or failed write never truncates or corrupts
/// the existing file. Creates the parent directory if needed.
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
