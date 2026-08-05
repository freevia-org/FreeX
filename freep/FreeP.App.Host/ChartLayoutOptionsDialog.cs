using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Host;

/// <summary>PowerPoint-style plot-area and legend manual-layout dialog.</summary>
public sealed class ChartLayoutOptionsDialog : Free.Shared.Ribbon.Wpf.DialogWindow
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

    public ChartLayoutOptionsDialog(EditingSession editor)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        var chart = editor.SelectedChart ?? throw new InvalidOperationException("No chart is currently selected.");
        _planner = ChartLayoutOptionsPlanner.FromChart(chart);
        var surface = ChartLayoutOptionsPlanner.BuildSurfacePlan();

        Title = surface.Title;
        Width = ChartLayoutOptionsPlanner.DefaultDialogWidth;
        Height = ChartLayoutOptionsPlanner.DefaultDialogHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;

        _targetCombo = new ComboBox
        {
            ItemsSource = ChartLayoutOptionsPlanner.TargetOptions,
            DisplayMemberPath = nameof(ChartLayoutTargetOption.Label),
            SelectedIndex = 0,
            MinWidth = 180,
        };
        _targetCombo.SelectionChanged += (_, _) =>
        {
            if (_targetCombo.SelectedItem is ChartLayoutTargetOption option)
            {
                _planner.SetTarget(option.Value);
                LoadControls();
            }
        };
        _layoutTargetCombo = MakeLayoutTargetCombo();
        _xModeCombo = MakeModeCombo();
        _yModeCombo = MakeModeCombo();
        _widthModeCombo = MakeModeCombo();
        _heightModeCombo = MakeModeCombo();
        _xBox = new TextBox { MinWidth = 120 };
        _yBox = new TextBox { MinWidth = 120 };
        _widthBox = new TextBox { MinWidth = 120 };
        _heightBox = new TextBox { MinWidth = 120 };
        LoadControls();

        var buttons = ChartOptionsDialogChrome.CreateActionRow(
            surface.OkLabel,
            OnOk,
            surface.CancelLabel,
            Close,
            new Thickness(8, 14, 8, 8));

        var content = new StackPanel { Margin = new Thickness(14) };
        content.Children.Add(ChartOptionsDialogChrome.CreateRow(surface.TargetLabel, _targetCombo, 130));
        content.Children.Add(ChartOptionsDialogChrome.CreateRow(surface.LayoutTargetLabel, _layoutTargetCombo, 130));
        content.Children.Add(ChartOptionsDialogChrome.CreateValueModeRow(surface.XLabel, _xBox, 130, surface.XModeLabel, _xModeCombo, 90));
        content.Children.Add(ChartOptionsDialogChrome.CreateValueModeRow(surface.YLabel, _yBox, 130, surface.YModeLabel, _yModeCombo, 90));
        content.Children.Add(ChartOptionsDialogChrome.CreateValueModeRow(surface.WidthLabel, _widthBox, 130, surface.WidthModeLabel, _widthModeCombo, 90));
        content.Children.Add(ChartOptionsDialogChrome.CreateValueModeRow(surface.HeightLabel, _heightBox, 130, surface.HeightModeLabel, _heightModeCombo, 90));
        content.Children.Add(new TextBlock { Text = surface.Hint, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8), Opacity = 0.7 });
        content.Children.Add(buttons);
        Content = content;
    }

    internal ChartLayoutOptions BuildCommitPlanForTests()
    {
        UpdatePlannerFromControls();
        return _planner.BuildCommitPlan();
    }

    internal void SetOptionsForTests(
        ChartLayoutTarget target,
        string? layoutTarget,
        ChartManualLayoutMode xMode,
        ChartManualLayoutMode yMode,
        ChartManualLayoutMode widthMode,
        ChartManualLayoutMode heightMode,
        double? x,
        double? y,
        double? width,
        double? height)
    {
        _targetCombo.SelectedIndex = FindTargetIndex(target);
        SelectLayoutTarget(layoutTarget);
        _xModeCombo.SelectedIndex = FindModeIndex(xMode);
        _yModeCombo.SelectedIndex = FindModeIndex(yMode);
        _widthModeCombo.SelectedIndex = FindModeIndex(widthMode);
        _heightModeCombo.SelectedIndex = FindModeIndex(heightMode);
        _xBox.Text = Format(x);
        _yBox.Text = Format(y);
        _widthBox.Text = Format(width);
        _heightBox.Text = Format(height);
    }

    private void OnOk()
    {
        try
        {
            _editor.ApplyChartLayoutOptions(BuildCommitPlanForTests());
            DialogResult = true;
        }
        catch (FormatException ex)
        {
            MessageBox.Show(this, ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void LoadControls()
    {
        SelectLayoutTarget(_planner.LayoutTarget);
        _xModeCombo.SelectedIndex = FindModeIndex(_planner.XMode);
        _yModeCombo.SelectedIndex = FindModeIndex(_planner.YMode);
        _widthModeCombo.SelectedIndex = FindModeIndex(_planner.WidthMode);
        _heightModeCombo.SelectedIndex = FindModeIndex(_planner.HeightMode);
        _xBox.Text = Format(_planner.X);
        _yBox.Text = Format(_planner.Y);
        _widthBox.Text = Format(_planner.Width);
        _heightBox.Text = Format(_planner.Height);
    }

    private void UpdatePlannerFromControls()
    {
        _planner.SetLayoutTarget(SelectedLayoutTarget());
        _planner.SetXMode(SelectedMode(_xModeCombo));
        _planner.SetYMode(SelectedMode(_yModeCombo));
        _planner.SetWidthMode(SelectedMode(_widthModeCombo));
        _planner.SetHeightMode(SelectedMode(_heightModeCombo));
        _planner.SetX(ParseOptional(_xBox.Text, surface: "X"));
        _planner.SetY(ParseOptional(_yBox.Text, surface: "Y"));
        _planner.SetWidth(ParseOptional(_widthBox.Text, surface: "Width"));
        _planner.SetHeight(ParseOptional(_heightBox.Text, surface: "Height"));
    }

    private static ComboBox MakeModeCombo() => new()
    {
        ItemsSource = ChartLayoutOptionsPlanner.ModeOptions,
        DisplayMemberPath = nameof(ChartLayoutModeOption.Label),
        MinWidth = 100,
    };

    private static ComboBox MakeLayoutTargetCombo() => new()
    {
        DisplayMemberPath = nameof(ChartLayoutTargetSemanticOption.Label),
        MinWidth = 180,
    };

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
        _layoutTargetCombo.ItemsSource = options;
        _layoutTargetCombo.SelectedIndex = ChartDialogOptionProjection.FindIndex(
            options,
            value,
            option => option.Value,
            comparer: StringComparer.OrdinalIgnoreCase);
    }

    private static ChartManualLayoutMode SelectedMode(ComboBox combo) =>
        ChartDialogOptionProjection.ValueAtOrDefault(
            ChartLayoutOptionsPlanner.ModeOptions,
            combo.SelectedIndex,
            option => option.Value,
            ChartManualLayoutMode.Factor);

    private static double? ParseOptional(string? text, string surface)
    {
        return ChartDialogOptionProjection.ParseOptionalDouble(
            text,
            CultureInfo.CurrentCulture,
            double.IsFinite,
            $"{surface} must be a finite number or blank.");
    }

    private static string Format(double? value) =>
        ChartDialogOptionProjection.Format(value, CultureInfo.CurrentCulture);

    private static int FindTargetIndex(ChartLayoutTarget value) =>
        ChartDialogOptionProjection.FindIndex(
            ChartLayoutOptionsPlanner.TargetOptions,
            value,
            option => option.Value);

    private static int FindModeIndex(ChartManualLayoutMode value) =>
        ChartDialogOptionProjection.FindIndex(
            ChartLayoutOptionsPlanner.ModeOptions,
            value,
            option => option.Value);
}
