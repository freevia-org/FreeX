using Free.Shared.AppServices;
using FreeP.App.Localization;

namespace FreeP.App.Compositor;

public static class PresentationFileTextResources
{
    public static string PictureFileTypeName => Loc.Get("File_PictureFileTypeName");
    public static string VideoFileTypeName => Loc.Get("File_VideoFileTypeName");
    public static string AudioFileTypeName => Loc.Get("File_AudioFileTypeName");
    public static string InsertVideoCommand => Loc.Get("File_InsertVideoCommand");
    public static string InsertVideoPickerTitle => Loc.Get("File_InsertVideoPickerTitle");
    public static string InsertAudioCommand => Loc.Get("File_InsertAudioCommand");
    public static string InsertAudioPickerTitle => Loc.Get("File_InsertAudioPickerTitle");
    public static string VideoExportFailed => Loc.Get("File_VideoExportFailed");
    public static string PrintJobFallbackName => Loc.Get("File_PrintJobFallbackName");

    public static string NormalizePrintJobName(string? value) =>
        string.IsNullOrWhiteSpace(value) ? PrintJobFallbackName : value.Trim();

    public static SisterAppFileTextSpec Presentation => new(
        OpenPickerTitle: Loc.Get("File_OpenPresentationPickerTitle"),
        SavePickerTitle: Loc.Get("File_SavePresentationPickerTitle"),
        FallbackDisplayName: Loc.Get("File_PresentationFallbackDisplayName"),
        NewAction: Loc.Get("File_NewPresentationAction"),
        OpenAction: Loc.Get("File_OpenPresentationAction"),
        OpenCommand: Loc.Get("File_OpenCommand"),
        SaveCommand: Loc.Get("File_SaveCommand"),
        InsertPictureCommand: Loc.Get("File_InsertPictureCommand"),
        InsertPicturePickerTitle: Loc.Get("File_InsertPicturePickerTitle"),
        Status: StatusText);

    private static SisterAppFileStatusTextSpec StatusText =>
        SisterAppFileTextPlanner.CreateStatusText(Loc.Get);
}
