using Avalonia.Platform.Storage;
using Free.Shared.Shell.Avalonia;
using FreeW.App.Avalonia.Editing;
using FreeW.App.Presentation.DocumentFragments;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia;

internal sealed class AvaloniaDocumentFragmentPickerPort(IStorageProvider storageProvider)
    : IFreeWDocumentFragmentPickerPort
{
    public async Task<FreeWDocumentFragmentPickerResult> PickAsync(
        FreeWDocumentFragmentImportRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var file = await AvaloniaFilePickerService.PickSingleOpenFileWithLocalPathAsync(
            storageProvider,
            AvaloniaFilePickerOpenRequest.FromDescriptors(
                request.PickerPlan.Title,
                request.PickerPlan.FileTypes));
        if (file?.LocalPath is not { } localPath)
            return FreeWDocumentFragmentPickerResult.Cancelled;

        return FreeWDocumentFragmentPickerResult.Selected(
            Path.GetFileName(localPath),
            localPath,
            localPath);
    }
}

internal sealed class AvaloniaDocumentFragmentSourceReaderPort : IFreeWDocumentFragmentSourceReaderPort
{
    public Task<byte[]> ReadBytesAsync(
        FreeWDocumentFragmentImportSelection selection,
        CancellationToken cancellationToken) =>
        File.ReadAllBytesAsync((string)selection.Source, cancellationToken);

    public Task<string> ReadTextAsync(
        FreeWDocumentFragmentImportSelection selection,
        CancellationToken cancellationToken) =>
        File.ReadAllTextAsync((string)selection.Source, cancellationToken);

    public void ResolveLinkedImagePreviews(
        FreeWDocumentFragmentImportSelection selection,
        TextDocument document)
    {
    }
}

internal sealed class AvaloniaDocumentFragmentInsertionPort(DocumentView editor)
    : IFreeWDocumentFragmentInsertionPort
{
    public FreeWDocumentFragmentInsertionResult Insert(FreeWDocumentFragmentInsertionRequest request)
    {
        switch (request.Kind)
        {
            case FreeWDocumentFragmentInsertionKind.Document when request.Document is not null:
                editor.InsertDocument(request.Document);
                break;
            case FreeWDocumentFragmentInsertionKind.PlainText when request.PlainText is not null:
                editor.InsertQuickPartText(request.PlainText);
                break;
            case FreeWDocumentFragmentInsertionKind.EmbeddedObject when request.EmbeddedObject is not null:
                editor.InsertEmbeddedObject(request.EmbeddedObject);
                break;
            default:
                return FreeWDocumentFragmentInsertionResult.NotApplied();
        }

        editor.Focus();
        return FreeWDocumentFragmentInsertionResult.Success;
    }
}
