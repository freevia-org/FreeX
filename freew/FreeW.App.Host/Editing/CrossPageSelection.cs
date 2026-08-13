using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace FreeW.App.Host.Editing;

/// <summary>
/// Panel-level cross-page selection model for <see cref="PaginatedEditorPanel"/>.
///
/// <para>
/// Maintains an <em>anchor</em> (the point where a selection started) and an <em>active end</em>
/// (the point it currently extends to).  Both are expressed as (box index, TextPointer) pairs.
/// When anchor and active end are in the same box the selection is entirely within that box and
/// is rendered by the native <see cref="RichTextBox"/> selection — the panel selection model
/// tracks but does not override it.  When they differ, the model renders:
/// <list type="bullet">
///   <item>Partial selection in the anchor box (from the anchor pointer to the box end).</item>
///   <item>Full selection in every fully-covered intermediate box.</item>
///   <item>Partial selection in the active-end box (from the box start to the active pointer).</item>
/// </list>
/// </para>
///
/// <para>
/// <strong>Selection rendering</strong> is done by directly manipulating each spanned
/// <see cref="RichTextBox.Selection"/> — this is the correct WPF approach for multi-element
/// selections when each element owns its own <see cref="FlowDocument"/>.  No custom adorners
/// are needed for the in-box portions; the inter-page gap is a non-content area so no highlight
/// is drawn there (deferred as noted in the spec).
/// </para>
///
/// <para>
/// <strong>Usage:</strong>
/// <list type="number">
///   <item><see cref="BeginSelection"/> — called on mouse-down or when Shift+arrow is pressed
///   from within a box at a boundary.</item>
///   <item><see cref="ExtendSelection"/> — called on each Shift+arrow step or mouse-move.</item>
///   <item><see cref="Clear"/> — called when a plain navigation key (no Shift) moves the caret,
///   or when the native within-box selection takes over.</item>
/// </list>
/// </para>
/// </summary>
internal sealed class CrossPageSelection
{
    // ── selection state ───────────────────────────────────────────────────────────────────────────

    /// <summary>Box index of the anchor point (where the selection started).</summary>
    internal int AnchorBoxIndex { get; private set; } = -1;

    /// <summary>Text position of the anchor within its box's FlowDocument.</summary>
    internal TextPointer? AnchorPointer { get; private set; }

    /// <summary>Box index of the active (moving) end of the selection.</summary>
    internal int ActiveBoxIndex { get; private set; } = -1;

    /// <summary>Text position of the active end within its box's FlowDocument.</summary>
    internal TextPointer? ActivePointer { get; private set; }

    /// <summary>True when a cross-page selection exists (anchor and active in different boxes).</summary>
    internal bool IsActive =>
        AnchorBoxIndex >= 0 &&
        ActiveBoxIndex >= 0 &&
        AnchorBoxIndex != ActiveBoxIndex;

    /// <summary>True when any selection state is recorded (even single-box).</summary>
    internal bool HasAnchor => AnchorBoxIndex >= 0 && AnchorPointer is not null;

    // ── public API ────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Sets the anchor to the current caret position in <paramref name="anchorBox"/> and clears
    /// any previous cross-page selection.
    /// </summary>
    internal void BeginSelection(IReadOnlyList<PageBox> boxes, PageBox anchorBox, TextPointer anchorPointer)
    {
        var idx = IndexOf(boxes, anchorBox);
        if (idx < 0)
            return;

        // Clear previous cross-page rendering before resetting state.
        if (IsActive)
            ClearRenderedSelection(boxes);

        AnchorBoxIndex = idx;
        AnchorPointer = anchorPointer;
        ActiveBoxIndex = idx;
        ActivePointer = anchorPointer;
    }

    /// <summary>
    /// Moves the active end of the selection to <paramref name="activePointer"/> in
    /// <paramref name="activeBox"/> and re-renders the selection across all spanned boxes.
    /// </summary>
    internal void ExtendSelection(IReadOnlyList<PageBox> boxes, PageBox activeBox, TextPointer activePointer)
    {
        if (!HasAnchor)
            return;

        var idx = IndexOf(boxes, activeBox);
        if (idx < 0)
            return;

        // Clear previous rendering.
        if (IsActive)
            ClearRenderedSelection(boxes);

        ActiveBoxIndex = idx;
        ActivePointer = activePointer;

        // Re-render.
        if (IsActive)
            ApplyRenderedSelection(boxes);
    }

