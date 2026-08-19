namespace FreeW.Core.Model;

/// <summary>
/// Shared paragraph walk used by <see cref="RevisionList"/> and <see cref="DocumentInspector"/> (and any
/// future consumer that needs the same reach): every paragraph reachable in the document body — top-level
/// paragraphs and those nested in table cells (including tables nested inside table cells, to any depth) —
/// plus the text-box content of any <see cref="Run.Shape"/> a paragraph's run carries, yielded immediately
/// after the paragraph that carries it (recursively, for a shape nested inside another shape's text box).
/// Mirrors the walk <see cref="TrackChanges"/> already uses for <c>HasRevisions</c>/<c>AcceptAll</c>/
/// <c>RejectAll</c>. Before this helper existed, <see cref="RevisionList"/> and <see cref="DocumentInspector"/>
/// each carried their own copy of this walk and only <see cref="TrackChanges"/>'s ever learned about text
/// boxes — a document whose only tracked change lived in a text box showed an empty Reviewing Pane and an
/// Inspect() result of zero revisions even though Accept All/Reject All handled it correctly. One shared
/// walk keeps that from drifting apart again.
/// </summary>
internal static class BodyParagraphWalk
{
    public static IEnumerable<Paragraph> Enumerate(TextDocument document) =>
        document.Blocks.SelectMany(ParagraphsInBlock);

    // Same shape (Run.Shape/text-box) descent as Enumerate(TextDocument), but starting from an
    // already-selected paragraph list rather than the document body — for a header/footer slot, or a
    // footnote's/endnote's own Content list, none of which are reachable through document.Blocks. Callers
    // that walk one of those lists need the identical text-box reach TrackChanges.ParagraphHasRevisions/
    // ResolveParagraphContainer already give them for headers/footers/footnotes/endnotes (TrackChanges.cs),
    // or a tracked change/bookmark/comment anchor living in a text box embedded in one of those slots is
    // silently missed by callers that iterate the paragraph list directly.
    public static IEnumerable<Paragraph> Enumerate(IEnumerable<Paragraph> paragraphs) =>
        paragraphs.SelectMany(WithShapeParagraphs);

    private static IEnumerable<Paragraph> ParagraphsInBlock(Block block) => block switch
    {
        Paragraph paragraph => WithShapeParagraphs(paragraph),
        Table table => TableParagraphs(table),
        _ => [],
    };

    private static IEnumerable<Paragraph> TableParagraphs(Table table)
    {
        foreach (var row in table.Rows)
        {
            foreach (var cell in row.Cells)
            {
                foreach (var cellParagraph in cell.Paragraphs)
                    foreach (var paragraph in WithShapeParagraphs(cellParagraph))
                        yield return paragraph;

                foreach (var nestedTable in cell.NestedTables)
                    foreach (var nestedParagraph in TableParagraphs(nestedTable))
                        yield return nestedParagraph;
            }
        }
    }

    // Yield the paragraph itself, then every paragraph inside any text box (Run.Shape) one of its runs
    // carries — recursively, so a shape nested inside another shape's text box is reached too. Mirrors
    // TrackChanges.ParagraphHasRevisions / ResolveRunsAndFormat's shape walk.
    private static IEnumerable<Paragraph> WithShapeParagraphs(Paragraph paragraph)
    {
        yield return paragraph;

        foreach (var run in paragraph.Runs)
        {
            if (run.Shape is not { } shape)
                continue;

            foreach (var shapeParagraph in shape.TextParagraphs)
                foreach (var nested in WithShapeParagraphs(shapeParagraph))
                    yield return nested;
        }
    }
}
