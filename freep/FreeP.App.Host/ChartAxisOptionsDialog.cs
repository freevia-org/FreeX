using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Host;

/// <summary>Small PowerPoint-style chart axis scale/display dialog.</summary>
public sealed class ChartAxisOptionsDialog : Free.Shared.Ribbon.Wpf.DialogWindow
{
    private readonly EditingSession _editor;
    private readonly ChartAxisOptionsPlanner _planner;
    private readonly ComboBox _axisCombo;
    private readonly TextBox _titleBox;
    private readonly TextBox _minimumBox;
    private readonly TextBox _maximumBox;
    private readonly TextBox _majorUnitBox;
    private readonly TextBox _minorUnitBox;
    private readonly TextBox _numberFormatBox;
    private readonly CheckBox _majorGridlinesCheck;
    private readonly ComboBox _majorTickMarkCombo;
    private readonly ComboBox _minorTickMarkCombo;
    private readonly ComboBox _tickLabelPositionCombo;
    private readonly ComboBox _crossesCombo;
    private readonly TextBox _crossesAtBox;

    public ChartAxisOptionsDialog(EditingSession editor)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        var chart = editor.SelectedChart
            ?? throw new InvalidOperationException("No chart is currently selected.");
        _planner = ChartAxisOptionsPlanner.FromChart(chart);
        var surface = ChartAxisOptionsPlanner.BuildSurfacePlan();

        Title = surface.Title;
        Width = ChartAxisOptionsPlanner.DefaultDialogWidth;
        Height = ChartAxisOptionsPlanner.DefaultDialogHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;

