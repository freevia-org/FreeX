using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Host;

/// <summary>PowerPoint-style first-slice and doughnut-hole options dialog.</summary>
public sealed class ChartPieOptionsDialog : Free.Shared.Ribbon.Wpf.DialogWindow
{
    private readonly EditingSession _editor;
    private readonly ChartPieOptionsPlanner _planner;
    private readonly TextBox _angleBox;
    private readonly TextBox _holeBox;

    public ChartPieOptionsDialog(EditingSession editor)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        var chart = editor.SelectedChart
            ?? throw new InvalidOperationException("No chart is currently selected.");
        if (chart.ChartType is not (ChartType.Pie or ChartType.Doughnut))
            throw new InvalidOperationException("Select a pie or doughnut chart before editing pie options.");

        _planner = ChartPieOptionsPlanner.FromChart(chart);
        var surface = ChartPieOptionsPlanner.BuildSurfacePlan();
        Title = surface.Title;
        Width = ChartPieOptionsPlanner.DefaultDialogWidth;
        Height = ChartPieOptionsPlanner.DefaultDialogHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;

        _angleBox = new TextBox
        {
            Text = (_planner.FirstSliceAngleDegrees ?? 0).ToString(CultureInfo.CurrentCulture),
            MinWidth = 150,
        };
        _holeBox = new TextBox
        {
            Text = _planner.DoughnutHolePercent.ToString(CultureInfo.CurrentCulture),
            MinWidth = 150,
            IsEnabled = chart.ChartType == ChartType.Doughnut,
        };

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
        content.Children.Add(MakeRow(surface.FirstSliceAngleLabel, _angleBox));
        content.Children.Add(MakeRow(surface.DoughnutHoleLabel, _holeBox));
        content.Children.Add(new TextBlock { Text = surface.Hint, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8), Opacity = 0.7 });
        content.Children.Add(buttons);
        Content = content;
    }

    internal ChartPieOptions BuildCommitPlanForTests()
    {
        UpdatePlannerFromControls();
        return _planner.BuildCommitPlan();
    }

    internal void SetOptionsForTests(int? firstSliceAngleDegrees, int doughnutHolePercent)
    {
        _angleBox.Text = (firstSliceAngleDegrees ?? 0).ToString(CultureInfo.CurrentCulture);
        _holeBox.Text = doughnutHolePercent.ToString(CultureInfo.CurrentCulture);
    }

    private void OnOk()
    {
        try
        {
            _editor.ApplyChartPieOptions(BuildCommitPlanForTests());
            DialogResult = true;
        }
        catch (FormatException ex)
        {
            MessageBox.Show(this, ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void UpdatePlannerFromControls()
    {
        _planner.SetFirstSliceAngleDegrees(ParseAngle(_angleBox.Text));
        _planner.SetDoughnutHolePercent(ParseHole(_holeBox.Text));
    }

    private static int ParseAngle(string? text)
    {
        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.CurrentCulture, out var value) && value is >= 0 and <= 359)
            return value;
        throw new FormatException("First slice angle must be a whole number from 0 to 359.");
    }

    private static int ParseHole(string? text)
    {
        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.CurrentCulture, out var value) && value is >= 10 and <= 90)
            return value;
        throw new FormatException("Doughnut hole must be a whole number from 10 to 90.");
    }

    private static StackPanel MakeRow(string label, Control control)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        row.Children.Add(new Label { Content = label, Width = 220, VerticalContentAlignment = VerticalAlignment.Center });
        row.Children.Add(control);
        return row;
    }
}
