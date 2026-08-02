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
    private readonly ComboBox? _ofPieTypeCombo;
    private readonly ComboBox? _ofPieSplitTypeCombo;
    private readonly TextBox? _ofPieSplitPositionBox;
    private readonly TextBox? _ofPieSizeBox;
    private readonly TextBox? _ofPieCustomPointsBox;
    private readonly TextBox? _ofPieGapWidthBox;
    private readonly CheckBox? _ofPieSeriesLinesCheck;

    internal ChartPieOptionsDialog(EditingSession editor)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        var chart = editor.SelectedChart
            ?? throw new InvalidOperationException("No chart is currently selected.");
        if (chart.ChartType is not (ChartType.Pie or ChartType.Doughnut or ChartType.OfPie))
            throw new InvalidOperationException("Select a pie, doughnut, or pie-of-pie chart before editing pie options.");

        _planner = ChartPieOptionsPlanner.FromChart(chart);
        var surface = ChartPieOptionsPlanner.BuildSurfacePlan();
        Title = surface.Title;
        Width = ChartPieOptionsPlanner.DefaultDialogWidth;
        Height = chart.ChartType == ChartType.OfPie ? ChartPieOptionsPlanner.DefaultDialogHeight : 250;
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

        if (chart.ChartType == ChartType.OfPie)
        {
            _ofPieTypeCombo = new ComboBox { ItemsSource = new[] { "Pie", "Bar" }, SelectedIndex = _planner.OfPieType == OfPieType.Bar ? 1 : 0, MinWidth = 150 };
            _ofPieSplitTypeCombo = new ComboBox { ItemsSource = new[] { "Auto", "Custom", "Percent", "Position", "Value" }, SelectedIndex = (int)_planner.OfPieSplitType, MinWidth = 150 };
            _ofPieSplitPositionBox = new TextBox { Text = (_planner.OfPieSplitPosition ?? 0).ToString(CultureInfo.CurrentCulture), MinWidth = 150 };
            _ofPieSizeBox = new TextBox { Text = _planner.OfPieSecondPieSizePercent.ToString(CultureInfo.CurrentCulture), MinWidth = 150 };
            _ofPieCustomPointsBox = new TextBox { Text = string.Join(",", _planner.OfPieCustomPointIndices), MinWidth = 150 };
            _ofPieGapWidthBox = new TextBox { Text = _planner.OfPieGapWidthPercent?.ToString(CultureInfo.CurrentCulture) ?? string.Empty, MinWidth = 150 };
            _ofPieSeriesLinesCheck = new CheckBox { IsChecked = _planner.OfPieSeriesLines, MinWidth = 150 };
        }

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

        var content = new StackPanel { Margin = new Thickness(14), Spacing = 8 };
        content.Children.Add(MakeRow(surface.FirstSliceAngleLabel, _angleBox));
        content.Children.Add(MakeRow(surface.DoughnutHoleLabel, _holeBox));
        if (chart.ChartType == ChartType.OfPie)
        {
            content.Children.Add(MakeRow(surface.OfPieTypeLabel, _ofPieTypeCombo!));
            content.Children.Add(MakeRow(surface.OfPieSplitTypeLabel, _ofPieSplitTypeCombo!));
            content.Children.Add(MakeRow(surface.OfPieSplitPositionLabel, _ofPieSplitPositionBox!));
            content.Children.Add(MakeRow(surface.OfPieSecondPieSizeLabel, _ofPieSizeBox!));
            content.Children.Add(MakeRow(surface.OfPieCustomPointIndicesLabel, _ofPieCustomPointsBox!));
            content.Children.Add(MakeRow(surface.OfPieGapWidthLabel, _ofPieGapWidthBox!));
            content.Children.Add(MakeRow(surface.OfPieSeriesLinesLabel, _ofPieSeriesLinesCheck!));
        }
        content.Children.Add(new TextBlock { Text = surface.Hint, Opacity = 0.7, TextWrapping = global::Avalonia.Media.TextWrapping.Wrap });
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

    internal void SetOfPieOptionsForTests(OfPieType type, OfPieSplitType splitType, double? splitPosition, int secondPieSizePercent, string customPointIndices, int? gapWidthPercent = null, bool seriesLines = false)
    {
        if (_ofPieTypeCombo is null)
            throw new InvalidOperationException("The selected chart is not an OfPie chart.");
        _ofPieTypeCombo.SelectedIndex = type == OfPieType.Bar ? 1 : 0;
        _ofPieSplitTypeCombo!.SelectedIndex = (int)splitType;
        _ofPieSplitPositionBox!.Text = (splitPosition ?? 0).ToString(CultureInfo.CurrentCulture);
        _ofPieSizeBox!.Text = secondPieSizePercent.ToString(CultureInfo.CurrentCulture);
        _ofPieCustomPointsBox!.Text = customPointIndices;
        _ofPieGapWidthBox!.Text = gapWidthPercent?.ToString(CultureInfo.CurrentCulture) ?? string.Empty;
        _ofPieSeriesLinesCheck!.IsChecked = seriesLines;
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
        if (_ofPieTypeCombo is null)
            return;

        _planner.SetOfPieType(_ofPieTypeCombo.SelectedIndex == 1 ? OfPieType.Bar : OfPieType.Pie);
        _planner.SetOfPieSplitType((OfPieSplitType)Math.Clamp(_ofPieSplitTypeCombo!.SelectedIndex, 0, 4));
        _planner.SetOfPieSplitPosition(ParseOptionalDouble(_ofPieSplitPositionBox!.Text));
        _planner.SetOfPieSecondPieSizePercent(ParseOfPieSize(_ofPieSizeBox!.Text));
        _planner.SetOfPieCustomPointIndices(ParsePointIndices(_ofPieCustomPointsBox!.Text));
        _planner.SetOfPieGapWidthPercent(ParseOptionalInt(_ofPieGapWidthBox!.Text, 0, 500, "Secondary plot gap width"));
        _planner.SetOfPieSeriesLines(_ofPieSeriesLinesCheck!.IsChecked == true);
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

    private static double? ParseOptionalDouble(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out var value) && value >= 0)
            return value;
        throw new FormatException("OfPie split position must be a non-negative number or blank.");
    }

    private static int ParseOfPieSize(string? text)
    {
        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.CurrentCulture, out var value) && value is >= 5 and <= 200)
            return value;
        throw new FormatException("Secondary plot size must be a whole number from 5 to 200.");
    }

    private static int? ParseOptionalInt(string? text, int min, int max, string label)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.CurrentCulture, out var value) && value >= min && value <= max)
            return value;
        throw new FormatException($"{label} must be a whole number from {min} to {max}, or blank.");
    }

    private static IReadOnlyList<int> ParsePointIndices(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return Array.Empty<int>();
        var values = new List<int>();
        foreach (var token in text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!int.TryParse(token, NumberStyles.Integer, CultureInfo.CurrentCulture, out var value) || value < 0)
                throw new FormatException("Custom secondary points must be non-negative whole numbers separated by commas.");
            values.Add(value);
        }
        return values;
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