    /// <summary>
    /// Clears all cross-page selection state and removes any rendered selection highlights from
    /// all boxes.
    /// </summary>
    internal void Clear(IReadOnlyList<PageBox> boxes)
    {
        if (IsActive)
            ClearRenderedSelection(boxes);

        AnchorBoxIndex = -1;
        AnchorPointer = null;
        ActiveBoxIndex = -1;
        ActivePointer = null;
    }

    // ── text extraction ───────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the plain text of the current cross-page selection in document order.
    /// Returns an empty string when the selection is not active or not valid.
    /// </summary>
    internal string GetSelectedText(IReadOnlyList<PageBox> boxes)
    {
        if (!IsActive || AnchorPointer is null || ActivePointer is null)
            return string.Empty;

        var (startBox, startPtr, endBox, endPtr) = NormalizeDirection(boxes);
        var sb = new System.Text.StringBuilder();

        for (int i = startBox; i <= endBox; i++)
        {
            var box = boxes[i];
            TextPointer from = (i == startBox) ? startPtr : box.Body.Document.ContentStart;
            TextPointer to   = (i == endBox)   ? endPtr   : box.Body.Document.ContentEnd;

            try
            {
                var range = new TextRange(from, to);
                sb.Append(range.Text);
            }
            catch { /* ignore position validity issues */ }
        }

        return sb.ToString();
    }

    // ── internal rendering helpers ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Normalizes anchor/active so that (startBox, startPtr) ≤ (endBox, endPtr) in document
    /// order, regardless of which direction the user dragged.
    /// </summary>
    private (int startBox, TextPointer startPtr, int endBox, TextPointer endPtr)
        NormalizeDirection(IReadOnlyList<PageBox> boxes)
    {
        // AnchorBoxIndex and ActiveBoxIndex are both valid here (IsActive guarantees they differ).
        if (AnchorBoxIndex <= ActiveBoxIndex)
            return (AnchorBoxIndex, AnchorPointer!, ActiveBoxIndex, ActivePointer!);
        else
            return (ActiveBoxIndex, ActivePointer!, AnchorBoxIndex, AnchorPointer!);
    }

    private void ApplyRenderedSelection(IReadOnlyList<PageBox> boxes)
    {
        var (startBox, startPtr, endBox, endPtr) = NormalizeDirection(boxes);

        for (int i = startBox; i <= endBox; i++)
        {
            var box = boxes[i];
            try
            {
                TextPointer from = (i == startBox) ? startPtr : box.Body.Document.ContentStart.GetInsertionPosition(LogicalDirection.Forward) ?? box.Body.Document.ContentStart;
                TextPointer to   = (i == endBox)   ? endPtr   : box.Body.Document.ContentEnd.GetInsertionPosition(LogicalDirection.Backward) ?? box.Body.Document.ContentEnd;

                box.Body.Selection.Select(from, to);
            }
            catch { /* ignore invalid pointer states */ }
        }
    }

    private static void ClearRenderedSelection(IReadOnlyList<PageBox> boxes)
    {
        foreach (var box in boxes)
        {
            try
            {
                // Collapse selection to caret position (no-op if already empty).
                var pos = box.Body.CaretPosition;
                box.Body.Selection.Select(pos, pos);
            }
            catch { /* ignore */ }
        }
    }

    // IReadOnlyList<T> has no IndexOf; provide a linear search helper.
    private static int IndexOf(IReadOnlyList<PageBox> list, PageBox item)
        => IndexOfBox(list, item);

    /// <summary>
    /// Returns the 0-based index of <paramref name="item"/> in <paramref name="list"/>, or -1 if
    /// not found.  Exposed internally so <see cref="PaginatedEditorPanel"/> can use the same
    /// reference-equality search without duplicating it.
    /// </summary>
    internal static int IndexOfBox(IReadOnlyList<PageBox> list, PageBox item)
    {
        for (int i = 0; i < list.Count; i++)
            if (ReferenceEquals(list[i], item))
                return i;
        return -1;
    }
}
