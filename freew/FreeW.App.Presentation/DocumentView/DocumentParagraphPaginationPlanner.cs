using FreeW.Core.Model;

namespace FreeW.App.Presentation.DocumentView;

/// <summary>
/// Owns the renderer-neutral paragraph pagination policy for Word's widow/orphan defaults.
/// Platform renderers may still choose their native keep-together primitive, but the decision must
/// come from this planner so WPF and Avalonia compose the same ordinary body paragraphs.
/// </summary>
public static class DocumentParagraphPaginationPlanner
{
    /// <summary>
    /// Returns whether an ordinary body paragraph should enter the renderer's keep-together path.
    /// Word treats an omitted widow-control token as enabled; an explicit off token is different.
    /// Table cells and paragraphs containing non-text layout objects remain caller-owned because
    /// their pagination has separate table/drawing contracts.
    /// </summary>
    public static bool ShouldKeepParagraphTogether(
        ParagraphFormatting formatting,
        bool isTableCell = false,
        bool hasNonTextLayoutObject = false)
    {
        ArgumentNullException.ThrowIfNull(formatting);

        if (hasNonTextLayoutObject || isTableCell)
            return formatting.KeepLinesTogether || formatting.WidowControl;

        return formatting.KeepLinesTogether
            || !formatting.WidowControlIsSet
            || formatting.WidowControl;
    }
}
