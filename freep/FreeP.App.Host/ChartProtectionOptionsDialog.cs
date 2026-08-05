using System.Windows;
using System.Windows.Controls;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Host;

/// <summary>PowerPoint-style chart object/data/formatting/selection protection dialog.</summary>
public sealed class ChartProtectionOptionsDialog : Free.Shared.Ribbon.Wpf.DialogWindow
{
    private readonly EditingSession _editor;
    private readonly ChartProtectionOptionsPlanner _planner;
    private readonly ComboBox _chartObjectCombo;
    private readonly ComboBox _dataCombo;
    private readonly ComboBox _formattingCombo;
    private readonly ComboBox _selectionCombo;

    public ChartProtectionOptionsDialog(EditingSession editor)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        var chart = editor.SelectedChart
            ?? throw new InvalidOperationException("No chart is currently selected.");
        _planner = ChartProtectionOptionsPlanner.FromChart(chart);
        var surface = ChartProtectionOptionsPlanner.BuildSurfacePlan();

        Title = surface.Title;
        Width = ChartProtectionOptionsPlanner.DefaultDialogWidth;
        Height = ChartProtectionOptionsPlanner.DefaultDialogHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;

        _chartObjectCombo = BuildBooleanCombo(_planner.ChartObject);
        _dataCombo = BuildBooleanCombo(_planner.Data);
        _formattingCombo = BuildBooleanCombo(_planner.Formatting);
        _selectionCombo = BuildBooleanCombo(_planner.Selection);

        var buttons = ChartOptionsDialogChrome.CreateActionRow(
            surface.OkLabel,
            OnOk,
            surface.CancelLabel,
            Close,
            new Thickness(8, 14, 8, 8));

        var content = new StackPanel { Margin = new Thickness(14) };
        content.Children.Add(ChartOptionsDialogChrome.CreateRow(surface.ChartObjectLabel, _chartObjectCombo, 180));
        content.Children.Add(ChartOptionsDialogChrome.CreateRow(surface.DataLabel, _dataCombo, 180));
        content.Children.Add(ChartOptionsDialogChrome.CreateRow(surface.FormattingLabel, _formattingCombo, 180));
        content.Children.Add(ChartOptionsDialogChrome.CreateRow(surface.SelectionLabel, _selectionCombo, 180));
        content.Children.Add(new TextBlock { Text = surface.Hint, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8), Opacity = 0.7 });
        content.Children.Add(buttons);
        Content = content;
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
        DialogResult = true;
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
        ItemsSource = ChartProtectionOptionsPlanner.BooleanOptions,
        DisplayMemberPath = nameof(ChartProtectionBooleanOption.Label),
        SelectedIndex = FindBooleanIndex(value),
        MinWidth = 180,
    };

    private static bool? ReadBoolean(ComboBox combo) =>
        ChartDialogOptionProjection.ValueAtOrDefault(
            ChartProtectionOptionsPlanner.BooleanOptions,
            combo.SelectedIndex,
            option => option.Value,
            default(bool?));

    private static int FindBooleanIndex(bool? value) =>
        ChartDialogOptionProjection.FindIndex(
            ChartProtectionOptionsPlanner.BooleanOptions,
            value,
            option => option.Value);
}
