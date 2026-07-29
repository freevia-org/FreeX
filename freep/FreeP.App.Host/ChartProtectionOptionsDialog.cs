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
        content.Children.Add(MakeRow(surface.ChartObjectLabel, _chartObjectCombo));
        content.Children.Add(MakeRow(surface.DataLabel, _dataCombo));
        content.Children.Add(MakeRow(surface.FormattingLabel, _formattingCombo));
        content.Children.Add(MakeRow(surface.SelectionLabel, _selectionCombo));
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
        SelectedIndex = ChartProtectionOptionsPlanner.BooleanOptions
            .Select((option, index) => (option, index))
            .First(item => item.option.Value == value).index,
        MinWidth = 180,
    };

    private static bool? ReadBoolean(ComboBox combo) =>
        combo.SelectedItem is ChartProtectionBooleanOption option ? option.Value : null;

    private static int FindBooleanIndex(bool? value) => ChartProtectionOptionsPlanner.BooleanOptions
        .Select((option, index) => (option, index))
        .First(item => item.option.Value == value).index;

    private static StackPanel MakeRow(string label, Control control)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        row.Children.Add(new Label { Content = label, Width = 180, VerticalContentAlignment = VerticalAlignment.Center });
        row.Children.Add(control);
        return row;
    }
}
