using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Immutable;

using Free.Shared.Shell.Avalonia;
using Free.Shared.Ribbon.Avalonia;
using FreeX.App.Presentation.Filtering;
using FreeX.Core.Commands;
using FreeX.Core.Model;

using AvaloniaGrid = Avalonia.Controls.Grid;
using AvaloniaHorizontalAlignment = Avalonia.Layout.HorizontalAlignment;
using AvaloniaPath = Avalonia.Controls.Shapes.Path;
using AvaloniaVerticalAlignment = Avalonia.Layout.VerticalAlignment;

namespace FreeX.App.Avalonia;

public sealed partial class MainWindow
{
    internal bool AutoFilterFlyoutOpenForTest => _autoFilterFlyout is not null;

    internal void RunAutoFilterForTest(
        GridRange range,
        uint columnOffset,
        IReadOnlyList<string> allowedValues) =>
        RunAutoFilter(range, columnOffset, allowedValues);

}