        _axisCombo = new ComboBox
        {
            ItemsSource = ChartAxisOptionsPlanner.AxisOptions,
            DisplayMemberPath = nameof(ChartAxisKindOption.Label),
            SelectedIndex = 1,
            MinWidth = 180,
        };
        _axisCombo.SelectionChanged += (_, _) =>
        {
            if (_axisCombo.SelectedItem is ChartAxisKindOption option)
            {
                _planner.SetAxis(option.Value);
                LoadControls();
            }
        };
        _titleBox = new TextBox { MinWidth = 240 };
        _minimumBox = new TextBox { MinWidth = 120 };
        _maximumBox = new TextBox { MinWidth = 120 };
        _majorUnitBox = new TextBox { MinWidth = 120 };
        _minorUnitBox = new TextBox { MinWidth = 120 };
        _numberFormatBox = new TextBox { MinWidth = 180 };
        _majorGridlinesCheck = new CheckBox { Content = surface.MajorGridlinesLabel };
        _majorTickMarkCombo = MakeChoiceCombo(ChartAxisOptionsPlanner.TickMarkOptions);
        _minorTickMarkCombo = MakeChoiceCombo(ChartAxisOptionsPlanner.TickMarkOptions);
        _tickLabelPositionCombo = MakeChoiceCombo(ChartAxisOptionsPlanner.TickLabelPositionOptions);
        _crossesCombo = MakeChoiceCombo(ChartAxisOptionsPlanner.CrossingOptions);
        _crossesAtBox = new TextBox { MinWidth = 120 };
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
        content.Children.Add(MakeRow(surface.AxisLabel, _axisCombo));
        content.Children.Add(MakeRow(surface.AxisTitleLabel, _titleBox));
        content.Children.Add(MakeRow(surface.MinimumLabel, _minimumBox));
        content.Children.Add(MakeRow(surface.MaximumLabel, _maximumBox));
        content.Children.Add(MakeRow(surface.MajorUnitLabel, _majorUnitBox));
        content.Children.Add(MakeRow(surface.MinorUnitLabel, _minorUnitBox));
        content.Children.Add(MakeRow(surface.NumberFormatLabel, _numberFormatBox));
        content.Children.Add(new TextBlock { Text = surface.AutoHint, Margin = new Thickness(150, -4, 0, 8), Opacity = 0.7 });
        content.Children.Add(_majorGridlinesCheck);
        content.Children.Add(MakeRow(surface.MajorTickMarkLabel, _majorTickMarkCombo));
        content.Children.Add(MakeRow(surface.MinorTickMarkLabel, _minorTickMarkCombo));
        content.Children.Add(MakeRow(surface.TickLabelPositionLabel, _tickLabelPositionCombo));
        content.Children.Add(MakeRow(surface.CrossingLabel, _crossesCombo));
        content.Children.Add(MakeRow(surface.CrossesAtLabel, _crossesAtBox));
        content.Children.Add(buttons);
        Content = content;
    }

    internal ChartAxisOptions BuildCommitPlanForTests()
    {
        UpdatePlannerFromControls();
        return _planner.BuildCommitPlan();
    }

    private void OnOk()
    {
        try
        {
            _editor.ApplyChartAxisOptions(BuildCommitPlanForTests());
            DialogResult = true;
        }
        catch (FormatException ex)
        {
            MessageBox.Show(this, ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void LoadControls()
    {
        _titleBox.Text = _planner.Title;
        _minimumBox.Text = Format(_planner.Minimum);
        _maximumBox.Text = Format(_planner.Maximum);
        _majorUnitBox.Text = Format(_planner.MajorUnit);
        _minorUnitBox.Text = Format(_planner.MinorUnit);
        _numberFormatBox.Text = _planner.NumberFormatCode;
        _majorGridlinesCheck.IsChecked = _planner.MajorGridlines;
        _majorTickMarkCombo.SelectedItem = ChartAxisOptionsPlanner.TickMarkOptions.FirstOrDefault(x => x.Value == _planner.MajorTickMark);
        _minorTickMarkCombo.SelectedItem = ChartAxisOptionsPlanner.TickMarkOptions.FirstOrDefault(x => x.Value == _planner.MinorTickMark);
        _tickLabelPositionCombo.SelectedItem = ChartAxisOptionsPlanner.TickLabelPositionOptions.FirstOrDefault(x => x.Value == _planner.TickLabelPosition);
        _crossesCombo.SelectedItem = ChartAxisOptionsPlanner.CrossingOptions.FirstOrDefault(x => x.Value == _planner.Crosses);
        _crossesAtBox.Text = Format(_planner.CrossesAt);
    }

    private void UpdatePlannerFromControls()
    {
        _planner.SetTitle(_titleBox.Text);
        _planner.SetMinimum(ParseOptional(_minimumBox.Text, "Minimum"));
        _planner.SetMaximum(ParseOptional(_maximumBox.Text, "Maximum"));
        _planner.SetMajorUnit(ParseOptional(_majorUnitBox.Text, "Major unit"));
        _planner.SetMinorUnit(ParseOptional(_minorUnitBox.Text, "Minor unit"));
        _planner.SetNumberFormatCode(_numberFormatBox.Text);
        _planner.SetMajorGridlines(_majorGridlinesCheck.IsChecked == true);
        _planner.SetMajorTickMark(((ChartTickMarkOption)_majorTickMarkCombo.SelectedItem).Value);
        _planner.SetMinorTickMark(((ChartTickMarkOption)_minorTickMarkCombo.SelectedItem).Value);
        _planner.SetTickLabelPosition(((ChartTickLabelPositionOption)_tickLabelPositionCombo.SelectedItem).Value);
        _planner.SetCrosses(((ChartAxisCrossingOption)_crossesCombo.SelectedItem).Value);
        _planner.SetCrossesAt(ParseOptional(_crossesAtBox.Text, "Crosses at"));
    }

    private static double? ParseOptional(string text, string label)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out var value) &&
            double.IsFinite(value))
            return value;
        throw new FormatException($"{label} must be a finite number or blank.");
    }

    private static string Format(double? value) =>
        value?.ToString("G", CultureInfo.CurrentCulture) ?? string.Empty;

    private static StackPanel MakeRow(string label, Control control)
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 8),
        };
        row.Children.Add(new Label { Content = label, Width = 150, VerticalContentAlignment = VerticalAlignment.Center });
        row.Children.Add(control);
        return row;
    }

    private static ComboBox MakeChoiceCombo<T>(IReadOnlyList<T> options) where T : class =>
        new()
        {
            ItemsSource = options,
            DisplayMemberPath = "Label",
            MinWidth = 150,
        };
}
