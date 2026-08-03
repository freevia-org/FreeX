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
    private static readonly AvaloniaCompactDialogChromeStyle DialogChromeStyle = new(FontFamily.Default);
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
                MakeRow(surface.TargetLabel, _targetCombo),
                MakeRow(surface.FillLabel, _fillBox),
                MakeRow(surface.FillTransparencyLabel, _fillTransparencyBox),
                _noFillCheck,
                MakeRow(surface.OutlineLabel, _outlineBox),
                _noOutlineCheck,
                MakeRow(surface.WidthLabel, _widthBox),
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

    private ChartAreaFormattingTarget SelectedTarget() => _targetCombo.SelectedIndex == 1 ? ChartAreaFormattingTarget.PlotArea : ChartAreaFormattingTarget.ChartArea;
    private static double? ParseOptional(string? text) { if (string.IsNullOrWhiteSpace(text)) return null; if (double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out var value) && double.IsFinite(value)) return value; throw new FormatException("The value must be a finite number or blank."); }
    private static string Format(double? value) => value?.ToString("G", CultureInfo.CurrentCulture) ?? string.Empty;
    private static Control MakeRow(string label, Control control) { var row = new Grid { ColumnDefinitions = new ColumnDefinitions("170, *") }; row.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) }); Grid.SetColumn(control, 1); row.Children.Add(control); return row; }
    private static Button MakeButton(string label, bool isDefault, Action action) { var button = new Button { Content = label, IsDefault = isDefault, MinWidth = 80 }; AvaloniaCompactDialogChrome.ApplyButton(button, DialogChromeStyle, minWidth: 80, isDefault: isDefault); button.Click += (_, _) => action(); return button; }
}
