using FreeW.App.Presentation.Ribbon;

namespace FreeW.App.Host.Editing;

public sealed partial class DocumentView
{
    /// <summary>
    /// Moves the body caret to the start of a section without taking keyboard focus, so section-scoped
    /// commands (Different First Page, margins, ...) follow the section whose header/footer is being
    /// edited after a Previous/Next navigation. Returns false when the section has no caret position
    /// to land on (for example, it starts with a table); the caret is then left where it was.
    /// </summary>
    public bool MoveCaretToSectionStart(int sectionIndex)
    {
        var sectionCount = _model.Sections.Count;
        if (sectionIndex < 0 || sectionIndex >= sectionCount)
            return false;

        var blockSections = HeaderFooterPagePlanner.MapBlocksToSections(_model.Blocks, sectionCount);
        for (var blockIndex = 0; blockIndex < blockSections.Length; blockIndex++)
        {
            if (blockSections[blockIndex] != sectionIndex)
                continue;
            if (TextPointerAtModelTextOffset(blockIndex, 0) is not { } pointer)
                continue;
            CaretPosition = pointer;
            return true;
        }

        return false;
    }
}
