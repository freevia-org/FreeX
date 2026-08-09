namespace Free.ToolsShared;

public static class RepositoryRootLocator
{
    public static string? Find(string startDirectory, string marker) =>
        Find(startDirectory, marker, File.Exists);

    public static string? FindByDirectoryMarker(string startDirectory, string marker) =>
        Find(startDirectory, marker, Directory.Exists);

    private static string? Find(
        string startDirectory,
        string marker,
        Func<string, bool> markerExists)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(startDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(marker);

        for (var current = new DirectoryInfo(startDirectory);
             current is not null;
             current = current.Parent)
        {
            if (markerExists(Path.Combine(current.FullName, marker)))
                return current.FullName;
        }

        return null;
    }
}
