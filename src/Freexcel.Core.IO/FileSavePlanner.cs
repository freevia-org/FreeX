namespace Freexcel.Core.IO;

public sealed record FileSaveTarget(string Path, IFileAdapter Adapter);

public static class FileSavePlanner
{
    public static bool TryResolveExistingPath(
        string? currentFilePath,
        IEnumerable<IFileAdapter> adapters,
        out FileSaveTarget? target)
    {
        target = null;
        if (string.IsNullOrWhiteSpace(currentFilePath))
            return false;

        var extension = Path.GetExtension(currentFilePath);
        if (string.IsNullOrWhiteSpace(extension))
            return false;

        var adapter = adapters.FirstOrDefault(candidate =>
            string.Equals(candidate.Extension, extension, StringComparison.OrdinalIgnoreCase));
        if (adapter is null)
            return false;

        target = new FileSaveTarget(currentFilePath, adapter);
        return true;
    }
}
