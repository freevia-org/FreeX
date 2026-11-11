using System.Text;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using FreeW.App.Presentation.Dialogs;
using FreeW.Core.Model;
using WpfList = System.Windows.Documents.List;
using WpfParagraph = System.Windows.Documents.Paragraph;
using WpfRun = System.Windows.Documents.Run;
using WpfTable = System.Windows.Documents.Table;

namespace FreeW.App.Host.Editing;

/// <summary>
/// The rendered body story projected for Find and Replace: the shared matcher's segmented haystack
/// (<see cref="Segments"/> / <see cref="Text"/>) plus the TextPointer range each stretch of it came
/// from, so a match span can be turned back into a selection.
/// </summary>
internal sealed class FindReplaceBodyProjection
{
    private readonly List<Entry> _entries = [];
    private readonly List<FindTextSegment> _segments = [];
    private readonly StringBuilder _text = new();
    private string? _cachedText;

    private readonly record struct Entry(int Offset, int Length, TextPointer Start, TextPointer End, bool IsText);

    public IReadOnlyList<FindTextSegment> Segments => _segments;

    public string Text => _cachedText ??= _text.ToString();

    public int Length => _text.Length;

    internal void AddText(WpfRun run, string text, RunFormatting formatting, string? styleId)
    {
        _entries.Add(new Entry(_text.Length, text.Length, run.ContentStart, run.ContentEnd, IsText: true));
        Append(new FindTextSegment(text, formatting, styleId));
    }

    internal void AddAtomic(
        string text,
        TextPointer start,
        TextPointer end,
        RunFormatting? formatting = null,
        string? styleId = null,
        bool structural = true)
    {
        _entries.Add(new Entry(_text.Length, text.Length, start, end, IsText: false));
        Append(new FindTextSegment(text, formatting ?? RunFormatting.Default, styleId, structural));
    }

    private void Append(FindTextSegment segment)
    {
        _segments.Add(segment);
        _text.Append(segment.Text);
        _cachedText = null;
    }

    /// <summary>The haystack offset of a document position (the first stretch at or after it).</summary>
    public int OffsetOf(TextPointer position)
    {
        foreach (var entry in _entries)
        {
            if (position.CompareTo(entry.Start) <= 0)
                return entry.Offset;
            if (position.CompareTo(entry.End) >= 0)
                continue;

            return entry.IsText
                ? entry.Offset + Math.Clamp(entry.Start.GetOffsetToPosition(position), 0, entry.Length)
                : entry.Offset + entry.Length;
        }

        return Length;
    }

    /// <summary>The document range covering haystack span [start, start + length).</summary>
    public bool TryGetRange(int start, int length, out TextPointer rangeStart, out TextPointer rangeEnd)
    {
        rangeStart = rangeEnd = null!;
        if (length <= 0 || start < 0 || start + length > Length)
            return false;

        var first = EntryContaining(start);
        var last = EntryContaining(start + length - 1);
        if (first is not { } firstEntry || last is not { } lastEntry)
            return false;

        var from = firstEntry.IsText
            ? firstEntry.Start.GetPositionAtOffset(start - firstEntry.Offset, LogicalDirection.Forward)
            : firstEntry.Start;
        var to = lastEntry.IsText
            ? lastEntry.Start.GetPositionAtOffset(start + length - lastEntry.Offset, LogicalDirection.Backward)
            : lastEntry.End;
        if (from is null || to is null)
            return false;

        rangeStart = from;
        rangeEnd = to;
        return true;
    }

    private Entry? EntryContaining(int offset)
    {
        var lo = 0;
        var hi = _entries.Count - 1;
        while (lo <= hi)
        {
            var mid = (lo + hi) / 2;
            var entry = _entries[mid];
            if (offset < entry.Offset)
                hi = mid - 1;
            else if (offset >= entry.Offset + entry.Length)
                lo = mid + 1;
            else
                return entry;
        }

        return null;
    }
}

public sealed partial class DocumentView
{
    /// <summary>
    /// Projects the live body FlowDocument for Find and Replace. The search itself is shared
    /// (<see cref="FindReplaceDialogPlanner.FindAllInSegments"/>); only this traversal is native,
    /// because a match has to come back as TextPointers the selection can take.
    /// </summary>
    /// <remarks>
    /// A paragraph mark between two consecutive paragraphs of one flow is a matchable
    /// <see cref="FindSpecialCodes.ParagraphMark"/>; the last paragraph of a cell, or one followed by a
    /// table, ends in a <see cref="FindSpecialCodes.CellMark"/> that nothing matches. Tabs (rendered
    /// as tab-stop elements) and manual page/column breaks are single matchable characters; note
    /// references, pictures, other inline objects and tracked-deleted text are placeholders that no
    /// search matches, so a match can neither span nor rewrite them.
    /// </remarks>
    internal FindReplaceBodyProjection BuildFindReplaceProjection(bool includeFormatting)
    {
        var projection = new FindReplaceBodyProjection();
        WpfParagraph? pending = null;
        ProjectFindBlocks(Document.Blocks, projection, includeFormatting, ref pending);
        EndFindFlow(projection, ref pending);
        return projection;
    }

