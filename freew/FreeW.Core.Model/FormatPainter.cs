namespace FreeW.Core.Model;

/// <summary>
/// Pure formatting-copy semantics for the editor's Format Painter gesture: capture run/paragraph
/// formatting from a source, then stamp it onto a target. The WPF selection plumbing (reading the
/// caret run, applying property values) lives in the App.Host editor; this type holds the
/// model-only copy logic so it can be unit-tested without WPF.
/// </summary>
/// <param name="Run">The run formatting captured from the source selection.</param>
/// <param name="Paragraph">The paragraph formatting captured from the source paragraph.</param>
public sealed record FormatPainterClipboard(RunFormatting Run, ParagraphFormatting Paragraph)
{
    /// <summary>
    /// Capture the run and paragraph formatting that the painter will later stamp onto a target.
    /// Both arguments default to <c>.Default</c> so a capture from a wholly-unformatted source is
    /// representable (and replayable) rather than null.
    /// </summary>
    public static FormatPainterClipboard Capture(RunFormatting? run, ParagraphFormatting? paragraph) =>
        new(run ?? RunFormatting.Default, paragraph ?? ParagraphFormatting.Default);

    /// <summary>
    /// Apply the captured run formatting onto a target run, replacing the target's character
    /// formatting wholesale with the captured source's. Mirrors Word's Format Painter, which copies
    /// the source's formatting verbatim rather than merging it with the target's.
    /// </summary>
    public RunFormatting ApplyTo(RunFormatting target) => Run;

    /// <summary>
    /// Apply the captured paragraph formatting onto a target paragraph, replacing it wholesale with
    /// the captured source's paragraph formatting.
    /// </summary>
    public ParagraphFormatting ApplyTo(ParagraphFormatting target) => Paragraph;
}

/// <summary>
/// Owns Format Painter's command-level double-click rule independently of renderer event systems.
/// </summary>
public sealed class FormatPainterActivationSession
{
    public static readonly TimeSpan DefaultDoubleClickWindow = TimeSpan.FromMilliseconds(500);

    private readonly TimeProvider _clock;
    private readonly TimeSpan _doubleClickWindow;
    private DateTimeOffset? _lastActivation;

    public FormatPainterActivationSession(
        TimeProvider? clock = null,
        TimeSpan? doubleClickWindow = null)
    {
        _clock = clock ?? TimeProvider.System;
        _doubleClickWindow = doubleClickWindow ?? DefaultDoubleClickWindow;
        if (_doubleClickWindow <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(doubleClickWindow));
    }

    /// <summary>
    /// Records one command activation and returns true when it falls inside the double-click window of the
    /// preceding activation. Backward clock movement starts a new gesture instead of producing a false lock.
    /// </summary>
    public bool Activate()
    {
        var now = _clock.GetUtcNow();
        var elapsed = _lastActivation is { } last ? now - last : TimeSpan.MaxValue;
        _lastActivation = now;
        return elapsed >= TimeSpan.Zero && elapsed <= _doubleClickWindow;
    }

    public void Reset() => _lastActivation = null;
}
