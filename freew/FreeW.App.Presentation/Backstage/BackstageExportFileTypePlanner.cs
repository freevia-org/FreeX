using Free.Shared.Shell;
using FreeW.Core.IO;

namespace FreeW.App.Presentation.Backstage;

public static class BackstageExportFileTypePlanner
{
    public static BackstageActionGroup BuildChangeFileTypeGroup(
        IEnumerable<FileFormatDescriptor> formats,
        Action<string> saveAsExtension) =>
        BuildChangeFileTypeGroup(formats, (extension, _) => saveAsExtension(extension));

    public static BackstageActionGroup BuildChangeFileTypeGroup(
        IEnumerable<FileFormatDescriptor> formats,
        Action<string, int> saveAsFormat)
    {
        ArgumentNullException.ThrowIfNull(formats);
        ArgumentNullException.ThrowIfNull(saveAsFormat);

        return BackstageFileTypeActionPlanner.BuildGroup(
            "Change File Type",
            BackstageSaveAsFileTypePlanner.BuildRows(formats),
            saveAsFormat);
    }
}
