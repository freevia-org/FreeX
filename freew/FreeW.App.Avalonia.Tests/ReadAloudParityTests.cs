using System.Threading;
using Avalonia;
using Avalonia.Headless;
using FluentAssertions;
using Free.Shared.Ribbon;
using FreeW.App.Avalonia;
using FreeW.App.Avalonia.Editing;
using FreeW.App.Avalonia.Ribbon;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia.Tests;

/// <summary>
/// AV-REVIEW: focused parity coverage for Review &gt; Speech &gt; Read Aloud. The WPF authority is covered by
/// FreeW.App.Host.Tests/ReadAloudTests; these tests cover the Avalonia host wiring, caret mapping, and the
/// portable speech adapter's deterministic fallback/cancellation contract.
/// </summary>
public sealed class ReadAloudParityTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreeWHeadlessApp).Assembly);

    [Fact]
    public async Task Avalonia_caret_mapping_matches_Wpf_for_blank_and_table_blocks()
    {
        var index = -1;
        await OnUiThread(() =>
        {
            var document = TextDocument.CreateEmpty();
            document.Blocks.Clear();
            document.Blocks.Add(new Paragraph("First"));
            document.Blocks.Add(new Paragraph("   "));

            var table = new Table();
            var row = new TableRow();
            var left = new TableCell();
            left.Paragraphs.Add(new Paragraph("Cell one"));
            var right = new TableCell();
            right.Paragraphs.Add(new Paragraph("Cell two"));
            row.Cells.Add(left);
            row.Cells.Add(right);
            table.Rows.Add(row);
            document.Blocks.Add(table);
            document.Blocks.Add(new Paragraph("Last"));

            var view = new DocumentView();
            view.LoadDocument(document);
            view.MoveCaretToBlockForTest(3, 0);
            index = view.ReadAloudStartSegmentIndex();
        });

        // The caret is in the final paragraph: First + Cell one + Cell two have already been passed.
        index.Should().Be(3);
    }

    [Fact]
    public void Avalonia_ribbon_read_aloud_routes_callback_and_checked_state()
    {
        var active = false;
        var callbacks = NoopCallbacks() with
        {
            ToggleReadAloud = () => active = !active,
            IsReadAloudActive = () => active,
        };
        var registry = FreeWRibbon.BuildRegistry(new DocumentView(), callbacks, out _);

        registry.TryGet(new RibbonCommandId("freew.read-aloud"), out var command).Should().BeTrue();
        var stateful = command.Should().BeAssignableTo<IRibbonStatefulCommand>().Subject;
        stateful.GetState().IsChecked.Should().BeFalse();
        command!.Execute(RibbonCommandContext.Empty);
        stateful.GetState().IsChecked.Should().BeTrue();
        command.Execute(RibbonCommandContext.Empty);
        stateful.GetState().IsChecked.Should().BeFalse();
    }

    [Fact]
    public void Avalonia_process_engine_without_tool_completes_asynchronously_and_does_not_stall()
    {
        var posted = new Queue<Action>();
        using var engine = new AvaloniaSpeechEngine(_ => null, posted.Enqueue);
        var completed = false;

        engine.SpeakAsync("No installed speech tool", () => completed = true);

        completed.Should().BeFalse();
        posted.Should().ContainSingle();
        posted.Dequeue()();
        completed.Should().BeTrue();
        engine.IsBackendAvailable.Should().BeFalse();
        engine.SupportsPause.Should().BeFalse();
    }

    [Fact]
    public void Controller_does_not_claim_pause_when_Avalonia_backend_cannot_pause()
    {
        var posted = new Queue<Action>();
        using var engine = new AvaloniaSpeechEngine(_ => null, posted.Enqueue);
        var controller = new ReadAloudController(engine);
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(new Paragraph("One"));

        controller.Start(document);
        controller.TogglePause();

        controller.State.Should().Be(ReadAloudState.Playing);
        posted.Dequeue()();
        controller.State.Should().Be(ReadAloudState.Stopped);
    }

    [Fact]
    public void Controller_advances_and_completes_after_each_utterance()
    {
        var engine = new RecordingSpeechEngine();
        var controller = new ReadAloudController(engine);
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(new Paragraph("One"));
        document.Blocks.Add(new Paragraph("Two"));

        controller.Start(document);
        controller.State.Should().Be(ReadAloudState.Playing);
        engine.CompleteCurrent();
        controller.State.Should().Be(ReadAloudState.Playing);
        engine.Spoken.Should().Equal("One", "Two");
        engine.CompleteCurrent();
        controller.State.Should().Be(ReadAloudState.Stopped);
        controller.IsActive.Should().BeFalse();
    }

    [Fact]
    public void Controller_stop_suppresses_pending_completion_and_clears_active_state()
    {
        var engine = new RecordingSpeechEngine();
        var controller = new ReadAloudController(engine);
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(new Paragraph("One"));
        document.Blocks.Add(new Paragraph("Two"));

        controller.Start(document);
        controller.IsActive.Should().BeTrue();
        controller.Stop();
        engine.CompleteCurrent();

        controller.State.Should().Be(ReadAloudState.Stopped);
        controller.IsActive.Should().BeFalse();
        engine.Spoken.Should().Equal("One");
    }

    [Fact]
    public void Avalonia_process_engine_stop_cancels_child_and_suppresses_late_completion()
    {
        var posted = new Queue<Action>();
        var runner = new RecordingProcessRunner();
        using var engine = new AvaloniaSpeechEngine(
            _ => new AvaloniaSpeechEngine.SpeechBackend("fake", []),
            posted.Enqueue,
            runner);
        var completed = false;

        engine.SpeakAsync("pending", () => completed = true);
        engine.Stop();
        runner.Process.WasKilled.Should().BeTrue();
        runner.Complete();
        while (posted.Count > 0)
            posted.Dequeue()();

        completed.Should().BeFalse();
    }

    [Fact]
    public void Avalonia_process_engine_dispose_kills_and_disposes_child()
    {
        var runner = new RecordingProcessRunner();
        var engine = new AvaloniaSpeechEngine(
            _ => new AvaloniaSpeechEngine.SpeechBackend("fake", []),
            _ => { },
            runner);

        engine.SpeakAsync("pending", () => { });
        engine.Dispose();

        runner.Process.WasKilled.Should().BeTrue();
        runner.Process.WasDisposed.Should().BeTrue();
    }

    [Fact]
    public void Avalonia_process_runner_reaps_started_child_when_standard_input_fails()
    {
        var process = new FailingInputProcess();
        var runner = new AvaloniaSpeechEngine.ProcessSpeechRunner((_, _) => process);
        using var engine = new AvaloniaSpeechEngine(
            _ => new AvaloniaSpeechEngine.SpeechBackend(
                "fake",
                [],
                WriteTextToStandardInput: true),
            _ => { },
            runner);

        engine.SpeakAsync("pending", () => { });

        process.WasStarted.Should().BeTrue();
        process.WasKilled.Should().BeTrue();
        process.WasDisposed.Should().BeTrue();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Avalonia_process_engine_reaps_child_when_stop_or_dispose_wins_during_start(bool dispose)
    {
        var process = new RecordingProcess();
        var runner = new BlockingProcessRunner(process);
        var engine = new AvaloniaSpeechEngine(
            _ => new AvaloniaSpeechEngine.SpeechBackend("fake", []),
            _ => { },
            runner);
        var speakTask = Task.Run(() => engine.SpeakAsync("pending", () => { }));

        try
        {
            await runner.StartEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            if (dispose)
                engine.Dispose();
            else
                engine.Stop();
        }
        finally
        {
            runner.AllowReturn.Set();
            await speakTask;
        }

        process.WasKilled.Should().BeTrue();
        process.WasDisposed.Should().BeTrue();
        engine.Dispose();
    }

    [Fact]
    public void MainWindow_wires_read_aloud_callbacks_and_lifecycle_guards()
    {
        var source = File.ReadAllText(RepositoryFile("freew", "FreeW.App.Avalonia", "MainWindow.cs"));

        source.Should().Contain("ToggleReadAloud: ToggleReadAloud");
        source.Should().Contain("IsReadAloudActive: IsReadAloudActive");
        source.Should().Contain("StopReadAloud();");
        source.Should().Contain("DisposeReadAloud();");
        source.Should().Contain("_editor.ReadAloudStartSegmentIndex()");
    }

    private static Task OnUiThread(Action action) =>
        Session.Dispatch(action, CancellationToken.None);

    private static RibbonHostCallbacks NoopCallbacks() =>
        new(
            Open: () => { }, Save: () => { }, Cut: () => { }, Copy: () => { }, Paste: () => { },
            Backstage: () => { }, NewDocument: () => { }, ToggleNavigationPane: () => { },
            ToggleReviewingPane: () => { }, ToggleRevealFormatting: () => { },
            OpenFindReplaceDialog: () => { }, SetPrintLayout: () => { }, SetWebLayout: () => { },
            SetDraftView: () => { }, OpenFontDialog: () => { }, OpenParagraphDialog: () => { },
            OpenPageSetupDialog: () => { }, ToggleOrientation: () => { }, ApplyMarginPreset: _ => { },
            ApplyPaperSize: _ => { }, InsertPicture: () => { }, OpenWordCountDialog: () => { },
            ApplyZoom: (_, _) => { });

    private static string RepositoryFile(params string[] parts)
    {
        foreach (var startingDirectory in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            var directory = Path.GetFullPath(startingDirectory);
            while (!string.IsNullOrEmpty(directory))
            {
                var candidate = Path.Combine([directory, .. parts]);
                if (File.Exists(candidate))
                    return candidate;
                directory = Directory.GetParent(directory)?.FullName ?? string.Empty;
            }
        }

        throw new FileNotFoundException(string.Join(Path.DirectorySeparatorChar, parts));
    }

    private sealed class RecordingSpeechEngine : ISpeechEngine
    {
        private Action? _completion;

        public List<string> Spoken { get; } = [];
        public bool SupportsPause => false;

        public void SpeakAsync(string text, Action onCompleted)
        {
            Spoken.Add(text);
            _completion = onCompleted;
        }

        public void Pause() { }
        public void Resume() { }
        public void Stop() => _completion = null;

        public void CompleteCurrent()
        {
            var completion = _completion;
            _completion = null;
            completion?.Invoke();
        }
    }

    private sealed class RecordingProcessRunner : AvaloniaSpeechEngine.ISpeechProcessRunner
    {
        public RecordingProcess Process { get; } = new();

        public AvaloniaSpeechEngine.ISpeechProcess Start(
            AvaloniaSpeechEngine.SpeechBackend backend,
            string text,
            Action onExited)
        {
            Process.Completion = onExited;
            return Process;
        }

        public void Complete() => Process.Completion?.Invoke();
    }

    private sealed class BlockingProcessRunner(RecordingProcess process)
        : AvaloniaSpeechEngine.ISpeechProcessRunner
    {
        public TaskCompletionSource<bool> StartEntered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ManualResetEventSlim AllowReturn { get; } = new();

        public AvaloniaSpeechEngine.ISpeechProcess Start(
            AvaloniaSpeechEngine.SpeechBackend backend,
            string text,
            Action onExited)
        {
            process.Completion = onExited;
            StartEntered.TrySetResult(true);
            AllowReturn.Wait();
            return process;
        }
    }

    private sealed class RecordingProcess : AvaloniaSpeechEngine.ISpeechProcess
    {
        public Action? Completion { get; set; }
        public bool WasKilled { get; private set; }
        public bool WasDisposed { get; private set; }
        public bool HasExited => WasKilled;
        public void Kill() => WasKilled = true;
        public void Dispose() => WasDisposed = true;
    }

    private sealed class FailingInputProcess : AvaloniaSpeechEngine.IPlatformSpeechProcess
    {
        public bool WasStarted { get; private set; }
        public bool WasKilled { get; private set; }
        public bool WasDisposed { get; private set; }
        public bool HasExited => WasKilled;

        public bool Start()
        {
            WasStarted = true;
            return true;
        }

        public void WriteStandardInput(string text) =>
            throw new IOException("Simulated stdin failure.");

        public void Kill() => WasKilled = true;
        public void Dispose() => WasDisposed = true;
    }
}
