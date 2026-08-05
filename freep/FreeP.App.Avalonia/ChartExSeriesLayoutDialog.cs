using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Free.Shared.Shell.Avalonia;
using FreeP.App.Compositor;

namespace FreeP.App.Avalonia;

internal sealed class ChartExSeriesLayoutDialog : Window
{
    private static readonly AvaloniaCompactDialogChromeStyle DialogChromeStyle = new(FontFamily.Default);
    private readonly ChartExSeriesLayoutDialogSession _session;
    private readonly ComboBox _seriesCombo;
    private readonly ComboBox _layoutCombo;

    internal ChartExSeriesLayoutDialog(EditingSession editor)
    {
        _session = new ChartExSeriesLayoutDialogSession(editor);
        Title = ChartExSeriesLayoutPlanner.DialogTitle;
        Width = 430;
        Height = 220;
        MinWidth = 400;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(0xF3, 0xF3, 0xF3));

        _seriesCombo = new ComboBox { ItemsSource = _session.SeriesOptions.Select(option => option.Label).ToArray(), SelectedIndex = 0, MinWidth = 260 };
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

    private void OnOk()
    {
        if (_session.TryApply(_layoutCombo.SelectedIndex, out _))
        {
            Close(true);
            return;
        }

        Close(false);
    }

    private void LoadLayoutChoices()
    {
        var selection = _session.SelectSeries(_seriesCombo.SelectedIndex);
        _layoutCombo.ItemsSource = selection.LayoutChoices
            .Select(choice => choice.Label)
            .ToArray();
        _layoutCombo.SelectedIndex = selection.LayoutIndex;
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

}
