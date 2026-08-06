using System.Text;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Editing;

[Flags]
public enum DocumentEditorInputModifiers
{
    None = 0,
    Control = 1,
    Shift = 2,
    Alt = 4,
}

public enum DocumentEditorInputKey
{
    None,
    Backspace,
    Delete,
    Enter,
    Tab,
    Left,
    Right,
    Up,
    Down,
    Home,
    End,
    B,
    I,
    U,
    Y,
    Z,
}

public enum DocumentEditorInputIntent
{
    None,
    Undo,
    Redo,
    ToggleBold,
    ToggleItalic,
    ToggleUnderline,
    DeleteBackward,
    DeleteForward,
    InsertParagraphBreak,
    MovePrevious,
    MoveNext,
    MoveLineStart,
    MoveLineEnd,
    MoveLineUp,
    MoveLineDown,
    NavigateTab,
}

public readonly record struct DocumentEditorInputPlan(
    DocumentEditorInputIntent Intent,
    bool ExtendSelection = false,
    bool IsEditingMutation = false);

public enum DocumentPasteTextKind
{
    TextOnly,
    MergeFormatting,
}

public sealed record DocumentPasteTextPlan(
    string Text,
    IReadOnlyList<string> Lines,
    string UndoLabel)
{
    public bool HasText => Text.Length > 0;
}

public readonly record struct DocumentSectionPosition(int Current, int Total);

/// <summary>
/// Owns layout-independent editor projections and input decisions. Native controls translate keys,
/// carets, selections, clipboard data, and coordinates at the renderer boundary.
/// </summary>
public sealed class DocumentEditorInteractionSession
{
    private readonly DocumentEditingSession _editing;
    private FormatPainterClipboard? _formatPainter;
    private bool _formatPainterLocked;

    internal DocumentEditorInteractionSession(DocumentEditingSession editing)
    {
        _editing = editing;
    }

    public DocumentSectionPosition SectionPosition(int caretBlockIndex)
    {
        var document = _editing.Document;
        var total = Math.Max(1, document.Sections.Count);
        if (total == 1)
            return new DocumentSectionPosition(1, 1);
        if (caretBlockIndex < 0)
            return new DocumentSectionPosition(total, total);

        var current = 1;
        var limit = Math.Min(caretBlockIndex, document.Blocks.Count);
        for (var index = 0; index < limit; index++)
        {
            if (document.Blocks[index] is Paragraph { SectionBreak: not null })
                current++;
        }

        return new DocumentSectionPosition(Math.Clamp(current, 1, total), total);
    }

    public bool IsFormatPainterArmed => _formatPainter is not null;

    public bool ToggleFormatPainter(
        RunFormatting? run,
        ParagraphFormatting? paragraph,
        bool locked = false)
    {
        if (_formatPainter is not null)
        {
            if (locked)
            {
                _formatPainterLocked = true;
                return true;
            }

            CancelFormatPainter();
            return false;
        }

        _formatPainter = FormatPainterClipboard.Capture(run, paragraph);
        _formatPainterLocked = locked;
        return true;
    }

    public void CancelFormatPainter()
    {
        _formatPainter = null;
        _formatPainterLocked = false;
    }

    public bool TryApplyFormatPainter(DocumentTextRange selection)
    {
        if (_formatPainter is not { } painter)
            return false;

        var ranges = ParagraphRanges(selection);
        if (ranges.Count == 0)
            return false;

        var commands = new List<IDocumentCommand>();
        foreach (var range in ranges)
        {
            commands.Add(new FormatPainterRangeCommand(
                range.Start.BlockIndex,
                range.Start.Offset,
                range.End.Offset,
                painter.ApplyTo,
                _editing.RevisionDateXmlForEdit));
        }

        foreach (var blockIndex in ranges
                     .Select(range => range.Start.BlockIndex)
                     .Distinct())
        {
            var paragraph = (Paragraph)_editing.Document.Blocks[blockIndex];
            commands.Add(new SetParagraphFormattingCommand(
                blockIndex,
                painter.ApplyTo(paragraph.Formatting)));
        }

        _editing.ExecuteCommands(commands, "Format Painter");
        if (!_formatPainterLocked)
            _formatPainter = null;
        return true;
    }

