namespace FreeW.Core.Model;

/// <summary>
/// The play/pause/stop state of a <see cref="ReadAloudController"/>. Mirrors Word's Read Aloud toolbar
/// states: <see cref="Stopped"/> (nothing speaking), <see cref="Playing"/> (actively speaking), and
/// <see cref="Paused"/> (mid-utterance, resumable).
/// </summary>
public enum ReadAloudState
{
    Stopped,
    Playing,
    Paused
}

/// <summary>
/// A single speakable unit of the document — one body paragraph's text (table-cell paragraphs included),
/// in reading order. <see cref="ParagraphIndex"/> is the position of this segment within the ordered list
/// the controller speaks, so the host can highlight or scroll to the matching paragraph as it advances.
/// </summary>
public readonly record struct ReadAloudSegment(int ParagraphIndex, string Text);

/// <summary>
/// A thin, audio-free abstraction over a text-to-speech engine, so the <see cref="ReadAloudController"/>
/// state machine is unit-testable without any sound hardware or installed voice. The Windows host supplies
/// a <c>System.Speech</c>-backed implementation; tests supply a fake that records calls.
///
/// The contract is deliberately small and asynchronous-by-convention: <see cref="SpeakAsync"/> begins
/// speaking a segment and the engine must invoke the supplied <c>onCompleted</c> callback exactly once
/// when that segment finishes naturally (not when it is cancelled). <see cref="Stop"/> cancels any
/// in-progress speech without firing the completion callback. <see cref="Pause"/>/<see cref="Resume"/>
/// suspend and continue the current utterance.
/// </summary>
public interface ISpeechEngine
{
    /// <summary>
    /// True when <see cref="Pause"/> and <see cref="Resume"/> suspend and continue the current utterance.
    /// Engines that only provide speak/stop should return false so the controller does not report a
    /// paused state that the backend cannot actually honour.
    /// </summary>
    bool SupportsPause => true;

    /// <summary>
    /// Begins speaking <paramref name="text"/>. The engine invokes <paramref name="onCompleted"/> once,
    /// when (and only when) the utterance finishes on its own. Cancelling via <see cref="Stop"/> must not
    /// invoke the callback.
    /// </summary>
    void SpeakAsync(string text, Action onCompleted);

    /// <summary>Pauses the current utterance (no-op when nothing is speaking).</summary>
    void Pause();

    /// <summary>Resumes a paused utterance (no-op when not paused).</summary>
    void Resume();

    /// <summary>
    /// Attempts to pause the current utterance. The default preserves the original void contract for
    /// engines whose pause operation cannot report failure; process-backed engines should override this
    /// so the controller does not expose a paused state after a failed signal.
    /// </summary>
    bool TryPause()
    {
        if (!SupportsPause)
            return false;

        Pause();
        return true;
    }

    /// <summary>Attempts to resume the current utterance and reports whether the operation succeeded.</summary>
    bool TryResume()
    {
        if (!SupportsPause)
            return false;

        Resume();
        return true;
    }

    /// <summary>Cancels all speech immediately; the pending completion callback must not fire.</summary>
    void Stop();
}

/// <summary>
/// Drives Word's Review &gt; Speech &gt; Read Aloud over a <see cref="TextDocument"/>: it flattens the
/// document into ordered, speakable paragraph segments and walks them through an <see cref="ISpeechEngine"/>
/// one at a time, exposing a Play / Pause / Stop state machine and a progress callback as it advances.
///
/// Pure and UI-free: all speech is delegated to the injected engine, so this class is fully unit-testable
/// (text-extraction order + the state transitions) without audio. Construction never touches a voice, so a
/// machine with no TTS voice installed cannot crash here — robustness lives in the host engine.
///
/// <para><b>Text extraction</b> walks the body in reading order: top-level paragraphs, then every paragraph
/// inside a table (row by row, cell by cell) — the same walk the document inspector/track-changes use.
/// Empty / whitespace-only paragraphs are skipped (nothing to read), so the spoken sequence matches what a
/// listener expects.</para>
///
/// <para><b>Start position</b> is a paragraph index into the <em>ordered, non-empty</em> segment list (Word
/// reads from the caret, or the selection, to the end). The host maps the caret/selection to that index;
/// the controller clamps it into range.</para>
/// </summary>
public sealed class ReadAloudController
{
    private readonly ISpeechEngine _engine;
    private IReadOnlyList<ReadAloudSegment> _segments = [];
    private int _current = -1;

