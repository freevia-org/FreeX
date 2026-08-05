using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Free.Shared.Ribbon.Wpf;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Host;

/// <summary>PowerPoint-style chart-area and plot-area formatting dialog.</summary>
public sealed class ChartAreaOptionsDialog : DialogWindow
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

    public ChartAreaOptionsDialog(EditingSession editor, ChartAreaFormattingTarget? initialTarget = null)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        _planner = ChartAreaOptionsPlanner.FromChart(editor.SelectedChart ?? throw new InvalidOperationException("No chart is currently selected."));
        var surface = ChartAreaOptionsPlanner.BuildSurfacePlan();
        Title = surface.Title;
        Width = 390;
        Height = 340;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;

        _targetCombo = new ComboBox
        {
            ItemsSource = ChartAreaOptionsPlanner.TargetOptions,
            DisplayMemberPath = nameof(ChartAreaFormattingTargetOption.Label),
            MinWidth = 190,
        };
        _targetCombo.SelectionChanged += (_, _) =>
        {
            if (_targetCombo.SelectedItem is ChartAreaFormattingTargetOption option)
            {
                _planner.SetTarget(option.Value);
                LoadControls();
            }
        };
        _fillBox = new TextBox { MinWidth = 190 };
        _fillTransparencyBox = new TextBox { MinWidth = 120 };
        _noFillCheck = new CheckBox { Content = surface.NoFillLabel };
        _outlineBox = new TextBox { MinWidth = 190 };
        _noOutlineCheck = new CheckBox { Content = surface.NoOutlineLabel };
        _widthBox = new TextBox { MinWidth = 120 };
        _targetCombo.SelectedIndex = initialTarget == ChartAreaFormattingTarget.PlotArea ? 1 : 0;
        LoadControls();

        var buttons = ChartOptionsDialogChrome.CreateActionRow(
            surface.OkLabel,
            OnOk,
            surface.CancelLabel,
            Close,
            new Thickness());

        var content = new StackPanel { Margin = new Thickness(14) };
        content.Children.Add(ChartOptionsDialogChrome.CreateRow(surface.TargetLabel, _targetCombo, 170));
        content.Children.Add(ChartOptionsDialogChrome.CreateRow(surface.FillLabel, _fillBox, 170));
        content.Children.Add(ChartOptionsDialogChrome.CreateRow(surface.FillTransparencyLabel, _fillTransparencyBox, 170));
        content.Children.Add(_noFillCheck);
        content.Children.Add(ChartOptionsDialogChrome.CreateRow(surface.OutlineLabel, _outlineBox, 170));
        content.Children.Add(_noOutlineCheck);
        content.Children.Add(ChartOptionsDialogChrome.CreateRow(surface.WidthLabel, _widthBox, 170));
        content.Children.Add(new TextBlock { Text = surface.Hint, TextWrapping = TextWrapping.Wrap, Opacity = 0.7, Margin = new Thickness(0, 0, 0, 8) });
        content.Children.Add(buttons);
        Content = content;
    }

    internal ChartAreaOptions BuildCommitPlanForTests()
    {
        UpdatePlannerFromControls();
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
        try
        {
            _editor.ApplyChartAreaOptions(BuildCommitPlanForTests());
            DialogResult = true;
        }
        catch (FormatException ex)
        {
            MessageBox.Show(this, ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
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

    private void UpdatePlannerFromControls()
    {
        _planner.SetFillColor(_fillBox.Text);
        _planner.SetFillTransparency(ParseOptional(_fillTransparencyBox.Text));
        _planner.SetNoFill(_noFillCheck.IsChecked == true);
        _planner.SetOutlineColor(_outlineBox.Text);
        _planner.SetNoOutline(_noOutlineCheck.IsChecked == true);
        _planner.SetOutlineWidth(ParseOptional(_widthBox.Text));
    }

    private static double? ParseOptional(string? text)
    {
        return ChartDialogOptionProjection.ParseOptionalDouble(
            text,
            CultureInfo.CurrentCulture,
            double.IsFinite,
            "The value must be a finite number or blank.");
    }

    private static string Format(double? value) =>
        ChartDialogOptionProjection.Format(value, CultureInfo.CurrentCulture);
}