    private static void ProjectFindBlocks(
        BlockCollection blocks,
        FindReplaceBodyProjection projection,
        bool includeFormatting,
        ref WpfParagraph? pending)
    {
        foreach (var block in blocks)
        {
            switch (block)
            {
                case WpfParagraph paragraph:
                    if (pending is not null)
                    {
                        projection.AddAtomic(
                            FindSpecialCodes.ParagraphMark.ToString(),
                            pending.ContentEnd,
                            paragraph.ContentStart.GetInsertionPosition(LogicalDirection.Forward) ?? paragraph.ContentStart);
                    }

                    ProjectFindInlines(
                        paragraph.Inlines,
                        projection,
                        includeFormatting,
                        (paragraph.Tag as ParagraphTag)?.StyleId);
                    pending = paragraph;
                    break;
                case WpfList list:
                    foreach (var item in list.ListItems)
                        ProjectFindBlocks(item.Blocks, projection, includeFormatting, ref pending);
                    break;
                case System.Windows.Documents.Section section:
                    ProjectFindBlocks(section.Blocks, projection, includeFormatting, ref pending);
                    break;
                case WpfTable table:
                    EndFindFlow(projection, ref pending);
                    foreach (var group in table.RowGroups)
                    {
                        foreach (var row in group.Rows)
                        {
                            foreach (var cell in row.Cells)
                            {
                                WpfParagraph? cellPending = null;
                                ProjectFindBlocks(cell.Blocks, projection, includeFormatting, ref cellPending);
                                EndFindFlow(projection, ref cellPending);
                            }
                        }
                    }

                    break;
                default:
                    EndFindFlow(projection, ref pending);
                    break;
            }
        }
    }

    private static void EndFindFlow(FindReplaceBodyProjection projection, ref WpfParagraph? pending)
    {
        if (pending is null)
            return;

        projection.AddAtomic(FindSpecialCodes.CellMark.ToString(), pending.ContentEnd, pending.ContentEnd);
        pending = null;
    }

    private static void ProjectFindInlines(
        InlineCollection inlines,
        FindReplaceBodyProjection projection,
        bool includeFormatting,
        string? styleId)
    {
        foreach (var inline in inlines)
        {
            switch (inline)
            {
                case WpfRun { Tag: MultiLevelMarker }:
                    // Synthetic list-number chrome; never part of the document text.
                    break;
                case WpfRun { Tag: PageBreakMarker } pageBreak:
                    projection.AddAtomic(
                        FindSpecialCodes.ManualPageBreak.ToString(), pageBreak.ElementStart, pageBreak.ElementEnd);
                    break;
                case WpfRun { Tag: ColumnBreakMarker } columnBreak:
                    projection.AddAtomic(
                        FindSpecialCodes.ColumnBreak.ToString(), columnBreak.ElementStart, columnBreak.ElementEnd);
                    break;
                case WpfRun run when run.Text.Length > 0:
                    if (IsFindableRun(run))
                    {
                        projection.AddText(
                            run,
                            run.Text,
                            includeFormatting ? EffectiveFindFormatting(run) : RunFormatting.Default,
                            styleId);
                    }
                    else
                    {
                        projection.AddAtomic(
                            new string(FindSpecialCodes.ObjectPlaceholder, run.Text.Length),
                            run.ElementStart,
                            run.ElementEnd);
                    }

                    break;
                case LineBreak lineBreak:
                    projection.AddAtomic(
                        FindSpecialCodes.ManualLineBreak.ToString(),
                        lineBreak.ElementStart,
                        lineBreak.ElementEnd,
                        styleId: styleId,
                        structural: false);
                    break;
                case InlineUIContainer { Child: FrameworkElement { Tag: RenderedTabStopSpan tab } } tabHost:
                    projection.AddAtomic(
                        FindSpecialCodes.Tab.ToString(),
                        tabHost.ElementStart,
                        tabHost.ElementEnd,
                        includeFormatting ? tab.Formatting : null,
                        styleId,
                        structural: !includeFormatting);
                    break;
                case InlineUIContainer { Child: FrameworkElement { Tag: RenderedBookmarkBoundary } }:
                    break;
                case InlineUIContainer container:
                    projection.AddAtomic(
                        FindSpecialCodes.ObjectPlaceholder.ToString(), container.ElementStart, container.ElementEnd);
                    break;
                case AnchoredBlock anchored:
                    // A floater/figure is its own little flow (for example a floating table); text
                    // must not match across its edge.
                    projection.AddAtomic(
                        FindSpecialCodes.CellMark.ToString(), anchored.ElementStart, anchored.ContentStart);
                    WpfParagraph? anchoredPending = null;
                    ProjectFindBlocks(anchored.Blocks, projection, includeFormatting, ref anchoredPending);
                    EndFindFlow(projection, ref anchoredPending);
                    break;
                case Span span:
                    ProjectFindInlines(span.Inlines, projection, includeFormatting, styleId);
                    break;
            }
        }
    }

