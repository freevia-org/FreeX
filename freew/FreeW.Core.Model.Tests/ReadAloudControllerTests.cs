namespace FreeW.Core.Model.Tests;

/// <summary>
/// Unit tests for the pure <see cref="ReadAloudController"/>: ordered text extraction (top-level
/// paragraphs then table-cell paragraphs, whitespace-only paragraphs skipped) and the Play / Pause / Stop
/// state machine, exercised through a <see cref="FakeSpeechEngine"/> so no audio or installed voice is
/// needed. The fake records spoken text and lets the test fire each segment's natural completion to drive
/// the controller forward.
/// </summary>
public class ReadAloudControllerTests
{
    // A controllable, audio-free ISpeechEngine: records spoken text and stores each utterance's completion
    // callback so a test can fire it ("the segment finished speaking") on demand.
    private sealed class FakeSpeechEngine : ISpeechEngine
    {
        public List<string> Spoken { get; } = [];
        public int PauseCount { get; private set; }
        public int ResumeCount { get; private set; }
        public int StopCount { get; private set; }
        private Action? _pending;

        public void SpeakAsync(string text, Action onCompleted)
        {
            Spoken.Add(text);
            _pending = onCompleted;
        }

        public void Pause() => PauseCount++;
        public void Resume() => ResumeCount++;

        public void Stop()
        {
            StopCount++;
            _pending = null; // cancelled utterances never complete
        }

        /// <summary>Simulates the current utterance finishing naturally.</summary>
        public void CompleteCurrent()
        {
            var callback = _pending;
            _pending = null;
            callback?.Invoke();
        }
    }

