using FreeX.App.Presentation.DrawingUI;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia;

public sealed partial class MainWindow
{
    // Test seams drive the same copy/paste entry points used by Ctrl+C/Ctrl+V without depending on
    // a platform clipboard implementation, which is unavailable in Avalonia headless tests.
    internal void SelectDrawingObjectForTest(
        SelectionPaneObjectKind kind,
        Guid objectId,
        CellAddress anchor)
    {
        _session.SelectCell(anchor);
        _selectedDrawingObjectKind = kind;
        _selectedDrawingObjectId = objectId;
        _ribbonContextSource.OnDrawingObjectSelected(kind);
        RefreshTableContextualTab();
        RefreshPivotContextualTab();
    }

    internal void SelectCellForTest(CellAddress address) => SelectCell(address);

}
