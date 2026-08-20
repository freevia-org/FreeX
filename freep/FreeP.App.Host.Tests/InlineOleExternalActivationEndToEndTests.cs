using System.IO;
using System.Windows.Threading;
using FreeP.App.Compositor;
using FreeP.Core.Model;
using ModelParagraph = FreeP.Core.Model.Paragraph;
using ModelRun = FreeP.Core.Model.Run;

namespace FreeP.App.Host.Tests;

/// <summary>
/// The in-place route is only available when a native OLE server can be hosted; every other case
/// -- no server registered, and every non-Windows shell -- falls back to external activation,
/// where the payload is written to a temp file, opened in the associated application, and read
/// back when that application exits. That route writes the live model but reported nothing to the
/// shell, so a user could edit an embedded workbook in Excel, save it, close FreeP and be told
/// there was nothing to save.
///
/// <see cref="OleActivationService.ExternalActivationOverrideForTests"/> substitutes the OS
/// boundary (temp-file store + process launcher) so the real editor path runs end to end without
/// launching an application: the real <c>InCanvasTextEditor.TryActivateInlineOleObject</c>, the
/// real <c>EditingSession.TryActivateInlineOleObject</c>, and the real commit-on-exit path.
/// </summary>
public sealed class InlineOleExternalActivationEndToEndTests
{
    private static SlideShape AddInlineOleShape(MainWindow window, byte[] embeddedBytes)
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

        var slide = window.Editor.CurrentSlide!;
        slide.Shapes.Clear();
        var shape = new SlideShape
        {
            Id = 721,
            OffsetXEmu = 0,
            OffsetYEmu = 0,
            ExtentCxEmu = 2743200L,
            ExtentCyEmu = 1371600L,
            TextBody = body,
        };
        slide.Shapes.Add(shape);
        return shape;
    }

    [StaFact]
    public void ExternalInlineActivation_MarksDocumentDirty_WhenTheApplicationSavesThePayload()
    {
        var window = new MainWindow(new FreePOptions(), messageService: TestUserMessageService.DiscardUnsavedChanges);
        byte[] rewritten = [4, 4, 4, 4];
        var shape = AddInlineOleShape(window, [1, 2, 3]);
        var store = new RecordingTempFileStore();
        var launcher = new ControllableLauncher();

        OleActivationService.ExternalActivationOverrideForTests = () => (store, launcher);
        try
        {
            window.IsDirty.Should().BeFalse();
            window.SlideCanvas.TextEditor!.Activate(shape.Id);

            window.SlideCanvas.TextEditor!.TryActivateInlineOleObject().Should().BeTrue(
                "the caret sits on the inline embedded object, so activation must start a session");

            // The "application" saves and exits.
            File.WriteAllBytes(store.LastPath!, rewritten);
            launcher.LastProcess!.Exit();

            WaitFor(() => window.IsDirty).Should().BeTrue(
                "an external application's save must be reported to the shell, or the edit is " +
                "silently lost when the user closes an apparently clean document");
            LiveInlineOleBytes(shape).Should().Equal(rewritten);
        }
        finally
        {
            OleActivationService.ExternalActivationOverrideForTests = null;
            window.Close();
        }
    }

    /// <summary>
    /// Sibling no-regression test: an application that exits without touching the payload -- open,
    /// look, close -- must leave the document clean.
    /// </summary>
    [StaFact]
    public void ExternalInlineActivation_DoesNotMarkDocumentDirty_WhenThePayloadIsUnchanged()
    {
        var window = new MainWindow(new FreePOptions(), messageService: TestUserMessageService.DiscardUnsavedChanges);
        var shape = AddInlineOleShape(window, [1, 2, 3]);
        var store = new RecordingTempFileStore();
        var launcher = new ControllableLauncher();

        OleActivationService.ExternalActivationOverrideForTests = () => (store, launcher);
        try
        {
            window.SlideCanvas.TextEditor!.Activate(shape.Id);
            window.SlideCanvas.TextEditor!.TryActivateInlineOleObject().Should().BeTrue();

            launcher.LastProcess!.Exit();
            WaitFor(() => launcher.LastProcess!.IsCompleted);

            window.IsDirty.Should().BeFalse(
                "closing the external application without saving must leave the document clean");
            LiveInlineOleBytes(shape).Should().Equal([1, 2, 3]);
        }
        finally
        {
            OleActivationService.ExternalActivationOverrideForTests = null;
            window.Close();
        }
    }

    private static byte[] LiveInlineOleBytes(SlideShape shape) =>
        shape.TextBody!.Paragraphs
            .SelectMany(paragraph => paragraph.Runs)
            .Select(run => run.InlineOleObject)
            .OfType<InlineOleObjectInfo>()
            .Single()
            .EmbeddedBytes;

    /// <summary>
    /// The commit runs on the session's continuation, so the assertion waits for it while keeping
    /// the WPF dispatcher pumping (the shell may marshal its notification onto the UI thread).
    /// </summary>
    private static bool WaitFor(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
                return true;

            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);
            Thread.Sleep(15);
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
