using FreeX.App.Presentation.PivotUI;
using FreeX.Core.Model;

using System.Diagnostics;

namespace FreeX.App.Avalonia;

public sealed partial class MainWindow
{
    /// <summary>Test seam for the real WPF-parity double-click precedence route.</summary>
    internal bool TryShowPivotTableDetailsFromDoubleClickForTest() =>
        TryShowPivotTableDetailsFromDoubleClick();

}
