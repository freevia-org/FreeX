using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Free.Shared.Shell.Avalonia;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Avalonia;

internal sealed class ChartAreaOptionsDialog : Window
{
    private readonly EditingSession _editor;
    private readonly ChartAreaOptionsPlanner _planner;
    private readonly ComboBox _targetCombo;
    private readonly TextBox _fillBox;
    private readonly TextBox _fillTransparencyBox;
    private readonly CheckBox _noFillCheck;
    private readonly TextBox _outlineBox;
    private readonly CheckBox _noOutlineCheck;
    private readonly TextBox _widthBox;

    internal ChartAreaOptionsDialog(EditingSession editor, ChartAreaFormattingTarget? initialTarget = null)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        _planner = ChartAreaOptionsPlanner.FromChart(editor.SelectedChart ?? throw new InvalidOperationException("No chart is currently selected."));
        var surface = ChartAreaOptionsPlanner.BuildSurfacePlan();
        Title = surface.Title;
        Width = 400;
        Height = 340;
        MinWidth = 400;
        MinHeight = 280;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(0xF3, 0xF3, 0xF3));

        _targetCombo = new ComboBox { ItemsSource = ChartAreaOptionsPlanner.TargetOptions.Select(x => x.Label).ToArray(), SelectedIndex = initialTarget == ChartAreaFormattingTarget.PlotArea ? 1 : 0, MinWidth = 190 };
        _targetCombo.SelectionChanged += (_, _) => { _planner.SetTarget(SelectedTarget()); LoadControls(); };
        _fillBox = new TextBox { MinWidth = 190 };
        _fillTransparencyBox = new TextBox { MinWidth = 120 };
        _noFillCheck = new CheckBox { Content = surface.NoFillLabel };
        _outlineBox = new TextBox { MinWidth = 190 };
        _noOutlineCheck = new CheckBox { Content = surface.NoOutlineLabel };
        _widthBox = new TextBox { MinWidth = 120 };
        _planner.SetTarget(SelectedTarget());
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
                ChartOptionsDialogChrome.CreateRow(surface.TargetLabel, _targetCombo, 170),
                ChartOptionsDialogChrome.CreateRow(surface.FillLabel, _fillBox, 170),
                ChartOptionsDialogChrome.CreateRow(surface.FillTransparencyLabel, _fillTransparencyBox, 170),
                _noFillCheck,
                ChartOptionsDialogChrome.CreateRow(surface.OutlineLabel, _outlineBox, 170),
                _noOutlineCheck,
                ChartOptionsDialogChrome.CreateRow(surface.WidthLabel, _widthBox, 170),
                new TextBlock { Text = surface.Hint, TextWrapping = TextWrapping.Wrap, Opacity = 0.7 },
                buttons,
            },
        };
    }

    internal ChartAreaOptions BuildCommitPlanForTests()
    {
        _planner.SetFillColor(_fillBox.Text);
        _planner.SetFillTransparency(ParseOptional(_fillTransparencyBox.Text));
        _planner.SetNoFill(_noFillCheck.IsChecked == true);
        _planner.SetOutlineColor(_outlineBox.Text);
        _planner.SetNoOutline(_noOutlineCheck.IsChecked == true);
        _planner.SetOutlineWidth(ParseOptional(_widthBox.Text));
        return _planner.BuildCommitPlan();
    }

    internal void SetOptionsForTests(ChartAreaFormattingTarget target, string? fill, string? outline, double? width, bool noFill = false, bool noOutline = false, double? fillTransparency = null)
    {
        _targetCombo.SelectedIndex = target == ChartAreaFormattingTarget.PlotArea ? 1 : 0;
        _fillBox.Text = fill ?? string.Empty;
        _fillTransparencyBox.Text = Format(fillTransparency);
        _noFillCheck.IsChecked = noFill;
        _outlineBox.Text = outline ?? string.Empty;
        _noOutlineCheck.IsChecked = noOutline;
        _widthBox.Text = Format(width);
    }

    private void OnOk()
    {
        try { _editor.ApplyChartAreaOptions(BuildCommitPlanForTests()); Close(true); }
        catch (FormatException) { Close(false); }
    }

    private void LoadControls()
    {
        _fillBox.Text = _planner.FillColor;
        _fillTransparencyBox.Text = Format(_planner.FillTransparencyPercent);
        _noFillCheck.IsChecked = _planner.NoFill;
        _outlineBox.Text = _planner.OutlineColor;
        _noOutlineCheck.IsChecked = _planner.NoOutline;
        _widthBox.Text = Format(_planner.OutlineWidthPt);
    }

    private ChartAreaFormattingTarget SelectedTarget() =>
        ChartDialogOptionProjection.ValueAtOrDefault(
            ChartAreaOptionsPlanner.TargetOptions,
            _targetCombo.SelectedIndex,
            option => option.Value,
            ChartAreaFormattingTarget.ChartArea);

    private static double? ParseOptional(string? text) =>
        ChartDialogOptionProjection.ParseOptionalDouble(
            text,
            CultureInfo.CurrentCulture,
            double.IsFinite,
            "The value must be a finite number or blank.");

    private static string Format(double? value) =>
        ChartDialogOptionProjection.Format(value, CultureInfo.CurrentCulture);
}
