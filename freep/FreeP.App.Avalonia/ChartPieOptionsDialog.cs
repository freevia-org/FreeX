using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Free.Shared.Shell.Avalonia;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Avalonia;

internal sealed class ChartPieOptionsDialog : Window
{
    private static readonly AvaloniaCompactDialogChromeStyle DialogChromeStyle = new(FontFamily.Default);
    private readonly EditingSession _editor;
    private readonly ChartPieOptionsPlanner _planner;
    private readonly TextBox _angleBox;
    private readonly TextBox _holeBox;

    internal ChartPieOptionsDialog(EditingSession editor)
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
        MinWidth = 380;
        MinHeight = 220;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(0xF3, 0xF3, 0xF3));

        _angleBox = new TextBox { Text = (_planner.FirstSliceAngleDegrees ?? 0).ToString(CultureInfo.CurrentCulture), MinWidth = 150 };
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
                MakeRow(surface.FirstSliceAngleLabel, _angleBox),
                MakeRow(surface.DoughnutHoleLabel, _holeBox),
                new TextBlock { Text = surface.Hint, Opacity = 0.7, TextWrapping = global::Avalonia.Media.TextWrapping.Wrap },
                buttons,
            },
        };
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
            Close(true);
        }
        catch (FormatException)
        {
            // Keep the dialog open so the user can correct the input.
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

    private static Control MakeRow(string label, Control control)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("220, *") };
        row.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        Grid.SetColumn(control, 1);
        row.Children.Add(control);
        return row;
    }

    private static Button MakeButton(string label, bool isDefault, Action action)
    {
        var button = new Button { Content = label, IsDefault = isDefault, MinWidth = 80 };
        AvaloniaCompactDialogChrome.ApplyButton(button, DialogChromeStyle, minWidth: 80, isDefault: isDefault);
        button.Click += (_, _) => action();
        return button;
    }
}
