using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using FreeW.App.Presentation.DocumentView;
using FreeW.Core.Model;

namespace FreeW.App.Host.Editing;

internal sealed record FootnoteContinuationFlowComposition(
    IReadOnlyDictionary<int, DocumentFootnoteContinuationPlan> ContinuationById,
    IReadOnlyDictionary<int, System.Windows.Documents.Block> AnchorById);

/// <summary>
/// Reserves long-footnote fragments inside one canonical WPF body flow so preview, print, PDF,
/// and fidelity rendering share the same physical page sequence.
/// </summary>
internal static class FootnoteContinuationFlowComposer
{
    internal static bool TryApply(
        FlowDocument flow,
        TextDocument document,
        double contentWidthDip,
        double contentHeightDip,
        double trailingReserveDip,
        out FootnoteContinuationFlowComposition composition)
    {
        composition = new FootnoteContinuationFlowComposition(
            new Dictionary<int, DocumentFootnoteContinuationPlan>(),
            new Dictionary<int, System.Windows.Documents.Block>());
        if (document.Footnotes.Count == 0
            || document.Page.ColumnCount != 1
            || document.Sections.Count > 1
            || document.Blocks.Any(block => block is not FreeW.Core.Model.Paragraph))
        {
            return false;
        }

        var references = document.Blocks
            .Select((block, index) => (Paragraph: (FreeW.Core.Model.Paragraph)block, Index: index))
            .Select(pair => (
                pair.Index,
                Ids: pair.Paragraph.Runs
                    .Where(run => run.FootnoteId is not null)
                    .Select(run => run.FootnoteId!.Value)
                    .Distinct()
                    .ToList()))
            .Where(pair => pair.Ids.Count > 0)
            .ToList();
        if (references.Count == 0 || references.Any(reference => reference.Ids.Count != 1))
            return false;

        var overflowReferences = references.Where(reference =>
                DocumentNoteRegionPlanner.BuildFootnoteRegion(
                    document,
                    reference.Ids,
                    pageNumber: 1,
                    contentWidthDip).EstimatedHeightDip + trailingReserveDip > contentHeightDip)
            .ToList();
        if (overflowReferences.Count != 1
            || references.Any(reference => reference.Index < overflowReferences[0].Index))
        {
            return false;
        }

        var continuationById = new Dictionary<int, DocumentFootnoteContinuationPlan>();
        foreach (var reference in references)
        {
            // This estimate includes content only through the reference paragraph. The historical
            // detached-note reserve is removed because the Figure below owns the page-local band.
            var leadingHeightDip = Math.Max(
                0,
                DocumentViewLayoutPlanner.EstimateLeadingContentHeightDip(document, reference.Index + 1) - 80);
            var continuationLineHeightDip = DocumentNoteRegionPlanner.NoteTextFontSizePt
                * (96.0 / 72.0)
                * 1.25;
            var firstAvailableHeightDip = Math.Clamp(
                contentHeightDip - leadingHeightDip % contentHeightDip - trailingReserveDip
                    + continuationLineHeightDip,
                48,
                Math.Max(48, contentHeightDip - trailingReserveDip));
            var plan = DocumentNoteRegionPlanner.BuildFootnoteContinuation(
                document,
                reference.Ids,
                firstPageNumber: 1,
                contentWidthDip,
                firstAvailableHeightDip,
                Math.Max(48, contentHeightDip - trailingReserveDip));
            if (plan.Pages.Count == 0)
                return false;
            continuationById[reference.Ids[0]] = plan;
        }

        var flowBlocks = flow.Blocks.ToList();
        if (flowBlocks.Count != document.Blocks.Count
            || references.Any(reference => flowBlocks[reference.Index] is not System.Windows.Documents.Paragraph))
        {
            return false;
        }

        var anchorById = references.ToDictionary(
            reference => reference.Ids[0],
            reference => flowBlocks[reference.Index]);
        foreach (var reference in references.OrderByDescending(reference => reference.Index))
        {
            var plan = continuationById[reference.Ids[0]];
            System.Windows.Documents.Block insertionPoint = flowBlocks[reference.Index];
            var firstReserve = BuildBottomReserve(
                contentWidthDip,
                plan.Pages[0].EstimatedHeightDip + trailingReserveDip,
                breakPageBefore: false);
            flow.Blocks.InsertAfter(insertionPoint, firstReserve);
            insertionPoint = firstReserve;

            for (var pageIndex = 1; pageIndex < plan.Pages.Count - 1; pageIndex++)
            {
                var continuationOnlyPage = new BlockUIContainer(new Border
                {
                    Width = contentWidthDip,
                    Height = Math.Max(1, contentHeightDip - 1),
                    Background = Brushes.Transparent
                })
                {
                    BreakPageBefore = true,
                    Margin = new Thickness(0)
                };
                flow.Blocks.InsertAfter(insertionPoint, continuationOnlyPage);
                insertionPoint = continuationOnlyPage;
            }

            if (plan.Pages.Count > 1)
            {
                var finalReserve = BuildBottomReserve(
                    contentWidthDip,
                    plan.Pages[^1].EstimatedHeightDip + trailingReserveDip,
                    breakPageBefore: true);
                flow.Blocks.InsertAfter(insertionPoint, finalReserve);
            }
        }

        composition = new FootnoteContinuationFlowComposition(continuationById, anchorById);
        return true;
    }

