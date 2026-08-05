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

        var buttons = ChartOptionsDialogChrome.CreateActionRow(
            surface.OkLabel,
            OnOk,
            surface.CancelLabel,
            () => Close(false));
        Content = new StackPanel
        {
            Margin = new Thickness(14), Spacing = 8,
            Children =
            {
                ChartOptionsDialogChrome.CreateRow(surface.TargetLabel, _targetCombo, 140), ChartOptionsDialogChrome.CreateRow(surface.LayoutTargetLabel, _layoutTargetCombo, 140),
                ChartOptionsDialogChrome.CreateValueModeRow(surface.XLabel, _xBox, _xModeCombo, 140, 110), ChartOptionsDialogChrome.CreateValueModeRow(surface.YLabel, _yBox, _yModeCombo, 140, 110),
                ChartOptionsDialogChrome.CreateValueModeRow(surface.WidthLabel, _widthBox, _widthModeCombo, 140, 110), ChartOptionsDialogChrome.CreateValueModeRow(surface.HeightLabel, _heightBox, _heightModeCombo, 140, 110),
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

    private ChartLayoutTarget SelectedTarget() =>
        ChartDialogOptionProjection.ValueAtOrDefault(
            ChartLayoutOptionsPlanner.TargetOptions,
            _targetCombo.SelectedIndex,
            option => option.Value,
            ChartLayoutTarget.PlotArea);
    private string? SelectedLayoutTarget()
    {
        var options = ChartLayoutOptionsPlanner.LayoutTargetOptionsFor(_planner.LayoutTarget);
        return ChartDialogOptionProjection.ValueAtOrDefault(
            options,
            _layoutTargetCombo.SelectedIndex,
            option => option.Value,
            default(string?));
    }

    private void SelectLayoutTarget(string? value)
    {
        var options = ChartLayoutOptionsPlanner.LayoutTargetOptionsFor(value);
        _layoutTargetCombo.ItemsSource = options.Select(option => option.Label).ToArray();
        _layoutTargetCombo.SelectedIndex = ChartDialogOptionProjection.FindIndex(
            options,
            value,
            option => option.Value,
            comparer: StringComparer.OrdinalIgnoreCase);
    }

    private static ComboBox MakeLayoutTargetCombo() => new() { MinWidth = 190 };
    private static ComboBox MakeModeCombo() => new() { ItemsSource = ChartLayoutOptionsPlanner.ModeOptions.Select(x => x.Label).ToArray(), MinWidth = 105 };
    private static ChartManualLayoutMode SelectedMode(ComboBox combo) =>
        ChartDialogOptionProjection.ValueAtOrDefault(
            ChartLayoutOptionsPlanner.ModeOptions,
            combo.SelectedIndex,
            option => option.Value,
            ChartManualLayoutMode.Factor);
    private static double? ParseOptional(string? text, string label) =>
        ChartDialogOptionProjection.ParseOptionalDouble(text, CultureInfo.CurrentCulture, double.IsFinite, $"{label} must be a finite number or blank.");
    private static string Format(double? value) => ChartDialogOptionProjection.Format(value, CultureInfo.CurrentCulture);
    private static int FindTargetIndex(ChartLayoutTarget value) =>
        ChartDialogOptionProjection.FindIndex(ChartLayoutOptionsPlanner.TargetOptions, value, option => option.Value);
    private static int FindModeIndex(ChartManualLayoutMode value) =>
        ChartDialogOptionProjection.FindIndex(ChartLayoutOptionsPlanner.ModeOptions, value, option => option.Value);
}
