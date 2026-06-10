using System.Windows;
using System.Windows.Controls.Primitives;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public partial class MainWindow
{
    private void OnPivotHeaderDropdownRequested(CellAddress headerCell, Point position)
    {
        if (!_pivotHeaderDropdownTargets.TryGetValue((headerCell.Row, headerCell.Col), out var target))
            return;

        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet is null)
            return;

        var pivotTable = FindPivotTableByName(sheet, target.PivotTableName);
        if (pivotTable is null)
            return;

        _pivotFieldMenuContextCaption = target.FieldCaption;
        SetActiveCell(headerCell);
        RefreshPivotFieldListPane();

        var menu = CreatePivotFieldContextMenu();
        menu.Closed += (_, _) => _pivotFieldMenuContextCaption = null;
        menu.PlacementTarget = SheetGrid;
        menu.Placement = PlacementMode.RelativePoint;
        menu.HorizontalOffset = position.X;
        menu.VerticalOffset = position.Y;
        menu.IsOpen = true;
    }
}
