namespace FreeW.Core.Model;

/// <summary>The text stories of a document that live outside the body block list.</summary>
public enum DocumentStoryKind
{
    HeaderFooter,
    Footnote,
    Endnote,
    Comment,
    TextBox,
}

/// <summary>Which header/footer slot of a section a paragraph belongs to.</summary>
public enum StoryHeaderFooterSlot
{
    Header,
    Footer,
    FirstHeader,
    FirstFooter,
    EvenHeader,
    EvenFooter,
}

/// <summary>
/// A stable address of one non-body paragraph: its story kind plus its ordinal in
/// <see cref="DocumentStories.Enumerate"/>'s canonical walk of that kind. An address stays valid for
/// as long as the story structure is unchanged, which is what an undoable in-place run edit needs --
/// it must find the same paragraph again on undo and redo even if the object was replaced meanwhile.
/// </summary>
public readonly record struct StoryParagraphAddress(DocumentStoryKind Kind, int Ordinal);

/// <summary>One paragraph of a non-body story, with enough context to describe where it is.</summary>
/// <param name="Address">Stable address for <see cref="DocumentStories.Resolve"/>.</param>
/// <param name="Paragraph">The live paragraph.</param>
/// <param name="StoryNumber">
/// 1-based section number for a header/footer, the note or comment id for a note or comment, and the
/// 1-based text-box number for a text box.
/// </param>
/// <param name="Slot">The header/footer slot; meaningless for other kinds.</param>
/// <param name="TextBoxHostRun">
/// For a text box, the run whose <see cref="Run.Text"/> mirrors the box's text, so an edit can keep
/// the mirror current.
/// </param>
public sealed record StoryParagraph(
    StoryParagraphAddress Address,
    Paragraph Paragraph,
    int StoryNumber,
    StoryHeaderFooterSlot Slot = StoryHeaderFooterSlot.Header,
    Run? TextBoxHostRun = null)
{
    public DocumentStoryKind Kind => Address.Kind;
}

/// <summary>
/// Canonical walk of every paragraph outside <see cref="TextDocument.Blocks"/> that carries user
/// text: all sections' header/footer slots, footnotes, endnotes, comments (with replies) and text
/// boxes (wherever their anchoring run lives, including inside a header, a note or a table cell).
/// </summary>
public static class DocumentStories
{
    public static IEnumerable<StoryParagraph> Enumerate(TextDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var seen = new HashSet<Paragraph>(ReferenceEqualityComparer.Instance);
        var textBoxHosts = new List<Paragraph>();
        CollectBlockParagraphs(document.Blocks, textBoxHosts);

        var ordinal = 0;
        var sections = document.Sections;
        for (var sectionIndex = 0; sectionIndex < sections.Count; sectionIndex++)
        {
            foreach (var (slot, region) in Slots(sections[sectionIndex].HeadersFooters))
            {
                foreach (var paragraph in region.Paragraphs)
                {
                    if (!seen.Add(paragraph))
                        continue;
                    textBoxHosts.Add(paragraph);
                    yield return new StoryParagraph(
                        new StoryParagraphAddress(DocumentStoryKind.HeaderFooter, ordinal++),
                        paragraph,
                        sectionIndex + 1,
                        slot);
                }
            }
        }

        ordinal = 0;
        foreach (var (id, note) in document.Footnotes.Where(pair => pair.Key > 0).OrderBy(pair => pair.Key))
        {
            foreach (var paragraph in note.Content)
            {
                if (!seen.Add(paragraph))
                    continue;
                textBoxHosts.Add(paragraph);
                yield return new StoryParagraph(
                    new StoryParagraphAddress(DocumentStoryKind.Footnote, ordinal++), paragraph, id);
            }
        }

        ordinal = 0;
        foreach (var (id, note) in document.Endnotes.Where(pair => pair.Key > 0).OrderBy(pair => pair.Key))
        {
            foreach (var paragraph in note.Content)
            {
                if (!seen.Add(paragraph))
                    continue;
                textBoxHosts.Add(paragraph);
                yield return new StoryParagraph(
                    new StoryParagraphAddress(DocumentStoryKind.Endnote, ordinal++), paragraph, id);
            }
        }

        ordinal = 0;
        foreach (var comment in document.Comments
                     .OrderBy(pair => pair.Key)
                     .SelectMany(pair => pair.Value.ThreadInOrder()))
        {
            foreach (var paragraph in comment.Content)
            {
                if (!seen.Add(paragraph))
                    continue;
                yield return new StoryParagraph(
                    new StoryParagraphAddress(DocumentStoryKind.Comment, ordinal++), paragraph, comment.Id);
            }
        }

        ordinal = 0;
        var boxNumber = 0;
        var seenShapes = new HashSet<Shape>(ReferenceEqualityComparer.Instance);
        foreach (var host in textBoxHosts)
        {
            foreach (var run in host.Runs)
            {
                foreach (var shape in TextShapes(run))
                {
                    if (!seenShapes.Add(shape) || shape.TextParagraphs.Count == 0)
                        continue;

                    boxNumber++;
                    foreach (var paragraph in shape.TextParagraphs)
                    {
                        if (!seen.Add(paragraph))
                            continue;
                        yield return new StoryParagraph(
                            new StoryParagraphAddress(DocumentStoryKind.TextBox, ordinal++),
                            paragraph,
                            boxNumber,
                            TextBoxHostRun: ReferenceEquals(run.Shape, shape) ? run : null);
                    }
                }
            }
        }
    }

