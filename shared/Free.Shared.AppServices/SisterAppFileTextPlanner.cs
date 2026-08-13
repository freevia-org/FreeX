using System.Globalization;

namespace Free.Shared.AppServices;

public sealed record SisterAppFileStatusTextSpec(
    string CommandUnavailableFormat,
    string SelectedFileNotLocalPathFormat,
    string UnsupportedFileTypeFormat,
    string UnsupportedExtensionFormat,
    string CommandFailedFormat,
    string OpenedFormat,
    string SavedFormat,
    string InsertedFormat,
    string SaveAsTitleFormat);

public sealed record SisterAppFileTextSpec(
    string OpenPickerTitle,
    string SavePickerTitle,
    string FallbackDisplayName,
    string NewAction,
    string OpenAction,
    string OpenCommand,
    string SaveCommand,
    string InsertPictureCommand,
    string InsertPicturePickerTitle,
    SisterAppFileStatusTextSpec Status);

public static class SisterAppFileTextPlanner
{
    public static SisterAppFileStatusTextSpec CreateStatusText(Func<string, string> getText) => new(
        CommandUnavailableFormat: getText("File_CommandUnavailableFormat"),
        SelectedFileNotLocalPathFormat: getText("File_SelectedFileNotLocalPathFormat"),
        UnsupportedFileTypeFormat: getText("File_UnsupportedFileTypeFormat"),
        UnsupportedExtensionFormat: getText("File_UnsupportedExtensionFormat"),
        CommandFailedFormat: getText("File_CommandFailedFormat"),
        OpenedFormat: getText("File_OpenedFormat"),
        SavedFormat: getText("File_SavedFormat"),
        InsertedFormat: getText("File_InsertedFormat"),
        SaveAsTitleFormat: getText("File_SaveAsTitleFormat"));

    public static string FormatCommandUnavailable(SisterAppFileTextSpec text, string command) =>
        Format(text, status => status.CommandUnavailableFormat, command);

    public static string FormatSelectedFileNotLocalPath(SisterAppFileTextSpec text, string command) =>
        Format(text, status => status.SelectedFileNotLocalPathFormat, command);

    public static string FormatUnsupportedFileType(SisterAppFileTextSpec text, string command, string extension) =>
        Format(text, status => status.UnsupportedFileTypeFormat, command, extension);

    public static string FormatUnsupportedExtension(SisterAppFileTextSpec text, string extension) =>
        Format(text, status => status.UnsupportedExtensionFormat, extension);

    public static string FormatCommandFailed(SisterAppFileTextSpec text, string command, string message) =>
        Format(text, status => status.CommandFailedFormat, command, message);

    public static string FormatOpened(SisterAppFileTextSpec text, string fileName) =>
        Format(text, status => status.OpenedFormat, fileName);

    public static string FormatSaved(SisterAppFileTextSpec text, string fileName) =>
        Format(text, status => status.SavedFormat, fileName);

    public static string FormatInserted(SisterAppFileTextSpec text, string fileName) =>
        Format(text, status => status.InsertedFormat, fileName);

    public static string FormatSaveAsTitle(SisterAppFileTextSpec text, string formatName) =>
        Format(text, status => status.SaveAsTitleFormat, formatName);

    private static string Format(
        SisterAppFileTextSpec text,
        Func<SisterAppFileStatusTextSpec, string> selectTemplate,
        params object?[] args)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(selectTemplate);

        var template = selectTemplate(text.Status);
        ArgumentException.ThrowIfNullOrWhiteSpace(template);

        return string.Format(CultureInfo.CurrentCulture, template, args);
    }
}
