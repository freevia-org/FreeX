using Free.Shared.Ribbon;
using FreeW.App.Presentation.Speech;

namespace FreeW.App.Presentation.Tests.Speech;

public sealed class ReadAloudSessionTests
{
    [Fact]
    public void StartPlanner_NormalizesSegmentsStartAndSettings()
    {
        var document = Document("  First  ", "", "Second");

        var plan = ReadAloudStartPlanner.Plan(
            document,
            requestedStartSegmentIndex: 99,
            settings: new ReadAloudSettings("  Narrator  ", Rate: 40, Volume: -5));

        plan.Segments.Select(segment => segment.Text).Should().Equal("First", "Second");
        plan.StartSegmentIndex.Should().Be(1);
        plan.Settings.Should().Be(new ReadAloudSettings("Narrator", Rate: 10, Volume: 0));
        plan.HasSpeakableContent.Should().BeTrue();
    }

    [Fact]
    public void PrimaryCommand_LazilyStartsFromCaretAndStopsWithSharedState()
    {
        var document = Document("First", "Second");
        var engine = new RecordingSpeechEngine();
        var factoryCalls = 0;
        using var session = new ReadAloudSession(new ReadAloudSessionPorts(
            GetDocument: () => document,
            GetStartSegmentIndex: () => 1,
            CreateEngine: _ =>
            {
                factoryCalls++;
                return engine;
            }));
        using var command = new FreeWReadAloudRibbonCommand(session);
        var stateChanges = 0;
        command.StateChanged += () => stateChanges++;

        command.GetState().Should().Be(new RibbonCommandState(IsEnabled: true, IsChecked: false));
        factoryCalls.Should().Be(0);

        command.Execute(RibbonCommandContext.Empty);

        engine.Spoken.Should().Equal("Second");
        command.GetState().IsChecked.Should().BeTrue();
        session.CommandAvailability.Should().Match<ReadAloudCommandAvailability>(state =>
            state.State == ReadAloudState.Playing
            && state.CanPause
            && state.CanStop
            && state.CanMovePrevious
            && !state.CanMoveNext);

        command.Execute(RibbonCommandContext.Empty);

        engine.StopCount.Should().Be(1);
        command.GetState().IsChecked.Should().BeFalse();
        factoryCalls.Should().Be(1);
        stateChanges.Should().Be(2);
    }

    [Fact]
    public void NavigationPauseAndResume_UseOneDomainSequence()
    {
        var engine = new RecordingSpeechEngine();
        using var session = Session(Document("One", "Two", "Three"), engine);

        session.Start().Should().BeTrue();
        session.Pause().Should().BeTrue();
        session.CommandAvailability.CanResume.Should().BeTrue();
        session.Resume().Should().BeTrue();
        session.MoveNext().Should().BeTrue();
        session.MovePrevious().Should().BeTrue();

        engine.Calls.Should().Equal(
            "speak:One",
            "pause",
            "resume",
            "stop",
            "speak:Two",
            "stop",
            "speak:One");
        session.CommandAvailability.State.Should().Be(ReadAloudState.Playing);
    }

    [Fact]
    public void DocumentChangeAndDispose_StopAndReleaseOnlyOwnedActiveEngine()
    {
        var engine = new RecordingSpeechEngine();
        var session = Session(Document("One"), engine);

        session.HandleDocumentChanged().Should().BeFalse();
        session.Start().Should().BeTrue();
        session.HandleDocumentChanged().Should().BeTrue();
        session.HandleDocumentChanged().Should().BeFalse();

        session.Dispose();
        session.Dispose();

        engine.StopCount.Should().Be(1);
        engine.DisposeCount.Should().Be(1);
    }

