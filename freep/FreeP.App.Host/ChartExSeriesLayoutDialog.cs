using System.Windows;
using System.Windows.Controls;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Host;

public sealed class ChartExSeriesLayoutDialog : Free.Shared.Ribbon.Wpf.DialogWindow
{
    private readonly EditingSession _editor;
    private readonly ChartShape _chart;
    private readonly IReadOnlyList<ChartExSeriesLayoutOption> _seriesOptions;
    private readonly ComboBox _seriesCombo;
    private readonly ComboBox _layoutCombo;

    public ChartExSeriesLayoutDialog(EditingSession editor)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        _chart = editor.SelectedChart
            ?? throw new InvalidOperationException("No chart is currently selected.");
        if (!ChartExSeriesLayoutPlanner.CanEdit(_chart))
            throw new InvalidOperationException("The selected chart has no editable native ChartEx series layouts.");

        _seriesOptions = ChartExSeriesLayoutPlanner.BuildOptions(_chart);
        Title = ChartExSeriesLayoutPlanner.DialogTitle;
        Width = 430;
        Height = 220;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;

        _seriesCombo = new ComboBox
        {
            ItemsSource = _seriesOptions,
            DisplayMemberPath = nameof(ChartExSeriesLayoutOption.Label),
            SelectedIndex = 0,
            MinWidth = 260,
        };
        _seriesCombo.SelectionChanged += (_, _) => LoadLayoutChoices();
        _layoutCombo = new ComboBox { MinWidth = 260, DisplayMemberPath = nameof(LayoutChoice.Label) };
        LoadLayoutChoices();

        var ok = new Button { Content = ChartExSeriesLayoutPlanner.OkLabel, IsDefault = true, MinWidth = 80, Margin = new Thickness(4) };
        var cancel = new Button { Content = ChartExSeriesLayoutPlanner.CancelLabel, IsCancel = true, MinWidth = 80, Margin = new Thickness(4) };
        ok.Click += (_, _) => OnOk();
        cancel.Click += (_, _) => Close();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);

        var content = new StackPanel { Margin = new Thickness(14) };
        content.Children.Add(MakeRow(ChartExSeriesLayoutPlanner.SeriesLabel, _seriesCombo));
        content.Children.Add(MakeRow(ChartExSeriesLayoutPlanner.LayoutLabel, _layoutCombo));
        content.Children.Add(buttons);
        Content = content;
    }

    internal int SelectedSeriesIndexForTests => (_seriesCombo.SelectedItem as ChartExSeriesLayoutOption)?.SeriesIndex ?? -1;
    internal string? SelectedLayoutIdForTests => (_layoutCombo.SelectedItem as LayoutChoice)?.LayoutId;

    internal void ApplyForTests()
    {
        var seriesIndex = SelectedSeriesIndexForTests;
        var layoutId = SelectedLayoutIdForTests ?? string.Empty;
        var plan = ChartExSeriesLayoutPlanner.BuildCommitPlan(_chart, seriesIndex, layoutId);
        _editor.SetChartExSeriesLayout(plan.SeriesIndex, plan.LayoutId);
    }

    private void OnOk()
    {
        try
        {
            ApplyForTests();
            DialogResult = true;
        }
        catch (ArgumentException ex)
        {
            MessageBox.Show(this, ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void LoadLayoutChoices()
    {
        if (_seriesCombo.SelectedItem is not ChartExSeriesLayoutOption series)
            return;
        var choices = ChartExSeriesLayoutPlanner.BuildLayoutChoices(_chart)
            .Select(layoutId => new LayoutChoice(layoutId, FormatLayoutLabel(layoutId)))
            .ToArray();
        _layoutCombo.ItemsSource = choices;
        _layoutCombo.SelectedIndex = Math.Max(0,
            Array.FindIndex(choices, choice => string.Equals(choice.LayoutId, series.LayoutId, StringComparison.OrdinalIgnoreCase)));
    }

    private static StackPanel MakeRow(string label, Control control)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
        row.Children.Add(new Label { Content = label, Width = 80, VerticalContentAlignment = VerticalAlignment.Center });
        row.Children.Add(control);
        return row;
    }

    private static string FormatLayoutLabel(string layoutId) =>
        string.Concat(layoutId.Replace('_', ' ').Replace('-', ' ').Select((character, index) =>
            index == 0 ? char.ToUpperInvariant(character).ToString() : character.ToString()));

    private sealed record LayoutChoice(string LayoutId, string Label);
}
