using Avalonia;
using FreeW.App.Presentation.Dialogs;
using FreeW.App.Presentation.DocumentView;
using FreeW.App.Presentation.Editing;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia.Editing;

internal sealed partial class ScreenClipOverlay
{
    internal ScreenPixelRect? ResultForTest => _result;

    internal void BeginSelectionForTest(Point point)
    {
        _origin = point;
        _dragging = true;
        UpdateSelectionVisual(point);
    }

    internal ScreenPixelRect? CompleteSelectionForTest(Point point, double renderScale)
    {
        _dragging = false;
        _result = ScreenClipPlanner.BuildPhysicalSelection(
            _origin.X,
            _origin.Y,
            point.X,
            point.Y,
            _virtualBounds.X,
            _virtualBounds.Y,
            renderScale);
        return _result;
    }

    internal void CancelForTest() => _result = null;
}

internal sealed partial class OutlineView
{
    internal IReadOnlyList<OutlineRow> VisibleRows => _controller.VisibleRows;
    internal int? SelectedBlockIndex => _controller.SelectedBlockIndex;
    internal void SelectBlockIndex(int blockIndex) => SelectBlock(blockIndex);
    internal void SetShowLevel(int level) => _controller.SetShowLevel(level);
    internal void SetFirstLineOnly(bool firstLineOnly) => _controller.SetFirstLineOnly(firstLineOnly);
    internal void SetOutlineLevel(int level) => _controller.SetOutlineLevel(level);
    internal int CurrentOutlineLevel => _controller.CurrentOutlineLevel;
    internal string? RowDisplayTextForTests(int blockIndex) =>
        _list.Items.OfType<OutlineRowItem>()
            .FirstOrDefault(item => item.Row.BlockIndex == blockIndex)
            ?.ToString();

    internal void ExecuteForTests(OutlineCommand command) => _controller.Execute(command);
}
