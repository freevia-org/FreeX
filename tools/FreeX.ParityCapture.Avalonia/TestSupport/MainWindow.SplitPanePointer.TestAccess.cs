using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using FreeX.App.Presentation.GridInteraction;
using FreeX.Core.Commands;
using FreeX.Core.Model;
using AvaloniaGrid = Avalonia.Controls.Grid;
using AvaloniaHorizontalAlignment = Avalonia.Layout.HorizontalAlignment;
using AvaloniaVerticalAlignment = Avalonia.Layout.VerticalAlignment;

namespace FreeX.App.Avalonia;

public sealed partial class MainWindow
{
    /// <summary>Test-only entry point for the production divider command route.</summary>
    internal void RaiseSplitPaneDividerDragForTest(GridPoint pointer, SplitPanePointerHandle handle)
    {
        if (!TryGetSplitPanePointerLayout(out var layout))
            return;

        if (SplitPanePointerPlanner.CalculateDividerDragTarget(
                layout.Viewport,
                handle,
                pointer,
                layout.RowHeaderWidth,
                layout.ColumnHeaderHeight,
                layout.MetricScale) is { } target)
        {
            ApplySplitPaneDividerTarget(target);
        }
    }

    /// <summary>Test-only access to the production active-pane wheel route.</summary>
    internal void RaiseSplitPaneWheelForTest(Point position, Vector delta, KeyModifiers modifiers = KeyModifiers.None)
    {
        var pointer = new Pointer(1, PointerType.Mouse, true);
        var args = new PointerWheelEventArgs(
            this,
            pointer,
            _sheetGridHost,
            position,
            0,
            new PointerPointProperties(),
            modifiers,
            delta);
        SheetScrollViewer_PointerWheelChanged(_sheetGridHost, args);
    }

}
