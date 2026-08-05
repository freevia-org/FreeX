using System.Runtime.InteropServices;
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
    public void Avalonia_process_engine_reports_pause_only_for_a_capable_backend()
    {
        using var unsupported = new AvaloniaSpeechEngine(
            _ => new AvaloniaSpeechEngine.SpeechBackend("fake", []),
            _ => { });
        using var supported = new AvaloniaSpeechEngine(
            _ => new AvaloniaSpeechEngine.SpeechBackend("fake", [], SupportsPause: true),
            _ => { });

        unsupported.SupportsPause.Should().BeFalse();
        supported.SupportsPause.Should().BeTrue();
    }

    [Fact]
    public void Avalonia_pause_capability_is_backend_specific_not_unix_wide()
    {
        var dispatcherBackend = new AvaloniaSpeechEngine.SpeechBackend(
            "spd-say", ["-w", "text"], SupportsPause: false);
        var synthesizerBackend = new AvaloniaSpeechEngine.SpeechBackend(
            "espeak", ["text"], SupportsPause: true);

        dispatcherBackend.SupportsPause.Should().BeFalse();
        synthesizerBackend.SupportsPause.Should().BeTrue();
    }

    [Fact]
    public void Avalonia_process_engine_pause_and_resume_do_not_complete_the_utterance()
    {
        var posted = new Queue<Action>();
        var runner = new RecordingProcessRunner(new RecordingProcess(supportsPause: true));
        using var engine = new AvaloniaSpeechEngine(
            _ => new AvaloniaSpeechEngine.SpeechBackend("fake", [], SupportsPause: true),
            posted.Enqueue,
            runner);
        var completed = false;

        engine.SpeakAsync("pending", () => completed = true);

        engine.TryPause().Should().BeTrue();
        runner.Process.PauseCount.Should().Be(1);
        engine.TryResume().Should().BeTrue();
        runner.Process.ResumeCount.Should().Be(1);
        completed.Should().BeFalse();
        posted.Should().BeEmpty();
    }

    [Fact]
    public void Avalonia_process_engine_suppresses_a_queued_completion_while_paused()
    {
        var posted = new Queue<Action>();
        var runner = new RecordingProcessRunner(new RecordingProcess(supportsPause: true));
        using var engine = new AvaloniaSpeechEngine(
            _ => new AvaloniaSpeechEngine.SpeechBackend("fake", [], SupportsPause: true),
            posted.Enqueue,
            runner);
        var completed = false;

        engine.SpeakAsync("pending", () => completed = true);
        engine.TryPause().Should().BeTrue();
        runner.Complete();
        posted.Should().ContainSingle();
        posted.Dequeue()();
        completed.Should().BeFalse();

        engine.TryResume().Should().BeTrue();
        runner.Complete();
        posted.Should().ContainSingle();
        posted.Dequeue()();
        completed.Should().BeTrue();
    }

    [Fact]
    public void Failed_pause_signal_leaves_controller_playing()
    {
        var runner = new RecordingProcessRunner(new RecordingProcess(supportsPause: true)
        {
            PauseResult = false,
        });
        using var engine = new AvaloniaSpeechEngine(
            _ => new AvaloniaSpeechEngine.SpeechBackend("fake", [], SupportsPause: true),
            _ => { },
            runner);
        var controller = new ReadAloudController(engine);
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(new Paragraph("One"));

        controller.Start(document);
        controller.TogglePause();

        controller.State.Should().Be(ReadAloudState.Playing);
        runner.Process.PauseCount.Should().Be(1);
    }

    [Fact]
    public void Controller_repeated_pause_toggles_round_trip_and_only_completion_stops()
    {
        var runner = new RecordingProcessRunner(new RecordingProcess(supportsPause: true));
        var posted = new Queue<Action>();
        using var engine = new AvaloniaSpeechEngine(
            _ => new AvaloniaSpeechEngine.SpeechBackend("fake", [], SupportsPause: true),
            posted.Enqueue,
            runner);
        var controller = new ReadAloudController(engine);
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(new Paragraph("One"));

        controller.Start(document);
        controller.TogglePause();
        controller.State.Should().Be(ReadAloudState.Paused);
        controller.TogglePause();
        controller.State.Should().Be(ReadAloudState.Playing);
        runner.Process.PauseCount.Should().Be(1);
        runner.Process.ResumeCount.Should().Be(1);

        runner.Complete();
        posted.Should().ContainSingle();
        posted.Dequeue()();
        controller.State.Should().Be(ReadAloudState.Stopped);
    }

    [Fact]
    public void Stop_while_paused_kills_child_and_suppresses_late_completion()
    {
        var posted = new Queue<Action>();
        var runner = new RecordingProcessRunner(new RecordingProcess(supportsPause: true));
        using var engine = new AvaloniaSpeechEngine(
            _ => new AvaloniaSpeechEngine.SpeechBackend("fake", [], SupportsPause: true),
            posted.Enqueue,
            runner);
        var completed = false;

        engine.SpeakAsync("pending", () => completed = true);
        engine.TryPause().Should().BeTrue();
        engine.Stop();
        runner.Process.WasKilled.Should().BeTrue();
        runner.Complete();
        while (posted.Count > 0)
            posted.Dequeue()();

        completed.Should().BeFalse();
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

    [Fact]
    public void Avalonia_process_runner_pauses_and_resumes_its_owned_Windows_child()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return;

        var powershell = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "WindowsPowerShell",
            "v1.0",
            "powershell.exe");
        File.Exists(powershell).Should().BeTrue();

        var outputPath = Path.Combine(Path.GetTempPath(), $"freew-read-aloud-{Guid.NewGuid():N}.txt");
        var backend = new AvaloniaSpeechEngine.SpeechBackend(
            powershell,
            [
                "-NoProfile",
                "-NonInteractive",
                "-Command",
                "$p=[Console]::In.ReadToEnd(); 1..500 | ForEach-Object { " +
                "[IO.File]::AppendAllText($p, 'x'); Start-Sleep -Milliseconds 20 }",
            ],
            WriteTextToStandardInput: true,
            SupportsPause: true);
        var runner = new AvaloniaSpeechEngine.ProcessSpeechRunner();
        using var process = runner.Start(backend, outputPath, () => { });

        try
        {
            SpinWait.SpinUntil(() => FileLength(outputPath) >= 5, TimeSpan.FromSeconds(5))
                .Should().BeTrue("the owned PowerShell child must begin producing output");

            process.TryPause().Should().BeTrue();
            var pausedLength = FileLength(outputPath);
            Thread.Sleep(300);
            FileLength(outputPath).Should().Be(pausedLength,
                "suspending the exact owned process must stop its observable work");

            process.TryResume().Should().BeTrue();
            SpinWait.SpinUntil(() => FileLength(outputPath) > pausedLength, TimeSpan.FromSeconds(5))
                .Should().BeTrue("resuming the exact owned process must allow work to continue");
        }
        finally
        {
            if (!process.HasExited)
            {
                process.TryResume();
                process.Kill();
            }

            DeleteFileWithRetry(outputPath);
        }
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
        public RecordingProcess Process { get; }

        public RecordingProcessRunner()
            : this(new RecordingProcess())
        {
        }

        public RecordingProcessRunner(RecordingProcess process) => Process = process;

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
        public RecordingProcess(bool supportsPause = false) => SupportsPause = supportsPause;

        public Action? Completion { get; set; }
        public bool WasKilled { get; private set; }
        public bool WasDisposed { get; private set; }
        public int? ProcessId => null;
        public bool SupportsPause { get; }
        public bool PauseResult { get; set; } = true;
        public bool ResumeResult { get; set; } = true;
        public int PauseCount { get; private set; }
        public int ResumeCount { get; private set; }
        public bool HasExited => WasKilled;
        public void Kill() => WasKilled = true;
        public bool TryPause()
        {
            PauseCount++;
            return SupportsPause && PauseResult;
        }
        public bool TryResume()
        {
            ResumeCount++;
            return SupportsPause && ResumeResult;
        }
        public void Dispose() => WasDisposed = true;
    }

    private static long FileLength(string path)
    {
        try
        {
            return File.Exists(path) ? new FileInfo(path).Length : 0;
        }
        catch (IOException)
        {
            return 0;
        }
    }

    private static void DeleteFileWithRetry(string path)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                File.Delete(path);
                return;
            }
            catch (IOException) when (attempt < 4)
            {
                Thread.Sleep(50);
            }
        }
    }

    private sealed class FailingInputProcess : AvaloniaSpeechEngine.IPlatformSpeechProcess
    {
        public bool WasStarted { get; private set; }
        public bool WasKilled { get; private set; }
        public bool WasDisposed { get; private set; }
        public int? ProcessId => null;
        public bool SupportsPause => false;
        public bool HasExited => WasKilled;

        public bool Start()
        {
            WasStarted = true;
            return true;
        }

        public void WriteStandardInput(string text) =>
            throw new IOException("Simulated stdin failure.");

        public void Kill() => WasKilled = true;
        public bool TryPause() => false;
        public bool TryResume() => false;
        public void Dispose() => WasDisposed = true;
    }
}
