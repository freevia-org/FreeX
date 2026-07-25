using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Host;

/// <summary>Small PowerPoint-style per-series chart formatting dialog.</summary>
public sealed class ChartSeriesOptionsDialog : Free.Shared.Ribbon.Wpf.DialogWindow
{
    private readonly EditingSession _editor;
    private readonly ChartSeriesOptionsPlanner _planner;
    private readonly ComboBox _seriesCombo;
    private readonly CheckBox _smoothLineCheck;
    private readonly CheckBox _secondaryAxisCheck;
    private readonly TextBox _lineWidthBox;
    private readonly TextBox _lineColorBox;
    private readonly ComboBox _lineDashCombo;
    private readonly TextBox _fillColorBox;
    private readonly ComboBox _markerCombo;
    private readonly TextBox _markerSizeBox;

    public ChartSeriesOptionsDialog(EditingSession editor)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        var chart = editor.SelectedChart
            ?? throw new InvalidOperationException("No chart is currently selected.");
        _planner = ChartSeriesOptionsPlanner.FromChart(chart);
        var surface = ChartSeriesOptionsPlanner.BuildSurfacePlan();

        Title = surface.Title;
        Width = ChartSeriesOptionsPlanner.DefaultDialogWidth;
        Height = ChartSeriesOptionsPlanner.DefaultDialogHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;

        _seriesCombo = new ComboBox
        {
            ItemsSource = _planner.SeriesOptions,
            DisplayMemberPath = nameof(ChartSeriesOption.Label),
            SelectedIndex = _planner.SeriesIndex,
            MinWidth = 200,
        };
        _seriesCombo.SelectionChanged += (_, _) =>
        {
            if (_seriesCombo.SelectedItem is ChartSeriesOption option)
            {
                _planner.SetSeriesIndex(option.Index);
                LoadControls();
            }
        };
        _smoothLineCheck = new CheckBox { Content = surface.SmoothLineLabel };
        _secondaryAxisCheck = new CheckBox { Content = surface.SecondaryAxisLabel };
        _lineWidthBox = new TextBox { MinWidth = 120 };
        _lineColorBox = new TextBox { MinWidth = 140 };
        _lineDashCombo = new ComboBox
        {
            ItemsSource = ChartSeriesOptionsPlanner.DashOptions,
            DisplayMemberPath = nameof(ChartDashOption.Label),
            MinWidth = 150,
        };
        _fillColorBox = new TextBox { MinWidth = 140 };
        _markerCombo = new ComboBox
        {
            ItemsSource = ChartSeriesOptionsPlanner.MarkerOptions,
            DisplayMemberPath = nameof(ChartMarkerSymbolOption.Label),
            MinWidth = 150,
        };
        _markerSizeBox = new TextBox { MinWidth = 120 };
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
        content.Children.Add(_smoothLineCheck);
        content.Children.Add(_secondaryAxisCheck);
        content.Children.Add(MakeRow(surface.LineWidthLabel, _lineWidthBox));
        content.Children.Add(MakeRow(surface.LineColorLabel, _lineColorBox));
        content.Children.Add(MakeRow(surface.LineDashLabel, _lineDashCombo));
        content.Children.Add(MakeRow(surface.FillColorLabel, _fillColorBox));
        content.Children.Add(MakeRow(surface.MarkerLabel, _markerCombo));
        content.Children.Add(MakeRow(surface.MarkerSizeLabel, _markerSizeBox));
        content.Children.Add(new TextBlock { Text = surface.AutoHint, Margin = new Thickness(0, 0, 0, 8), Opacity = 0.7 });
        content.Children.Add(buttons);
        Content = content;
    }

    internal ChartSeriesOptions BuildCommitPlanForTests()
    {
        UpdatePlannerFromControls();
        return _planner.BuildCommitPlan();
    }

    internal void SetOptionsForTests(
        int seriesIndex,
        bool smoothLine,
        bool onSecondaryAxis,
        double? lineWidthPt,
        ChartMarkerSymbol markerSymbol,
        double? markerSizePt,
        string? fillColor = null,
        string? lineColor = null,
        OutlineDash lineDash = OutlineDash.Solid)
    {
        _seriesCombo.SelectedIndex = seriesIndex;
        _smoothLineCheck.IsChecked = smoothLine;
        _secondaryAxisCheck.IsChecked = onSecondaryAxis;
        _lineWidthBox.Text = Format(lineWidthPt);
        _lineColorBox.Text = lineColor ?? string.Empty;
        _lineDashCombo.SelectedIndex = FindDashIndex(lineDash);
        _markerCombo.SelectedIndex = FindMarkerIndex(markerSymbol);
        _markerSizeBox.Text = Format(markerSizePt);
        _fillColorBox.Text = fillColor ?? string.Empty;
    }

    private void OnOk()
    {
        try
        {
            _editor.ApplyChartSeriesOptions(BuildCommitPlanForTests());
            DialogResult = true;
        }
        catch (FormatException ex)
        {
            MessageBox.Show(this, ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void LoadControls()
    {
        _smoothLineCheck.IsChecked = _planner.SmoothLine;
        _secondaryAxisCheck.IsChecked = _planner.OnSecondaryAxis;
        _lineWidthBox.Text = Format(_planner.LineWidthPt);
        _lineColorBox.Text = _planner.LineColorText;
        _lineDashCombo.SelectedIndex = FindDashIndex(_planner.LineDash);
        _fillColorBox.Text = _planner.FillColorText;
        _markerCombo.SelectedIndex = FindMarkerIndex(_planner.MarkerSymbol);
        _markerSizeBox.Text = Format(_planner.MarkerSizePt);
    }

    private void UpdatePlannerFromControls()
    {
        _planner.SetSmoothLine(_smoothLineCheck.IsChecked == true);
        _planner.SetOnSecondaryAxis(_secondaryAxisCheck.IsChecked == true);
        _planner.SetLineWidth(ParseOptional(_lineWidthBox.Text, "Line width"));
        _planner.SetLineColor(_lineColorBox.Text);
        if (_lineDashCombo.SelectedItem is ChartDashOption dash)
            _planner.SetLineDash(dash.Value);
        _planner.SetFillColor(_fillColorBox.Text);
        if (_markerCombo.SelectedItem is ChartMarkerSymbolOption marker)
            _planner.SetMarkerSymbol(marker.Value);
        _planner.SetMarkerSize(ParseOptional(_markerSizeBox.Text, "Marker size"));
    }

    private static double? ParseOptional(string? text, string label)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out var value) &&
            double.IsFinite(value) && value >= 0)
            return value;
        throw new FormatException($"{label} must be a non-negative finite number or blank.");
    }

    private static string Format(double? value) =>
        value?.ToString("G", CultureInfo.CurrentCulture) ?? string.Empty;

    private static int FindMarkerIndex(ChartMarkerSymbol symbol) =>
        Math.Max(0, ChartSeriesOptionsPlanner.MarkerOptions
            .Select((option, index) => (option, index))
            .FirstOrDefault(item => item.option.Value == symbol).index);

    private static int FindDashIndex(OutlineDash dash) =>
        Math.Max(0, ChartSeriesOptionsPlanner.DashOptions
            .Select((option, index) => (option, index))
            .FirstOrDefault(item => item.option.Value == dash).index);

    private static StackPanel MakeRow(string label, Control control)
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 8),
        };
        row.Children.Add(new Label { Content = label, Width = 160, VerticalContentAlignment = VerticalAlignment.Center });
        row.Children.Add(control);
        return row;
    }
}
