using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Free.Shared.Shell.Avalonia;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Avalonia;

internal sealed class ChartProtectionOptionsDialog : Window
{
    private readonly EditingSession _editor;
    private readonly ChartProtectionOptionsPlanner _planner;
    private readonly ComboBox _chartObjectCombo;
    private readonly ComboBox _dataCombo;
    private readonly ComboBox _formattingCombo;
    private readonly ComboBox _selectionCombo;

    internal ChartProtectionOptionsDialog(EditingSession editor)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        var chart = editor.SelectedChart
            ?? throw new InvalidOperationException("No chart is currently selected.");
        _planner = ChartProtectionOptionsPlanner.FromChart(chart);
        var surface = ChartProtectionOptionsPlanner.BuildSurfacePlan();

        Title = surface.Title;
        Width = ChartProtectionOptionsPlanner.DefaultDialogWidth;
        Height = ChartProtectionOptionsPlanner.DefaultDialogHeight;
        MinWidth = 380;
        MinHeight = 280;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(0xF3, 0xF3, 0xF3));

        _chartObjectCombo = BuildBooleanCombo(_planner.ChartObject);
        _dataCombo = BuildBooleanCombo(_planner.Data);
        _formattingCombo = BuildBooleanCombo(_planner.Formatting);
        _selectionCombo = BuildBooleanCombo(_planner.Selection);

        var buttons = ChartOptionsDialogChrome.CreateActionRow(
            surface.OkLabel,
            OnOk,
            surface.CancelLabel,
            () => Close(false));

        Content = new StackPanel
        {
            Margin = new Thickness(14),
            Spacing = 8,
            Children =
            {
                ChartOptionsDialogChrome.CreateRow(surface.ChartObjectLabel, _chartObjectCombo, 180),
                ChartOptionsDialogChrome.CreateRow(surface.DataLabel, _dataCombo, 180),
                ChartOptionsDialogChrome.CreateRow(surface.FormattingLabel, _formattingCombo, 180),
                ChartOptionsDialogChrome.CreateRow(surface.SelectionLabel, _selectionCombo, 180),
                new TextBlock { Text = surface.Hint, TextWrapping = TextWrapping.Wrap, Opacity = 0.7 },
                buttons,
            },
        };
    }

    internal ChartProtectionOptions BuildCommitPlanForTests()
    {
        UpdatePlannerFromControls();
        return _planner.BuildCommitPlan();
    }

    internal void SetOptionsForTests(bool? chartObject, bool? data, bool? formatting, bool? selection)
    {
        _chartObjectCombo.SelectedIndex = FindBooleanIndex(chartObject);
        _dataCombo.SelectedIndex = FindBooleanIndex(data);
        _formattingCombo.SelectedIndex = FindBooleanIndex(formatting);
        _selectionCombo.SelectedIndex = FindBooleanIndex(selection);
    }

    private void OnOk()
    {
        _editor.ApplyChartProtectionOptions(BuildCommitPlanForTests());
        Close(true);
    }

    private void UpdatePlannerFromControls()
    {
        _planner.SetChartObject(ReadBoolean(_chartObjectCombo));
        _planner.SetData(ReadBoolean(_dataCombo));
        _planner.SetFormatting(ReadBoolean(_formattingCombo));
        _planner.SetSelection(ReadBoolean(_selectionCombo));
    }

    private static ComboBox BuildBooleanCombo(bool? value) => new()
    {
        ItemsSource = ChartProtectionOptionsPlanner.BooleanOptions.Select(option => option.Label).ToArray(),
        SelectedIndex = FindBooleanIndex(value),
        MinWidth = 180,
    };

    private static bool? ReadBoolean(ComboBox combo)
    {
        return ChartDialogOptionProjection.ValueAtOrDefault(
            ChartProtectionOptionsPlanner.BooleanOptions,
            combo.SelectedIndex,
            option => option.Value,
            default(bool?));
    }

    private static int FindBooleanIndex(bool? value) =>
        ChartDialogOptionProjection.FindIndex(
            ChartProtectionOptionsPlanner.BooleanOptions,
            value,
            option => option.Value);
}