    /// <summary>Raised after the state changes (host updates the ribbon toggle / status indicator).</summary>
    public event Action? StateChanged;

    /// <summary>
    /// Raised as each segment begins speaking, with the segment about to be spoken. The host can use
    /// <see cref="ReadAloudSegment.ParagraphIndex"/> to highlight or scroll to the matching paragraph.
    /// </summary>
    public event Action<ReadAloudSegment>? SegmentStarted;

    public ReadAloudController(ISpeechEngine engine)
    {
        ArgumentNullException.ThrowIfNull(engine);
        _engine = engine;
    }

    /// <summary>The current Play / Pause / Stop state.</summary>
    public ReadAloudState State { get; private set; } = ReadAloudState.Stopped;

    /// <summary>True while speech is playing or paused (i.e. a read-through is active).</summary>
    public bool IsActive => State != ReadAloudState.Stopped;

    /// <summary>True when the current engine can suspend and continue an utterance.</summary>
    public bool SupportsPause => _engine.SupportsPause;

    public bool CanPause => State == ReadAloudState.Playing && SupportsPause;

    public bool CanResume => State == ReadAloudState.Paused && SupportsPause;

    public bool CanStop => IsActive;

    public bool CanMovePrevious => IsActive && _current >= 0;

    public bool CanMoveNext => IsActive && _current >= 0 && _current < _segments.Count - 1;

    /// <summary>The ordered, non-empty segments queued for the current read-through (empty when stopped).</summary>
    public IReadOnlyList<ReadAloudSegment> Segments => _segments;

    /// <summary>The index (into <see cref="Segments"/>) currently being spoken, or -1 when stopped.</summary>
    public int CurrentSegmentIndex => _current;

    /// <summary>
    /// Flattens <paramref name="document"/> into ordered, non-empty speakable segments: top-level
    /// paragraphs first, then every table-cell paragraph (row by row, cell by cell). Whitespace-only
    /// paragraphs are omitted. The returned <see cref="ReadAloudSegment.ParagraphIndex"/> is the segment's
    /// position in this list, so it is stable for highlighting.
    /// </summary>
    public static IReadOnlyList<ReadAloudSegment> ExtractSegments(TextDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var segments = new List<ReadAloudSegment>();
        foreach (var paragraph in EnumerateParagraphs(document))
        {
            var text = paragraph.PlainText;
            if (string.IsNullOrWhiteSpace(text))
                continue;
            segments.Add(new ReadAloudSegment(segments.Count, text.Trim()));
        }

        return segments;
    }

    /// <summary>
    /// Maps a renderer caret block to the first speakable segment at or after that body block. Tables count
    /// all non-empty cell paragraphs in reading order, matching <see cref="ExtractSegments"/>. A caret before
    /// the body maps to zero; a caret beyond the body maps one past the final segment and is subsequently
    /// clamped by <see cref="Start"/>.
    /// </summary>
    public static int MapCaretBlockToSegmentIndex(TextDocument document, int caretBlockIndex)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (caretBlockIndex <= 0 || document.Blocks.Count == 0)
            return 0;

        var segmentIndex = 0;
        var blocksBeforeCaret = Math.Min(caretBlockIndex, document.Blocks.Count);
        for (var blockIndex = 0; blockIndex < blocksBeforeCaret; blockIndex++)
        {
            segmentIndex += EnumerateParagraphs(document.Blocks[blockIndex])
                .Count(IsSpeakable);
        }