    /// <summary>Finds the paragraph an address names, or null when the story no longer has it.</summary>
    public static StoryParagraph? Resolve(TextDocument document, StoryParagraphAddress address) =>
        Enumerate(document).FirstOrDefault(story => story.Address == address);

    private static IEnumerable<(StoryHeaderFooterSlot Slot, HeaderFooter Region)> Slots(SectionHeadersFooters store)
    {
        if (store.Header is { } header)
            yield return (StoryHeaderFooterSlot.Header, header);
        if (store.Footer is { } footer)
            yield return (StoryHeaderFooterSlot.Footer, footer);
        if (store.FirstHeader is { } firstHeader)
            yield return (StoryHeaderFooterSlot.FirstHeader, firstHeader);
        if (store.FirstFooter is { } firstFooter)
            yield return (StoryHeaderFooterSlot.FirstFooter, firstFooter);
        if (store.EvenHeader is { } evenHeader)
            yield return (StoryHeaderFooterSlot.EvenHeader, evenHeader);
        if (store.EvenFooter is { } evenFooter)
            yield return (StoryHeaderFooterSlot.EvenFooter, evenFooter);
    }

    private static void CollectBlockParagraphs(IEnumerable<Block> blocks, List<Paragraph> target)
    {
        foreach (var block in blocks)
        {
            switch (block)
            {
                case Paragraph paragraph:
                    target.Add(paragraph);
                    break;
                case Table table:
                    CollectTableParagraphs(table, target);
                    break;
            }
        }
    }

    private static void CollectTableParagraphs(Table table, List<Paragraph> target)
    {
        foreach (var row in table.Rows)
        {
            foreach (var cell in row.Cells)
            {
                target.AddRange(cell.Paragraphs);
                foreach (var nested in cell.NestedTables)
                    CollectTableParagraphs(nested, target);
            }
        }
    }

    private static IEnumerable<Shape> TextShapes(Run run)
    {
        if (run.Shape is { } shape)
            yield return shape;
        if (run.DrawingGroup is { } group)
        {
            foreach (var child in GroupShapes(group))
                yield return child;
        }
    }

    private static IEnumerable<Shape> GroupShapes(DrawingGroup group)
    {
        foreach (var child in group.Children)
        {
            switch (child)
            {
                case Shape shape:
                    yield return shape;
                    break;
                case DrawingGroup nested:
                    foreach (var inner in GroupShapes(nested))
                        yield return inner;
                    break;
            }
        }
    }
}

/// <summary>
/// Undoable in-place edit of one non-body story paragraph (its runs, style and formatting), addressed
/// by <see cref="StoryParagraphAddress"/> so undo/redo re-resolve the live paragraph. The snapshot is
/// a deep copy of the runs: story edits mutate runs in place, so holding the original run objects
/// would snapshot nothing.
/// </summary>
public sealed class EditStoryParagraphCommand(
    StoryParagraphAddress address,
    Action<Paragraph> edit,
    string label = "Replace") : IDocumentCommand
{
    private List<Run>? _previousRuns;
    private string? _previousStyleId;
    private ParagraphFormatting? _previousFormatting;
    private List<BookmarkBoundary>? _previousBoundaries;

    public string Label => label;

    public DocumentCommandMutationKind MutationKind =>
        address.Kind == DocumentStoryKind.Comment
            ? DocumentCommandMutationKind.Comment
            : DocumentCommandMutationKind.BodyText;

    public void Apply(IDocumentCommandContext context)
    {
        _previousRuns = null;
        if (DocumentStories.Resolve(context.Document, address) is not { } story)
            return;

        var paragraph = story.Paragraph;
        _previousRuns = paragraph.Runs.Select(run => RevisionEditPlanner.CloneRunWithText(run, run.Text)).ToList();
        _previousStyleId = paragraph.StyleId;
        _previousFormatting = paragraph.Formatting;
        _previousBoundaries = [.. paragraph.BookmarkBoundaries];

        edit(paragraph);
        SyncTextBoxMirror(story);
    }

    public void Revert(IDocumentCommandContext context)
    {
        if (_previousRuns is null
            || DocumentStories.Resolve(context.Document, address) is not { } story)
        {
            return;
        }

        var paragraph = story.Paragraph;
        paragraph.Runs.Clear();
        paragraph.Runs.AddRange(_previousRuns);
        paragraph.StyleId = _previousStyleId;
        paragraph.Formatting = _previousFormatting!;
        paragraph.BookmarkBoundaries.Clear();
        paragraph.BookmarkBoundaries.AddRange(_previousBoundaries!);
        _previousRuns = null;
        SyncTextBoxMirror(story);
    }

    private static void SyncTextBoxMirror(StoryParagraph story)
    {
        if (story.TextBoxHostRun is { Shape: { } shape } hostRun)
            hostRun.Text = shape.HasText ? shape.PlainText : string.Empty;
    }
}
