using FreeW.App.Presentation.DocumentView;
using FreeW.App.Presentation.Editing;
using FreeW.Core.Model;

namespace FreeW.App.Host.Editing;

internal sealed partial class OutlineView
{
    internal IReadOnlyList<OutlineRow> VisibleRows => _controller.VisibleRows;
    internal void SelectBlockIndex(int blockIndex) => SelectBlock(blockIndex);
    internal void SetShowLevel(int level) => _controller.SetShowLevel(level);
    internal void SetFirstLineOnly(bool firstLineOnly) => _controller.SetFirstLineOnly(firstLineOnly);
    internal void SetOutlineLevel(int level) => _controller.SetOutlineLevel(level);
    internal int? SelectedBlockIndex => _controller.SelectedBlockIndex;
    internal int CurrentOutlineLevel => _controller.CurrentOutlineLevel;
    internal void ExecuteForTests(OutlineCommand command) => _controller.Execute(command);

    internal string? RowDisplayTextForTests(int blockIndex) =>
        _list.Items.OfType<OutlineDisplayRow>()
            .FirstOrDefault(item => item.Row.BlockIndex == blockIndex)
            ?.ToString();
}
