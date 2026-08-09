using Avalonia.Controls;
using Avalonia.Headless;
using Free.Shared.IO;
using FreeP.App.Avalonia;
using FreeP.App.Compositor;
using FreeP.Core.IO;
using FreeP.Core.Model;

namespace FreeP.App.Avalonia.Tests;

/// <summary>
/// Deterministic evidence for the app-owned half of the native picker boundary.
/// These tests substitute the picker result only; they do not claim to exercise GTK or Windows picker chrome.
/// </summary>
public sealed class NativePickerWorkflowEvidenceTests : IDisposable
{
    private readonly TestTemporaryDirectory _temporaryDirectory = new("FreeP.NativePickerWorkflowEvidence-");
    private string _tempDirectory => _temporaryDirectory.Path;

    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreePHeadlessApp).Assembly);

    [Fact]
    public async Task OpenPickerCancel_PreservesStateAndRestoresOwnerFocus()
    {
        var result = true;
        var focusBefore = 0;
        var focusAfter = 0;
        FileOpenPickerPlan? observedPlan = null;

        await RunOnUiThread(async () =>
        {
            var window = CreateWindow();
            focusBefore = window.OwnerFocusRestoreCountForTests;
            window.SetFilePickerOverridesForTests(
                plan =>
                {
                    observedPlan = plan;
                    return Task.FromResult<string?>(null);
                },
                null);

            result = await window.FileOpenAsyncForTests();
            focusAfter = window.OwnerFocusRestoreCountForTests;
            window.CurrentPath.Should().BeNull();
            window.IsDirty.Should().BeFalse();
        });

        result.Should().BeFalse();
        observedPlan.Should().NotBeNull();
        observedPlan!.FileTypes.SelectMany(type => type.Patterns)
            .Should().Contain(["*.pptx", "*.fxp"]);
        focusAfter.Should().Be(focusBefore + 1);
    }

    [Fact]
    public async Task OpenPickerExtensionSelection_LoadsLegacyFxpThroughSharedPlan()
    {
        var path = Path.Combine(_tempDirectory, "Selected.fxp");
        var source = Presentation.CreateEmpty();
        source.Properties.Title = "Legacy selected";
        FxpFormat.Write(source, path);

        var result = false;
        FileOpenPickerPlan? observedPlan = null;

        await RunOnUiThread(async () =>
        {
            var window = CreateWindow();
            window.SetFilePickerOverridesForTests(
                plan =>
                {
                    observedPlan = plan;
                    return Task.FromResult<string?>(path);
                },
                null);

            result = await window.FileOpenAsyncForTests();
            window.CurrentPath.Should().Be(path);
            window.Editor.Should().NotBeNull();
        });

        result.Should().BeTrue();
        observedPlan.Should().NotBeNull();
        observedPlan!.FileTypes.SelectMany(type => type.Patterns)
            .Should().Contain("*.fxp");
    }

    [Fact]
    public async Task OpenPickerError_PreservesCurrentDocumentAndRestoresOwnerFocus()
    {
        var path = Path.Combine(_tempDirectory, "invalid.pptx");
        File.WriteAllText(path, "not a presentation");

        var result = true;
        var focusBefore = 0;
        var focusAfter = 0;
        Exception? shownError = null;

        await RunOnUiThread(async () =>
        {
            var window = CreateWindow(showFileCommandErrorAsync: (_, exception) =>
            {
                shownError = exception;
                return Task.CompletedTask;
            });
            focusBefore = window.OwnerFocusRestoreCountForTests;
            window.SetFilePickerOverridesForTests(
                _ => Task.FromResult<string?>(path),
                null);

            result = await window.FileOpenAsyncForTests();
            focusAfter = window.OwnerFocusRestoreCountForTests;
            window.CurrentPath.Should().BeNull();
            window.IsDirty.Should().BeFalse();
        });

        result.Should().BeFalse();
        shownError.Should().NotBeNull();
        focusAfter.Should().Be(focusBefore + 1);
    }

    [Fact]
    public async Task SavePickerDecline_PreservesPathAndDirtyStateAndRestoresOwnerFocus()
    {
        var path = Path.Combine(_tempDirectory, "Current.pptx");
        var result = false;
        var focusBefore = 0;
        var focusAfter = 0;
        FileSavePickerPlan? observedPlan = null;

        await RunOnUiThread(async () =>
        {
            var window = CreateWindow();
            (await window.TrySavePresentationFileAsyncForTests(path)).Should().BeTrue();
            window.Editor.InsertSlide();
            window.IsDirty.Should().BeTrue();
            focusBefore = window.OwnerFocusRestoreCountForTests;
            window.SetFilePickerOverridesForTests(
                null,
                plan =>
                {
                    observedPlan = plan;
                    return Task.FromResult<string?>(null);
                });

            result = await window.FileSaveAsAsyncForTests();
            focusAfter = window.OwnerFocusRestoreCountForTests;
            window.CurrentPath.Should().Be(path);
            window.IsDirty.Should().BeTrue();
        });

        result.Should().BeFalse();
        observedPlan.Should().NotBeNull();
        observedPlan!.DefaultExtensionWithDot.Should().Be(".pptx");
        observedPlan.FileTypes.SelectMany(type => type.Patterns)
            .Should().Contain(["*.pptx", "*.fxp"]);
        focusAfter.Should().Be(focusBefore + 1);
    }

    [Fact]
    public async Task SavePickerError_DoesNotClearDirtyStateAndRestoresOwnerFocus()
    {
        var result = true;
        var focusBefore = 0;
        var focusAfter = 0;
        Exception? shownError = null;

        await RunOnUiThread(async () =>
        {
            var window = CreateWindow(showFileCommandErrorAsync: (_, exception) =>
            {
                shownError = exception;
                return Task.CompletedTask;
            });
            window.Editor.InsertSlide();
            window.IsDirty.Should().BeTrue();
            focusBefore = window.OwnerFocusRestoreCountForTests;
            window.SetFilePickerOverridesForTests(
                null,
                _ => Task.FromResult<string?>("\0invalid.pptx"));

            result = await window.FileSaveAsAsyncForTests();
            focusAfter = window.OwnerFocusRestoreCountForTests;
            window.CurrentPath.Should().BeNull();
            window.IsDirty.Should().BeTrue();
        });

        result.Should().BeFalse();
        shownError.Should().NotBeNull();
        focusAfter.Should().Be(focusBefore + 1);
    }

    [Fact]
    public async Task SavePickerUnsupportedExtension_IsRejectedBeforeWriting()
    {
        var result = true;
        var focusBefore = 0;
        var focusAfter = 0;
        Exception? shownError = null;

        await RunOnUiThread(async () =>
        {
            var window = CreateWindow(showFileCommandErrorAsync: (_, exception) =>
            {
                shownError = exception;
                return Task.CompletedTask;
            });
            window.Editor.InsertSlide();
            window.IsDirty.Should().BeTrue();
            focusBefore = window.OwnerFocusRestoreCountForTests;
            window.SetFilePickerOverridesForTests(
                null,
                _ => Task.FromResult<string?>(Path.Combine(_tempDirectory, "unsupported.txt")));

            result = await window.FileSaveAsAsyncForTests();
            focusAfter = window.OwnerFocusRestoreCountForTests;
            window.CurrentPath.Should().BeNull();
            window.IsDirty.Should().BeTrue();
        });

        result.Should().BeFalse();
        shownError.Should().NotBeNull();
        shownError!.Message.Should().Be(PresentationFileDialogPlanner.UnsupportedSavePathMessage);
        focusAfter.Should().Be(focusBefore + 1);
    }

    public void Dispose() => _temporaryDirectory.Dispose();

    private MainWindow CreateWindow(
        Func<string, Exception, Task>? showFileCommandErrorAsync = null) =>
        new(
            Array.Empty<string>(),
            loadRecentFilesStore: null,
            showFileCommandErrorAsync: showFileCommandErrorAsync ?? ((_, _) => Task.CompletedTask));

    private static async Task RunOnUiThread(Func<Task> action)
    {
        await Session.Dispatch(
            async () =>
            {
                await action();
                return true;
            },
            CancellationToken.None);
    }
}
