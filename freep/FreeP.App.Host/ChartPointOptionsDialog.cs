using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Host;

/// <summary>PowerPoint-style per-point chart formatting dialog.</summary>
public sealed class ChartPointOptionsDialog : Free.Shared.Ribbon.Wpf.DialogWindow
{
    private readonly EditingSession _editor;
    private readonly ChartPointOptionsPlanner _planner;
    private readonly ComboBox _seriesCombo;
    private readonly ComboBox _pointCombo;
    private readonly TextBox _fillColorBox;
    private readonly TextBox _strokeColorBox;
    private readonly TextBox _strokeWidthBox;
    private readonly ComboBox _markerCombo;
    private readonly TextBox _markerSizeBox;

    public ChartPointOptionsDialog(EditingSession editor)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        var chart = editor.SelectedChart
            ?? throw new InvalidOperationException("No chart is currently selected.");
        _planner = ChartPointOptionsPlanner.FromChart(chart);
        var surface = ChartPointOptionsPlanner.BuildSurfacePlan();

        Title = surface.Title;
        Width = ChartPointOptionsPlanner.DefaultDialogWidth;
        Height = ChartPointOptionsPlanner.DefaultDialogHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;

        _seriesCombo = new ComboBox
        {
            ItemsSource = _planner.SeriesOptions,
            DisplayMemberPath = nameof(ChartSeriesOption.Label),
            SelectedIndex = _planner.SeriesIndex,
            MinWidth = 220,
        };
        _seriesCombo.SelectionChanged += (_, _) =>
        {
            if (_seriesCombo.SelectedItem is ChartSeriesOption option)
            {
                _planner.SetSeriesIndex(option.Index);
                RefreshPoints();
                LoadControls();
            }
        };

        _pointCombo = new ComboBox { DisplayMemberPath = nameof(ChartPointOption.Label), MinWidth = 220 };
        _pointCombo.SelectionChanged += (_, _) =>
        {
            if (_pointCombo.SelectedItem is ChartPointOption option)
            {
                _planner.SetPointIndex(option.Index);
                LoadControls();
            }
        };
        _fillColorBox = new TextBox { MinWidth = 140 };
        _strokeColorBox = new TextBox { MinWidth = 140 };
        _strokeWidthBox = new TextBox { MinWidth = 120 };
        _markerCombo = new ComboBox
        {
            ItemsSource = ChartPointOptionsPlanner.MarkerOptions,
            DisplayMemberPath = nameof(ChartMarkerSymbolOption.Label),
            MinWidth = 160,
        };
        _markerSizeBox = new TextBox { MinWidth = 120 };
        RefreshPoints();
        LoadControls();

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
        content.Children.Add(MakeRow(surface.SeriesLabel, _seriesCombo));
        content.Children.Add(MakeRow(surface.PointLabel, _pointCombo));
        content.Children.Add(MakeRow(surface.FillColorLabel, _fillColorBox));
        content.Children.Add(MakeRow(surface.StrokeColorLabel, _strokeColorBox));
        content.Children.Add(MakeRow(surface.StrokeWidthLabel, _strokeWidthBox));
        content.Children.Add(MakeRow(surface.MarkerLabel, _markerCombo));
        content.Children.Add(MakeRow(surface.MarkerSizeLabel, _markerSizeBox));
        content.Children.Add(new TextBlock { Text = surface.AutoHint, Margin = new Thickness(0, 0, 0, 8), Opacity = 0.7 });
        content.Children.Add(buttons);
        Content = content;
    }

    internal ChartPointOptions BuildCommitPlanForTests()
    {
        UpdatePlannerFromControls();
        return _planner.BuildCommitPlan();
    }

    internal void SetOptionsForTests(
        int seriesIndex,
        int pointIndex,
        string? fillColor,
        string? strokeColor,
        double? strokeWidthPt,
        ChartMarkerSymbol? markerSymbol,
        double? markerSizePt)
    {
        _seriesCombo.SelectedIndex = seriesIndex;
        RefreshPoints();
        _pointCombo.SelectedIndex = pointIndex;
        _fillColorBox.Text = fillColor ?? string.Empty;
        _strokeColorBox.Text = strokeColor ?? string.Empty;
        _strokeWidthBox.Text = Format(strokeWidthPt);
        _markerCombo.SelectedIndex = FindMarkerIndex(markerSymbol);
        _markerSizeBox.Text = Format(markerSizePt);
    }

    private void OnOk()
    {
        try
        {
            _editor.ApplyChartPointOptions(BuildCommitPlanForTests());
            DialogResult = true;
        }
        catch (FormatException ex)
        {
            MessageBox.Show(this, ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void RefreshPoints()
    {
        _pointCombo.ItemsSource = _planner.PointOptions;
        _pointCombo.SelectedIndex = Math.Min(_planner.PointIndex, Math.Max(0, _planner.PointOptions.Count - 1));
    }

    private void LoadControls()
    {
        _fillColorBox.Text = _planner.FillColorText;
        _strokeColorBox.Text = _planner.StrokeColorText;
        _strokeWidthBox.Text = Format(_planner.StrokeWidthPt);
        _markerCombo.SelectedIndex = FindMarkerIndex(_planner.MarkerSymbol);
        _markerSizeBox.Text = Format(_planner.MarkerSizePt);
    }

    private void UpdatePlannerFromControls()
    {
        _planner.SetFillColor(_fillColorBox.Text);
        _planner.SetStrokeColor(_strokeColorBox.Text);
        _planner.SetStrokeWidth(ParseOptional(_strokeWidthBox.Text, "Outline width"));
        var marker = _markerCombo.SelectedItem as ChartMarkerSymbolOption;
        _planner.SetMarkerSymbol(marker is null || marker.Value == ChartMarkerSymbol.Auto ? null : marker.Value);
        _planner.SetMarkerSize(ParseOptional(_markerSizeBox.Text, "Marker size"));
    }

    private static double? ParseOptional(string? text, string label)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out var value) &&
            double.IsFinite(value) && value >= 0)
            return value;
        throw new FormatException($"{label} must be a non-negative finite number or blank.");
    }

    private static string Format(double? value) =>
        value?.ToString("G", CultureInfo.CurrentCulture) ?? string.Empty;

    private static int FindMarkerIndex(ChartMarkerSymbol? symbol)
    {
        var value = symbol ?? ChartMarkerSymbol.Auto;
        return Math.Max(0, ChartPointOptionsPlanner.MarkerOptions
            .Select((option, index) => (option, index))
            .FirstOrDefault(item => item.option.Value == value).index);
    }

    private static StackPanel MakeRow(string label, Control control)
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 8),
        };
        row.Children.Add(new Label { Content = label, Width = 180, VerticalContentAlignment = VerticalAlignment.Center });
        row.Children.Add(control);
        return row;
    }
}