    private static TextDocument DocWith(params string[] paragraphTexts)
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        foreach (var text in paragraphTexts)
            doc.Blocks.Add(new Paragraph(text));
        return doc;
    }

    [Fact]
    public void ExtractSegments_OrdersParagraphsAndSkipsBlank()
    {
        var doc = DocWith("First", "   ", "Second", "");

        var segments = ReadAloudController.ExtractSegments(doc);

        segments.Select(s => s.Text).Should().Equal("First", "Second");
        segments.Select(s => s.ParagraphIndex).Should().Equal(0, 1);
    }

    [Fact]
    public void ExtractSegments_IncludesTableCellParagraphsInReadingOrder()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph("Intro"));

        var table = new Table();
        var row = new TableRow();
        var a = new TableCell();
        a.Paragraphs.Add(new Paragraph("CellA"));
        var b = new TableCell();
        b.Paragraphs.Add(new Paragraph("CellB"));
        row.Cells.Add(a);
        row.Cells.Add(b);
        table.Rows.Add(row);
        doc.Blocks.Add(table);

        doc.Blocks.Add(new Paragraph("Outro"));

        var segments = ReadAloudController.ExtractSegments(doc);

        segments.Select(s => s.Text).Should().Equal("Intro", "CellA", "CellB", "Outro");
    }

    [Fact]
    public void MapCaretBlockToSegmentIndex_CountsSpeakableParagraphsAndTableCellsBeforeCaret()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph("Intro"));
        var table = Table.Create(1, 2);
        table.Rows[0].Cells[0].Paragraphs[0] = new Paragraph("Cell A");
        table.Rows[0].Cells[1].Paragraphs[0] = new Paragraph("   ");
        doc.Blocks.Add(table);
        doc.Blocks.Add(new Paragraph("Outro"));

        ReadAloudController.MapCaretBlockToSegmentIndex(doc, -1).Should().Be(0);
        ReadAloudController.MapCaretBlockToSegmentIndex(doc, 0).Should().Be(0);
        ReadAloudController.MapCaretBlockToSegmentIndex(doc, 1).Should().Be(1);
        ReadAloudController.MapCaretBlockToSegmentIndex(doc, 2).Should().Be(2);
        ReadAloudController.MapCaretBlockToSegmentIndex(doc, 99).Should().Be(3);
    }

    [Fact]
    public void ExtractSegments_IncludesParagraphsInsideNestedTableInReadingOrder()
    {
        // A table nested inside a table cell: Read Aloud must not silently skip that text.
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph("Intro"));

        var outerTable = Table.Create(1, 1);
        var nestedTable = Table.Create(1, 1);
        nestedTable.Rows[0].Cells[0].Paragraphs[0] = new Paragraph("Nested");
        outerTable.Rows[0].Cells[0].NestedTables.Add(nestedTable);
        doc.Blocks.Add(outerTable);

        doc.Blocks.Add(new Paragraph("Outro"));

        var segments = ReadAloudController.ExtractSegments(doc);

        segments.Select(s => s.Text).Should().Equal("Intro", "Nested", "Outro");
    }

    [Fact]
    public void Start_SpeaksFirstSegmentAndIsPlaying()
    {
        var engine = new FakeSpeechEngine();
        var controller = new ReadAloudController(engine);

        controller.Start(DocWith("One", "Two", "Three"));

        controller.State.Should().Be(ReadAloudState.Playing);
        controller.IsActive.Should().BeTrue();
        engine.Spoken.Should().Equal("One");
        controller.CurrentSegmentIndex.Should().Be(0);
    }

    [Fact]
    public void Completion_AdvancesThroughAllSegmentsThenStops()
    {
        var engine = new FakeSpeechEngine();
        var controller = new ReadAloudController(engine);
        var started = new List<string>();
        controller.SegmentStarted += s => started.Add(s.Text);

        controller.Start(DocWith("One", "Two", "Three"));
        engine.CompleteCurrent(); // One done -> Two
        engine.CompleteCurrent(); // Two done -> Three
        engine.CompleteCurrent(); // Three done -> stop

        engine.Spoken.Should().Equal("One", "Two", "Three");
        started.Should().Equal("One", "Two", "Three");
        controller.State.Should().Be(ReadAloudState.Stopped);
        controller.IsActive.Should().BeFalse();
        controller.CurrentSegmentIndex.Should().Be(-1);
    }

    [Fact]
    public void Start_FromMidDocument_ReadsRemainingSegments()
    {
        var engine = new FakeSpeechEngine();
        var controller = new ReadAloudController(engine);

        controller.Start(DocWith("One", "Two", "Three"), startParagraphIndex: 1);
        engine.CompleteCurrent(); // Two -> Three

        engine.Spoken.Should().Equal("Two", "Three");
    }

    [Fact]
    public void Start_ClampsOutOfRangeStartIndex()
    {
        var engine = new FakeSpeechEngine();
        var controller = new ReadAloudController(engine);

        controller.Start(DocWith("One", "Two"), startParagraphIndex: 99);

        engine.Spoken.Should().Equal("Two"); // clamped to last segment
    }

    [Fact]
    public void Start_OnEmptyDocument_StaysStopped()
    {
        var engine = new FakeSpeechEngine();
        var controller = new ReadAloudController(engine);

        controller.Start(DocWith("   ", ""));

        controller.State.Should().Be(ReadAloudState.Stopped);
        engine.Spoken.Should().BeEmpty();
    }

    [Fact]
    public void TogglePause_TransitionsBetweenPlayingAndPaused()
    {
        var engine = new FakeSpeechEngine();
        var controller = new ReadAloudController(engine);
        controller.Start(DocWith("One", "Two"));

        controller.TogglePause();
        controller.State.Should().Be(ReadAloudState.Paused);
        engine.PauseCount.Should().Be(1);

        controller.TogglePause();
        controller.State.Should().Be(ReadAloudState.Playing);
        engine.ResumeCount.Should().Be(1);
    }

    [Fact]
    public void TogglePause_WhenStopped_IsNoOp()
    {
        var engine = new FakeSpeechEngine();
        var controller = new ReadAloudController(engine);

        controller.TogglePause();

        controller.State.Should().Be(ReadAloudState.Stopped);
        engine.PauseCount.Should().Be(0);
        engine.ResumeCount.Should().Be(0);
    }

    [Fact]
    public void Stop_CancelsEngineAndClearsQueue()
    {
        var engine = new FakeSpeechEngine();
        var controller = new ReadAloudController(engine);
        controller.Start(DocWith("One", "Two"));

        controller.Stop();

        controller.State.Should().Be(ReadAloudState.Stopped);
        controller.Segments.Should().BeEmpty();
        controller.CurrentSegmentIndex.Should().Be(-1);
        engine.StopCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public void StateChanged_RaisedOnStartAndStop()
    {
        var engine = new FakeSpeechEngine();
        var controller = new ReadAloudController(engine);
        var changes = 0;
        controller.StateChanged += () => changes++;

        controller.Start(DocWith("One"));   // Stopped -> Playing
        controller.Stop();                  // Playing -> Stopped

        changes.Should().Be(2);
    }

    [Fact]
    public void Start_WhileActive_RestartsCleanly()
    {
        var engine = new FakeSpeechEngine();
        var controller = new ReadAloudController(engine);

        controller.Start(DocWith("One", "Two"));
        controller.Start(DocWith("Alpha", "Beta")); // implicit Stop then fresh start

        engine.StopCount.Should().BeGreaterThan(0);
        engine.Spoken.Last().Should().Be("Alpha");
        controller.State.Should().Be(ReadAloudState.Playing);
    }
}
