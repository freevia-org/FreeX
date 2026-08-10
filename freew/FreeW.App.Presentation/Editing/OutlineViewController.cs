using FreeW.Core.Model;

namespace FreeW.App.Presentation.Editing;

public enum OutlineCommand
{
    PromoteToHeading1,
    Promote,
    Demote,
    MoveUp,
    MoveDown,
    Expand,
    Collapse,
}

public sealed record OutlineLevelOption(string Label, int Level)
{
    public override string ToString() => Label;
}

public readonly record struct OutlineCommandPlan(
    OutlineCommand Command,
    string Label,
    bool StartsGroup = false);

public readonly record struct OutlineRowMarkers(
    string ExpandedHeading,
    string CollapsedHeading,
    string Body);

public readonly record struct OutlineProjectedRow(OutlineRow Row, bool IsCollapsed);

/// <summary>
/// Canonical labels, option catalogs, toolbar ordering, and row text projection for the outline surface.
/// Native renderers retain control construction and choose only their heading marker glyphs.
/// </summary>
public static class OutlineViewPlanner
{
    public const string ShowLevelLabel = "Show Level:";
    public const string OutlineLevelLabel = "Outline Level:";
    public const string ShowFirstLineOnlyLabel = "Show First Line Only";

    public static IReadOnlyList<OutlineLevelOption> ShowLevelOptions { get; } =
        BuildShowLevelOptions();

    public static IReadOnlyList<OutlineLevelOption> OutlineLevelOptions { get; } =
        BuildOutlineLevelOptions();

    public static IReadOnlyList<OutlineCommandPlan> CommandPlans { get; } =
    [
        new(OutlineCommand.PromoteToHeading1, "Promote to Heading 1"),
        new(OutlineCommand.Promote, "Promote"),
        new(OutlineCommand.Demote, "Demote"),
        new(OutlineCommand.MoveUp, "Move Up", StartsGroup: true),
        new(OutlineCommand.MoveDown, "Move Down"),
        new(OutlineCommand.Expand, "Expand", StartsGroup: true),
        new(OutlineCommand.Collapse, "Collapse"),
    ];

    public static int OutlineLevelOptionIndex(int level)
    {
        for (var index = 0; index < OutlineLevelOptions.Count; index++)
        {
            if (OutlineLevelOptions[index].Level == level)
                return index;
        }

        return 0;
    }

    public static string FormatRow(OutlineProjectedRow projectedRow, OutlineRowMarkers markers)
    {
        var row = projectedRow.Row;
        var indent = new string(' ', Math.Max(0, row.Level) * 4);
        var marker = row.IsHeading
            ? projectedRow.IsCollapsed ? markers.CollapsedHeading : markers.ExpandedHeading
            : markers.Body;
        var text = row.Text.Length > 0
            ? row.Text
            : row.IsHeading ? "(untitled heading)" : string.Empty;
        return indent + marker + text;
    }

    private static IReadOnlyList<OutlineLevelOption> BuildShowLevelOptions()
    {
        var options = new List<OutlineLevelOption>
        {
            new("All Levels", OutlineViewModel.ShowAllLevels),
        };
        for (var level = OutlineViewModel.MinShowLevel; level <= OutlineViewModel.MaxShowLevel; level++)
            options.Add(new OutlineLevelOption($"Level {level}", level));
        return options.AsReadOnly();
    }

    private static IReadOnlyList<OutlineLevelOption> BuildOutlineLevelOptions()
    {
        var options = new List<OutlineLevelOption>
        {
            new("Body Text", -1),
            new("Title", 0),
        };
        for (var level = 1; level <= OutlineTools.MaxHeadingLevel; level++)
            options.Add(new OutlineLevelOption($"Level {level}", level));
        return options.AsReadOnly();
    }
}

/// <summary>
/// Native editor operations required by the portable outline controller. The renderer adapts its editor
/// once at construction; selection, command routing, and row projection remain canonical.
/// </summary>
public sealed class OutlineViewOperations
{
    public OutlineViewOperations(
        Func<TextDocument> getDocument,
        Action<int, int> setHeadingLevel,
        Func<int, bool, int> moveHeading,
        Action<int> promoteToHeading1,
        Action<int> promote,
        Action<int> demote,
        Action<int> expand,
        Action<int> collapse,
        Func<int, bool> isHeadingCollapsed,
        Action<int>? navigateToBlock = null)
    {
        GetDocument = getDocument ?? throw new ArgumentNullException(nameof(getDocument));
        SetHeadingLevel = setHeadingLevel ?? throw new ArgumentNullException(nameof(setHeadingLevel));
        MoveHeading = moveHeading ?? throw new ArgumentNullException(nameof(moveHeading));
        PromoteToHeading1 = promoteToHeading1 ?? throw new ArgumentNullException(nameof(promoteToHeading1));
        Promote = promote ?? throw new ArgumentNullException(nameof(promote));
        Demote = demote ?? throw new ArgumentNullException(nameof(demote));
        Expand = expand ?? throw new ArgumentNullException(nameof(expand));
        Collapse = collapse ?? throw new ArgumentNullException(nameof(collapse));
        IsHeadingCollapsed = isHeadingCollapsed ?? throw new ArgumentNullException(nameof(isHeadingCollapsed));
        NavigateToBlock = navigateToBlock;
    }

