namespace Free.Shared.AppServices;

public sealed record SisterAppFileTextSpec(
    string OpenPickerTitle,
    string SavePickerTitle,
    string FallbackDisplayName,
    string NewAction,
    string OpenAction);

public static class SisterAppFileTextPlanner
{
    public const string OpenCommand = "Open";
    public const string SaveCommand = "Save";
    public const string InsertPictureCommand = "Insert picture";
    public const string InsertPicturePickerTitle = "Insert Picture";

    public static SisterAppFileTextSpec Document { get; } = new(
        OpenPickerTitle: "Open document",
        SavePickerTitle: "Save document",
        FallbackDisplayName: "Document",
        NewAction: "replace the current document",
        OpenAction: "opening another document");

    public static SisterAppFileTextSpec Presentation { get; } = new(
        OpenPickerTitle: "Open Presentation",
        SavePickerTitle: "Save Presentation",
        FallbackDisplayName: "Presentation",
        NewAction: "creating a new presentation",
        OpenAction: "opening another presentation");

    public static string FormatCommandUnavailable(string command) =>
        $"{command} unavailable.";

    public static string FormatSelectedFileNotLocalPath(string command) =>
        $"{command} failed: selected file is not available as a local path.";

    public static string FormatUnsupportedFileType(string command, string extension) =>
        $"{command} failed: unsupported file type \"{extension}\".";

    public static string FormatUnsupportedExtension(string extension) =>
        $"Save failed: unsupported extension \"{extension}\".";

    public static string FormatCommandFailed(string command, string message) =>
        $"{command} failed: {message}";

    public static string FormatOpened(string fileName) =>
        $"Opened {fileName}";

    public static string FormatSaved(string fileName) =>
        $"Saved {fileName}";

    public static string FormatInserted(string fileName) =>
        $"Inserted {fileName}";

    public static string FormatSaveAsTitle(string formatName) =>
        $"Save as {formatName}";
}
