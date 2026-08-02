using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Free.Shared.Shell.Avalonia;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Avalonia;

internal sealed class ChartLayoutOptionsDialog : Window
{
    private static readonly AvaloniaCompactDialogChromeStyle DialogChromeStyle = new(FontFamily.Default);
    private readonly EditingSession _editor;
    private readonly ChartLayoutOptionsPlanner _planner;
    private readonly ComboBox _targetCombo;
    private readonly ComboBox _layoutTargetCombo;
    private readonly ComboBox _xModeCombo;
    private readonly ComboBox _yModeCombo;
    private readonly ComboBox _widthModeCombo;
    private readonly ComboBox _heightModeCombo;
    private readonly TextBox _xBox;
    private readonly TextBox _yBox;
    private readonly TextBox _widthBox;
    private readonly TextBox _heightBox;

    internal ChartLayoutOptionsDialog(EditingSession editor)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        var chart = editor.SelectedChart ?? throw new InvalidOperationException("No chart is currently selected.");
        _planner = ChartLayoutOptionsPlanner.FromChart(chart);
        var surface = ChartLayoutOptionsPlanner.BuildSurfacePlan();

        Title = surface.Title;
        Width = ChartLayoutOptionsPlanner.DefaultDialogWidth;
        Height = ChartLayoutOptionsPlanner.DefaultDialogHeight;
        MinWidth = 450;
        MinHeight = 430;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(0xF3, 0xF3, 0xF3));

        _targetCombo = new ComboBox { ItemsSource = ChartLayoutOptionsPlanner.TargetOptions.Select(x => x.Label).ToArray(), SelectedIndex = 0, MinWidth = 190 };
        _targetCombo.SelectionChanged += (_, _) => { _planner.SetTarget(SelectedTarget()); LoadControls(); };
        _layoutTargetCombo = MakeLayoutTargetCombo();
        _xModeCombo = MakeModeCombo();
        _yModeCombo = MakeModeCombo();
        _widthModeCombo = MakeModeCombo();
        _heightModeCombo = MakeModeCombo();
        _xBox = new TextBox { MinWidth = 110 };
        _yBox = new TextBox { MinWidth = 110 };
        _widthBox = new TextBox { MinWidth = 110 };
        _heightBox = new TextBox { MinWidth = 110 };
        LoadControls();

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Thickness(0, 12, 0, 0),
            Children = { MakeButton(surface.OkLabel, true, OnOk), MakeButton(surface.CancelLabel, false, () => Close(false)) },
        };
        Content = new StackPanel
        {
            Margin = new Thickness(14), Spacing = 8,
            Children =
            {
                MakeRow(surface.TargetLabel, _targetCombo), MakeRow(surface.LayoutTargetLabel, _layoutTargetCombo),
                MakeRow(surface.XLabel, _xBox, _xModeCombo), MakeRow(surface.YLabel, _yBox, _yModeCombo),
                MakeRow(surface.WidthLabel, _widthBox, _widthModeCombo), MakeRow(surface.HeightLabel, _heightBox, _heightModeCombo),
                new TextBlock { Text = surface.Hint, TextWrapping = TextWrapping.Wrap, Opacity = 0.7 }, buttons,
            },
        };
    }

    internal ChartLayoutOptions BuildCommitPlanForTests()
    {
        UpdatePlannerFromControls();
        return _planner.BuildCommitPlan();
    }

    internal void SetOptionsForTests(ChartLayoutTarget target, string? layoutTarget, ChartManualLayoutMode xMode, ChartManualLayoutMode yMode, ChartManualLayoutMode widthMode, ChartManualLayoutMode heightMode, double? x, double? y, double? width, double? height)
    {
        _targetCombo.SelectedIndex = FindTargetIndex(target);
        SelectLayoutTarget(layoutTarget);
        _xModeCombo.SelectedIndex = FindModeIndex(xMode); _yModeCombo.SelectedIndex = FindModeIndex(yMode);
        _widthModeCombo.SelectedIndex = FindModeIndex(widthMode); _heightModeCombo.SelectedIndex = FindModeIndex(heightMode);
        _xBox.Text = Format(x); _yBox.Text = Format(y); _widthBox.Text = Format(width); _heightBox.Text = Format(height);
    }

    private void OnOk()
    {
        try { _editor.ApplyChartLayoutOptions(BuildCommitPlanForTests()); Close(true); }
        catch (FormatException) { Close(false); }
    }

    private void LoadControls()
    {
        SelectLayoutTarget(_planner.LayoutTarget);
        _xModeCombo.SelectedIndex = FindModeIndex(_planner.XMode); _yModeCombo.SelectedIndex = FindModeIndex(_planner.YMode);
        _widthModeCombo.SelectedIndex = FindModeIndex(_planner.WidthMode); _heightModeCombo.SelectedIndex = FindModeIndex(_planner.HeightMode);
        _xBox.Text = Format(_planner.X); _yBox.Text = Format(_planner.Y); _widthBox.Text = Format(_planner.Width); _heightBox.Text = Format(_planner.Height);
    }

    private void UpdatePlannerFromControls()
    {
        _planner.SetLayoutTarget(SelectedLayoutTarget());
        _planner.SetXMode(SelectedMode(_xModeCombo)); _planner.SetYMode(SelectedMode(_yModeCombo));
        _planner.SetWidthMode(SelectedMode(_widthModeCombo)); _planner.SetHeightMode(SelectedMode(_heightModeCombo));
        _planner.SetX(ParseOptional(_xBox.Text, "X")); _planner.SetY(ParseOptional(_yBox.Text, "Y"));
        _planner.SetWidth(ParseOptional(_widthBox.Text, "Width")); _planner.SetHeight(ParseOptional(_heightBox.Text, "Height"));
    }

    private ChartLayoutTarget SelectedTarget() => _targetCombo.SelectedIndex == 1 ? ChartLayoutTarget.Legend : ChartLayoutTarget.PlotArea;
    private string? SelectedLayoutTarget()
    {
        var options = ChartLayoutOptionsPlanner.LayoutTargetOptionsFor(_planner.LayoutTarget);
        return _layoutTargetCombo.SelectedIndex >= 0 && _layoutTargetCombo.SelectedIndex < options.Count
            ? options[_layoutTargetCombo.SelectedIndex].Value
            : null;
    }

    private void SelectLayoutTarget(string? value)
    {
        var options = ChartLayoutOptionsPlanner.LayoutTargetOptionsFor(value);
        _layoutTargetCombo.ItemsSource = options.Select(option => option.Label).ToArray();
        _layoutTargetCombo.SelectedIndex = Math.Max(0,
            options.Select((item, index) => (item, index))
                .FirstOrDefault(x => string.Equals(x.item.Value, value, StringComparison.OrdinalIgnoreCase)).index);
    }

    private static ComboBox MakeLayoutTargetCombo() => new() { MinWidth = 190 };
    private static ComboBox MakeModeCombo() => new() { ItemsSource = ChartLayoutOptionsPlanner.ModeOptions.Select(x => x.Label).ToArray(), MinWidth = 105 };
    private static ChartManualLayoutMode SelectedMode(ComboBox combo) =>
        combo.SelectedIndex >= 0 && combo.SelectedIndex < ChartLayoutOptionsPlanner.ModeOptions.Count
            ? ChartLayoutOptionsPlanner.ModeOptions[combo.SelectedIndex].Value
            : ChartManualLayoutMode.Factor;
    private static double? ParseOptional(string? text, string label) { if (string.IsNullOrWhiteSpace(text)) return null; if (double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out var value) && double.IsFinite(value)) return value; throw new FormatException($"{label} must be a finite number or blank."); }
    private static string Format(double? value) => value?.ToString("G", CultureInfo.CurrentCulture) ?? string.Empty;
    private static int FindTargetIndex(ChartLayoutTarget value) => value == ChartLayoutTarget.Legend ? 1 : 0;
    private static int FindModeIndex(ChartManualLayoutMode value) =>
        Math.Max(0, ChartLayoutOptionsPlanner.ModeOptions.Select((item, index) => (item, index)).FirstOrDefault(x => x.item.Value == value).index);
    private static Control MakeRow(string label, Control control) { var row = new Grid { ColumnDefinitions = new ColumnDefinitions("140, *") }; row.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) }); Grid.SetColumn(control, 1); row.Children.Add(control); return row; }
    private static Control MakeRow(string label, Control value, Control mode) { var row = new Grid { ColumnDefinitions = new ColumnDefinitions("140, 110, *") }; row.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) }); Grid.SetColumn(value, 1); row.Children.Add(value); Grid.SetColumn(mode, 2); row.Children.Add(mode); return row; }
    private static Button MakeButton(string label, bool isDefault, Action action) { var button = new Button { Content = label, IsDefault = isDefault, MinWidth = 80 }; AvaloniaCompactDialogChrome.ApplyButton(button, DialogChromeStyle, minWidth: 80, isDefault: isDefault); button.Click += (_, _) => action(); return button; }
}
