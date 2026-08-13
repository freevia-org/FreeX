namespace Free.ToolsShared;

/// <summary>
/// Owns a uniquely named temporary directory and removes it with bounded retries on disposal.
/// </summary>
public sealed class ToolTemporaryDirectory : IDisposable
{
    private const int MaximumDeleteAttempts = 60;
    private static readonly TimeSpan DeleteRetryDelay = TimeSpan.FromMilliseconds(50);

    public ToolTemporaryDirectory(string prefix)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prefix);
        if (prefix.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new ArgumentException(
                "The temporary-directory prefix must be a valid file-name prefix.",
                nameof(prefix));
        }

        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            string.Concat(prefix, System.IO.Path.GetRandomFileName()));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string GetPath(string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        if (System.IO.Path.IsPathRooted(relativePath))
            throw new ArgumentException("The temporary resource path must be relative.", nameof(relativePath));

        var resolvedPath = System.IO.Path.GetFullPath(relativePath, Path);
        var directoryPrefix = Path.EndsWith(System.IO.Path.DirectorySeparatorChar)
            ? Path
            : Path + System.IO.Path.DirectorySeparatorChar;
        var pathComparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (!resolvedPath.StartsWith(directoryPrefix, pathComparison))
        {
            throw new ArgumentException(
                "The temporary resource path must remain inside the owned directory.",
                nameof(relativePath));
        }

        return resolvedPath;
    }

    public void Dispose()
    {
        for (var attempt = 1; attempt <= MaximumDeleteAttempts; attempt++)
        {
            try
            {
                if (Directory.Exists(Path))
                    Directory.Delete(Path, recursive: true);
                return;
            }
            catch (Exception exception) when (
                (exception is IOException or UnauthorizedAccessException) &&
                attempt < MaximumDeleteAttempts)
            {
                Thread.Sleep(DeleteRetryDelay);
            }
        }
    }
}
