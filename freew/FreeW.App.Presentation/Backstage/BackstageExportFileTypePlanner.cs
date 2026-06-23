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

        var rows = BackstageSaveAsFileTypePlanner
            .Build(formats, saveAsExtension)
            .SelectMany(group => group.Actions)
            .ToArray();

        return new BackstageActionGroup("Change File Type", rows);
    }
}
