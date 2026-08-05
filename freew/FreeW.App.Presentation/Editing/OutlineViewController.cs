using FreeW.Core.Model;

namespace FreeW.App.Presentation.Editing;

/// <summary>
/// Renderer-neutral state and operations for an outline surface. Renderers provide the current document
/// and editor mutations, then project <see cref="VisibleRows"/> and <see cref="SelectedBlockIndex"/> into
/// their native controls.
/// </summary>
public sealed class OutlineViewController
{
    private readonly Func<TextDocument> _getDocument;
    private readonly Action<int, int> _setHeadingLevel;
    private readonly Func<int, bool, int> _moveHeading;
    private IReadOnlyList<OutlineRow> _visibleRows = [];

    public OutlineViewController(
        Func<TextDocument> getDocument,
        Action<int, int> setHeadingLevel,
        Func<int, bool, int> moveHeading)
    {
        _getDocument = getDocument ?? throw new ArgumentNullException(nameof(getDocument));
        _setHeadingLevel = setHeadingLevel ?? throw new ArgumentNullException(nameof(setHeadingLevel));
        _moveHeading = moveHeading ?? throw new ArgumentNullException(nameof(moveHeading));
    }

    public int ShowLevel { get; private set; } = OutlineViewModel.ShowAllLevels;

    public bool FirstLineOnly { get; private set; }

    public IReadOnlyList<OutlineRow> VisibleRows => _visibleRows;

    public int? SelectedBlockIndex { get; private set; }

    public event Action? RowsChanged;

    public int CurrentOutlineLevel
    {
        get
        {
            if (SelectedBlockIndex is not int selectedBlockIndex)
                return -1;

            foreach (var row in _visibleRows)
            {
                if (row.BlockIndex == selectedBlockIndex)
                    return row.IsHeading ? row.Level : -1;
            }

            return -1;
        }
    }

    public void Refresh()
    {
        _visibleRows = OutlineViewModel.Build(_getDocument(), ShowLevel, FirstLineOnly);
        if (SelectedBlockIndex is int selectedBlockIndex
            && !_visibleRows.Any(row => row.BlockIndex == selectedBlockIndex))
        {
            SelectedBlockIndex = null;
        }
        RowsChanged?.Invoke();
    }

    public bool Apply(Action<int> command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (SelectedBlockIndex is not int selectedBlockIndex)
            return false;

        command(selectedBlockIndex);
        Refresh();
        return true;
    }

    public bool Move(bool moveUp)
    {
        if (SelectedBlockIndex is not int selectedBlockIndex)
            return false;

        SelectedBlockIndex = _moveHeading(selectedBlockIndex, moveUp);
        Refresh();
        return true;
    }

    public bool SelectBlock(int blockIndex)
    {
        if (!_visibleRows.Any(row => row.BlockIndex == blockIndex))
            return false;

        SelectedBlockIndex = blockIndex;
        return true;
    }

    public void ClearSelection() => SelectedBlockIndex = null;

    public void SetShowLevel(int level)
    {
        ShowLevel = level;
        Refresh();
    }

    public void SetFirstLineOnly(bool firstLineOnly)
    {
        FirstLineOnly = firstLineOnly;
        Refresh();
    }

    public bool SetOutlineLevel(int level) =>
        Apply(blockIndex => _setHeadingLevel(blockIndex, level));
}
