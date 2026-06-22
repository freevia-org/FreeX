using FreeX.Core.Model;

namespace FreeX.App.UI;

public readonly record struct PivotRowLabelAdornment(
    CellAddress Cell,
    int IndentLevel,
    bool ShowExpandCollapseButton,
    bool IsExpanded,
    bool ReserveTextPadding = true);
