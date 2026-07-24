using System.Windows;
using System.Windows.Controls;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Host;

/// <summary>PowerPoint-style chart data-table options dialog.</summary>
public sealed class ChartDataTableOptionsDialog : Free.Shared.Ribbon.Wpf.DialogWindow
{
    private readonly EditingSession _editor;
    private readonly ChartDataTableOptionsPlanner _planner;
    private readonly CheckBox _showTableCheck;
    private readonly CheckBox _horizontalBorderCheck;
    private readonly CheckBox _verticalBorderCheck;
    private readonly CheckBox _outlineBorderCheck;
    private readonly CheckBox _legendKeysCheck;

    public ChartDataTableOptionsDialog(EditingSession editor)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        var chart = editor.SelectedChart
            ?? throw new InvalidOperationException("No chart is currently selected.");
        _planner = ChartDataTableOptionsPlanner.FromChart(chart);
        var surface = ChartDataTableOptionsPlanner.BuildSurfacePlan();

        Title = surface.Title;
        Width = ChartDataTableOptionsPlanner.DefaultDialogWidth;
        Height = ChartDataTableOptionsPlanner.DefaultDialogHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;

        _showTableCheck = new CheckBox { Content = surface.ShowDataTableLabel, IsChecked = _planner.ShowDataTable };
        _horizontalBorderCheck = new CheckBox { Content = surface.HorizontalBorderLabel, IsChecked = _planner.ShowHorizontalBorder };
        _verticalBorderCheck = new CheckBox { Content = surface.VerticalBorderLabel, IsChecked = _planner.ShowVerticalBorder };
        _outlineBorderCheck = new CheckBox { Content = surface.OutlineBorderLabel, IsChecked = _planner.ShowOutlineBorder };
        _legendKeysCheck = new CheckBox { Content = surface.LegendKeysLabel, IsChecked = _planner.ShowLegendKeys };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(8, 14, 8, 8),
        };
        var ok = new Button { Content = surface.OkLabel, IsDefault = true, MinWidth = 80, Margin = new Thickness(4) };
        var cancel = new Button { Content = surface.CancelLabel, IsCancel = true, MinWidth = 80, Margin = new Thickness(4) };
        ok.Click += (_, _) => OnOk();
        cancel.Click += (_, _) => Close();
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);

        var content = new StackPanel { Margin = new Thickness(14) };
        content.Children.Add(_showTableCheck);
        content.Children.Add(_horizontalBorderCheck);
        content.Children.Add(_verticalBorderCheck);
        content.Children.Add(_outlineBorderCheck);
        content.Children.Add(_legendKeysCheck);
        content.Children.Add(buttons);
        Content = content;
    }

    internal ChartDataTableOptions BuildCommitPlanForTests()
    {
        UpdatePlannerFromControls();
        return _planner.BuildCommitPlan();
    }

    private void OnOk()
    {
        _editor.ApplyChartDataTableOptions(BuildCommitPlanForTests());
        DialogResult = true;
    }

    private void UpdatePlannerFromControls()
    {
        _planner.SetShowDataTable(_showTableCheck.IsChecked == true);
        _planner.SetShowHorizontalBorder(_horizontalBorderCheck.IsChecked == true);
        _planner.SetShowVerticalBorder(_verticalBorderCheck.IsChecked == true);
        _planner.SetShowOutlineBorder(_outlineBorderCheck.IsChecked == true);
        _planner.SetShowLegendKeys(_legendKeysCheck.IsChecked == true);
    }
}
