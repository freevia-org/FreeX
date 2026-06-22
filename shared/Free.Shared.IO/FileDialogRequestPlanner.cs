namespace Free.Shared.IO;

public sealed record FileOpenDialogPlan(string Filter, string DefaultExtensionWithDot);

public sealed record FileSaveDialogPlan(
    string Filter,
    string SuggestedFileName,
    string DefaultExtensionWithDot,
    string DefaultExtensionWithoutDot,
    int FilterIndex);

public sealed record FileOpenPickerPlan(IReadOnlyList<FileDialogPickerTypeDescriptor> FileTypes);

public sealed record FileSavePickerPlan(
    IReadOnlyList<FileDialogPickerTypeDescriptor> FileTypes,
    string SuggestedFileName,
    string DefaultExtensionWithDot,
    string DefaultExtensionWithoutDot);

/// <summary>
/// Plans the neutral open/save dialog data shared by WPF, Avalonia, and app-specific adapter facades.
/// </summary>
public static class FileDialogRequestPlanner
{
    public static FileOpenDialogPlan BuildOpenDialogPlan(
        IEnumerable<FileDialogFormatDescriptor> formats,
        string allSupportedName = "All supported files")
    {
        ArgumentNullException.ThrowIfNull(formats);

        var formatRows = formats.ToList();
        var openRows = formatRows.Where(format => format.CanOpen).ToList();
        return new FileOpenDialogPlan(
            FileDialogFilterBuilder.BuildOpenFilter(formatRows, allSupportedName),
            FileDialogFilterBuilder.GetDefaultExtension(openRows));
    }

    public static FileSaveDialogPlan BuildSaveDialogPlan(
        IEnumerable<FileDialogFormatDescriptor> formats,
        string suggestedFileName,
        string defaultExtensionWithDot)
    {
        ArgumentNullException.ThrowIfNull(formats);

        var formatRows = formats.ToList();
        var normalizedExtension = FileDialogFilterBuilder.NormalizeExtension(defaultExtensionWithDot);
        return new FileSaveDialogPlan(
            FileDialogFilterBuilder.BuildSaveFilter(formatRows),
            suggestedFileName,
            normalizedExtension,
            WithoutLeadingDot(normalizedExtension),
            FileDialogFilterBuilder.FindSaveFilterIndex(formatRows, normalizedExtension));
    }

    public static FileOpenPickerPlan BuildOpenPickerPlan(
        IEnumerable<FileDialogFormatDescriptor> formats,
        string allSupportedName = "All supported files")
    {
        ArgumentNullException.ThrowIfNull(formats);

        return new FileOpenPickerPlan(
            FileDialogFilterBuilder.BuildOpenPickerTypes(formats.ToList(), allSupportedName));
    }

    public static FileSavePickerPlan BuildSavePickerPlan(
        IEnumerable<FileDialogFormatDescriptor> formats,
        string? sourceName,
        string fallbackDisplayName,
        string defaultExtensionWithDot,
        string? preferredFirstExtension = null)
    {
        ArgumentNullException.ThrowIfNull(formats);

        var formatRows = formats.ToList();
        var normalizedExtension = FileDialogFilterBuilder.NormalizeExtension(defaultExtensionWithDot);
        var normalizedPreferred = preferredFirstExtension is null
            ? normalizedExtension
            : FileDialogFilterBuilder.NormalizeExtension(preferredFirstExtension);
        return new FileSavePickerPlan(
            FileDialogFilterBuilder.BuildSavePickerTypes(formatRows, normalizedPreferred),
            BuildSuggestedSaveAsFileName(sourceName, fallbackDisplayName, normalizedExtension),
            normalizedExtension,
            WithoutLeadingDot(normalizedExtension));
    }

    public static string BuildSuggestedSaveAsFileName(
        string? sourceName,
        string fallbackDisplayName,
        string defaultExtensionWithDot)
    {
        var normalizedExtension = FileDialogFilterBuilder.NormalizeExtension(defaultExtensionWithDot);
        var effectiveSourceName = string.IsNullOrWhiteSpace(sourceName)
            ? fallbackDisplayName
            : sourceName;
        var baseName = Path.GetFileNameWithoutExtension(effectiveSourceName);
        if (string.IsNullOrWhiteSpace(baseName))
            baseName = "Document";

        return baseName + normalizedExtension;
    }

    private static string WithoutLeadingDot(string extension) =>
        extension.StartsWith(".", StringComparison.Ordinal)
            ? extension[1..]
            : extension;
}
