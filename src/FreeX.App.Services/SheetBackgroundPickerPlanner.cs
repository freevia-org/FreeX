using FreeX.Core.IO;
using FreeX.Core.Model;
using Free.Shared.IO;
using FileDialogPickerTypeDescriptor = Free.Shared.IO.FileDialogPickerTypeDescriptor;

namespace FreeX.App.Services;

public sealed record SheetBackgroundOpenDialogPlan(
    bool CheckFileExists,
    bool Multiselect);

public sealed record SheetBackgroundOpenPickerPlan(
    IReadOnlyList<FileDialogPickerTypeDescriptor> FileTypes);

/// <summary>
/// UI-free sheet-background image picker policy shared by native shells.
/// Native shells still own titles, localization, and file reading; this owns supported
/// formats, picker descriptors, and background model construction.
/// </summary>
public static class SheetBackgroundPickerPlanner
{
    public const string ImagePickerDisplayName = "Images";

    public static IReadOnlyList<string> SupportedImagePatterns { get; } =
    [
        "*.png",
        "*.jpg",
        "*.jpeg",
        "*.bmp",
        "*.gif"
    ];

    public static SheetBackgroundOpenDialogPlan BuildOpenDialogPlan() =>
        new(CheckFileExists: true, Multiselect: false);

    public static SheetBackgroundOpenPickerPlan BuildOpenPickerPlan() =>
        new([BuildPickerType()]);

    public static FileDialogPickerTypeDescriptor BuildPickerType() =>
        new(ImagePickerDisplayName, SupportedImagePatterns);

    public static bool IsSupportedImagePath(string path) =>
        FilePathPolicy.GetExtensionOrEmpty(path).ToLowerInvariant() switch
        {
            ".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif" => true,
            _ => false
        };

    public static bool TryResolveContentTypeForPath(string path, out string contentType)
    {
        contentType = "";
        if (!IsSupportedImagePath(path))
            return false;

        var resolved = InsertPictureCommandFactory.ContentTypeForPath(path);
        if (resolved is null)
            return false;

        contentType = resolved;
        return true;
    }

    public static bool TryBuildBackgroundImage(
        byte[] imageBytes,
        string fileNameOrPath,
        out WorksheetBackgroundImage? background)
    {
        background = null;
        if (!TryResolveContentTypeForPath(fileNameOrPath, out var contentType))
            return false;

        background = new WorksheetBackgroundImage(
            imageBytes,
            contentType,
            FilePathPolicy.FileNameOrPath(fileNameOrPath));
        return true;
    }
}