    public string ProjectSelectionText(DocumentTextRange selection)
    {
        var range = selection.Normalize();
        if (range.IsCollapsed
            || range.Start.BlockIndex < 0
            || range.End.BlockIndex >= _editing.Document.Blocks.Count)
        {
            return string.Empty;
        }

        var text = new StringBuilder();
        for (var blockIndex = range.Start.BlockIndex; blockIndex <= range.End.BlockIndex; blockIndex++)
        {
            if (_editing.Document.Blocks[blockIndex] is not Paragraph paragraph)
                continue;

            if (text.Length > 0)
                text.Append('\n');
            var start = blockIndex == range.Start.BlockIndex ? range.Start.Offset : 0;
            var end = blockIndex == range.End.BlockIndex ? range.End.Offset : paragraph.PlainText.Length;
            start = Math.Clamp(start, 0, paragraph.PlainText.Length);
            end = Math.Clamp(end, start, paragraph.PlainText.Length);
            text.Append(paragraph.PlainText.AsSpan(start, end - start));
        }

        return text.ToString();
    }

    public DocumentPasteTextPlan PlanPasteText(string? clipboardText, DocumentPasteTextKind kind)
    {
        var normalized = PasteText.Normalize(clipboardText);
        return new DocumentPasteTextPlan(
            normalized,
            normalized.Length == 0 ? [] : normalized.Split('\n'),
            kind == DocumentPasteTextKind.TextOnly ? "Paste Text Only" : "Merge Formatting");
    }

    public static DocumentEditorInputPlan PlanBodyKey(
        DocumentEditorInputKey key,
        DocumentEditorInputModifiers modifiers)
    {
        var control = (modifiers & DocumentEditorInputModifiers.Control) != 0;
        var shift = (modifiers & DocumentEditorInputModifiers.Shift) != 0;
        return (key, control) switch
        {
            (DocumentEditorInputKey.Z, true) => Mutation(DocumentEditorInputIntent.Undo),
            (DocumentEditorInputKey.Y, true) => Mutation(DocumentEditorInputIntent.Redo),
            (DocumentEditorInputKey.B, true) => Mutation(DocumentEditorInputIntent.ToggleBold),
            (DocumentEditorInputKey.I, true) => Mutation(DocumentEditorInputIntent.ToggleItalic),
            (DocumentEditorInputKey.U, true) => Mutation(DocumentEditorInputIntent.ToggleUnderline),
            (DocumentEditorInputKey.Backspace, _) => Mutation(DocumentEditorInputIntent.DeleteBackward),
            (DocumentEditorInputKey.Delete, _) => Mutation(DocumentEditorInputIntent.DeleteForward),
            (DocumentEditorInputKey.Enter, _) => Mutation(DocumentEditorInputIntent.InsertParagraphBreak),
            (DocumentEditorInputKey.Tab, _) => Mutation(DocumentEditorInputIntent.NavigateTab, shift),
            (DocumentEditorInputKey.Left, _) => Move(DocumentEditorInputIntent.MovePrevious, shift),
            (DocumentEditorInputKey.Right, _) => Move(DocumentEditorInputIntent.MoveNext, shift),
            (DocumentEditorInputKey.Home, _) => Move(DocumentEditorInputIntent.MoveLineStart, shift),
            (DocumentEditorInputKey.End, _) => Move(DocumentEditorInputIntent.MoveLineEnd, shift),
            (DocumentEditorInputKey.Up, _) => Move(DocumentEditorInputIntent.MoveLineUp, shift),
            (DocumentEditorInputKey.Down, _) => Move(DocumentEditorInputIntent.MoveLineDown, shift),
            _ => default,
        };
    }

    private static DocumentEditorInputPlan Mutation(
        DocumentEditorInputIntent intent,
        bool extendSelection = false) =>
        new(intent, extendSelection, IsEditingMutation: true);

    private static DocumentEditorInputPlan Move(
        DocumentEditorInputIntent intent,
        bool extendSelection) =>
        new(intent, extendSelection);

