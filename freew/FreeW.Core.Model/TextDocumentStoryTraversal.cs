namespace FreeW.Core.Model;

[Flags]
public enum TextDocumentStoryTraversalOptions
{
    None = 0,
    IncludeTextBoxes = 1,
    PreserveDuplicateParagraphs = 2,
    IncludeShapeTextBoxes = 4,
    IncludeNestedTables = 8,
}

[Flags]
public enum TextDocumentStorySubset
{
    None = 0,
    Body = 1,
    HeadersFooters = 2,
    Footnotes = 4,
    Endnotes = 8,
    All = Body | HeadersFooters | Footnotes | Endnotes,
}

/// <summary>
/// Enumerates the paragraph-bearing stories shared by package and document services. Comment ordering is
/// supplied by the caller because package serialization and model services intentionally use different
/// thread-order policies.
/// </summary>
public static class TextDocumentStoryTraversal
{
    public static IEnumerable<Paragraph> EnumerateBlockParagraphs(
        IEnumerable<Block> blocks,
        TextDocumentStoryTraversalOptions options = TextDocumentStoryTraversalOptions.None)
    {
        ArgumentNullException.ThrowIfNull(blocks);
        foreach (var block in blocks)
            foreach (var paragraph in EnumerateBlockParagraphsCore(block, options))
                yield return paragraph;
    }

    public static IEnumerable<Paragraph> EnumerateParagraphs(
        TextDocument document,
        IEnumerable<Paragraph> commentParagraphs,
        TextDocumentStoryTraversalOptions options = TextDocumentStoryTraversalOptions.None)
        => EnumerateParagraphs(
            document,
            TextDocumentStorySubset.All,
            commentParagraphs,
            options);

    public static IEnumerable<Paragraph> EnumerateParagraphs(
        TextDocument document,
        TextDocumentStorySubset stories,
        TextDocumentStoryTraversalOptions options = TextDocumentStoryTraversalOptions.None)
        => EnumerateParagraphs(document, stories, [], options);

    private static IEnumerable<Paragraph> EnumerateParagraphs(
        TextDocument document,
        TextDocumentStorySubset stories,
        IEnumerable<Paragraph> commentParagraphs,
        TextDocumentStoryTraversalOptions options)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(commentParagraphs);

        var preserveDuplicates = options.HasFlag(TextDocumentStoryTraversalOptions.PreserveDuplicateParagraphs);
        var seen = preserveDuplicates ? null : new HashSet<Paragraph>(ReferenceEqualityComparer.Instance);

        foreach (var paragraph in EnumerateRawParagraphs(document, stories, commentParagraphs, options))
            if (seen is null || seen.Add(paragraph))
                yield return paragraph;
    }

    private static IEnumerable<Paragraph> EnumerateRawParagraphs(
        TextDocument document,
        TextDocumentStorySubset stories,
        IEnumerable<Paragraph> commentParagraphs,
        TextDocumentStoryTraversalOptions options)
    {
        if (stories.HasFlag(TextDocumentStorySubset.Body))
        {
            foreach (var block in document.Blocks)
                foreach (var paragraph in EnumerateBlockParagraphsCore(block, options))
                    foreach (var item in ExpandParagraph(paragraph, options))
                        yield return item;
        }

        if (stories.HasFlag(TextDocumentStorySubset.HeadersFooters))
        {
            foreach (var section in document.Sections)
            {
                foreach (var content in EnumerateHeadersFooters(section.HeadersFooters))
                    foreach (var paragraph in content.Paragraphs)
                        foreach (var item in ExpandParagraph(paragraph, options))
                            yield return item;
            }
        }

        if (stories.HasFlag(TextDocumentStorySubset.Footnotes))
        {
            foreach (var paragraph in document.Footnotes.Values.SelectMany(note => note.Content))
                foreach (var item in ExpandParagraph(paragraph, options))
                    yield return item;
        }

        if (stories.HasFlag(TextDocumentStorySubset.Endnotes))
        {
            foreach (var paragraph in document.Endnotes.Values.SelectMany(note => note.Content))
                foreach (var item in ExpandParagraph(paragraph, options))
                    yield return item;
        }

        foreach (var paragraph in commentParagraphs)
            foreach (var item in ExpandParagraph(paragraph, options))
                yield return item;
    }

    private static IEnumerable<HeaderFooter> EnumerateHeadersFooters(SectionHeadersFooters headersFooters)
    {
        if (headersFooters.Header is { } header)
            yield return header;
        if (headersFooters.Footer is { } footer)
            yield return footer;
        if (headersFooters.EvenHeader is { } evenHeader)
            yield return evenHeader;
        if (headersFooters.EvenFooter is { } evenFooter)
            yield return evenFooter;
        if (headersFooters.FirstHeader is { } firstHeader)
            yield return firstHeader;
        if (headersFooters.FirstFooter is { } firstFooter)
            yield return firstFooter;
    }

    private static IEnumerable<Paragraph> EnumerateBlockParagraphsCore(
        Block block,
        TextDocumentStoryTraversalOptions options)
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
            {
                foreach (var cellParagraph in cell.Paragraphs)
                    yield return cellParagraph;

                if (options.HasFlag(TextDocumentStoryTraversalOptions.IncludeNestedTables))
                {
                    foreach (var nestedTable in cell.NestedTables)
                        foreach (var nestedParagraph in EnumerateBlockParagraphsCore(nestedTable, options))
                            yield return nestedParagraph;
                }
            }
    }

    private static IEnumerable<Paragraph> ExpandParagraph(
        Paragraph paragraph,
        TextDocumentStoryTraversalOptions options)
    {
        yield return paragraph;
        foreach (var run in paragraph.Runs)
        {
            if ((options.HasFlag(TextDocumentStoryTraversalOptions.IncludeTextBoxes)
                    || options.HasFlag(TextDocumentStoryTraversalOptions.IncludeShapeTextBoxes))
                && run.Shape is { } shape)
            {
                foreach (var nested in shape.TextParagraphs.SelectMany(item => ExpandParagraph(item, options)))
                    yield return nested;
            }

            if (options.HasFlag(TextDocumentStoryTraversalOptions.IncludeTextBoxes)
                && run.DrawingGroup is { } group)
            {
                foreach (var shapeChild in group.Children.OfType<Shape>())
                    foreach (var nested in shapeChild.TextParagraphs.SelectMany(item => ExpandParagraph(item, options)))
                        yield return nested;
            }
        }
    }
}