    /// <summary>
    /// Ordinary text and field results can be found. Note and comment reference marks and other
    /// tagged marker runs are not document text, and tracked-deleted text is skipped so a tracked
    /// Replace All cannot re-find the original it has just struck through.
    /// </summary>
    private static bool IsFindableRun(WpfRun run) => run.Tag switch
    {
        null => true,
        RunMarkers markers => markers.Comment is not { IsReference: true }
            && markers.Revision is not { Kind: RevisionKind.Deleted },
        FieldMarker or ComplexFieldMarker or CrossReferenceMarker or TableFormulaMarker => true,
        _ => false,
    };

    /// <summary>
    /// The formatting a run is actually displayed with (its own plus whatever its style gives it),
    /// with review chrome removed so a tracked insertion's underline or a comment's highlight is not
    /// mistaken for real formatting.
    /// </summary>
    private static RunFormatting EffectiveFindFormatting(WpfRun run)
    {
        var markers = run.Tag as RunMarkers;
        var scaled = run.BaselineAlignment is BaselineAlignment.Superscript or BaselineAlignment.Subscript;
        var hasChromeBackground = markers?.Comment is not null || markers?.Control is not null;
        var formatting = new RunFormatting
        {
            Bold = run.FontWeight >= FontWeights.Bold,
            Italic = run.FontStyle == FontStyles.Italic,
            Underline = run.TextDecorations?.Contains(TextDecorations.Underline[0]) == true,
            FontFamily = run.FontFamily?.Source is { Length: > 0 } source ? source.Split(',')[0].Trim() : null,
            FontSizePt = Math.Round(run.FontSize / PxPerPoint / (scaled ? SuperSubScale : 1), 2),
            ColorHex = run.Foreground is SolidColorBrush foreground ? ToHex(foreground.Color) : null,
            HighlightColorHex = !hasChromeBackground
                && run.ReadLocalValue(TextElement.BackgroundProperty) is SolidColorBrush background
                    ? ToHex(background.Color)
                    : null,
        };

        if (markers?.Revision is { } revision)
            formatting = StripRevisionChrome(formatting, revision.Kind, revision.RenderedColorHex);
        if (markers?.FormatRevision is { } formatRevision)
            formatting = StripFormatRevisionChrome(formatting, formatRevision.RenderedColorHex);
        return formatting;
    }

    /// <summary>
    /// Replaces the current selection with Find and Replace replacement text: empty text deletes the
    /// selection, and each <see cref="FindSpecialCodes.ParagraphMark"/> becomes a real paragraph break.
    /// </summary>
    internal void ReplaceSelectionForFind(string text)
    {
        if (!AllowsRestrictEditingOperation(RestrictEditingOperationKind.BodyTextEdit))
            return;

        Focus();
        if (string.IsNullOrEmpty(text))
        {
            if (!Selection.IsEmpty && !TryApplyBodyDeletion(backward: true))
                RunNativeFindEdit(() => Selection.Text = string.Empty);
            return;
        }

        var pieces = text.Split(FindSpecialCodes.ParagraphMark);
        for (var i = 0; i < pieces.Length; i++)
        {
            if (i > 0 && !TryApplyBodyParagraphBreak())
                RunNativeFindEdit(() => EditingCommands.EnterParagraphBreak.Execute(null, this));
            if (pieces[i].Length > 0)
                InsertText(pieces[i]);
        }
    }

    /// <summary>
    /// Applies the character part of Replace-with formatting to the current selection. The paragraph
    /// style part is <see cref="SetParagraphStyle"/>; the caller applies it separately because every
    /// edit re-renders and drops the selection.
    /// </summary>
    internal void ApplyFindCharacterFormatToSelection(FindFormat format)
    {
        ArgumentNullException.ThrowIfNull(format);
        if (!format.HasCharacterFormatting || Selection.IsEmpty)
            return;

        TrySetSelectedRunFormatting(formatting => format.ApplyTo(formatting) == formatting, format.ApplyTo);
    }

    /// <summary>
    /// Runs an edit the portable body session declined (for example inside a table cell) directly on
    /// the FlowDocument, captured as one command-bus entry exactly as <see cref="InsertText"/>'s
    /// structural fallback does -- WPF's own undo does not survive the re-render.
    /// </summary>
    private void RunNativeFindEdit(Action edit)
    {
        if (!TryPrepareNativeFallback(out var restoreReadOnly))
            return;

        try
        {
            CommitToModel();
            var before = _model.Blocks.ToList();
            edit();
            CommitToModel();
            var after = _model.Blocks.ToList();
            _commands.Execute(new ReplaceAllBlocksCommand(before, after, "Replace"));
        }
        finally
        {
            if (restoreReadOnly)
                IsReadOnly = true;
        }
    }
}