    public Func<TextDocument> GetDocument { get; }
    public Action<int, int> SetHeadingLevel { get; }
    public Func<int, bool, int> MoveHeading { get; }
    public Action<int> PromoteToHeading1 { get; }
    public Action<int> Promote { get; }
    public Action<int> Demote { get; }
    public Action<int> Expand { get; }
    public Action<int> Collapse { get; }
    public Func<int, bool> IsHeadingCollapsed { get; }
    public Action<int>? NavigateToBlock { get; }
}

/// <summary>
/// Renderer-neutral outline session. It owns display state, document projection, selection/navigation
/// semantics, and command decisions; renderers translate native events and paint <see cref="ProjectedRows"/>.
/// </summary>
public sealed class OutlineViewController
{
    private readonly OutlineViewOperations _operations;
    private IReadOnlyList<OutlineRow> _visibleRows = [];
    private IReadOnlyList<OutlineProjectedRow> _projectedRows = [];

    public OutlineViewController(OutlineViewOperations operations)
    {
        _operations = operations ?? throw new ArgumentNullException(nameof(operations));
    }

    public int ShowLevel { get; private set; } = OutlineViewModel.ShowAllLevels;

    public bool FirstLineOnly { get; private set; }

    public IReadOnlyList<OutlineRow> VisibleRows => _visibleRows;

    public IReadOnlyList<OutlineProjectedRow> ProjectedRows => _projectedRows;

    public int? SelectedBlockIndex { get; private set; }

    public event Action? RowsChanged;

    public static string HeadingStyleIdForLevel(int level) => level switch
    {
        < 0 => "Normal",
        0 => "Title",
        _ => $"Heading{Math.Min(level, OutlineTools.MaxHeadingLevel)}",
    };

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
        _visibleRows = OutlineViewModel.Build(_operations.GetDocument(), ShowLevel, FirstLineOnly);
        if (SelectedBlockIndex is int selectedBlockIndex
            && !_visibleRows.Any(row => row.BlockIndex == selectedBlockIndex))
        {
            SelectedBlockIndex = null;
        }

        _projectedRows = _visibleRows
            .Select(row => new OutlineProjectedRow(row, _operations.IsHeadingCollapsed(row.BlockIndex)))
            .ToList();
        RowsChanged?.Invoke();
    }

    public bool Execute(OutlineCommand command)
    {
        if (SelectedBlockIndex is not int selectedBlockIndex)
            return false;

        switch (command)
        {
            case OutlineCommand.PromoteToHeading1:
                _operations.PromoteToHeading1(selectedBlockIndex);
                break;
            case OutlineCommand.Promote:
                _operations.Promote(selectedBlockIndex);
                break;
            case OutlineCommand.Demote:
                _operations.Demote(selectedBlockIndex);
                break;
            case OutlineCommand.MoveUp:
                SelectedBlockIndex = _operations.MoveHeading(selectedBlockIndex, true);
                break;
            case OutlineCommand.MoveDown:
                SelectedBlockIndex = _operations.MoveHeading(selectedBlockIndex, false);
                break;
            case OutlineCommand.Expand:
                _operations.Expand(selectedBlockIndex);
                break;
            case OutlineCommand.Collapse:
                _operations.Collapse(selectedBlockIndex);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(command), command, null);
        }

        Refresh();
        return true;
    }

    public bool SelectBlock(int blockIndex, bool navigate = false)
    {
        if (!_visibleRows.Any(row => row.BlockIndex == blockIndex))
            return false;

        SelectedBlockIndex = blockIndex;
        if (navigate)
            _operations.NavigateToBlock?.Invoke(blockIndex);
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

    public bool SetOutlineLevel(int level)
    {
        if (SelectedBlockIndex is not int selectedBlockIndex)
            return false;

        _operations.SetHeadingLevel(selectedBlockIndex, level);
        Refresh();
        return true;
    }
}
