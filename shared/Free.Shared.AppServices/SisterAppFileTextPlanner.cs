using System.Globalization;
using System.Reflection;

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
    private const string FreeWLocTypeName = "FreeW.App.Localization.Loc, FreeW.App.Localization";
    private const string FreePLocTypeName = "FreeP.App.Localization.Loc, FreeP.App.Localization";
    private const string OpenCommandKey = "File_OpenCommand";
    private const string SaveCommandKey = "File_SaveCommand";
    private const string InsertPictureCommandKey = "File_InsertPictureCommand";
    private const string InsertPicturePickerTitleKey = "File_InsertPicturePickerTitle";
    private const string CommandUnavailableFormatKey = "File_CommandUnavailableFormat";
    private const string SelectedFileNotLocalPathFormatKey = "File_SelectedFileNotLocalPathFormat";
    private const string UnsupportedFileTypeFormatKey = "File_UnsupportedFileTypeFormat";
    private const string UnsupportedExtensionFormatKey = "File_UnsupportedExtensionFormat";
    private const string CommandFailedFormatKey = "File_CommandFailedFormat";
    private const string OpenedFormatKey = "File_OpenedFormat";
    private const string SavedFormatKey = "File_SavedFormat";
    private const string InsertedFormatKey = "File_InsertedFormat";
    private const string SaveAsTitleFormatKey = "File_SaveAsTitleFormat";

    public static string OpenCommand => ResolveAnyResource(OpenCommandKey);

    public static string SaveCommand => ResolveAnyResource(SaveCommandKey);

    public static string InsertPictureCommand => ResolveAnyResource(InsertPictureCommandKey);

    public static string InsertPicturePickerTitle => ResolveAnyResource(InsertPicturePickerTitleKey);

    public static SisterAppFileTextSpec Document => BuildDocumentText();

    public static SisterAppFileTextSpec Presentation => BuildPresentationText();

    public static string FormatCommandUnavailable(SisterAppFileTextSpec text, string command) =>
        Format(text, status => status.CommandUnavailableFormat, command);

    public static string FormatCommandUnavailable(string command) =>
        FormatCommandUnavailable(BuildCompatibilityText(), ResolveCommand(command));

    public static string FormatSelectedFileNotLocalPath(SisterAppFileTextSpec text, string command) =>
        Format(text, status => status.SelectedFileNotLocalPathFormat, command);

    public static string FormatSelectedFileNotLocalPath(string command) =>
        FormatSelectedFileNotLocalPath(BuildCompatibilityText(), ResolveCommand(command));

    public static string FormatUnsupportedFileType(SisterAppFileTextSpec text, string command, string extension) =>
        Format(text, status => status.UnsupportedFileTypeFormat, command, extension);

    public static string FormatUnsupportedFileType(string command, string extension) =>
        FormatUnsupportedFileType(BuildCompatibilityText(), ResolveCommand(command), extension);

    public static string FormatUnsupportedExtension(SisterAppFileTextSpec text, string extension) =>
        Format(text, status => status.UnsupportedExtensionFormat, extension);

    public static string FormatUnsupportedExtension(string extension) =>
        FormatUnsupportedExtension(BuildCompatibilityText(), extension);

    public static string FormatCommandFailed(SisterAppFileTextSpec text, string command, string message) =>
        Format(text, status => status.CommandFailedFormat, command, message);

    public static string FormatCommandFailed(string command, string message) =>
        FormatCommandFailed(BuildCompatibilityText(), ResolveCommand(command), message);

    public static string FormatOpened(SisterAppFileTextSpec text, string fileName) =>
        Format(text, status => status.OpenedFormat, fileName);

    public static string FormatOpened(string fileName) =>
        FormatOpened(BuildCompatibilityText(), fileName);

    public static string FormatSaved(SisterAppFileTextSpec text, string fileName) =>
        Format(text, status => status.SavedFormat, fileName);

    public static string FormatSaved(string fileName) =>
        FormatSaved(BuildCompatibilityText(), fileName);

    public static string FormatInserted(SisterAppFileTextSpec text, string fileName) =>
        Format(text, status => status.InsertedFormat, fileName);

    public static string FormatInserted(string fileName) =>
        FormatInserted(BuildCompatibilityText(), fileName);

    public static string FormatSaveAsTitle(SisterAppFileTextSpec text, string formatName) =>
        Format(text, status => status.SaveAsTitleFormat, formatName);

    public static string FormatSaveAsTitle(string formatName) =>
        FormatSaveAsTitle(BuildCompatibilityText(), formatName);

    private static SisterAppFileTextSpec BuildDocumentText() =>
        new(
            OpenPickerTitle: ResolveResource(FreeWLocTypeName, "File_OpenDocumentPickerTitle"),
            SavePickerTitle: ResolveResource(FreeWLocTypeName, "File_SaveDocumentPickerTitle"),
            FallbackDisplayName: ResolveResource(FreeWLocTypeName, "File_DocumentFallbackDisplayName"),
            NewAction: ResolveResource(FreeWLocTypeName, "File_NewDocumentAction"),
            OpenAction: ResolveResource(FreeWLocTypeName, "File_OpenDocumentAction"),
            OpenCommand: ResolveResource(FreeWLocTypeName, OpenCommandKey),
            SaveCommand: ResolveResource(FreeWLocTypeName, SaveCommandKey),
            InsertPictureCommand: ResolveResource(FreeWLocTypeName, InsertPictureCommandKey),
            InsertPicturePickerTitle: ResolveResource(FreeWLocTypeName, InsertPicturePickerTitleKey),
            Status: BuildStatusText(FreeWLocTypeName));

    private static SisterAppFileTextSpec BuildPresentationText() =>
        new(
            OpenPickerTitle: ResolveResource(FreePLocTypeName, "File_OpenPresentationPickerTitle"),
            SavePickerTitle: ResolveResource(FreePLocTypeName, "File_SavePresentationPickerTitle"),
            FallbackDisplayName: ResolveResource(FreePLocTypeName, "File_PresentationFallbackDisplayName"),
            NewAction: ResolveResource(FreePLocTypeName, "File_NewPresentationAction"),
            OpenAction: ResolveResource(FreePLocTypeName, "File_OpenPresentationAction"),
            OpenCommand: ResolveResource(FreePLocTypeName, OpenCommandKey),
            SaveCommand: ResolveResource(FreePLocTypeName, SaveCommandKey),
            InsertPictureCommand: ResolveResource(FreePLocTypeName, InsertPictureCommandKey),
            InsertPicturePickerTitle: ResolveResource(FreePLocTypeName, InsertPicturePickerTitleKey),
            Status: BuildStatusText(FreePLocTypeName));

    private static SisterAppFileTextSpec BuildCompatibilityText() =>
        new(
            OpenPickerTitle: ResolveAnyResource("File_OpenDocumentPickerTitle"),
            SavePickerTitle: ResolveAnyResource("File_SaveDocumentPickerTitle"),
            FallbackDisplayName: ResolveAnyResource("File_DocumentFallbackDisplayName"),
            NewAction: ResolveAnyResource("File_NewDocumentAction"),
            OpenAction: ResolveAnyResource("File_OpenDocumentAction"),
            OpenCommand: OpenCommand,
            SaveCommand: SaveCommand,
            InsertPictureCommand: InsertPictureCommand,
            InsertPicturePickerTitle: InsertPicturePickerTitle,
            Status: BuildStatusText());

    private static SisterAppFileStatusTextSpec BuildStatusText(string? locTypeName = null) =>
        new(
            CommandUnavailableFormat: ResolveResource(locTypeName, CommandUnavailableFormatKey),
            SelectedFileNotLocalPathFormat: ResolveResource(locTypeName, SelectedFileNotLocalPathFormatKey),
            UnsupportedFileTypeFormat: ResolveResource(locTypeName, UnsupportedFileTypeFormatKey),
            UnsupportedExtensionFormat: ResolveResource(locTypeName, UnsupportedExtensionFormatKey),
            CommandFailedFormat: ResolveResource(locTypeName, CommandFailedFormatKey),
            OpenedFormat: ResolveResource(locTypeName, OpenedFormatKey),
            SavedFormat: ResolveResource(locTypeName, SavedFormatKey),
            InsertedFormat: ResolveResource(locTypeName, InsertedFormatKey),
            SaveAsTitleFormat: ResolveResource(locTypeName, SaveAsTitleFormatKey));

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

    private static string ResolveCommand(string command) =>
        command.StartsWith("File_", StringComparison.Ordinal)
            ? ResolveAnyResource(command)
            : command;

    private static string ResolveResource(string? locTypeName, string key) =>
        string.IsNullOrWhiteSpace(locTypeName)
            ? ResolveAnyResource(key)
            : ResolveResourceFromCatalog(locTypeName, key);

    private static string ResolveAnyResource(string key)
    {
        var text = ResolveResourceFromCatalog(FreeWLocTypeName, key);
        return string.Equals(text, key, StringComparison.Ordinal)
            ? ResolveResourceFromCatalog(FreePLocTypeName, key)
            : text;
    }

    private static string ResolveResourceFromCatalog(string locTypeName, string key)
    {
        var type = Type.GetType(locTypeName, throwOnError: false);
        var get = type?.GetMethod(
            "Get",
            BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy,
            binder: null,
            types: [typeof(string)],
            modifiers: null);
        var text = get?.Invoke(null, [key]) as string;

        return string.IsNullOrEmpty(text) ? key : text;
    }
}
