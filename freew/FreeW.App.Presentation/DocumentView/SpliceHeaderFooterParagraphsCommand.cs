using FreeW.Core.Model;

namespace FreeW.App.Presentation.DocumentView;

/// <summary>Undoably replaces a contiguous paragraph span inside a section header/footer slot.</summary>
public sealed class SpliceHeaderFooterParagraphsCommand(
    int sectionIndex,
    bool useFinalSectionStore,
    int slot,
    int firstParagraphIndex,
    int removeCount,
    Func<IReadOnlyList<Paragraph>> buildReplacement) : IDocumentCommand
{
    private List<Paragraph>? _removed;
    private HeaderFooterTableParagraphAddress? _tableAddress;
    private int _insertedCount;

    public string Label => "Edit header/footer";

    public void Apply(IDocumentCommandContext context)
    {
        _removed = null;
        _tableAddress = null;
        _insertedCount = 0;
        var story = HeaderFooterCommandAddress.ResolveStory(
            context.Document,
            sectionIndex,
            useFinalSectionStore,
            slot);
        if (story is null || story.Paragraphs.Count == 0)
            return;

        var paragraphs = story.Paragraphs;
        var at = Math.Clamp(firstParagraphIndex, 0, paragraphs.Count - 1);
        var actualRemoveCount = Math.Clamp(removeCount, 0, paragraphs.Count - at);
        if (!HeaderFooterTableTextPlanner.CanSplice(story, at, actualRemoveCount))
            return;

        var tableAddress = HeaderFooterTableTextPlanner.TryResolveAddress(story, at, out var resolvedAddress)
            ? resolvedAddress
            : (HeaderFooterTableParagraphAddress?)null;
        var replacement = buildReplacement();
        if (tableAddress is not null && replacement.Count == 0)
            return;

        _removed = paragraphs.GetRange(at, actualRemoveCount);
        _tableAddress = tableAddress;
        paragraphs.RemoveRange(at, actualRemoveCount);
        paragraphs.InsertRange(at, replacement);
        if (_tableAddress is { } address && story.Table is { } table)
        {
            var cellParagraphs = table.Rows[address.RowIndex].Cells[address.CellIndex].Paragraphs;
            cellParagraphs.RemoveRange(address.CellParagraphIndex, actualRemoveCount);
            cellParagraphs.InsertRange(address.CellParagraphIndex, replacement);
        }
        _insertedCount = replacement.Count;
    }

    public void Revert(IDocumentCommandContext context)
    {
        if (_removed is null)
            return;
        var story = HeaderFooterCommandAddress.ResolveStory(
            context.Document,
            sectionIndex,
            useFinalSectionStore,
            slot);
        if (story is null)
            return;

        var paragraphs = story.Paragraphs;
        var at = Math.Clamp(firstParagraphIndex, 0, paragraphs.Count);
        var count = Math.Clamp(_insertedCount, 0, paragraphs.Count - at);
        paragraphs.RemoveRange(at, count);
        paragraphs.InsertRange(at, _removed);
        if (_tableAddress is { } address && story.Table is { } table)
        {
            var cellParagraphs = table.Rows[address.RowIndex].Cells[address.CellIndex].Paragraphs;
            var cellCount = Math.Clamp(_insertedCount, 0, cellParagraphs.Count - address.CellParagraphIndex);
            cellParagraphs.RemoveRange(address.CellParagraphIndex, cellCount);
            cellParagraphs.InsertRange(address.CellParagraphIndex, _removed);
        }
    }
}

internal static class HeaderFooterCommandAddress
{
    public static HeaderFooter? ResolveStory(
        TextDocument document,
        int sectionIndex,
        bool useFinalSectionStore,
        int slot)
    {
        var store = useFinalSectionStore || sectionIndex < 0 || sectionIndex >= document.Sections.Count
            ? document.FinalSectionHeadersFooters
            : document.Sections[sectionIndex].HeadersFooters;
        return slot switch
        {
            0 => store.Header,
            1 => store.Footer,
            2 => store.FirstHeader,
            3 => store.FirstFooter,
            4 => store.EvenHeader,
            5 => store.EvenFooter,
            _ => null
        };
    }
}
