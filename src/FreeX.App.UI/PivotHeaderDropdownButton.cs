using FreeX.Core.Model;

namespace FreeX.App.UI;

public readonly record struct PivotHeaderDropdownButton(
    CellAddress HeaderCell,
    bool IsActive);
