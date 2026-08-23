namespace FreeW.Core.Model;

[Flags]
public enum TextDocumentStoryTraversalOptions
{
    None = 0,
    IncludeTextBoxes = 1,
    PreserveDuplicateParagraphs = 2,
}

/// <summary>
/// Enumerates the paragraph-bearing stories shared by package and document services. Comment ordering is
/// supplied by the caller because package serialization and model services intentionally use different
/// thread-order policies.
/// </summary>
public static class TextDocumentStoryTraversal
{
    public static IEnumerable<Paragraph> EnumerateParagraphs(
        TextDocument document,
        IEnumerable<Paragraph> commentParagraphs,
        TextDocumentStoryTraversalOptions options = TextDocumentStoryTraversalOptions.None)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(commentParagraphs);

        var preserveDuplicates = options.HasFlag(TextDocumentStoryTraversalOptions.PreserveDuplicateParagraphs);
        var seen = preserveDuplicates ? null : new HashSet<Paragraph>(ReferenceEqualityComparer.Instance);

        foreach (var paragraph in EnumerateRawParagraphs(document, commentParagraphs, options))
            if (seen is null || seen.Add(paragraph))
                yield return paragraph;
    }

    private static IEnumerable<Paragraph> EnumerateRawParagraphs(
        TextDocument document,
        IEnumerable<Paragraph> commentParagraphs,
        TextDocumentStoryTraversalOptions options)
    {
        foreach (var block in document.Blocks)
            foreach (var paragraph in EnumerateBlockParagraphs(block))
                foreach (var item in ExpandParagraph(paragraph, options))
                    yield return item;

        foreach (var section in document.Sections)
        {
            foreach (var content in EnumerateHeadersFooters(section.HeadersFooters))
                foreach (var paragraph in content.Paragraphs)
                    foreach (var item in ExpandParagraph(paragraph, options))
                        yield return item;
        }

        foreach (var paragraph in document.Footnotes.Values.SelectMany(note => note.Content))
            foreach (var item in ExpandParagraph(paragraph, options))
                yield return item;

        foreach (var paragraph in document.Endnotes.Values.SelectMany(note => note.Content))
            foreach (var item in ExpandParagraph(paragraph, options))
                yield return item;

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

    private static IEnumerable<Paragraph> EnumerateBlockParagraphs(Block block)
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

    private static IEnumerable<Paragraph> ExpandParagraph(
        Paragraph paragraph,
        TextDocumentStoryTraversalOptions options)
    {
        yield return paragraph;
        if (!options.HasFlag(TextDocumentStoryTraversalOptions.IncludeTextBoxes))
            yield break;

        foreach (var run in paragraph.Runs)
        {
            if (run.Shape is { } shape)
                foreach (var nested in shape.TextParagraphs.SelectMany(item => ExpandParagraph(item, options)))
                    yield return nested;

            if (run.DrawingGroup is { } group)
            {
                foreach (var shapeChild in group.Children.OfType<Shape>())
                    foreach (var nested in shapeChild.TextParagraphs.SelectMany(item => ExpandParagraph(item, options)))
                        yield return nested;
            }
        }
    }
}