    [Fact]
    public void ApplyingChangedSettings_StopsDisposesAndRecreatesWithNormalizedValues()
    {
        var document = Document("One");
        var engines = new Queue<RecordingSpeechEngine>(
            [new RecordingSpeechEngine(), new RecordingSpeechEngine()]);
        var settingsSeen = new List<ReadAloudSettings>();
        using var session = new ReadAloudSession(
            new ReadAloudSessionPorts(
                GetDocument: () => document,
                GetStartSegmentIndex: () => 0,
                CreateEngine: settings =>
                {
                    settingsSeen.Add(settings);
                    return engines.Dequeue();
                }));

        session.Start();
        session.ApplySettings(new ReadAloudSettings("  Voice  ", Rate: -99, Volume: 999))
            .Should().BeTrue();
        session.Settings.Should().Be(new ReadAloudSettings("Voice", Rate: -10, Volume: 100));
        session.Start();

        settingsSeen.Should().Equal(
            ReadAloudSettings.Default,
            new ReadAloudSettings("Voice", Rate: -10, Volume: 100));
    }

    private static ReadAloudSession Session(TextDocument document, RecordingSpeechEngine engine) =>
        new(new ReadAloudSessionPorts(
            GetDocument: () => document,
            GetStartSegmentIndex: () => 0,
            CreateEngine: _ => engine));

    private static TextDocument Document(params string[] paragraphs)
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        foreach (var text in paragraphs)
            document.Blocks.Add(new Paragraph(text));
        return document;
    }

    private sealed class RecordingSpeechEngine : ISpeechEngine, IDisposable
    {
        private Action? _completion;

        public List<string> Spoken { get; } = [];
        public List<string> Calls { get; } = [];
        public int StopCount { get; private set; }
        public int DisposeCount { get; private set; }
        public bool SupportsPause => true;

        public void SpeakAsync(string text, Action onCompleted)
        {
            Spoken.Add(text);
            Calls.Add($"speak:{text}");
            _completion = onCompleted;
        }

        public void Pause() => Calls.Add("pause");

        public void Resume() => Calls.Add("resume");

        public void Stop()
        {
            Calls.Add("stop");
            StopCount++;
            _completion = null;
        }

        public void Dispose() => DisposeCount++;
    }
}

public sealed class ReadAloudSourceOwnershipTests
{
    [Fact]
    public void AvaloniaHost_DelegatesPortableLifecyclePolicyToSharedSession()
    {
        var source = ReadSource("freew", "FreeW.App.Avalonia", "MainWindow.cs");

        source.Should().Contain("ReadAloudSession? _readAloudSession");
        source.Should().Contain("EnsureReadAloudSession().ToggleStartStop()");
        source.Should().Contain("_readAloudSession?.HandleDocumentChanged()");
        source.Should().NotContain("_readAloudController");
        source.Should().NotContain("_readAloudEngine");
        source.Should().NotContain("new ReadAloudController(");
    }

    [Fact]
    public void SharedReadAloudPolicy_HasNoRendererOrNativeSpeechDependencies()
    {
        var source = ReadSource(
            "freew", "FreeW.App.Presentation", "Speech", "ReadAloudSession.cs");

        source.Should().NotContain("using Avalonia");
        source.Should().NotContain("using System.Windows");
        source.Should().NotContain("System.Speech");
        source.Should().NotContain("Dispatcher");
        source.Should().NotContain("ProcessStartInfo");
    }

    [Fact]
    public void NativeEngines_RemainThinRendererOwnedSpeechPorts()
    {
        var wpf = ReadSource("freew", "FreeW.App.Host", "SystemSpeechEngine.cs");
        var avalonia = ReadSource("freew", "FreeW.App.Avalonia", "AvaloniaSpeechEngine.cs");

        foreach (var source in new[] { wpf, avalonia })
        {
            source.Should().Contain(": ISpeechEngine, IDisposable");
            source.Should().NotContain("ReadAloudStartPlanner");
            source.Should().NotContain("ReadAloudCommandAvailability");
            source.Should().NotContain("ReadAloudSessionPorts");
        }
    }

    private static string ReadSource(params string[] parts)
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        return File.ReadAllText(Path.Combine(new[] { root }.Concat(parts).ToArray()));
    }
}
