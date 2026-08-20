using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Headless;
using Free.Shared.AppServices;
using FreeP.App.Compositor;
using FreeP.Core.Model;
using ModelParagraph = FreeP.Core.Model.Paragraph;
using ModelRun = FreeP.Core.Model.Run;

namespace FreeP.App.Avalonia.Tests;

/// <summary>
/// External activation is the only inline embedded-object route on Linux and macOS, and the
/// fallback on Windows whenever no native server can be hosted. It writes the live model but
/// reported nothing to the shell, so an edit saved in the external application left the document
/// looking clean.
///
/// The in-place route is taken out of the picture here by making
/// <see cref="FreeP.App.Ole.Windows.WindowsOleInPlaceEngine.PayloadCreatedObserver"/> throw, which
/// is how a machine with no usable in-place server behaves: the host factory returns null and the
/// editor falls back. <see cref="OleActivationService.ExternalActivationOverrideForTests"/> then
/// substitutes the OS boundary so no real application is launched.
/// </summary>
public sealed class InlineOleExternalActivationEndToEndTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreePHeadlessApp).Assembly);

    static InlineOleExternalActivationEndToEndTests()
    {
        if (AppProduct.Current is null)
            AppProduct.Current = new AppProductIdentity("FreeP", "FREEP_DIAGNOSTICS", "FreeP");
    }

    private static Task<bool> OnUiThread(Action action) =>
        Session.Dispatch(action, CancellationToken.None)
            .ContinueWith(task => task.Exception is null, CancellationToken.None);

    private static SlideShape CreateInlineOleShape(byte[] embeddedBytes)
    {
        var body = new TextBody();
        var paragraph = new ModelParagraph();
        paragraph.Runs.Add(new ModelRun
        {
            Text = "￼",
            InlineOleObject = new InlineOleObjectInfo
            {
                EmbeddedBytes = embeddedBytes,
                FileName = "Book.xlsx",
                ClassName = "Excel.Sheet.12",
            },
        });
        body.Paragraphs.Add(paragraph);

        return new SlideShape
        {
            Id = 631,
            OffsetXEmu = 0,
            OffsetYEmu = 0,
            ExtentCxEmu = 2743200L,
            ExtentCyEmu = 1371600L,
            TextBody = body,
        };
    }

    [Fact]
    public async Task ExternalInlineActivation_MarksDocumentDirty_WhenTheApplicationSavesThePayload()
    {
        byte[] rewritten = [4, 4, 4, 4];
        var shape = CreateInlineOleShape([1, 2, 3]);
        var store = new RecordingTempFileStore();
        var launcher = new ControllableLauncher();
        MainWindow? window = null;
        var activated = false;

        FreeP.App.Ole.Windows.WindowsOleInPlaceEngine.PayloadCreatedObserver =
            _ => throw new InvalidOperationException("no in-place server on this machine");
        OleActivationService.ExternalActivationOverrideForTests = () => (store, launcher);
        try
        {
            var ran = await OnUiThread(() =>
            {
                window = new MainWindow(Array.Empty<string>());
                var slide = window.Editor.CurrentSlide!;
                slide.Shapes.Clear();
                slide.Shapes.Add(shape);

                window.ActivateShapeTextEditForTests(shape.Id);
                activated = window.TryActivateInlineOleObjectForTests();
            });

            if (!ran)
                return; // no headless drawing backend in this environment

            activated.Should().BeTrue(
                "with no in-place host available the editor must fall back to external activation");
            window!.IsDirty.Should().BeFalse();

            // The "application" saves and exits.
            File.WriteAllBytes(store.LastPath!, rewritten);
            launcher.LastProcess!.Exit();

            (await WaitForAsync(() => window!.IsDirty)).Should().BeTrue(
                "an external application's save must be reported to the shell, or the edit is " +
                "silently lost when the user closes an apparently clean document");
            LiveInlineOleBytes(shape).Should().Equal(rewritten);
        }
        finally
        {
            FreeP.App.Ole.Windows.WindowsOleInPlaceEngine.PayloadCreatedObserver = null;
            OleActivationService.ExternalActivationOverrideForTests = null;
        }
    }

    /// <summary>
    /// Sibling no-regression test: an application that exits without saving must leave the
    /// document clean.
    /// </summary>
    [Fact]
    public async Task ExternalInlineActivation_DoesNotMarkDocumentDirty_WhenThePayloadIsUnchanged()
    {
        var shape = CreateInlineOleShape([1, 2, 3]);
        var store = new RecordingTempFileStore();
        var launcher = new ControllableLauncher();
        MainWindow? window = null;

        FreeP.App.Ole.Windows.WindowsOleInPlaceEngine.PayloadCreatedObserver =
            _ => throw new InvalidOperationException("no in-place server on this machine");
        OleActivationService.ExternalActivationOverrideForTests = () => (store, launcher);
        try
        {
            var ran = await OnUiThread(() =>
            {
                window = new MainWindow(Array.Empty<string>());
                var slide = window.Editor.CurrentSlide!;
                slide.Shapes.Clear();
                slide.Shapes.Add(shape);

                window.ActivateShapeTextEditForTests(shape.Id);
                window.TryActivateInlineOleObjectForTests();
            });

            if (!ran)
                return; // no headless drawing backend in this environment

            launcher.LastProcess!.Exit();
            await WaitForAsync(() => launcher.LastProcess!.IsCompleted);

            window!.IsDirty.Should().BeFalse(
                "closing the external application without saving must leave the document clean");
            LiveInlineOleBytes(shape).Should().Equal([1, 2, 3]);
        }
        finally
        {
            FreeP.App.Ole.Windows.WindowsOleInPlaceEngine.PayloadCreatedObserver = null;
            OleActivationService.ExternalActivationOverrideForTests = null;
        }
    }

    private static byte[] LiveInlineOleBytes(SlideShape shape) =>
        shape.TextBody!.Paragraphs
            .SelectMany(paragraph => paragraph.Runs)
            .Select(run => run.InlineOleObject)
            .OfType<InlineOleObjectInfo>()
            .Single()
            .EmbeddedBytes;

    /// <summary>The commit runs on the activation session's continuation, so give it a moment.</summary>
    private static async Task<bool> WaitForAsync(Func<bool> condition)
    {
        for (int attempt = 0; attempt < 200; attempt++)
        {
            if (condition())
                return true;

            await Task.Delay(25);
        }

        return condition();
    }

    private sealed class RecordingTempFileStore : IOleActivationTempFileStore
    {
        public string? LastPath { get; private set; }

        public IOleActivationTempFile Materialize(OleActivationPlan plan)
        {
            var path = Path.Combine(
                Path.GetTempPath(),
                "freep-ole-external-test-" + Guid.NewGuid().ToString("N") + "." + plan.Extension);
            File.WriteAllBytes(path, plan.Payload);
            LastPath = path;
            return new TempFile(path);
        }

        private sealed class TempFile(string path) : IOleActivationTempFile
        {
            public string Path { get; } = path;

            public byte[] ReadAllBytes() => File.ReadAllBytes(Path);

            public void Dispose()
            {
                try
                {
                    File.Delete(Path);
                }
                catch
                {
                    // A leftover temp file must never fail a test.
                }
            }
        }
    }

    private sealed class ControllableLauncher : IOleActivationLauncher
    {
        public ControllableProcess? LastProcess { get; private set; }

        public IOleActivationProcess Launch(string path) => LastProcess = new ControllableProcess();
    }

    private sealed class ControllableProcess : IOleActivationProcess
    {
        private readonly TaskCompletionSource _exit = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task ExitTask => _exit.Task;

        public bool SupportsEditBack => true;

        public bool IsCompleted { get; private set; }

        public void Exit() => _exit.TrySetResult();

        public void Dispose() => IsCompleted = true;
    }
}
