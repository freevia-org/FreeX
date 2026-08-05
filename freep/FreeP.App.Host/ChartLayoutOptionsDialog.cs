using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Host;

/// <summary>PowerPoint-style plot-area and legend manual-layout dialog.</summary>
public sealed class ChartLayoutOptionsDialog : Free.Shared.Ribbon.Wpf.DialogWindow
{
    private readonly ChartLayoutOptionsDialogSession _session;
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
        _session = new ChartLayoutOptionsDialogSession(editor);
        var state = _session.State;
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
            SelectedIndex = state.TargetIndex,
            MinWidth = 180,
        };
        _targetCombo.SelectionChanged += (_, _) =>
        {
            LoadControls(_session.SelectTarget(_targetCombo.SelectedIndex));
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
        LoadControls(state);

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

    internal ChartLayoutOptions BuildCommitPlanForTests() =>
        _session.BuildCommitPlan(ReadInput(), CultureInfo.CurrentCulture);

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
        var result = _session.TryCommit(ReadInput(), CultureInfo.CurrentCulture);
        if (result.Succeeded)
        {
            DialogResult = true;
            return;
        }

        MessageBox.Show(this, result.Error, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private ChartLayoutOptionsDialogInput ReadInput() => new(
        _targetCombo.SelectedIndex,
        _layoutTargetCombo.SelectedIndex,
        _xModeCombo.SelectedIndex,
        _yModeCombo.SelectedIndex,
        _widthModeCombo.SelectedIndex,
        _heightModeCombo.SelectedIndex,
        _xBox.Text,
        _yBox.Text,
        _widthBox.Text,
        _heightBox.Text);

    private void LoadControls(ChartLayoutOptionsDialogState state)
    {
        _layoutTargetCombo.ItemsSource = state.LayoutTargetOptions;
        _layoutTargetCombo.SelectedIndex = state.LayoutTargetIndex;
        _xModeCombo.SelectedIndex = state.XModeIndex;
        _yModeCombo.SelectedIndex = state.YModeIndex;
        _widthModeCombo.SelectedIndex = state.WidthModeIndex;
        _heightModeCombo.SelectedIndex = state.HeightModeIndex;
        _xBox.Text = Format(state.X);
        _yBox.Text = Format(state.Y);
        _widthBox.Text = Format(state.Width);
        _heightBox.Text = Format(state.Height);
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
