using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Free.Shared.Shell.Avalonia;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Avalonia;

internal sealed class ChartDataTableOptionsDialog : Window
{
    private static readonly AvaloniaCompactDialogChromeStyle DialogChromeStyle = new(FontFamily.Default);
    private readonly EditingSession _editor;
    private readonly ChartDataTableOptionsPlanner _planner;
    private readonly CheckBox _showTableCheck;
    private readonly CheckBox _horizontalBorderCheck;
    private readonly CheckBox _verticalBorderCheck;
    private readonly CheckBox _outlineBorderCheck;
    private readonly CheckBox _legendKeysCheck;

    internal ChartDataTableOptionsDialog(EditingSession editor)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        var chart = editor.SelectedChart
            ?? throw new InvalidOperationException("No chart is currently selected.");
        _planner = ChartDataTableOptionsPlanner.FromChart(chart);
        var surface = ChartDataTableOptionsPlanner.BuildSurfacePlan();

        Title = surface.Title;
        Width = ChartDataTableOptionsPlanner.DefaultDialogWidth;
        Height = ChartDataTableOptionsPlanner.DefaultDialogHeight;
        MinWidth = 340;
        MinHeight = 260;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(0xF3, 0xF3, 0xF3));

        _showTableCheck = new CheckBox { Content = surface.ShowDataTableLabel, IsChecked = _planner.ShowDataTable };
        _horizontalBorderCheck = new CheckBox { Content = surface.HorizontalBorderLabel, IsChecked = _planner.ShowHorizontalBorder };
        _verticalBorderCheck = new CheckBox { Content = surface.VerticalBorderLabel, IsChecked = _planner.ShowVerticalBorder };
        _outlineBorderCheck = new CheckBox { Content = surface.OutlineBorderLabel, IsChecked = _planner.ShowOutlineBorder };
        _legendKeysCheck = new CheckBox { Content = surface.LegendKeysLabel, IsChecked = _planner.ShowLegendKeys };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Thickness(0, 12, 0, 0),
            Children =
            {
                MakeButton(surface.OkLabel, true, OnOk),
                MakeButton(surface.CancelLabel, false, () => Close(false)),
            },
        };

        Content = new StackPanel
        {
            Margin = new Thickness(14),
            Spacing = 8,
            Children =
            {
                _showTableCheck,
                _horizontalBorderCheck,
                _verticalBorderCheck,
                _outlineBorderCheck,
                _legendKeysCheck,
                buttons,
            },
        };
    }

    internal ChartDataTableOptions BuildCommitPlanForTests()
    {
        UpdatePlannerFromControls();
        return _planner.BuildCommitPlan();
    }

    internal void SetOptionsForTests(
        bool showDataTable,
        bool showHorizontalBorder,
        bool showVerticalBorder,
        bool showOutlineBorder,
        bool showLegendKeys)
    {
        _showTableCheck.IsChecked = showDataTable;
        _horizontalBorderCheck.IsChecked = showHorizontalBorder;
        _verticalBorderCheck.IsChecked = showVerticalBorder;
        _outlineBorderCheck.IsChecked = showOutlineBorder;
        _legendKeysCheck.IsChecked = showLegendKeys;
    }

    private void OnOk()
    {
        _editor.ApplyChartDataTableOptions(BuildCommitPlanForTests());
        Close(true);
    }

    private void UpdatePlannerFromControls()
    {
        _planner.SetShowDataTable(_showTableCheck.IsChecked == true);
        _planner.SetShowHorizontalBorder(_horizontalBorderCheck.IsChecked == true);
        _planner.SetShowVerticalBorder(_verticalBorderCheck.IsChecked == true);
        _planner.SetShowOutlineBorder(_outlineBorderCheck.IsChecked == true);
        _planner.SetShowLegendKeys(_legendKeysCheck.IsChecked == true);
    }

    private static Button MakeButton(string label, bool isDefault, Action action)
    {
        var button = new Button { Content = label, IsDefault = isDefault, MinWidth = 80 };
        AvaloniaCompactDialogChrome.ApplyButton(button, DialogChromeStyle, minWidth: 80, isDefault: isDefault);
        button.Click += (_, _) => action();
        return button;
    }
}
