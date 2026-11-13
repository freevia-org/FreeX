using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows.Documents;
using FreeW.Core.Model;
using ModelBlock = FreeW.Core.Model.Block;
using ModelParagraph = FreeW.Core.Model.Paragraph;
using ModelRun = FreeW.Core.Model.Run;
using ModelTable = FreeW.Core.Model.Table;
using WpfList = System.Windows.Documents.List;
using WpfParagraph = System.Windows.Documents.Paragraph;
using WpfTable = System.Windows.Documents.Table;

namespace FreeW.App.Host.Editing;

/// <summary>
/// Reading the paged editor's pages back into the model when a paragraph, list item or table continues
/// from one page onto the next: the continuing piece is merged into the block it continues.
/// </summary>
public sealed partial class DocumentView
{
    private static readonly ConditionalWeakTable<System.Windows.Documents.Block, object> s_readContinuations = new();

    /// <summary>
    /// Marks <paramref name="block"/> (the first top-level block of a page) as the continuation of the
    /// block that ends the page before it: reading the pages merges its first paragraph into that
    /// paragraph (text continues), or its rows into that table.
    /// </summary>
    internal static void MarkReadContinuation(System.Windows.Documents.Block block) =>
        s_readContinuations.AddOrUpdate(block, true);

    internal static bool IsReadContinuation(System.Windows.Documents.Block block) =>
        s_readContinuations.TryGetValue(block, out _);

    /// <summary>
    /// Reads consecutive pages' bodies into model blocks in one pass (see
    /// <see cref="ReadRenderedPagesInto"/>); <paramref name="pageStarts"/> receives, per page, the number of
    /// blocks read before it.
    /// </summary>
    internal void ReadPagesInto(IReadOnlyList<FlowDocument> pages, IList<ModelBlock> target, int[] pageStarts) =>
        ReadRenderedPagesInto(pages.Select(page => page.Blocks).ToList(), target, _model, pageStarts);

    private static bool TryReadContinuation(
        System.Windows.Documents.Block block,
        IList<ModelBlock> target,
        TextDocument document,
        ModelTable? pendingSegmentTable)
    {
        switch (block)
        {
            case WpfTable wpfTable:
            {
                var into = pendingSegmentTable ?? (target.Count > 0 ? target[^1] as ModelTable : null);
                if (into is null)
                    return false;
                foreach (var row in ReadTable(wpfTable, document).Rows)
                    into.Rows.Add(row);
                return true;
            }
            case WpfParagraph wpfParagraph:
            {
                if (target.Count == 0 || target[^1] is not ModelParagraph head)
                    return false;
                MergeParagraphContinuation(head, ReadParagraph(wpfParagraph, document));
                return true;
            }
            case WpfList wpfList:
            {
                if (target.Count == 0 || target[^1] is not ModelParagraph head)
                    return false;
                var read = new List<ModelBlock>();
                ReadList(read, wpfList, document);
                if (read.Count == 0 || read[0] is not ModelParagraph first)
                    return false;
                MergeParagraphContinuation(head, first);
                foreach (var rest in read.Skip(1))
                    target.Add(rest);
                return true;
            }
            default:
                return false;
        }
    }

    /// <summary>
    /// Appends <paramref name="tail"/>'s runs and bookmark boundaries to <paramref name="head"/> (the head
    /// keeps its own paragraph properties: the tail's were rendered as a continuation). The run split by
    /// the page boundary is joined back when the two halves are alike in everything but their text.
    /// </summary>
    internal static void MergeParagraphContinuation(ModelParagraph head, ModelParagraph tail)
    {
        var baseIndex = head.Runs.Count;
        var join = baseIndex > 0
            && tail.Runs.Count > 0
            && !head.BookmarkBoundaries.Any(boundary => boundary.RunIndex >= baseIndex)
            && !tail.BookmarkBoundaries.Any(boundary => boundary.RunIndex <= 0)
            && AreSplitTextRuns(head.Runs[^1], tail.Runs[0]);
        if (join)
        {
            head.Runs[^1].Text = head.Runs[^1].Text + tail.Runs[0].Text;
            head.Runs.AddRange(tail.Runs.Skip(1));
        }
        else
        {
            head.Runs.AddRange(tail.Runs);
        }

        var shift = join ? baseIndex - 1 : baseIndex;
        foreach (var boundary in tail.BookmarkBoundaries)
            head.BookmarkBoundaries.Add(boundary with { RunIndex = boundary.RunIndex + shift });
        RebindBookmarkBoundaryControls(head);
    }

    private static readonly PropertyInfo[] s_runProperties = typeof(ModelRun)
        .GetProperties(BindingFlags.Public | BindingFlags.Instance)
        .Where(property => property.CanRead && property.GetIndexParameters().Length == 0 && property.Name != nameof(ModelRun.Text))
        .ToArray();

    /// <summary>Two plain text runs that differ only in their text (one model run the page boundary split).</summary>
    private static bool AreSplitTextRuns(ModelRun left, ModelRun right)
    {
        if (left.Text.Length == 0 || right.Text.Length == 0 || left.Ruby is not null || right.Ruby is not null)
            return false;
        foreach (var property in s_runProperties)
        {
            if (!Equals(property.GetValue(left), property.GetValue(right)))
                return false;
        }
        return true;
    }
}