        return segmentIndex;
    }

    // Every paragraph reachable in the document body, in reading order: top-level paragraphs and those
    // nested in table cells (row by row, cell by cell) — the same walk DocumentInspector uses.
    private static IEnumerable<Paragraph> EnumerateParagraphs(TextDocument document)
    {
        foreach (var block in document.Blocks)
        foreach (var paragraph in EnumerateParagraphs(block))
            yield return paragraph;
    }

    private static IEnumerable<Paragraph> EnumerateParagraphs(Block block)
    {
        if (block is Paragraph paragraph)
        {
            yield return paragraph;
            yield break;
        }

        if (block is not Table table)
            yield break;

        foreach (var row in table.Rows)
        foreach (var cell in row.Cells)
        foreach (var cellParagraph in cell.Paragraphs)
            yield return cellParagraph;
    }

    private static bool IsSpeakable(Paragraph paragraph) =>
        !string.IsNullOrWhiteSpace(paragraph.PlainText);

    /// <summary>
    /// Starts reading <paramref name="document"/> aloud from <paramref name="startParagraphIndex"/> (a
    /// segment index; Word reads from the caret/selection to the end). The index is clamped into range.
    /// Any in-progress read-through is stopped first. A no-op (stays <see cref="ReadAloudState.Stopped"/>)
    /// when the document has no speakable text.
    /// </summary>
    public void Start(TextDocument document, int startParagraphIndex = 0)
    {
        ArgumentNullException.ThrowIfNull(document);

        Start(ExtractSegments(document), startParagraphIndex);
    }

    /// <summary>
    /// Starts a read-through from a precomputed segment plan. Presentation workflows use this overload to
    /// normalize a request once before handing the ordered speech sequence to the controller.
    /// </summary>
    public void Start(IReadOnlyList<ReadAloudSegment> segments, int startParagraphIndex = 0)
    {
        ArgumentNullException.ThrowIfNull(segments);

        Stop();

        if (segments.Count == 0)
            return;

        _segments = [.. segments];
        _current = Math.Clamp(startParagraphIndex, 0, _segments.Count - 1);
        SetState(ReadAloudState.Playing);
        SpeakCurrent();
    }

    /// <summary>Pauses the active utterance when the engine supports it.</summary>
    public bool Pause()
    {
        if (!CanPause || !_engine.TryPause())
            return false;

        SetState(ReadAloudState.Paused);
        return true;
    }

    /// <summary>Resumes a paused utterance when the engine supports it.</summary>
    public bool Resume()
    {
        if (!CanResume || !_engine.TryResume())
            return false;

        SetState(ReadAloudState.Playing);
        return true;
    }

    /// <summary>
    /// Toggles between playing and paused. Pausing a playing read-through suspends the current utterance;
    /// resuming a paused one continues it. A no-op when stopped (there is nothing to pause/resume).
    /// </summary>
    public void TogglePause()
    {
        switch (State)
        {
            case ReadAloudState.Playing:
                Pause();
                break;
            case ReadAloudState.Paused:
                Resume();
                break;
            case ReadAloudState.Stopped:
            default:
                break;
        }
    }

    /// <summary>Restarts the preceding segment, or the first segment when already at the beginning.</summary>
    public bool MovePrevious()
    {
        if (!CanMovePrevious)
            return false;

        return MoveTo(Math.Max(0, _current - 1));
    }

    /// <summary>Moves to and starts the next segment when one is available.</summary>
    public bool MoveNext()
    {
        if (!CanMoveNext)
            return false;

        return MoveTo(_current + 1);
    }

    /// <summary>Stops the read-through, cancelling any speech and clearing the queue. Idempotent.</summary>
    public void Stop()
    {
        if (State == ReadAloudState.Stopped)
            return;

        _engine.Stop();
        _segments = [];
        _current = -1;
        SetState(ReadAloudState.Stopped);
    }

    private void SpeakCurrent()
    {
        if (_current < 0 || _current >= _segments.Count)
        {
            Stop();
            return;
        }

        var segment = _segments[_current];
        SegmentStarted?.Invoke(segment);
        _engine.SpeakAsync(segment.Text, OnSegmentCompleted);
    }

    private bool MoveTo(int segmentIndex)
    {
        _engine.Stop();
        _current = segmentIndex;
        if (State == ReadAloudState.Paused)
            SetState(ReadAloudState.Playing);
        SpeakCurrent();
        return true;
    }

    // Invoked by the engine when a segment finishes naturally. Advance to the next segment (if still
    // playing — a Stop in between clears the queue and we must not resurrect it); otherwise we are done.
    private void OnSegmentCompleted()
    {
        if (State != ReadAloudState.Playing)
            return;

        _current++;
        if (_current >= _segments.Count)
        {
            Stop();
            return;
        }

        SpeakCurrent();
    }

    private void SetState(ReadAloudState state)
    {
        if (State == state)
            return;
        State = state;
        StateChanged?.Invoke();
    }
}
