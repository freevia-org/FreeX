namespace Free.ToolsShared;

public static class RepositoryRootLocator
{
    public static string? Find(string startDirectory, string marker)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(startDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(marker);

        for (var current = new DirectoryInfo(startDirectory);
             current is not null;
             current = current.Parent)
        {
            if (File.Exists(Path.Combine(current.FullName, marker)))
                return current.FullName;
        }

        return null;
    }
}
