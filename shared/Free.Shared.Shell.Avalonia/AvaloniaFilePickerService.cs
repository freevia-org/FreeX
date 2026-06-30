using Avalonia.Platform.Storage;
using Free.Shared.IO;

namespace Free.Shared.Shell.Avalonia;

public sealed record AvaloniaFilePickerOpenRequest(
    string Title,
    IReadOnlyList<FilePickerFileType> FileTypes,
    bool AllowMultiple = false)
{
    public static AvaloniaFilePickerOpenRequest FromDescriptors(
        string title,
        IEnumerable<FileDialogPickerTypeDescriptor> fileTypes,
        bool allowMultiple = false) =>
        new(title, AvaloniaFilePickerTypeAdapter.ToFileTypes(fileTypes), allowMultiple);

    public static AvaloniaFilePickerOpenRequest FromFileTypes(
        string title,
        IEnumerable<FilePickerFileType> fileTypes,
        bool allowMultiple = false)
    {
        ArgumentNullException.ThrowIfNull(fileTypes);

        return new AvaloniaFilePickerOpenRequest(title, fileTypes.ToArray(), allowMultiple);
    }
}

public sealed record AvaloniaFilePickerSaveRequest(
    string Title,
    IReadOnlyList<FilePickerFileType> FileTypes,
    string? SuggestedFileName = null,
    string? DefaultExtensionWithoutDot = null,
    bool ShowOverwritePrompt = false,
    bool SuggestFirstFileType = false)
{
    public static AvaloniaFilePickerSaveRequest FromDescriptors(
        string title,
        IEnumerable<FileDialogPickerTypeDescriptor> fileTypes,
        string? suggestedFileName = null,
        string? defaultExtensionWithoutDot = null,
        bool showOverwritePrompt = false,
        bool suggestFirstFileType = false)
    {
        ArgumentNullException.ThrowIfNull(fileTypes);

        return new AvaloniaFilePickerSaveRequest(
            title,
            AvaloniaFilePickerTypeAdapter.ToFileTypes(fileTypes),
            suggestedFileName,
            defaultExtensionWithoutDot,
            showOverwritePrompt,
            suggestFirstFileType);
    }

    public static AvaloniaFilePickerSaveRequest FromSavePlan(
        string title,
        FileSavePickerPlan plan,
        bool showOverwritePrompt = false,
        bool suggestFirstFileType = false)
    {
        ArgumentNullException.ThrowIfNull(plan);

        return new AvaloniaFilePickerSaveRequest(
            title,
            AvaloniaFilePickerTypeAdapter.ToFileTypes(plan.FileTypes),
            plan.SuggestedFileName,
            plan.DefaultExtensionWithoutDot,
            showOverwritePrompt,
            suggestFirstFileType);
    }

    public static AvaloniaFilePickerSaveRequest FromFileTypes(
        string title,
        IEnumerable<FilePickerFileType> fileTypes,
        string? suggestedFileName = null,
        string? defaultExtensionWithoutDot = null,
        bool showOverwritePrompt = false,
        bool suggestFirstFileType = false)
    {
        ArgumentNullException.ThrowIfNull(fileTypes);

        return new AvaloniaFilePickerSaveRequest(
            title,
            fileTypes.ToArray(),
            suggestedFileName,
            defaultExtensionWithoutDot,
            showOverwritePrompt,
            suggestFirstFileType);
    }
}

public sealed class AvaloniaPickedStorageFile : IDisposable
{
    public AvaloniaPickedStorageFile(IStorageFile storageFile)
    {
        ArgumentNullException.ThrowIfNull(storageFile);

        StorageFile = storageFile;
        LocalPath = storageFile.TryGetLocalPath();
    }

    public IStorageFile StorageFile { get; }

    public string Name => StorageFile.Name;

    public string? LocalPath { get; }

    public void Dispose() => StorageFile.Dispose();
}

public static class AvaloniaFilePickerService
{
    public static bool CanOpen(IStorageProvider storageProvider)
    {
        ArgumentNullException.ThrowIfNull(storageProvider);

        return storageProvider.CanOpen;
    }

    public static bool CanSave(IStorageProvider storageProvider)
    {
        ArgumentNullException.ThrowIfNull(storageProvider);

        return storageProvider.CanSave;
    }

    public static async Task<IReadOnlyList<IStorageFile>> PickOpenFilesAsync(
        IStorageProvider storageProvider,
        AvaloniaFilePickerOpenRequest request)
    {
        ArgumentNullException.ThrowIfNull(storageProvider);
        Validate(request);

        if (!storageProvider.CanOpen)
            return Array.Empty<IStorageFile>();

        return await storageProvider.OpenFilePickerAsync(CreateOpenOptions(request));
    }

    public static async Task<IStorageFile?> PickSingleOpenFileAsync(
        IStorageProvider storageProvider,
        AvaloniaFilePickerOpenRequest request)
    {
        var files = await PickOpenFilesAsync(storageProvider, request with { AllowMultiple = false });
        return files.Count == 0 ? null : files[0];
    }

    public static async Task<AvaloniaPickedStorageFile?> PickSingleOpenFileWithLocalPathAsync(
        IStorageProvider storageProvider,
        AvaloniaFilePickerOpenRequest request)
    {
        var file = await PickSingleOpenFileAsync(storageProvider, request);
        return file is null ? null : new AvaloniaPickedStorageFile(file);
    }

    public static async Task<IStorageFile?> PickSaveFileAsync(
        IStorageProvider storageProvider,
        AvaloniaFilePickerSaveRequest request)
    {
        ArgumentNullException.ThrowIfNull(storageProvider);
        Validate(request);

        if (!storageProvider.CanSave)
            return null;

        return await storageProvider.SaveFilePickerAsync(CreateSaveOptions(request));
    }

    public static async Task<AvaloniaPickedStorageFile?> PickSaveFileWithLocalPathAsync(
        IStorageProvider storageProvider,
        AvaloniaFilePickerSaveRequest request)
    {
        var file = await PickSaveFileAsync(storageProvider, request);
        return file is null ? null : new AvaloniaPickedStorageFile(file);
    }

    private static FilePickerOpenOptions CreateOpenOptions(AvaloniaFilePickerOpenRequest request) =>
        new()
        {
            Title = request.Title,
            AllowMultiple = request.AllowMultiple,
            FileTypeFilter = request.FileTypes.ToArray(),
        };

    private static FilePickerSaveOptions CreateSaveOptions(AvaloniaFilePickerSaveRequest request)
    {
        var fileTypes = request.FileTypes.ToArray();
        return new FilePickerSaveOptions
        {
            Title = request.Title,
            SuggestedFileName = request.SuggestedFileName,
            DefaultExtension = request.DefaultExtensionWithoutDot,
            FileTypeChoices = fileTypes,
            SuggestedFileType = request.SuggestFirstFileType ? fileTypes.FirstOrDefault() : null,
            ShowOverwritePrompt = request.ShowOverwritePrompt,
        };
    }

    private static void Validate(AvaloniaFilePickerOpenRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Title);
        ArgumentNullException.ThrowIfNull(request.FileTypes);
    }

    private static void Validate(AvaloniaFilePickerSaveRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Title);
        ArgumentNullException.ThrowIfNull(request.FileTypes);
    }
}
