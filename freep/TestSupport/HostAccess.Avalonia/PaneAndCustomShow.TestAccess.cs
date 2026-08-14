using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using FreeP.App.Compositor;

namespace FreeP.App.Avalonia;

internal sealed partial class CustomShowDialog
{
    internal void SelectCustomShowSlideForTests(int index) => _renderer.Form.SelectSlide(index);
    internal void MoveSelectedCustomShowSlideUpForTests() => _renderer.Controller.MoveSelectedSlide(-1);
    internal void MoveSelectedCustomShowSlideDownForTests() => _renderer.Controller.MoveSelectedSlide(1);
    internal void RemoveSelectedCustomShowSlideForTests() => _renderer.Controller.RemoveSelectedSlide();
    internal void AddCustomShowSlideOccurrenceForTests(string slideId) =>
        _renderer.Controller.AddSlideOccurrence(slideId);
    internal SlideShowCustomShowDragReorderPlan DragReorderCustomShowSlideForTests(
        int sourceSlideIndex,
        int targetDropIndex) =>
        _renderer.Controller.Reorder(sourceSlideIndex, targetDropIndex);

    internal void PrepareMissingNameForTests()
    {
        _nameBox.Text = string.Empty;
        _renderer.Controller.Create();
    }

    internal bool CompleteCustomShowSlideDragForTests(
        int sourceSlideIndex,
        int targetDropIndex,
        bool isInsideList) =>
        CompleteCustomShowSlideDrag(sourceSlideIndex, targetDropIndex, isInsideList);

    internal bool IsCustomShowSlideDragActiveForTests => _customShowSlideDragActive;

    internal IPointer BeginCustomShowSlideDragForTests(int sourceSlideIndex)
    {
        _customShowSlideDragStartPoint = new Point();
        _customShowSlideDragSourceIndex = sourceSlideIndex;
        _customShowSlideDragActive = true;
        var pointer = new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, isPrimary: true);
        pointer.Capture(this);
        return pointer;
    }
}

internal sealed partial class SelectionPane
{
    internal IReadOnlyList<string?> RenameToolTipsForTests =>
        _items.Children
            .OfType<DockPanel>()
            .Select(row => row.Children.OfType<TextBox>().SingleOrDefault())
            .Select(textBox => textBox is null ? null : ToolTip.GetTip(textBox)?.ToString())
            .ToArray();
}
