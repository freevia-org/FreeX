using Avalonia.Input;
using FreeX.App.Presentation;
using FreeX.App.Presentation.FormulaBar;
using FreeX.App.Presentation.Shell;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia;

public sealed partial class MainWindow
{
    internal bool RouteFormulaPointSelectionForTest(
        GridRange range,
        bool append = false,
        bool extendSelection = false) =>
        TryRouteFormulaPointModeSelection(range, append, extendSelection);

}
