using Free.Shared.AppServices;
using FreeX.Core.IO;

namespace FreeX.App.Services;

/// <summary>
/// Resolves a user- or shell-supplied workbook path into the neutral open target that hosts can load.
/// Hosts still own file pickers, dirty prompts, progress UI, and applying the loaded workbook.
/// </summary>
public static class WorkbookOpenTargetPlanner
{
    public const string LocalPathRequiredMessage = "Open requires a local file path.";
    public const string UnsupportedFileTypeMessage = "Unsupported file type.";

    public static bool TryCreateOpenTarget(
        IEnumerable<IFileAdapter> adapters,
        string path,
        out WorkbookOpenTarget? target,
        out string message) =>
        TryCreateOpenTarget(adapters, path, fileAccessIdentity: null, out target, out message);

    public static bool TryCreateOpenTarget(
        IEnumerable<IFileAdapter> adapters,
        string path,
        WorkbookFileAccessIdentity? fileAccessIdentity,
        out WorkbookOpenTarget? target,
        out string message)
    {
        ArgumentNullException.ThrowIfNull(adapters);

        target = null;
        if (!LocalFilePath.TryNormalize(path, out var openPath))
        {
            message = LocalPathRequiredMessage;
            return false;
        }

        if (!TryGetExtension(openPath, out var extension))
        {
            message = UnsupportedFileTypeMessage;
            return false;
        }

        var adapter = FileFormatResolver.FindOpenAdapter(adapters, extension, out var format);
        if (adapter is null || format is null)
        {
            message = $"Unsupported file type: {extension}.";
            return false;
        }

        target = new WorkbookOpenTarget(
            openPath,
            adapter,
            extension,
            format,
            ResolveFileAccessIdentity(openPath, fileAccessIdentity));
        message = "";
        return true;
    }

    public static WorkbookFileAccessIdentity ResolveFileAccessIdentity(
        string path,
        WorkbookFileAccessIdentity? fileAccessIdentity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (fileAccessIdentity is not null &&
            fileAccessIdentity.TryWithLocalPath(path, out var resolvedIdentity) &&
            resolvedIdentity is not null)
        {
            return resolvedIdentity;
        }

        return WorkbookFileAccessIdentity.FromLocalPath(path);
    }

    private static bool TryGetExtension(string path, out string extension)
    {
        try
        {
            if (path.Contains('\0', StringComparison.Ordinal) ||
                path.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
            {
                extension = "";
                return false;
            }

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
