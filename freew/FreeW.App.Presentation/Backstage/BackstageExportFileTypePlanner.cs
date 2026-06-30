using Free.Shared.Shell;
using FreeW.Core.IO;

namespace FreeW.App.Presentation.Backstage;

public static class BackstageExportFileTypePlanner
{
    public static BackstageActionGroup BuildChangeFileTypeGroup(
        IEnumerable<FileFormatDescriptor> formats,
        Action<string> saveAsExtension)
    {
        ArgumentNullException.ThrowIfNull(formats);
        ArgumentNullException.ThrowIfNull(saveAsExtension);

        return BackstageFileTypeActionPlanner.BuildGroup(
            "Change File Type",
            BackstageSaveAsFileTypePlanner.BuildRows(formats),
            saveAsExtension);
    }
}
