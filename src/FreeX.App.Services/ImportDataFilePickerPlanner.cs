using FreeX.Core.IO;

namespace FreeX.App.Services;

public sealed record ImportDataOpenDialogPlan(
    IReadOnlyList<IFileAdapter> Adapters,
    string Filter,
    bool CheckFileExists,
    bool Multiselect);

public sealed record ImportDataOpenPickerPlan(IReadOnlyList<FilePickerTypeDescriptor> FileTypes);

/// <summary>
/// UI-free picker policy for Data > Get Data local file imports. Hosts still own dialog titles,
/// localization, native storage objects, file reading, and import execution.
/// </summary>
public static class ImportDataFilePickerPlanner
{
    public const string TextDataPickerDisplayName = "Text/CSV files";

    public static IReadOnlyList<string> AdapterImportExtensions { get; } =
    [
        ".csv",
        ".txt",
        ".tsv",
        ".tab",
        ".xml"
    ];

    public static IReadOnlyList<string> TextImportPatterns { get; } =
    [
        "*.csv",
        "*.tsv",
        "*.tab",
        "*.txt"
    ];

    public static ImportDataOpenDialogPlan BuildAdapterOpenDialogPlan(IEnumerable<IFileAdapter> adapters)
    {
        var importAdapters = SelectAdapterImportAdapters(adapters);
        var filter = importAdapters.Count == 0
            ? string.Empty
            : FileDialogFilterBuilder.BuildOpenFilter(importAdapters);
        return new ImportDataOpenDialogPlan(
            importAdapters,
            filter,
            CheckFileExists: true,
            Multiselect: false);
    }

    public static ImportDataOpenPickerPlan BuildTextOpenPickerPlan(
        string displayName = TextDataPickerDisplayName) =>
        new([new FilePickerTypeDescriptor(displayName, TextImportPatterns)]);

    public static IReadOnlyList<IFileAdapter> SelectAdapterImportAdapters(IEnumerable<IFileAdapter> adapters)
    {
        ArgumentNullException.ThrowIfNull(adapters);

        return adapters
            .Where(adapter => adapter.Formats.Any(IsAdapterImportFormat))
            .ToList();
    }

    private static bool IsAdapterImportFormat(FileFormatDescriptor format) =>
        format.CanOpen &&
        AdapterImportExtensions.Contains(
            FileFormatResolver.NormalizeExtension(format.Extension),
            StringComparer.OrdinalIgnoreCase);
}
