namespace FreeX.Core.IO;

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

        var savePath = currentFilePath.Trim();
        if (!TryGetExtension(savePath, out var extension))
            return false;

        var adapter = FileFormatResolver.FindSaveAdapter(adapters, extension, out _);
        if (adapter is null)
            return false;

        target = new FileSaveTarget(savePath, adapter);
        return true;
    }

    private static bool TryGetExtension(string path, out string extension)
    {
        try
        {
            extension = Path.GetExtension(path) ?? "";
            return !string.IsNullOrWhiteSpace(extension);
        }
        catch (ArgumentException)
        {
            extension = "";
            return false;
        }
        catch (NotSupportedException)
        {
            extension = "";
            return false;
        }
        catch (PathTooLongException)
        {
            extension = "";
            return false;
        }
    }
}
