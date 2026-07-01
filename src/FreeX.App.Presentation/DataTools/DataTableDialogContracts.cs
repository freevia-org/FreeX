using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Presentation;

public enum DataTableMode
{
    OneVariable,
    TwoVariable
}

public sealed record DataTableDialogResult(
    DataTableMode Mode,
    DataTableInputOrientation Orientation,
    CellAddress FormulaCell,
    CellAddress? RowInputCell,
    CellAddress? ColumnInputCell);

public enum DataTableRangeSelectionTarget
{
    RowInputCell,
    ColumnInputCell
}

public sealed record DataTableRangeSelectionRequest(
    DataTableRangeSelectionTarget Target,
    string CurrentText,
    bool CollapseDialog = true);
