using FreeW.App.Presentation.Editing;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.DocumentView;

/// <summary>
/// Combines renderer-observed physical block pages with authored page boundaries. Renderers own only
/// native layout observation; model validation, authored-page precedence, and safe fallback policy are
/// shared for TOCs, indexes, cross-references, generated tables, and field refresh.
/// </summary>
public static class DocumentReferenceBlockPageResolverPlanner
{
    public static DocumentReferenceBlockPageResolution Build(
        TextDocument document,
        Func<int, int?>? observedPhysicalPageOfBlock,
        int pageCount,
        bool allowUnobservedFirstPageFallback)
    {
        ArgumentNullException.ThrowIfNull(document);
        pageCount = Math.Max(1, pageCount);

        int? Resolve(int blockIndex)
        {
            if (blockIndex < 0 || blockIndex >= document.Blocks.Count)
                return null;

            var observedPage = observedPhysicalPageOfBlock?.Invoke(blockIndex);
            var authoredPage = CrossReferences.ExplicitPageNumberAtBlock(document, blockIndex);
            if (observedPage is > 0)
            {
                var normalizedObservedPage = Math.Clamp(observedPage.Value, 1, pageCount);
                return authoredPage is { } explicitPage
                    ? Math.Max(normalizedObservedPage, explicitPage)
                    : normalizedObservedPage;
            }

            if (authoredPage is > 0)
                return authoredPage;

            return allowUnobservedFirstPageFallback && blockIndex == 0 ? 1 : null;
        }

        return new DocumentReferenceBlockPageResolution(Resolve, pageCount);
    }
}
