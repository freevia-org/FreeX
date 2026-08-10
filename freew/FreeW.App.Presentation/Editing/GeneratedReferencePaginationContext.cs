using FreeW.App.Presentation.Dialogs;
using FreeW.App.Presentation.DocumentView;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Editing;

/// <summary>
/// Owns renderer-neutral page-span expansion and formatted page references for generated reference regions.
/// Renderers supply only their physical page lookup for a top-level document block.
/// </summary>
public sealed class GeneratedReferencePaginationContext
{
    private readonly TextDocument _document;
    private readonly Func<int, int?> _physicalPageOfBlock;
    private readonly Func<int, string?> _displayTextOfPhysicalPage;

    private GeneratedReferencePaginationContext(
        TextDocument document,
        Func<int, int?> physicalPageOfBlock,
        int effectivePageCount)
    {
        _document = document;
        _physicalPageOfBlock = physicalPageOfBlock;
        EffectivePageCount = effectivePageCount;
        _displayTextOfPhysicalPage = PageNumberFormatDialogPlanner.BuildPhysicalPageReferenceResolver(
            document,
            ResolveKnownBlockPage,
            effectivePageCount);
    }

    public int EffectivePageCount { get; }

    public static GeneratedReferencePaginationContext Create(
        TextDocument document,
        int minimumPageCount,
        Func<int, int?> physicalPageOfBlock)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(physicalPageOfBlock);

        var pageCount = Math.Max(1, minimumPageCount);
        for (var blockIndex = 0; blockIndex < document.Blocks.Count; blockIndex++)
        {
            var firstPage = physicalPageOfBlock(blockIndex)
                ?? CrossReferences.ExplicitPageNumberAtBlock(document, blockIndex)
                ?? 1;
            pageCount = Math.Max(
                pageCount,
                firstPage + DocumentViewLayoutPlanner.ResolveTablePageSpan(document, blockIndex) - 1);
        }

        return new GeneratedReferencePaginationContext(document, physicalPageOfBlock, pageCount);
    }

    public string? ResolvePageText(int blockIndex, TableParagraphAddress? tableParagraph)
    {
        var blockPage = ResolveKnownBlockPage(blockIndex) ?? 1;
        var tablePageOffset = DocumentViewLayoutPlanner.ResolveTableParagraphPageOffset(
            _document,
            blockIndex,
            tableParagraph);
        var physicalPage = tablePageOffset is { } offset
            ? blockPage + offset
            : blockPage;
        return _displayTextOfPhysicalPage(physicalPage);
    }

    public ToaCitationPageReference? ResolveTableOfAuthoritiesPageReference(
        int blockIndex,
        TableParagraphAddress? tableParagraph,
        bool clampToEffectivePageCount = false)
    {
        var tablePageOffset = DocumentViewLayoutPlanner.ResolveTableParagraphPageOffset(
            _document,
            blockIndex,
            tableParagraph);
        var tableFirstPage = ResolveKnownBlockPage(blockIndex);
        if (tablePageOffset is null || tableFirstPage is null)
            return null;

        return CreateTableOfAuthoritiesPageReference(
            tableFirstPage.Value + tablePageOffset.Value,
            clampToEffectivePageCount);
    }

    public ToaCitationPageReference CreateTableOfAuthoritiesPageReference(
        int physicalPage,
        bool clampToEffectivePageCount = false)
    {
        var resolvedPhysicalPage = Math.Max(1, physicalPage);
        if (clampToEffectivePageCount)
            resolvedPhysicalPage = Math.Min(resolvedPhysicalPage, EffectivePageCount);

        var reference = TableOfAuthorities.CreatePageReference(resolvedPhysicalPage);
        return reference with
        {
            DisplayText = _displayTextOfPhysicalPage(reference.PageNumber) ?? reference.DisplayText
        };
    }

    private int? ResolveKnownBlockPage(int blockIndex) =>
        _physicalPageOfBlock(blockIndex)
        ?? CrossReferences.ExplicitPageNumberAtBlock(_document, blockIndex);
}
