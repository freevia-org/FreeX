using Avalonia;

namespace FreeW.App.Avalonia.Editing;

public sealed partial class DocumentView
{
    internal Rect? CaretRectForTest => TryGetCaretRect(out var rect) ? rect : null;

    internal double HorizontalPageExtentForTest =>
        _surfacePlan.UsesProjectedPageFlow
            ? _surfacePlan.ScrollableWidthForPages(_pageCount)
            : 0;

    internal Point RenderedPageOriginForTest(int pageIndex) =>
        new(_surfacePlan.RenderedPageLeftDip(pageIndex), _surfacePlan.RenderedPageTopDip(pageIndex));
}
