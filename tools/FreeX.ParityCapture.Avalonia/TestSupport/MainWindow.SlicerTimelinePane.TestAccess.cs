using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;

using FreeX.App.Presentation.PivotUI;
using FreeX.App.Presentation.SlicerTimeline;
using FreeX.Core.Model;

using AvaloniaGrid = Avalonia.Controls.Grid;
using AvaloniaHorizontalAlignment = Avalonia.Layout.HorizontalAlignment;
using AvaloniaVerticalAlignment = Avalonia.Layout.VerticalAlignment;

namespace FreeX.App.Avalonia;

public sealed partial class MainWindow
{
    internal Border SlicerTimelinePaneHostForTest => _slicerTimelinePaneHost;

    internal bool SlicerTimelinePaneVisibleForTest => _slicerTimelinePaneHost.IsVisible;

    internal int SlicerTimelinePaneBuildCountForTest => _slicerTimelinePaneBuildCount;

    internal void RefreshSlicerTimelinePaneForTest() => RefreshSlicerTimelinePane();

}
