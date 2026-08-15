using System.IO;
using System.Windows;
using Free.Shared.Shell;
using FreeW.App.Host.Editing;
using FreeW.App.Presentation.DocumentFragments;
using FreeW.Core.IO;
using FreeW.Core.Model;

namespace FreeW.App.Host;

internal sealed class WpfDocumentFragmentPickerPort(Window? owner) : IFreeWDocumentFragmentPickerPort
{
    public Task<FreeWDocumentFragmentPickerResult> PickAsync(
        FreeWDocumentFragmentImportRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = WpfFileDialogService.ShowOpenDialog(
            owner,
            request.PickerPlan.BuildWpfFilter(),
            request.PickerPlan.DefaultExtensionWithDot,
            title: request.PickerPlan.Title);
        if (!result.Chosen || string.IsNullOrWhiteSpace(result.FileName))
            return Task.FromResult(FreeWDocumentFragmentPickerResult.Cancelled);

        return Task.FromResult(FreeWDocumentFragmentPickerResult.Selected(
            Path.GetFileName(result.FileName),
            result.FileName,
            result.FileName));
    }
}

internal sealed class WpfDocumentFragmentSourceReaderPort : IFreeWDocumentFragmentSourceReaderPort
{
    public Task<byte[]> ReadBytesAsync(
        FreeWDocumentFragmentImportSelection selection,
        CancellationToken cancellationToken) =>
        FileByteReadWorkflow.ReadLocalPathBytesAsync(
            (string)selection.Source,
            cancellationToken);

    public Task<string> ReadTextAsync(
        FreeWDocumentFragmentImportSelection selection,
        CancellationToken cancellationToken) =>
        File.ReadAllTextAsync((string)selection.Source, cancellationToken);

    public void ResolveLinkedImagePreviews(
        FreeWDocumentFragmentImportSelection selection,
        TextDocument document) =>
        LinkedImagePreviewResolver.ResolveLocalPreviews(document, selection.LocalPath);
}

internal sealed class WpfDocumentFragmentInsertionPort(DocumentView editor) : IFreeWDocumentFragmentInsertionPort
{
    public FreeWDocumentFragmentInsertionResult Insert(FreeWDocumentFragmentInsertionRequest request)
    {
        editor.Focus();
        switch (request.Kind)
        {
            case FreeWDocumentFragmentInsertionKind.Document when request.Document is not null:
                editor.InsertDocument(request.Document);
                break;
            case FreeWDocumentFragmentInsertionKind.EmbeddedObject when request.EmbeddedObject is not null:
                editor.InsertEmbeddedObject(request.EmbeddedObject);
                break;
            default:
                return FreeWDocumentFragmentInsertionResult.NotApplied();
        }

        return FreeWDocumentFragmentInsertionResult.Success;
    }
}
