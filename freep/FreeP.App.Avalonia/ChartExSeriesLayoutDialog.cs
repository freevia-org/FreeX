using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Free.Shared.Shell.Avalonia;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Avalonia;

internal sealed class ChartExSeriesLayoutDialog : Window
{
    private static readonly AvaloniaCompactDialogChromeStyle DialogChromeStyle = new(FontFamily.Default);
    private readonly EditingSession _editor;
    private readonly ChartShape _chart;
    private readonly IReadOnlyList<ChartExSeriesLayoutOption> _seriesOptions;
    private readonly ComboBox _seriesCombo;
    private readonly ComboBox _layoutCombo;

    internal ChartExSeriesLayoutDialog(EditingSession editor)
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
        MinWidth = 400;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(0xF3, 0xF3, 0xF3));

        _seriesCombo = new ComboBox { ItemsSource = _seriesOptions.Select(option => option.Label).ToArray(), SelectedIndex = 0, MinWidth = 260 };
        _seriesCombo.SelectionChanged += (_, _) => LoadLayoutChoices();
        _layoutCombo = new ComboBox { MinWidth = 260 };
        LoadLayoutChoices();

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Thickness(0, 14, 0, 0),
            Children = { MakeButton(ChartExSeriesLayoutPlanner.OkLabel, true, OnOk), MakeButton(ChartExSeriesLayoutPlanner.CancelLabel, false, () => Close(false)) },
        };
        Content = new StackPanel
        {
            Margin = new Thickness(14),
            Spacing = 10,
            Children = { MakeRow(ChartExSeriesLayoutPlanner.SeriesLabel, _seriesCombo), MakeRow(ChartExSeriesLayoutPlanner.LayoutLabel, _layoutCombo), buttons },
        };
    }

    private int SelectedSeriesIndex => _seriesCombo.SelectedIndex >= 0 && _seriesCombo.SelectedIndex < _seriesOptions.Count
        ? _seriesOptions[_seriesCombo.SelectedIndex].SeriesIndex
        : -1;

    private string? SelectedLayoutId => _layoutCombo.SelectedIndex >= 0 && _layoutCombo.SelectedIndex < _layoutChoices.Length
        ? _layoutChoices[_layoutCombo.SelectedIndex]
        : null;

    private string[] _layoutChoices = Array.Empty<string>();

    private void OnOk()
    {
        try
        {
            var plan = ChartExSeriesLayoutPlanner.BuildCommitPlan(_chart, SelectedSeriesIndex, SelectedLayoutId ?? string.Empty);
            _editor.SetChartExSeriesLayout(plan.SeriesIndex, plan.LayoutId);
            Close(true);
        }
        catch (ArgumentException)
        {
            Close(false);
        }
    }

    private void LoadLayoutChoices()
    {
        if (SelectedSeriesIndex < 0)
            return;
        _layoutChoices = ChartExSeriesLayoutPlanner.BuildLayoutChoices(_chart).ToArray();
        _layoutCombo.ItemsSource = _layoutChoices.Select(FormatLayoutLabel).ToArray();
        var current = _seriesOptions.First(option => option.SeriesIndex == SelectedSeriesIndex).LayoutId;
        _layoutCombo.SelectedIndex = Math.Max(0, Array.FindIndex(_layoutChoices, layoutId => string.Equals(layoutId, current, StringComparison.OrdinalIgnoreCase)));
    }

    private static Control MakeRow(string label, Control control)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("80, *") };
        row.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        Grid.SetColumn(control, 1);
        row.Children.Add(control);
        return row;
    }

    private static Button MakeButton(string label, bool isDefault, Action action)
    {
        var button = new Button { Content = label, IsDefault = isDefault, MinWidth = 80 };
        AvaloniaCompactDialogChrome.ApplyButton(button, DialogChromeStyle, minWidth: 80, isDefault: isDefault);
        button.Click += (_, _) => action();
        return button;
    }

    private static string FormatLayoutLabel(string layoutId) =>
        string.Concat(layoutId.Replace('_', ' ').Replace('-', ' ').Select((character, index) =>
            index == 0 ? char.ToUpperInvariant(character).ToString() : character.ToString()));
}
