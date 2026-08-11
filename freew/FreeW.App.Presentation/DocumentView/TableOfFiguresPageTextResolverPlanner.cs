using FreeW.App.Presentation.Dialogs;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.DocumentView;

/// <summary>
/// Builds the page-label resolver used by generated Tables of Figures and Tables of Tables.
/// Renderer-owned layout supplies the first physical page of each block; this shared planner owns
/// table spillover and section-aware visible page-number formatting.
/// </summary>
public static class TableOfFiguresPageTextResolverPlanner
{
    public static Func<int, TableParagraphAddress?, string?>? Build(
        TextDocument document,
        Func<int, int?>? physicalPageOfBlock,
        int minimumPageCount = 1)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (physicalPageOfBlock is null)
            return null;

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

        var displayTextOfPhysicalPage = PageNumberFormatDialogPlanner.BuildPhysicalPageReferenceResolver(
            document,
            physicalPageOfBlock,
            pageCount);
        return (blockIndex, tableParagraph) =>
        {
            var blockPage = physicalPageOfBlock(blockIndex)
                ?? CrossReferences.ExplicitPageNumberAtBlock(document, blockIndex)
                ?? 1;
            var tablePageOffset = DocumentViewLayoutPlanner.ResolveTableParagraphPageOffset(
                document,
                blockIndex,
                tableParagraph);
            return displayTextOfPhysicalPage(blockPage + (tablePageOffset ?? 0));
        };
    }
}
