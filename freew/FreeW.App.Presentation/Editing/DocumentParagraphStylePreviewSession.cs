using FreeW.Core.Model;

namespace FreeW.App.Presentation.Editing;

/// <summary>
/// Result of committing a paragraph-style gallery preview. The captured target is returned so a native
/// renderer can restore its selection when a character-style projection is required.
/// </summary>
public sealed record ParagraphStylePreviewCommitResult(
    NamedStyleApplicationTarget Target,
    NamedStyleApplicationResult? Application);

/// <summary>
/// Owns the renderer-neutral transaction behind Home &gt; Styles live preview. The first hover captures
/// the exact paragraph baseline and named-style application target; later hovers always restore that
/// baseline before applying another temporary style. Cancellation never enters undo history, while commit
/// restores the baseline and delegates the real edit to <see cref="DocumentEditingSession.ApplyNamedStyle"/>.
/// </summary>
public sealed class DocumentParagraphStylePreviewSession
{
    private readonly DocumentEditingSession _session;
    private IReadOnlyDictionary<int, string?>? _styleIdBaseline;
    private NamedStyleApplicationTarget? _target;

    internal DocumentParagraphStylePreviewSession(DocumentEditingSession session) => _session = session;

    public bool HasActivePreview => _styleIdBaseline is not null;

    public NamedStyleApplicationTarget? ActiveTarget => _target;

    /// <summary>Applies a temporary paragraph style without creating an undo entry.</summary>
    public bool Preview(string? styleId, NamedStyleApplicationTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (styleId is { Length: > 0 } && !_session.Document.Styles.ContainsKey(styleId))
            return false;

        if (_styleIdBaseline is null)
        {
            var indices = ResolveParagraphIndices(target.ParagraphIndices);
            if (indices.Count == 0)
                return false;

            _target = CopyTarget(target, indices);
            _styleIdBaseline = indices.ToDictionary(
                index => index,
                index => ((Paragraph)_session.Document.Blocks[index]).StyleId);
        }
        else
        {
            RestoreBaseline();
        }

        foreach (var index in _styleIdBaseline.Keys)
        {
            if (TryGetParagraph(index, out var paragraph))
                paragraph.StyleId = styleId;
        }

        return true;
    }

    /// <summary>Restores the pre-hover paragraph styles and returns the captured renderer target.</summary>
    public NamedStyleApplicationTarget? Cancel()
    {
        if (_styleIdBaseline is null)
            return null;

        var target = _target;
        RestoreBaseline();
        Clear();
        return target;
    }

    /// <summary>
    /// Restores the preview baseline, clears the transient session, and applies the selected style through
    /// the shared reversible named-style workflow. Returns null when no preview session was active.
    /// </summary>
    public ParagraphStylePreviewCommitResult? Commit(string styleId)
    {
        if (string.IsNullOrWhiteSpace(styleId)
            || !_session.Document.Styles.ContainsKey(styleId)
            || _styleIdBaseline is null
            || _target is not { } target)
        {
            return null;
        }

        RestoreBaseline();
        Clear();
        return new ParagraphStylePreviewCommitResult(
            target,
            _session.ApplyNamedStyle(styleId, target));
    }

    private IReadOnlyList<int> ResolveParagraphIndices(IReadOnlyList<int> indices) =>
        indices
            .Distinct()
            .Where(index => TryGetParagraph(index, out _))
            .ToArray();

    private bool TryGetParagraph(int index, out Paragraph paragraph)
    {
        if (index >= 0
            && index < _session.Document.Blocks.Count
            && _session.Document.Blocks[index] is Paragraph candidate)
        {
            paragraph = candidate;
            return true;
        }

        paragraph = null!;
        return false;
    }

    private void RestoreBaseline()
    {
        if (_styleIdBaseline is null)
            return;

        foreach (var (index, styleId) in _styleIdBaseline)
        {
            if (TryGetParagraph(index, out var paragraph))
                paragraph.StyleId = styleId;
        }
    }

    private void Clear()
    {
        _styleIdBaseline = null;
        _target = null;
    }

    private static NamedStyleApplicationTarget CopyTarget(
        NamedStyleApplicationTarget target,
        IReadOnlyList<int> paragraphIndices) =>
        new(
            target.TextRanges.ToArray(),
            paragraphIndices.ToArray(),
            target.HasTextSelection,
            target.CanApplyCharacterFormatting);
}
