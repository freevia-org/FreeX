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
        content.Children.Add(MakeRow(surface.TargetLabel, _targetCombo));
        content.Children.Add(MakeRow(surface.LayoutTargetLabel, _layoutTargetCombo));
        content.Children.Add(MakeRow(surface.XLabel, _xBox, surface.XModeLabel, _xModeCombo));
        content.Children.Add(MakeRow(surface.YLabel, _yBox, surface.YModeLabel, _yModeCombo));
        content.Children.Add(MakeRow(surface.WidthLabel, _widthBox, surface.WidthModeLabel, _widthModeCombo));
        content.Children.Add(MakeRow(surface.HeightLabel, _heightBox, surface.HeightModeLabel, _heightModeCombo));
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

    private string? SelectedLayoutTarget() =>
        _layoutTargetCombo.SelectedItem is ChartLayoutTargetSemanticOption option ? option.Value : null;

    private void SelectLayoutTarget(string? value)
    {
        var options = ChartLayoutOptionsPlanner.LayoutTargetOptionsFor(value);
        _layoutTargetCombo.ItemsSource = options;
        _layoutTargetCombo.SelectedIndex = Math.Max(0,
            options.Select((item, index) => (item, index))
                .FirstOrDefault(x => string.Equals(x.item.Value, value, StringComparison.OrdinalIgnoreCase)).index);
    }

    private static ChartManualLayoutMode SelectedMode(ComboBox combo) =>
        combo.SelectedItem is ChartLayoutModeOption option ? option.Value : ChartManualLayoutMode.Factor;

    private static double? ParseOptional(string? text, string surface)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out var value) && double.IsFinite(value))
            return value;
        throw new FormatException($"{surface} must be a finite number or blank.");
    }

    private static string Format(double? value) => value?.ToString("G", CultureInfo.CurrentCulture) ?? string.Empty;

    private static int FindTargetIndex(ChartLayoutTarget value) =>
        Math.Max(0, ChartLayoutOptionsPlanner.TargetOptions.Select((item, index) => (item, index)).FirstOrDefault(x => x.item.Value == value).index);

    private static int FindModeIndex(ChartManualLayoutMode value) =>
        Math.Max(0, ChartLayoutOptionsPlanner.ModeOptions.Select((item, index) => (item, index)).FirstOrDefault(x => x.item.Value == value).index);

    private static StackPanel MakeRow(string label, Control control)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        row.Children.Add(new Label { Content = label, Width = 130, VerticalContentAlignment = VerticalAlignment.Center });
        row.Children.Add(control);
        return row;
    }

    private static StackPanel MakeRow(string valueLabel, Control value, string modeLabel, Control mode)
    {
        var row = MakeRow(valueLabel, value);
        row.Children.Add(new Label { Content = modeLabel, Width = 90, Margin = new Thickness(14, 0, 0, 0), VerticalContentAlignment = VerticalAlignment.Center });
        row.Children.Add(mode);
        return row;
    }
}
