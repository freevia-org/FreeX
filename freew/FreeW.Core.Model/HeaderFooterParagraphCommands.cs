namespace FreeW.Core.Model;

/// <summary>Creates a missing or empty document-level header or footer as one undoable edit.</summary>
public sealed class EnsureHeaderFooterCommand(bool isFooter) : IDocumentCommand
{
    private HeaderFooter? _previous;
    private bool _applied;

    public string Label => isFooter ? "Insert Footer" : "Insert Header";

    public void Apply(IDocumentCommandContext context)
    {
        _previous = isFooter ? context.Document.Footer : context.Document.Header;
        if (_previous is { IsEmpty: false })
            return;

        var region = new HeaderFooter();
        region.Paragraphs.Add(new Paragraph());
        if (isFooter)
            context.Document.Footer = region;
        else
            context.Document.Header = region;
        _applied = true;
    }

    public void Revert(IDocumentCommandContext context)
    {
        if (!_applied)
            return;

        if (isFooter)
            context.Document.Footer = _previous;
        else
            context.Document.Header = _previous;
        _applied = false;
    }
}

/// <summary>Rebuilds one section header/footer paragraph's runs while preserving undo state.</summary>
public sealed class EditHeaderFooterParagraphCommand(
    int sectionIndex,
    bool useFinalSectionStore,
    int slot,
    int paragraphIndex,
    Action<Paragraph> rebuild) : IDocumentCommand
{
    private List<Run>? _previous;

    public string Label => "Edit header/footer";

    public void Apply(IDocumentCommandContext context)
    {
        if (!HeaderFooterCommandAddress.TryGetParagraph(
                context.Document,
                sectionIndex,
                useFinalSectionStore,
                slot,
                paragraphIndex,
                out var paragraph))
        {
            return;
        }

        _previous = [.. paragraph.Runs];
        rebuild(paragraph);
    }

    public void Revert(IDocumentCommandContext context)
    {
        if (_previous is null
            || !HeaderFooterCommandAddress.TryGetParagraph(
                context.Document,
                sectionIndex,
                useFinalSectionStore,
                slot,
                paragraphIndex,
                out var paragraph))
        {
            return;
        }

        paragraph.Runs.Clear();
        paragraph.Runs.AddRange(_previous);
        _previous = null;
    }
}

/// <summary>Replaces one section header/footer paragraph with a generated paragraph range.</summary>
public sealed class SpliceHeaderFooterParagraphsCommand(
    int sectionIndex,
    bool useFinalSectionStore,
    int slot,
    int firstParagraphIndex,
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
        var region = HeaderFooterCommandAddress.ResolveSlot(
            context.Document,
            sectionIndex,
            useFinalSectionStore,
            slot);
        if (region is null || region.Paragraphs.Count == 0)
            return;

        var at = Math.Clamp(firstParagraphIndex, 0, region.Paragraphs.Count - 1);
        if (!HeaderFooterTableParagraphMap.CanSplice(region, at, removeCount: 1))
            return;

        var tableAddress = HeaderFooterTableParagraphMap.TryResolveAddress(region, at, out var resolvedAddress)
            ? resolvedAddress
            : (HeaderFooterTableParagraphAddress?)null;
        var replacement = buildReplacement();
        if (tableAddress is not null && replacement.Count == 0)
            return;

        _removed = region.Paragraphs.GetRange(at, 1);
        _tableAddress = tableAddress;
        region.Paragraphs.RemoveAt(at);
        region.Paragraphs.InsertRange(at, replacement);
        if (_tableAddress is { } address && region.Table is { } table)
        {
            var cellParagraphs = table.Rows[address.RowIndex].Cells[address.CellIndex].Paragraphs;
            cellParagraphs.RemoveAt(address.CellParagraphIndex);
            cellParagraphs.InsertRange(address.CellParagraphIndex, replacement);
        }
        _insertedCount = replacement.Count;
    }

    public void Revert(IDocumentCommandContext context)
    {
        if (_removed is null)
            return;

        var region = HeaderFooterCommandAddress.ResolveSlot(
            context.Document,
            sectionIndex,
            useFinalSectionStore,
            slot);
        if (region is null)
            return;

        var at = Math.Clamp(firstParagraphIndex, 0, region.Paragraphs.Count);
        var count = Math.Clamp(_insertedCount, 0, region.Paragraphs.Count - at);
        region.Paragraphs.RemoveRange(at, count);
        region.Paragraphs.InsertRange(at, _removed);
        if (_tableAddress is { } address && region.Table is { } table)
        {
            var cellParagraphs = table.Rows[address.RowIndex].Cells[address.CellIndex].Paragraphs;
            var cellCount = Math.Clamp(_insertedCount, 0, cellParagraphs.Count - address.CellParagraphIndex);
            cellParagraphs.RemoveRange(address.CellParagraphIndex, cellCount);
            cellParagraphs.InsertRange(address.CellParagraphIndex, _removed);
        }
        _removed = null;
        _tableAddress = null;
        _insertedCount = 0;
    }
}

internal static class HeaderFooterCommandAddress
{
    public static bool TryGetParagraph(
        TextDocument document,
        int sectionIndex,
        bool useFinalSectionStore,
        int slot,
        int paragraphIndex,
        out Paragraph paragraph)
    {
        paragraph = null!;
        var region = ResolveSlot(document, sectionIndex, useFinalSectionStore, slot);
        if (region is null
            || paragraphIndex < 0
            || paragraphIndex >= region.Paragraphs.Count)
        {
            return false;
        }

        paragraph = region.Paragraphs[paragraphIndex];
        return true;
    }

    public static HeaderFooter? ResolveSlot(
        TextDocument document,
        int sectionIndex,
        bool useFinalSectionStore,
        int slot)
    {
        var store = useFinalSectionStore
            || sectionIndex < 0
            || sectionIndex >= document.Sections.Count
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
            _ => null,
        };
    }
}
