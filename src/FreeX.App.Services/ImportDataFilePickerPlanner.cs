using FreeX.Core.IO;
using FileDialogFilterBuilder = Free.Shared.IO.FileDialogFilterBuilder;
using FileDialogPickerTypeDescriptor = Free.Shared.IO.FileDialogPickerTypeDescriptor;
using FileFormatDialogDescriptorAdapter = Free.Shared.IO.FileFormatDialogDescriptorAdapter;

namespace FreeX.App.Services;

public sealed record ImportDataOpenDialogPlan(
    IReadOnlyList<IFileAdapter> Adapters,
    string Filter,
    bool CheckFileExists,
    bool Multiselect);

public sealed record ImportDataOpenPickerPlan(IReadOnlyList<FileDialogPickerTypeDescriptor> FileTypes);

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
            : FileDialogFilterBuilder.BuildOpenFilter(
                FileFormatDialogDescriptorAdapter.ToOpenDialogDescriptors(
                    importAdapters.SelectMany(adapter => adapter.Formats)));
        return new ImportDataOpenDialogPlan(
            importAdapters,
            filter,
            CheckFileExists: true,
            Multiselect: false);
    }

    public static ImportDataOpenPickerPlan BuildTextOpenPickerPlan(
        string displayName = TextDataPickerDisplayName) =>
        new([new FileDialogPickerTypeDescriptor(displayName, TextImportPatterns)]);

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
