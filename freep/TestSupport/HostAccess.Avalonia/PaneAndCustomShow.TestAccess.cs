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
    /// <summary>
    /// Records each row's real LostFocus commit delegate as it is built, so tests can invoke the
    /// exact same production code path (production's <c>CommitName</c> local function) that a real
    /// focus round-trip runs, without depending on Avalonia's focus/visual-tree machinery being
    /// fully live in a headless test host.
    /// </summary>
    private readonly Dictionary<TextBox, Action> _renameCommitActionsForTests = new();

    partial void OnRenameCommitObserved(TextBox rename, Action commit) =>
        _renameCommitActionsForTests[rename] = commit;

    internal IReadOnlyList<string?> RenameToolTipsForTests =>
        _items.Children
            .OfType<DockPanel>()
            .Select(row => row.Children.OfType<TextBox>().SingleOrDefault())
            .Select(textBox => textBox is null ? null : ToolTip.GetTip(textBox)?.ToString())
            .ToArray();

    internal IReadOnlyList<TextBox> RenameTextBoxesForTests =>
        _items.Children
            .OfType<DockPanel>()
            .Select(row => row.Children.OfType<TextBox>().Single())
            .ToArray();

    /// <summary>
    /// Simulates a plain focus round-trip (tab in, tab out / click away with no typed edit) on
    /// the rename box for row <paramref name="rowIndex"/> by invoking production's real LostFocus
    /// commit delegate directly, exactly as Avalonia would when the box loses focus unedited.
    /// </summary>
    internal void BlurRenameWithoutEditingForTests(int rowIndex) =>
        _renameCommitActionsForTests[RenameTextBoxesForTests[rowIndex]]();
}
