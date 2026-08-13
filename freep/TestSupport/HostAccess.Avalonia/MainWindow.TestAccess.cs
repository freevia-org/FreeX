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
    internal Task<bool> FileSaveAsAsyncForTests() => FileSaveAsAsync();

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
        _nativeOutputCancellation?.Cancel();
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
}
