using FreeW.App.Presentation.Dialogs;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.DocumentView;

/// <summary>
/// Builds the page-reference resolver used by generated Tables of Authorities. Renderers supply only
/// observed physical-page geometry; this planner owns model validation, table spillover, safe fallbacks,
/// page-count bounds, and section-aware visible page-number formatting.
/// </summary>
public static class TableOfAuthoritiesPageResolverPlanner
{
    public static ToaCitationPageAddressResolver Build(
        TextDocument document,
        Func<int, int?>? observedPhysicalPageOfBlock,
        Func<int, int, int?>? observedPhysicalPageOfBlockOffset,
        int minimumPageCount = 1,
        bool allowSinglePageFallback = true)
    {
        ArgumentNullException.ThrowIfNull(document);

        int? KnownPhysicalPageOfBlock(int blockIndex)
        {
            var observed = observedPhysicalPageOfBlock?.Invoke(blockIndex);
            var explicitPage = CrossReferences.ExplicitPageNumberAtBlock(document, blockIndex);
            if (observed is > 0)
                return explicitPage is { } authoredPage
                    ? Math.Max(observed.Value, authoredPage)
                    : observed;
            return explicitPage
                ?? (blockIndex == 0 ? 1 : null);
        }

        var pageCount = Math.Max(1, minimumPageCount);
        for (var blockIndex = 0; blockIndex < document.Blocks.Count; blockIndex++)
        {
            var firstPage = KnownPhysicalPageOfBlock(blockIndex) ?? 1;
            pageCount = Math.Max(
                pageCount,
                firstPage + DocumentViewLayoutPlanner.ResolveTablePageSpan(document, blockIndex) - 1);
        }

        var displayTextOfPhysicalPage = PageNumberFormatDialogPlanner.BuildPhysicalPageReferenceResolver(
            document,
            KnownPhysicalPageOfBlock,
            pageCount);

        return (_, blockIndex, tableParagraph, runIndex, _) =>
        {
            if (tableParagraph is not null)
            {
                var tablePageOffset = DocumentViewLayoutPlanner.ResolveTableParagraphPageOffset(
                    document,
                    blockIndex,
                    tableParagraph);
                var tableFirstPage = KnownPhysicalPageOfBlock(blockIndex);
                return tablePageOffset is null || tableFirstPage is null
                    ? null
                    : CreatePageReference(
                        Math.Clamp(tableFirstPage.Value + tablePageOffset.Value, 1, pageCount),
                        displayTextOfPhysicalPage);
            }

            if (!TryGetCitationRunOffset(document, blockIndex, runIndex, out var textOffset))
                return null;

            var observedPage = observedPhysicalPageOfBlockOffset?.Invoke(blockIndex, textOffset);
            if (observedPage is > 0)
            {
                return CreatePageReference(
                    Math.Clamp(observedPage.Value, 1, pageCount),
                    displayTextOfPhysicalPage);
            }

            var blockPage = KnownPhysicalPageOfBlock(blockIndex);
            if (blockPage is > 0)
            {
                return CreatePageReference(
                    Math.Clamp(blockPage.Value, 1, pageCount),
                    displayTextOfPhysicalPage);
            }

            return pageCount == 1 && allowSinglePageFallback
                ? CreatePageReference(1, displayTextOfPhysicalPage)
                : null;
        };
    }

    private static bool TryGetCitationRunOffset(
        TextDocument document,
        int blockIndex,
        int runIndex,
        out int textOffset)
    {
        textOffset = 0;
        if (blockIndex < 0
            || blockIndex >= document.Blocks.Count
            || document.Blocks[blockIndex] is not Paragraph paragraph
            || runIndex < 0
            || runIndex >= paragraph.Runs.Count
            || paragraph.Runs[runIndex].Citation is null)
        {
            return false;
        }

        for (var index = 0; index < runIndex; index++)
            textOffset += paragraph.Runs[index].Text.Length;
        return true;
    }

    private static ToaCitationPageReference CreatePageReference(
        int physicalPage,
        Func<int, string?> displayTextOfPhysicalPage)
    {
        var reference = TableOfAuthorities.CreatePageReference(physicalPage);
        return reference with
        {
            DisplayText = displayTextOfPhysicalPage(reference.PageNumber) ?? reference.DisplayText
        };
    }
}
