using FreeW.App.Presentation.DocumentView;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Editing;

public sealed record FreeWDisplaySelectionClipboardDocuments(TextDocument? RichDocument, TextDocument? NativeDocument);

/// <summary>Copies exact displayed selections without treating their offsets as cached model-text offsets.</summary>
public static class FreeWDisplaySelectionClipboardBuilder
{
    public static FreeWDisplaySelectionClipboardDocuments Build(
        TextDocument source, Paragraph paragraph, DocumentDisplaySelectionPlan plan)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(paragraph);
        ArgumentNullException.ThrowIfNull(plan);
        if (plan.SelectedText.Length == 0)
            return new(null, null);

        var rich = new TextDocument();
        var richParagraph = new Paragraph { Formatting = DocumentParagraphFormattingResolver.Resolve(source, paragraph) };
        rich.Blocks.Add(richParagraph);
        if (!plan.CanMutate)
        {
            // Copy remains useful for a partial field result, but no field instruction or
            // native object may be manufactured from that partial display selection.
            richParagraph.Runs.Add(new Run(plan.SelectedText, source.DefaultRun));
            return new(rich, null);
        }

        var literals = plan.LiteralRanges.ToDictionary(slice => slice.Span.SourceRunIndex);
        var atomics = plan.WholeAtomicRuns.ToDictionary(span => span.SourceRunIndex);
        var selectedIndexes = literals.Keys.Concat(atomics.Keys).Order().ToArray();
        if (selectedIndexes.Length == 0)
            throw new ArgumentException("A nonempty mutable display plan must include selected run slices.", nameof(plan));
        var native = new TextDocument
        {
            DefaultRun = source.DefaultRun,
            DefaultParagraph = source.DefaultParagraph,
            Theme = source.Theme,
            ThemeScriptFonts = source.ThemeScriptFonts,
            UseWordApplicationDefaultLineSpacing = source.UseWordApplicationDefaultLineSpacing,
            UseWordApplicationDefaultRunFormatting = source.UseWordApplicationDefaultRunFormatting,
        };
        var nativeParagraph = new Paragraph
        {
            Formatting = paragraph.Formatting,
            StyleId = paragraph.StyleId,
            BlockContentControl = paragraph.BlockContentControl,
        };
        native.Blocks.Add(nativeParagraph);
        var supportsNative = true;
        foreach (var index in selectedIndexes)
        {
            var span = literals.TryGetValue(index, out var slice) ? slice.Span : atomics[index];
            if (index < 0 || index >= paragraph.Runs.Count || !ReferenceEquals(paragraph.Runs[index], span.SourceRun))
                throw new ArgumentException("The display plan must describe the supplied source paragraph.", nameof(plan));
            var run = span.SourceRun;
            var display = slice is not null
                ? plan.DisplayText.Substring(slice.DisplayStart, slice.DisplayLength)
                : span.DisplayText;
            richParagraph.Runs.Add(new Run(display, DocumentRunFormattingResolver.Resolve(source, paragraph, run)));
            if (slice is not null)
            {
                nativeParagraph.Runs.Add(RevisionEditPlanner.CloneRunWithText(run,
                    run.Text.Substring(slice.ModelStart, slice.ModelLength)));
            }
            else
            {
                // These atoms are fully represented by their model payload and annotations.
                // Package-backed drawings, citations and subdocuments need a separate graph
                // transfer; omit the native fragment rather than producing dangling references.
                supportsNative &= IsSelfContainedAtomicRun(run);
                var single = new Paragraph();
                single.Runs.Add(run);
                nativeParagraph.Runs.Add(((Paragraph)DocumentMerge.CloneBlock(single)).Runs[0]);
            }
        }
        if (!supportsNative)
            return new(rich, null);

        CopyBookmarks(paragraph, nativeParagraph, selectedIndexes, literals);
        foreach (var (id, style) in source.Styles)
            native.Styles[id] = style;
        CopyAnnotations(source, native, nativeParagraph);
        return new(rich, native);
    }

    private static bool IsSelfContainedAtomicRun(Run run) =>
        run.PreservedDrawing is null && run.SubDocument is null && run.Citation is null
        && (run.FieldKind != RunFieldKind.None || run.ComplexField is not null
            || run.FootnoteId is not null || run.EndnoteId is not null || run.Equation is not null);

    private static void CopyAnnotations(TextDocument source, TextDocument target, Paragraph paragraph)
    {
        var threads = CommentThreadIndex.BuildTopLevelByCommentId(source);
        var pending = new Queue<Run>(paragraph.Runs);
        while (pending.TryDequeue(out var run))
        {
            if (run.FootnoteId is { } footnoteId && !target.Footnotes.ContainsKey(footnoteId)
                && source.Footnotes.TryGetValue(footnoteId, out var footnote))
            {
                target.Footnotes[footnoteId] = footnote;
                foreach (var nested in footnote.Content.SelectMany(item => item.Runs)) pending.Enqueue(nested);
            }
            if (run.EndnoteId is { } endnoteId && !target.Endnotes.ContainsKey(endnoteId)
                && source.Endnotes.TryGetValue(endnoteId, out var endnote))
            {
                target.Endnotes[endnoteId] = endnote;
                foreach (var nested in endnote.Content.SelectMany(item => item.Runs)) pending.Enqueue(nested);
            }
            if (run.CommentId is { } commentId && threads.TryGetValue(commentId, out var thread)
                && !target.Comments.ContainsKey(thread.Id))
            {
                target.Comments[thread.Id] = thread;
                foreach (var nested in thread.ThreadInOrder().SelectMany(item => item.Content).SelectMany(item => item.Runs))
                    pending.Enqueue(nested);
            }
        }
    }

    private static void CopyBookmarks(Paragraph source, Paragraph target, int[] indexes,
        IReadOnlyDictionary<int, DocumentLiteralRunSelection> literals)
    {
        target.BookmarkNames.AddRange(source.BookmarkNames);
        var first = indexes[0];
        var last = indexes[^1];
        var start = (Index: first, Offset: literals.TryGetValue(first, out var firstSlice) ? firstSlice.ModelStart : 0);
        var end = literals.TryGetValue(last, out var lastSlice)
            && lastSlice.ModelStart + lastSlice.ModelLength < source.Runs[last].Text.Length
            ? (Index: last, Offset: lastSlice.ModelStart + lastSlice.ModelLength)
            : (Index: last + 1, Offset: 0);
        foreach (var boundary in source.BookmarkBoundaries)
        {
            var own = (Index: Math.Clamp(boundary.RunIndex, 0, source.Runs.Count), Offset: 0);
            var partner = source.BookmarkBoundaries.FirstOrDefault(other =>
                other.PairKey == boundary.PairKey && !ReferenceEquals(other, boundary));
            bool intersects;
            if (partner is null)
                intersects = boundary.Kind == BookmarkBoundaryKind.Start
                    ? own.CompareTo(end) < 0 : own.CompareTo(start) > 0;
            else
            {
                var other = (Index: Math.Clamp(partner.RunIndex, 0, source.Runs.Count), Offset: 0);
                var lo = own.CompareTo(other) <= 0 ? own : other;
                var hi = own.CompareTo(other) <= 0 ? other : own;
                intersects = lo == hi
                    ? lo.CompareTo(start) >= 0 && lo.CompareTo(end) <= 0
                    : lo.CompareTo(end) < 0 && hi.CompareTo(start) > 0;
            }
            if (intersects)
                target.BookmarkBoundaries.Add(boundary with { RunIndex = indexes.Count(index => index < boundary.RunIndex) });
        }
    }
}
