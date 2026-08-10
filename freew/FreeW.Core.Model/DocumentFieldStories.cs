namespace FreeW.Core.Model;

public enum DocumentFieldStoryKind
{
    MainDocument,
    TextBox,
    HeaderFooter,
    Footnote,
    Endnote,
    Comment,
}

public readonly record struct DocumentFieldStoryParagraph(
    DocumentFieldStoryKind StoryKind,
    int BodyBlockIndex,
    Paragraph Paragraph);

/// <summary>
/// Enumerates every paragraph-bearing Word story that FreeW models. The main story is emitted first in
/// document order, followed by final-section headers/footers, notes, and comment threads. Paragraph and
/// shape references are de-duplicated because imported sections may share the same header/footer part.
/// </summary>
public static class DocumentFieldStories
{
    public static IEnumerable<DocumentFieldStoryParagraph> Enumerate(TextDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var seenParagraphs = new HashSet<Paragraph>(ReferenceEqualityComparer.Instance);
        var seenGroups = new HashSet<DrawingGroup>(ReferenceEqualityComparer.Instance);

        for (var blockIndex = 0; blockIndex < document.Blocks.Count; blockIndex++)
        {
            switch (document.Blocks[blockIndex])
            {
                case Paragraph paragraph:
                    foreach (var item in EnumerateParagraph(
                                 paragraph,
                                 DocumentFieldStoryKind.MainDocument,
                                 blockIndex,
                                 seenParagraphs,
                                 seenGroups))
                    {
                        yield return item;
                    }

                    if (paragraph.SectionBreak is { } section)
                    {
                        foreach (var item in EnumerateHeadersFooters(
                                     section.HeadersFooters,
                                     seenParagraphs,
                                     seenGroups))
                        {
                            yield return item;
                        }
                    }
                    break;

                case Table table:
                    foreach (var paragraph in DocumentBodyParagraphs.EnumerateTable(table))
                    {
                        foreach (var item in EnumerateParagraph(
                                     paragraph,
                                     DocumentFieldStoryKind.MainDocument,
                                     blockIndex,
                                     seenParagraphs,
                                     seenGroups))
                        {
                            yield return item;
                        }
                    }
                    break;
            }
        }

        foreach (var item in EnumerateHeadersFooters(
                     document.FinalSectionHeadersFooters,
                     seenParagraphs,
                     seenGroups))
        {
            yield return item;
        }

        foreach (var note in document.Footnotes.OrderBy(item => item.Key).Select(item => item.Value))
            foreach (var paragraph in note.Content)
                foreach (var item in EnumerateParagraph(
                             paragraph,
                             DocumentFieldStoryKind.Footnote,
                             BodyBlockIndex: -1,
                             seenParagraphs,
                             seenGroups))
                    yield return item;

        foreach (var note in document.Endnotes.OrderBy(item => item.Key).Select(item => item.Value))
            foreach (var paragraph in note.Content)
                foreach (var item in EnumerateParagraph(
                             paragraph,
                             DocumentFieldStoryKind.Endnote,
                             BodyBlockIndex: -1,
                             seenParagraphs,
                             seenGroups))
                    yield return item;

        foreach (var comment in document.Comments.OrderBy(item => item.Key).SelectMany(item => item.Value.ThreadInOrder()))
            foreach (var paragraph in comment.Content)
                foreach (var item in EnumerateParagraph(
                             paragraph,
                             DocumentFieldStoryKind.Comment,
                             BodyBlockIndex: -1,
                             seenParagraphs,
                             seenGroups))
                    yield return item;
    }

    /// <summary>
    /// Position-sensitive fields use story-local ordering that FreeW does not yet model outside the main
    /// document. Other pure document/reference fields are safe to refresh in every story.
    /// </summary>
    public static bool CanRecomputeComplexField(DocumentFieldStoryKind storyKind, ComplexField field)
    {
        ArgumentNullException.ThrowIfNull(field);
        if (!ComplexFieldEngine.CanRecompute(field))
            return false;
        return storyKind == DocumentFieldStoryKind.MainDocument
            || !(field.ContainsKeyword("SEQ")
                 || field.ContainsKeyword("STYLEREF")
                 || field.ContainsKeyword("CITATION"));
    }

    private static IEnumerable<DocumentFieldStoryParagraph> EnumerateHeadersFooters(
        SectionHeadersFooters headersFooters,
        HashSet<Paragraph> seenParagraphs,
        HashSet<DrawingGroup> seenGroups)
    {
        foreach (var headerFooter in new[]
                 {
                     headersFooters.Header,
                     headersFooters.Footer,
                     headersFooters.EvenHeader,
                     headersFooters.EvenFooter,
                     headersFooters.FirstHeader,
                     headersFooters.FirstFooter,
                 })
        {
            if (headerFooter is null)
                continue;
            foreach (var paragraph in headerFooter.Paragraphs)
                foreach (var item in EnumerateParagraph(
                             paragraph,
                             DocumentFieldStoryKind.HeaderFooter,
                             BodyBlockIndex: -1,
                             seenParagraphs,
                             seenGroups))
                    yield return item;
        }
    }

    private static IEnumerable<DocumentFieldStoryParagraph> EnumerateParagraph(
        Paragraph paragraph,
        DocumentFieldStoryKind storyKind,
        int BodyBlockIndex,
        HashSet<Paragraph> seenParagraphs,
        HashSet<DrawingGroup> seenGroups)
    {
        if (!seenParagraphs.Add(paragraph))
            yield break;

        yield return new DocumentFieldStoryParagraph(storyKind, BodyBlockIndex, paragraph);
        foreach (var run in paragraph.Runs)
        {
            if (run.Shape is { } shape)
            {
                foreach (var nested in shape.TextParagraphs)
                    foreach (var item in EnumerateParagraph(
                                 nested,
                                 DocumentFieldStoryKind.TextBox,
                                 BodyBlockIndex,
                                 seenParagraphs,
                                 seenGroups))
                        yield return item;
            }

            if (run.DrawingGroup is { } group)
            {
                foreach (var item in EnumerateDrawingGroup(
                             group,
                             BodyBlockIndex,
                             seenParagraphs,
                             seenGroups))
                    yield return item;
            }
        }
    }

    private static IEnumerable<DocumentFieldStoryParagraph> EnumerateDrawingGroup(
        DrawingGroup group,
        int BodyBlockIndex,
        HashSet<Paragraph> seenParagraphs,
        HashSet<DrawingGroup> seenGroups)
    {
        if (!seenGroups.Add(group))
            yield break;

        foreach (var child in group.Children)
        {
            if (child is Shape shape)
            {
                foreach (var paragraph in shape.TextParagraphs)
                    foreach (var item in EnumerateParagraph(
                                 paragraph,
                                 DocumentFieldStoryKind.TextBox,
                                 BodyBlockIndex,
                                 seenParagraphs,
                                 seenGroups))
                        yield return item;
            }

            if (child is DrawingGroup nested)
                foreach (var item in EnumerateDrawingGroup(
                             nested,
                             BodyBlockIndex,
                             seenParagraphs,
                             seenGroups))
                    yield return item;
        }
    }
}
