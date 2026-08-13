using FreeW.Core.Model;

namespace FreeW.App.Presentation.DocumentView;

/// <summary>
/// Renderer-neutral state for collapsed document headings. It owns validation, duplicate suppression,
/// stale-index pruning, and the union of all hidden heading subtrees so WPF and Avalonia apply the same
/// outline visibility policy.
/// </summary>
public sealed class OutlineCollapseState
{
    private readonly HashSet<int> _collapsedHeadingIndices = [];

    public int Count => _collapsedHeadingIndices.Count;

    public bool Collapse(IReadOnlyList<Block> blocks, int headingIndex)
    {
        ArgumentNullException.ThrowIfNull(blocks);

        var (start, end) = OutlineTools.SubtreeRange(blocks, headingIndex);
        return end > start && _collapsedHeadingIndices.Add(headingIndex);
    }

    public bool Expand(int headingIndex) =>
        _collapsedHeadingIndices.Remove(headingIndex);

    public bool IsCollapsed(int headingIndex) =>
        _collapsedHeadingIndices.Contains(headingIndex);

    public void Clear() =>
        _collapsedHeadingIndices.Clear();

    /// <summary>
    /// Returns every block hidden below the currently collapsed headings. Collapsed headings remain
    /// visible; nested collapsed headings may themselves be hidden by an ancestor, but stay tracked so
    /// their state is restored when that ancestor expands. Indices that no longer identify headings are
    /// pruned as part of the projection.
    /// </summary>
    public IReadOnlySet<int> BuildHiddenBlockIndices(IReadOnlyList<Block> blocks)
    {
        ArgumentNullException.ThrowIfNull(blocks);

        var hidden = new HashSet<int>();
        foreach (var headingIndex in _collapsedHeadingIndices.ToArray())
        {
            var (start, end) = OutlineTools.SubtreeRange(blocks, headingIndex);
            if (end <= start)
            {
                _collapsedHeadingIndices.Remove(headingIndex);
                continue;
            }

            for (var blockIndex = start + 1; blockIndex < end; blockIndex++)
                hidden.Add(blockIndex);
        }

        return hidden;
    }
}