    private IReadOnlyList<DocumentTextRange> ParagraphRanges(DocumentTextRange selection)
    {
        var normalized = selection.Normalize();
        if (normalized.IsCollapsed
            || normalized.Start.BlockIndex < 0
            || normalized.End.BlockIndex >= _editing.Document.Blocks.Count)
        {
            return [];
        }

        var ranges = new List<DocumentTextRange>();
        for (var blockIndex = normalized.Start.BlockIndex;
             blockIndex <= normalized.End.BlockIndex;
             blockIndex++)
        {
            if (_editing.Document.Blocks[blockIndex] is not Paragraph paragraph)
                continue;

            var start = blockIndex == normalized.Start.BlockIndex ? normalized.Start.Offset : 0;
            var end = blockIndex == normalized.End.BlockIndex
                ? normalized.End.Offset
                : paragraph.PlainText.Length;
            start = Math.Clamp(start, 0, paragraph.PlainText.Length);
            end = Math.Clamp(end, start, paragraph.PlainText.Length);
            if (end > start)
            {
                ranges.Add(new DocumentTextRange(
                    new DocumentTextPosition(blockIndex, start),
                    new DocumentTextPosition(blockIndex, end)));
            }
        }

        return ranges;
    }

    private sealed class FormatPainterRangeCommand(
        int blockIndex,
        int startOffset,
        int endOffset,
        Func<RunFormatting, RunFormatting> transform,
        Func<string?> revisionDateXml) : IDocumentCommand
    {
        private List<Run>? _previous;
        private List<Run>? _replacement;

        public string Label => "Format Painter";

        public DocumentCommandMutationKind MutationKind => DocumentCommandMutationKind.BodyFormatting;

        public void Apply(IDocumentCommandContext context)
        {
            var paragraph = (Paragraph)context.Document.Blocks[blockIndex];
            if (_replacement is not null)
            {
                paragraph.Runs.Clear();
                paragraph.Runs.AddRange(_replacement);
                return;
            }

            _previous = [.. paragraph.Runs];
            var rebuilt = new List<Run>();
            var position = 0;
            foreach (var source in paragraph.Runs)
            {
                var runStart = position;
                var runEnd = runStart + source.Text.Length;
                position = runEnd;
                var coverStart = Math.Max(runStart, startOffset);
                var coverEnd = Math.Min(runEnd, endOffset);
                if (source.Text.Length == 0 || coverStart >= coverEnd)
                {
                    rebuilt.Add(RevisionEditPlanner.CloneRunWithText(source, source.Text));
                    continue;
                }

                var localStart = coverStart - runStart;
                var localEnd = coverEnd - runStart;
                if (localStart > 0)
                    rebuilt.Add(RevisionEditPlanner.CloneRunWithText(source, source.Text[..localStart]));

                var covered = RevisionEditPlanner.CloneRunWithText(
                    source,
                    source.Text[localStart..localEnd]);
                var formatting = transform(source.Formatting);
                covered.Formatting = formatting;
                if (context.Document is { TrackRevisions: true, DoNotTrackFormatting: false }
                    && formatting != source.Formatting
                    && covered.FormatRevision is null)
                {
                    var author = string.IsNullOrWhiteSpace(context.RevisionAuthor)
                        ? "FreeW User"
                        : context.RevisionAuthor.Trim();
                    covered.FormatRevision = new FormatRevision(
                        source.Formatting,
                        author,
                        revisionDateXml());
                }
                rebuilt.Add(covered);

                if (localEnd < source.Text.Length)
                    rebuilt.Add(RevisionEditPlanner.CloneRunWithText(source, source.Text[localEnd..]));
            }

            paragraph.Runs.Clear();
            paragraph.Runs.AddRange(rebuilt);
            _replacement = [.. paragraph.Runs];
        }

        public void Revert(IDocumentCommandContext context)
        {
            if (_previous is null)
                return;
            var paragraph = (Paragraph)context.Document.Blocks[blockIndex];
            paragraph.Runs.Clear();
            paragraph.Runs.AddRange(_previous);
        }
    }
}
