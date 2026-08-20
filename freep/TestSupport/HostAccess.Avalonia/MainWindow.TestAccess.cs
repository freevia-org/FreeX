using Avalonia.Controls;
using Avalonia.Input;
using Free.Shared.AppServices;
using Free.Shared.AppServices.Printing;
using Free.Shared.IO;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Avalonia;

public sealed partial class MainWindow
{
    private Func<FileOpenPickerPlan, Task<string?>>? _openPickerOverrideForTests;
    private Func<FileSavePickerPlan, Task<string?>>? _savePickerOverrideForTests;

    internal Func<FileSavePickerPlan, Task<VideoPickerSelectionForTests?>>? VideoPickerOverrideForTests { get; set; }
    internal Func<HyperlinkDialogRequest, Task<Hyperlink?>>? HyperlinkDialogResultProviderForTests { get; set; }
    internal Func<Task<PresentationPictureBulletPayload?>>? PictureBulletPayloadProviderForTests { get; set; }

    partial void ResolveOpenPickerOverride(FileOpenPickerPlan plan, ref Task<string?>? selectionTask)
    {
        if (_openPickerOverrideForTests is { } provider)
            selectionTask = provider(plan);
    }

    partial void ResolveSavePickerOverride(FileSavePickerPlan plan, ref Task<string?>? selectionTask)
    {
        if (_savePickerOverrideForTests is { } provider)
            selectionTask = provider(plan);
    }

    partial void ResolveVideoPickerOverride(
        FileSavePickerPlan plan,
        ref Task<PresentationFilePickerResult>? resultTask)
    {
        if (VideoPickerOverrideForTests is { } provider)
            resultTask = ResolveVideoPickerResultAsync(provider(plan));
    }

    private static async Task<PresentationFilePickerResult> ResolveVideoPickerResultAsync(
        Task<VideoPickerSelectionForTests?> selectionTask)
    {
        var selection = await selectionTask;
        if (selection is null)
            return PresentationFilePickerResult.Cancelled;
        return selection.LocalPath is { } path
            ? PresentationFilePickerResult.Selected(path)
            : PresentationFilePickerResult.NonLocal(
                SisterAppFileTextPlanner.FormatSelectedFileNotLocalPath(
                    FileText,
                    PresentationExportPlanner.VideoExportCommandText));
    }

    partial void ResolvePictureBulletPayloadOverride(
        ref Task<PresentationPictureBulletPayload?>? payloadTask)
    {
        if (PictureBulletPayloadProviderForTests is { } provider)
            payloadTask = provider();
    }

    partial void ResolveHyperlinkDialogOverride(
        HyperlinkDialogRequest request,
        ref Task<Hyperlink?>? resultTask)
    {
        if (HyperlinkDialogResultProviderForTests is { } provider)
            resultTask = provider(request);
    }

    internal void ShowBackstageForTests() => ShowBackstage();
    internal bool ActivateBackstageEntryForTests(string label) => _backstage.TryActivateEntry(label);
    internal Control? CurrentBackstagePaneContentForTests => _backstage.CurrentPaneContent;
    internal bool HandleBackstageKeyForTests(Key key) => _backstage.HandleKey(key);
    internal Task<bool> FileOpenAsyncForTests() => FileOpenAsync();
    internal Task<bool> FileSaveAsyncForTests() => FileSaveAsync();
    internal Task<bool> FileSaveAsAsyncForTests() => FileSaveAsAsync();
    internal async Task<bool> OpenPathAsyncForTests(string path) =>
        (await _fileSession.OpenPathAsync(path)).Succeeded;
    internal void MarkDirtyForTests() => _fileSession.MarkDirty();
    internal Presentation PresentationForTests => _presentation;

    internal void SetFilePickerOverridesForTests(
        Func<FileOpenPickerPlan, Task<string?>>? openPicker,
        Func<FileSavePickerPlan, Task<string?>>? savePicker)
    {
        _openPickerOverrideForTests = openPicker;
        _savePickerOverrideForTests = savePicker;
    }

    internal Task<bool> FileExportVideoAsyncForTests() => FileExportVideoAsync();

    internal void CancelNativeOutputForTests()
    {
        _videoExportSession.CancelActiveExport();
        _printCancellation?.Cancel();
    }

    internal bool ApplyBackstageCustomPrintRangeForTests(string rangeText) =>
        _backstage.ApplyCustomPrintRangeForTests(rangeText);
    internal IReadOnlyList<(string AutomationId, bool IsEnabled)> BackstagePrintActionsForTests =>
        _backstage.PrintActionsForTests;
    internal bool InvokeBackstagePrintActionForTests(string automationId) =>
        _backstage.InvokePrintActionForTests(automationId);
    internal Task<PrintSubmissionResult> BackstagePrintOperationForTests => _backstagePrintOperation;

    internal sealed record VideoPickerSelectionForTests(string? LocalPath);

#if FREEP_WINDOWS_CAPTURE
    /// <summary>
    /// Forwards to the private native in-place OLE entry point, so end-to-end tests can drive the
    /// exact call site that composes <c>onPayloadUpdated</c> for <c>AvaloniaOleInPlaceHost.TryShow</c>
    /// instead of only exercising the composition helper in isolation.
    /// </summary>
    internal bool TryOpenOleInPlaceForTests(SlideShape shape) => TryOpenOleInPlace(shape);

    /// <summary>
    /// Disposes the active in-place host the same way a routine gesture (reselect, navigate
    /// slides) does, driving <see cref="FreeP.App.Ole.Windows.WindowsOleInPlaceEngine.CloseAndCommit"/>.
    /// </summary>
    internal void CloseActiveOleHostForTests() => CloseActiveOleHost();

    /// <summary>
    /// Opens the in-canvas rich-text editor on a shape, the way a double-click gesture does, so
    /// end-to-end tests can reach the inline (in-text) embedded-object route that
    /// <see cref="TryOpenOleInPlaceForTests"/>'s slide-level route never touches.
    /// </summary>
    internal bool ActivateShapeTextEditForTests(uint shapeId)
    {
        _textEditor?.Activate(shapeId);
        return _textEditor?.IsRichTextEditActive == true;
    }

    /// <summary>
    /// Forwards to the same inline embedded-object entry point the command surface calls
    /// (<c>TryOpenInlineEmbeddedObject</c>), so tests drive the real inline-OLE host factory
    /// composed in <c>WireInteraction</c> rather than a hand-rolled copy of that wiring.
    /// </summary>
    internal bool TryActivateInlineOleObjectForTests() =>
        _textEditor?.TryActivateInlineOleObject() == true;

    /// <summary>
    /// Ends the text edit without committing it -- the Escape route -- which still closes the
    /// inline in-place host and so drives <c>CloseAndCommit</c> for the inline payload.
    /// </summary>
    internal void CancelShapeTextEditForTests() => _textEditor?.Cancel();
#endif
}