    internal static IReadOnlyDictionary<int, IReadOnlyList<int>> BuildPageOwnership(
        DocumentPaginator paginator,
        FootnoteContinuationFlowComposition composition)
    {
        paginator.ComputePageCount();
        if (paginator is not DynamicDocumentPaginator dynamicPaginator)
            return new Dictionary<int, IReadOnlyList<int>>();

        try
        {
            return composition.AnchorById
                .Select(pair => (FootnoteId: pair.Key, PageIndex: dynamicPaginator.GetPageNumber(pair.Value.ContentStart)))
                .Where(marker => marker.PageIndex >= 0 && marker.PageIndex < paginator.PageCount)
                .GroupBy(marker => marker.PageIndex)
                .ToDictionary(
                    group => group.Key,
                    group => (IReadOnlyList<int>)group.Select(marker => marker.FootnoteId).Distinct().ToList());
        }
        catch (NotSupportedException)
        {
            return new Dictionary<int, IReadOnlyList<int>>();
        }
        catch (InvalidOperationException)
        {
            return new Dictionary<int, IReadOnlyList<int>>();
        }
    }

    internal static Dictionary<int, DocumentFootnoteContinuationPagePlan> BuildPageMap(
        IReadOnlyDictionary<int, IReadOnlyList<int>> footnoteIdsByPaginatorPage,
        IReadOnlyDictionary<int, DocumentFootnoteContinuationPlan> continuationById)
    {
        var result = new Dictionary<int, DocumentFootnoteContinuationPagePlan>();
        foreach (var (pageIndex, footnoteIds) in footnoteIdsByPaginatorPage.OrderBy(pair => pair.Key))
        {
            foreach (var footnoteId in footnoteIds)
            {
                if (!continuationById.TryGetValue(footnoteId, out var continuation))
                    continue;
                for (var fragmentIndex = 0; fragmentIndex < continuation.Pages.Count; fragmentIndex++)
                    result.TryAdd(pageIndex + fragmentIndex, continuation.Pages[fragmentIndex]);
            }
        }
        return result;
    }

    private static System.Windows.Documents.Paragraph BuildBottomReserve(
        double contentWidthDip,
        double heightDip,
        bool breakPageBefore)
    {
        var figure = new Figure(new System.Windows.Documents.Paragraph())
        {
            Width = new FigureLength(contentWidthDip, FigureUnitType.Pixel),
            Height = new FigureLength(Math.Max(1, heightDip), FigureUnitType.Pixel),
            HorizontalAnchor = FigureHorizontalAnchor.ContentLeft,
            VerticalAnchor = FigureVerticalAnchor.ContentBottom,
            WrapDirection = WrapDirection.Both,
            CanDelayPlacement = false,
            Margin = new Thickness(0)
        };
        return new System.Windows.Documents.Paragraph(figure)
        {
            BreakPageBefore = breakPageBefore,
            FontSize = 1,
            LineHeight = 1,
            Margin = new Thickness(0),
            Padding = new Thickness(0)
        };
    }
}
